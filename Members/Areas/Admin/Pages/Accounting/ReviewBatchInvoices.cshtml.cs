using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations; // Not strictly needed for this PageModel if no input model, but good practice
using System.Linq;
using System.Text; // Added for StringBuilder
using System.Threading.Tasks;

namespace Members.Areas.Admin.Pages.Accounting
{
    [Authorize(Roles = "Admin,Manager")]
    public class ReviewBatchInvoicesModel(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager,
        ILogger<ReviewBatchInvoicesModel> logger) : PageModel
    {
        private readonly ApplicationDbContext _context = context;
        private readonly ILogger<ReviewBatchInvoicesModel> _logger = logger;
        private readonly UserManager<IdentityUser> _userManager = userManager;
        public string? AmountDueSort { get; set; }
        public List<BatchSelectItem> AvailableDraftBatches { get; set; } = [];
        public string? BatchDescription { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? BatchId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? CurrentSort { get; set; }

        public string? DescriptionSort { get; set; }
        public List<InvoiceViewModel> DraftInvoices { get; set; } = [];
        public string? DueDateSort { get; set; }
        public string? EmailSort { get; set; }
        public string? InvoiceDateSort { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? ReturnedFromUserId { get; set; }

        [DataType(DataType.Currency)]
        public decimal TotalInvoiceAmount { get; set; }

        public int TotalInvoiceCount { get; set; }
        public string? UserSort { get; set; }

        public async Task<IActionResult> OnGetAsync(string? batchId) // batchId comes from route/query or dropdown selection
        {
            _logger.LogInformation("ReviewBatchInvoices.OnGetAsync called. Initial batchId from route/query: {BatchIdParm}", batchId); // Changed param name for clarity
            if (!string.IsNullOrEmpty(ReturnedFromUserId))
            {
                _logger.LogInformation("ReviewBatchInvoices.OnGetAsync: ReturnedFromUserId = {ReturnedUserId}", ReturnedFromUserId);
                // Placeholder for potential future use (e.g., highlighting)
                // TempData["HighlightUserId"] = ReturnedFromUserId;
            }
            // Populate the dropdown of all available draft batches
            var draftBatchSummaries = await _context.Invoices
                .Where(i => i.Status == InvoiceStatus.Draft && i.BatchID != null)
                .GroupBy(i => i.BatchID)
                .Select(g => new
                { // Anonymous type for intermediate projection
                    BatchId = g.Key!,
                    FirstInvoiceDescription = g.OrderBy(inv => inv.InvoiceID).FirstOrDefault()!.Description,
                    BatchCreateDate = g.Min(inv => inv.DateCreated),
                    InvoiceCount = g.Count()
                })
                .OrderByDescending(b => b.BatchCreateDate)
                .ToListAsync();
            AvailableDraftBatches = [.. draftBatchSummaries.Select(s => new BatchSelectItem
            {
                BatchId = s.BatchId,
                DisplayText = $"Batch {s.BatchId[^(Math.Min(4, s.BatchId.Length))..]} ({s.BatchCreateDate:yyyy-MM-dd HH:mm}) - {s.FirstInvoiceDescription} ({s.InvoiceCount} invoices)"
            })];
            _logger.LogInformation("Found {AvailableDraftBatches.Count} distinct draft batches for dropdown.", AvailableDraftBatches.Count);
            string? currentBatchIdToLoad = null;
            if (!string.IsNullOrEmpty(batchId) && AvailableDraftBatches.Any(b => b.BatchId == batchId))
            {
                currentBatchIdToLoad = batchId;
                _logger.LogInformation("Using provided batchId: {currentBatchIdToLoad}", currentBatchIdToLoad);
            }
            else if (AvailableDraftBatches.Count != 0)
            {
                currentBatchIdToLoad = AvailableDraftBatches.First().BatchId;
                _logger.LogInformation("No valid batchId provided or found, defaulting to most recent: {currentBatchIdToLoad}", currentBatchIdToLoad);
            }
            this.BatchId = currentBatchIdToLoad;
            DraftInvoices = [];
            if (string.IsNullOrEmpty(this.BatchId))
            {
                if (AvailableDraftBatches.Count == 0)
                {
                    TempData["WarningMessage"] = "No active draft batches found.";
                }
                else
                {
                    TempData["InfoMessage"] = "Select a batch from the dropdown to review.";
                }
                TotalInvoiceCount = 0;
                TotalInvoiceAmount = 0;
                BatchDescription = "N/A";
                return Page();
            }
            _logger.LogInformation("Loading details for BatchID: {this.BatchId}", this.BatchId);
            var invoicesInBatch = await _context.Invoices
                .Where(i => i.BatchID == this.BatchId && i.Status == InvoiceStatus.Draft)
                .Include(i => i.User)
                .ToListAsync();
            if (invoicesInBatch.Count == 0 && this.BatchId != null)
            {
                _logger.LogWarning("No draft invoices found for selected BatchID: {this.BatchId}. It might have been processed by another session.", this.BatchId);
                TempData["WarningMessage"] = $"No draft invoices found for Batch ID '{this.BatchId}'. It might have been recently processed or an error occurred.";
                // Clear data for display
                TotalInvoiceCount = 0;
                TotalInvoiceAmount = 0;
                BatchDescription = "N/A";
                return Page();
            }
            if (invoicesInBatch.Count != 0) BatchDescription = invoicesInBatch.First().Description;
            DraftInvoices = [.. invoicesInBatch.Select(i => {
                var userProfile = _context.UserProfile.FirstOrDefault(up => up.UserId == i.User.Id);
                return new InvoiceViewModel
                {
                    InvoiceID = i.InvoiceID,
                    UserID = i.UserID,
                    User = i.User,
                    InvoiceDate = i.InvoiceDate,
                    DueDate = i.DueDate,
                    Description = i.Description,
                    AmountDue = i.AmountDue,
                    AmountPaid = i.AmountPaid,
                    Status = i.Status,
                    Type = i.Type,
                    BatchID = i.BatchID,
                    DateCreated = i.DateCreated,
                    LastUpdated = i.LastUpdated,
                    UserName = i.User?.Email,
                    UserFullName = (userProfile != null && !string.IsNullOrWhiteSpace(userProfile.FirstName) && !string.IsNullOrWhiteSpace(userProfile.LastName))
                                   ? $"{userProfile.LastName}, {userProfile.FirstName}"
                                   : (i.User?.UserName ?? "N/A")
                };
            })];
            TotalInvoiceCount = DraftInvoices.Count;
            TotalInvoiceAmount = DraftInvoices.Sum(i => i.AmountDue);
            _logger.LogInformation("Displaying {TotalInvoiceCount} draft invoices for BatchID: {this.BatchId} with total amount {TotalInvoiceAmount:C} before sorting.", TotalInvoiceCount, this.BatchId, TotalInvoiceAmount);
            // Initialize sorting properties
            string defaultSortColumn = "user_asc";
            string activeSort = CurrentSort ?? defaultSortColumn;
            this.CurrentSort = activeSort; // Update CurrentSort to reflect the active sort
            UserSort = activeSort == "user_asc" ? "user_desc" : "user_asc";
            EmailSort = activeSort == "email_asc" ? "email_desc" : "email_asc";
            DescriptionSort = activeSort == "desc_asc" ? "desc_desc" : "desc_asc";
            AmountDueSort = activeSort == "amount_asc" ? "amount_desc" : "amount_asc";
            InvoiceDateSort = activeSort == "invdate_asc" ? "invdate_desc" : "invdate_asc";
            DueDateSort = activeSort == "duedate_asc" ? "duedate_desc" : "duedate_asc";
            _logger.LogInformation("Sorting parameters initialized. CurrentSort/ActiveSort: {ActiveSort}, UserSort: {UserSortVal}, EmailSort: {EmailSortVal}, DescriptionSort: {DescSortVal}, AmountDueSort: {AmountSortVal}, InvoiceDateSort: {InvDateSortVal}, DueDateSort: {DueDateSortVal}",
                activeSort, UserSort, EmailSort, DescriptionSort, AmountDueSort, InvoiceDateSort, DueDateSort);
            // Apply Sorting to DraftInvoices
            DraftInvoices = activeSort switch
            {
                "user_desc" => [.. DraftInvoices.OrderByDescending(i => i.UserFullName ?? string.Empty)],
                "user_asc" => [.. DraftInvoices.OrderBy(i => i.UserFullName ?? string.Empty)],
                "email_desc" => [.. DraftInvoices.OrderByDescending(i => i.UserName ?? string.Empty)],
                "email_asc" => [.. DraftInvoices.OrderBy(i => i.UserName ?? string.Empty)],
                "desc_desc" => [.. DraftInvoices.OrderByDescending(i => i.Description ?? string.Empty)],
                "desc_asc" => [.. DraftInvoices.OrderBy(i => i.Description ?? string.Empty)],
                "amount_desc" => [.. DraftInvoices.OrderByDescending(i => i.AmountDue)],
                "amount_asc" => [.. DraftInvoices.OrderBy(i => i.AmountDue)],
                "invdate_desc" => [.. DraftInvoices.OrderByDescending(i => i.InvoiceDate)],
                "invdate_asc" => [.. DraftInvoices.OrderBy(i => i.InvoiceDate)],
                "duedate_desc" => [.. DraftInvoices.OrderByDescending(i => i.DueDate)],
                "duedate_asc" => [.. DraftInvoices.OrderBy(i => i.DueDate)],
                // Default sort if activeSort doesn't match any case
                _ => [.. DraftInvoices.OrderBy(i => i.UserFullName ?? string.Empty)],
            };
            _logger.LogInformation("DraftInvoices sorted by {ActiveSort}. Final Count: {Count}", activeSort, DraftInvoices.Count);
            return Page();
        }

        public async Task<IActionResult> OnGetExportCsvAsync(string batchId)
        {
            _logger.LogInformation("OnGetExportCsvAsync called for BatchID: {BatchId}", batchId);
            if (string.IsNullOrEmpty(batchId))
            {
                _logger.LogWarning("OnGetExportCsvAsync: BatchId is null or empty.");
                TempData["ErrorMessage"] = "Batch ID is required to export.";
                return RedirectToPage(new { this.BatchId, this.CurrentSort }); // Redirect to current page view
            }
            var invoicesInBatch = await _context.Invoices
                .Where(i => i.BatchID == batchId && i.Status == InvoiceStatus.Draft)
                .Include(i => i.User) // Include User for email and fallback username
                .ToListAsync();
            if (invoicesInBatch == null || invoicesInBatch.Count == 0)
            {
                _logger.LogWarning("OnGetExportCsvAsync: No draft invoices found for BatchID: {BatchId}", batchId);
                TempData["WarningMessage"] = $"No draft invoices found for Batch ID '{batchId}' to export.";
                return RedirectToPage(new { BatchId = batchId, this.CurrentSort });
            }
            // Efficiently get UserProfiles for all users in the batch
            var userIdsInBatch = invoicesInBatch.Select(i => i.UserID).Distinct().ToList();
            var userProfiles = await _context.UserProfile
                                        .Where(up => userIdsInBatch.Contains(up.UserId))
                                        .ToDictionaryAsync(up => up.UserId);
            var invoicesToExportDetails = invoicesInBatch.Select(i =>
            {
                userProfiles.TryGetValue(i.UserID, out UserProfile? profile);
                string fullName = (profile != null && !string.IsNullOrWhiteSpace(profile.FirstName) && !string.IsNullOrWhiteSpace(profile.LastName))
                                   ? $"{profile.LastName}, {profile.FirstName}"
                                   : (profile?.LastName ?? profile?.FirstName ?? i.User?.UserName ?? "N/A");
                return new
                {
                    i.InvoiceID,
                    UserFullName = fullName,
                    UserName = i.User?.Email ?? "N/A", // UserName here is effectively Email
                    i.Description,
                    i.AmountDue,
                    i.AmountPaid,
                    i.InvoiceDate,
                    i.DueDate,
                    Status = i.Status.ToString(), // Convert enums to string for CSV
                    Type = i.Type.ToString(),     // Convert enums to string for CSV
                    i.BatchID
                };
            }).ToList();
            var sb = new StringBuilder();
            // Header row
            sb.AppendLine("\"Invoice ID\",\"User Full Name\",\"User Email\",\"Description\",\"Amount Due\",\"Amount Paid\",\"Invoice Date\",\"Due Date\",\"Status\",\"Type\",\"Batch ID\"");
            foreach (var invoice in invoicesToExportDetails)
            {
                sb.AppendFormat("\"{0}\",", invoice.InvoiceID);
                sb.AppendFormat("\"{0}\",", EscapeCsvField(invoice.UserFullName));
                sb.AppendFormat("\"{0}\",", EscapeCsvField(invoice.UserName));
                sb.AppendFormat("\"{0}\",", EscapeCsvField(invoice.Description));
                sb.AppendFormat("{0},", invoice.AmountDue.ToString("F2")); // Currency
                sb.AppendFormat("{0},", invoice.AmountPaid.ToString("F2")); // Currency
                sb.AppendFormat("\"{0}\",", invoice.InvoiceDate.ToString("yyyy-MM-dd"));
                sb.AppendFormat("\"{0}\",", invoice.DueDate.ToString("yyyy-MM-dd"));
                sb.AppendFormat("\"{0}\",", EscapeCsvField(invoice.Status.ToString()));
                sb.AppendFormat("\"{0}\",", EscapeCsvField(invoice.Type.ToString()));
                sb.AppendLine($"\"{EscapeCsvField(invoice.BatchID)}\"");
            }
            byte[] csvBytes = Encoding.UTF8.GetBytes(sb.ToString());
            _logger.LogInformation("CSV file generated for BatchID: {BatchId}. Byte length: {Length}", batchId, csvBytes.Length);
            return File(csvBytes, "text/csv", $"batch_{batchId}_invoices_{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
        }

        public async Task<IActionResult> OnPostCancelBatchAsync()
        {
            _logger.LogInformation("OnPostCancelBatchAsync called for BatchID: {BatchId}", BatchId);
            if (string.IsNullOrEmpty(BatchId))
            {
                TempData["ErrorMessage"] = "Batch ID is missing. Cannot cancel.";
                return RedirectToPage("./CreateBatchInvoices");
            }
            var draftInvoicesInBatch = await _context.Invoices
                .Where(i => i.BatchID == BatchId && i.Status == InvoiceStatus.Draft)
                .ToListAsync();
            if (draftInvoicesInBatch.Count == 0)
            {
                TempData["WarningMessage"] = $"No draft invoices found for Batch ID '{BatchId}' to cancel.";
                return RedirectToPage("./CreateBatchInvoices");
            }
            _context.Invoices.RemoveRange(draftInvoicesInBatch);
            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Successfully cancelled/deleted {draftInvoicesInBatch.Count} draft invoices for BatchID: {BatchId}.", draftInvoicesInBatch.Count, BatchId);
                TempData["StatusMessage"] = $"Draft batch '{BatchId}' with {draftInvoicesInBatch.Count} invoices has been cancelled.";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error cancelling batch {BatchId}.", BatchId);
                TempData["ErrorMessage"] = $"Error cancelling batch '{BatchId}'. See logs.";
            }
            return RedirectToPage("./CreateBatchInvoices");
        }

        public async Task<IActionResult> OnPostFinalizeBatchAsync()
        {
            _logger.LogInformation("OnPostFinalizeBatchAsync called for BatchID: {BatchId}", BatchId);
            if (string.IsNullOrEmpty(BatchId))
            {
                TempData["ErrorMessage"] = "Batch ID is missing. Cannot finalize.";
                return RedirectToPage("./CreateBatchInvoices");
            }
            var draftInvoicesInBatch = await _context.Invoices
                .Where(i => i.BatchID == BatchId && i.Status == InvoiceStatus.Draft)
                .ToListAsync();
            if (draftInvoicesInBatch.Count == 0)
            {
                TempData["WarningMessage"] = $"No draft invoices found for Batch ID '{BatchId}' to finalize.";
                return RedirectToPage("./CreateBatchInvoices");
            }
            int finalizedCount = 0;
            foreach (var invoice in draftInvoicesInBatch)
            {
                invoice.Status = InvoiceStatus.Due; // Change status from Draft to Due
                invoice.LastUpdated = DateTime.UtcNow;
                decimal remainingAmountDueOnInvoice = invoice.AmountDue - invoice.AmountPaid;
                if (remainingAmountDueOnInvoice > 0)
                {
                    var availableCredits = await _context.UserCredits
                        .Where(uc => uc.UserID == invoice.UserID && !uc.IsApplied && uc.Amount > 0)
                        .OrderBy(uc => uc.CreditDate)
                        .ToListAsync();
                    if (availableCredits.Count != 0)
                    {
                        foreach (var credit in availableCredits)
                        {
                            if (remainingAmountDueOnInvoice <= 0) break;
                            decimal originalCreditAmountBeforeThisApplication = credit.Amount; // Store original amount for notes
                            decimal amountToApplyFromThisCredit = Math.Min(credit.Amount, remainingAmountDueOnInvoice);
                            // Update invoice
                            invoice.AmountPaid += amountToApplyFromThisCredit;
                            remainingAmountDueOnInvoice -= amountToApplyFromThisCredit;
                            // Update credit
                            credit.Amount -= amountToApplyFromThisCredit;
                            credit.AppliedToInvoiceID = invoice.InvoiceID;
                            credit.LastUpdated = DateTime.UtcNow;
                            credit.AppliedDate = DateTime.UtcNow;
                            if (credit.Amount <= 0)
                            {
                                credit.IsApplied = true;
                                credit.Amount = 0; // Ensure it doesn't go negative
                                credit.ApplicationNotes = $"Fully applied to INV-{invoice.InvoiceID:D5} (Batch: {BatchId}). Original credit amount was {originalCreditAmountBeforeThisApplication:C}. No balance remaining.";
                            }
                            else
                            {
                                credit.IsApplied = false; // Explicitly keep it false as it's partially applied
                                credit.ApplicationNotes = $"Partially applied {amountToApplyFromThisCredit:C} to INV-{invoice.InvoiceID:D5} (Batch: {BatchId}). Original credit amount was {originalCreditAmountBeforeThisApplication:C}. Remaining balance: {credit.Amount:C}.";
                            }
                            _context.UserCredits.Update(credit);
                        }
                    }
                }
                // After iterating through credits (or if no credits were available/applicable), update invoice status
                if (invoice.AmountPaid >= invoice.AmountDue)
                {
                    invoice.Status = InvoiceStatus.Paid;
                    invoice.AmountPaid = invoice.AmountDue; // Cap at AmountDue
                }
                else if (invoice.Status == InvoiceStatus.Due && invoice.DueDate < DateTime.UtcNow.Date) // Check if it's currently Due and past due date
                {
                    invoice.Status = InvoiceStatus.Overdue;
                }
                _context.Invoices.Update(invoice);
                finalizedCount++;
            }
            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Successfully finalized {finalizedCount} invoices for BatchID: {BatchId}.", finalizedCount, BatchId);
                TempData["StatusMessage"] = $"{finalizedCount} invoices in batch '{BatchId}' have been finalized and are now Due (or Paid if credits applied).";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error finalizing batch {BatchId}.", BatchId);
                TempData["ErrorMessage"] = $"Error finalizing batch '{BatchId}'. See logs.";
            }
            return RedirectToPage("./AdminBalances");
        }

        private static string EscapeCsvField(string? field)
        {
            if (string.IsNullOrEmpty(field))
                return string.Empty;
            // Replace double quotes with two double quotes
            return field.Replace("\"", "\"\"");
        }

        public class BatchSelectItem
        {
            public string BatchId { get; set; } = string.Empty;
            public string DisplayText { get; set; } = string.Empty;
        }

        public class InvoiceViewModel : Invoice
        {
            public string? UserFullName { get; set; }
            public string? UserName { get; set; }
        }
    }
}