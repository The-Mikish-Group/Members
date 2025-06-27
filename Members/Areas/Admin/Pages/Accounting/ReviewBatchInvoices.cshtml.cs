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
            _logger.LogInformation("ReviewBatchInvoices.OnGetAsync called. Received batchId parameter from route/query: '{BatchIdParam}'", batchId);

            if (!string.IsNullOrEmpty(ReturnedFromUserId))
            {
                _logger.LogInformation("ReviewBatchInvoices.OnGetAsync: ReturnedFromUserId = {ReturnedUserId}", ReturnedFromUserId);
            }

            // 1. Populate AvailableDraftBatches first. This list is needed to validate the batchId parameter.
            var draftBatchSummaries = await _context.Invoices
                .Where(i => i.Status == InvoiceStatus.Draft && i.BatchID != null && i.BatchID != "") // Ensure BatchID is not null or empty
                .GroupBy(i => i.BatchID)
                .Select(g => new
                {
                    BatchId = g.Key!,
                    BatchCreateDate = g.Min(inv => inv.DateCreated),
                    InvoiceCount = g.Count()
                })
                .OrderByDescending(b => b.BatchCreateDate)
                .ToListAsync();

            AvailableDraftBatches = [.. draftBatchSummaries.Select(s => new BatchSelectItem
            {
                BatchId = s.BatchId,
                DisplayText = $"Batch {s.BatchId[^(Math.Min(4, s.BatchId.Length))..]} ({s.BatchCreateDate:yyyy-MM-dd HH:mm}) ({s.InvoiceCount} invoices)"
            })]; // Using ToList() for clarity, though .. is fine.
            _logger.LogInformation("Populated AvailableDraftBatches. Count: {AvailableDraftBatchesCount}. Batch IDs: [{AvailableBatchIds}]",
                AvailableDraftBatches.Count, string.Join(", ", AvailableDraftBatches.Select(b => b.BatchId)));

            // 2. Determine the effective BatchId for this request.
            //    Priority: batchId parameter from query string, if valid. Otherwise, default.
            string? effectiveBatchIdToLoad = null;
            bool isQueryBatchIdValid = !string.IsNullOrEmpty(batchId) && AvailableDraftBatches.Any(b => b.BatchId == batchId);

            if (isQueryBatchIdValid)
            {
                effectiveBatchIdToLoad = batchId;
                _logger.LogInformation("Valid batchId ('{EffectiveBatchIdToLoad}') provided via query parameter. This will be used.", effectiveBatchIdToLoad);
            }
            else
            {
                if (!string.IsNullOrEmpty(batchId))
                {
                    _logger.LogWarning("batchId ('{BatchIdParam}') was provided via query, but it's not found in AvailableDraftBatches or is invalid. Will attempt to default.", batchId);
                }

                if (AvailableDraftBatches.Count != 0)
                {
                    effectiveBatchIdToLoad = AvailableDraftBatches.First().BatchId;
                    _logger.LogInformation("Defaulting to the most recent available batchId: '{EffectiveBatchIdToLoad}'.", effectiveBatchIdToLoad);
                }
                else
                {
                    _logger.LogInformation("No batchId provided via query and no available draft batches found. Effective batchId will be null.");
                }
            }

            // 3. Set the PageModel's BatchId property. This property is used by asp-for and for loading data.
            this.BatchId = effectiveBatchIdToLoad;
            _logger.LogInformation("Final this.BatchId (PageModel property) set to: '{PageModelBatchId}'", this.BatchId);

            // 4. Load data based on this.BatchId
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

            // Final pre-render logging
            _logger.LogInformation("Exiting OnGetAsync. Final PageModel.BatchId: '{PageModelBatchId}'. AvailableBatchIds for dropdown: [{AvailableBatchIds}]",
                this.BatchId, string.Join(", ", AvailableDraftBatches.Select(b => b.BatchId)));

            return Page();
        }

        public async Task<IActionResult> OnGetExportCsvAsync(string batchId)
        {
            _logger.LogInformation("[ReviewBatchInvoices Export CSV] Handler started. Received batchId: '{BatchId}'", batchId);

            if (string.IsNullOrEmpty(batchId))
            {
                _logger.LogWarning("[ReviewBatchInvoices Export CSV] BatchId is null or empty. Cannot export.");
                TempData["ErrorMessage"] = "Batch ID is required to export.";
                // Redirect to the current page view, preserving BatchId if it was part of the model, or a general page if not.
                return RedirectToPage(new { this.BatchId, this.CurrentSort });
            }

            try
            {
                var invoicesInBatch = await _context.Invoices
                    .Where(i => i.BatchID == batchId && i.Status == InvoiceStatus.Draft)
                    .Include(i => i.User) // Include User for email and fallback username
                    .ToListAsync();

                _logger.LogInformation("[ReviewBatchInvoices Export CSV] Found {InvoiceCount} draft invoices for BatchID: '{BatchId}'.", invoicesInBatch.Count, batchId);

                if (invoicesInBatch.Count == 0) // Check specifically for empty list after query
                {
                    _logger.LogWarning("[ReviewBatchInvoices Export CSV] No draft invoices found for BatchID: '{BatchId}' to export.", batchId);
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
                        Status = i.Status.ToString(),
                        Type = i.Type.ToString(),
                        i.BatchID
                    };
                }).ToList();
                _logger.LogInformation("[ReviewBatchInvoices Export CSV] Processed {ExportCount} invoices for CSV details.", invoicesToExportDetails.Count);


                var sb = new StringBuilder();
                sb.AppendLine("\"Invoice ID\",\"User Full Name\",\"User Email\",\"Description\",\"Amount Due\",\"Amount Paid\",\"Invoice Date\",\"Due Date\",\"Status\",\"Type\",\"Batch ID\"");
                foreach (var invoice in invoicesToExportDetails)
                {
                    sb.AppendFormat("\"{0}\",", invoice.InvoiceID);
                    sb.AppendFormat("\"{0}\",", EscapeCsvField(invoice.UserFullName));
                    sb.AppendFormat("\"{0}\",", EscapeCsvField(invoice.UserName));
                    sb.AppendFormat("\"{0}\",", EscapeCsvField(invoice.Description));
                    sb.AppendFormat("{0},", invoice.AmountDue.ToString("F2"));
                    sb.AppendFormat("{0},", invoice.AmountPaid.ToString("F2"));
                    sb.AppendFormat("\"{0}\",", invoice.InvoiceDate.ToString("yyyy-MM-dd"));
                    sb.AppendFormat("\"{0}\",", invoice.DueDate.ToString("yyyy-MM-dd"));
                    sb.AppendFormat("\"{0}\",", EscapeCsvField(invoice.Status));
                    sb.AppendFormat("\"{0}\",", EscapeCsvField(invoice.Type));
                    sb.AppendLine($"\"{EscapeCsvField(invoice.BatchID)}\"");
                }

                byte[] csvBytes = Encoding.UTF8.GetBytes(sb.ToString());
                string fileName = $"batch_{batchId}_invoices_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
                _logger.LogInformation("[ReviewBatchInvoices Export CSV] CSV string generated for BatchID: '{BatchId}'. Byte length: {Length}. Filename: {FileName}", batchId, csvBytes.Length, fileName);

                if (csvBytes.Length <= sb.ToString().Split(Environment.NewLine)[0].Length + 2 && invoicesToExportDetails.Count == 0) // Check if only header or empty
                {
                    _logger.LogWarning("[ReviewBatchInvoices Export CSV] CSV is empty or contains only header for BatchID '{BatchId}'. This might not trigger a download.", batchId);
                }

                return File(csvBytes, "text/csv", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReviewBatchInvoices Export CSV] CRITICAL ERROR during CSV export for BatchID: '{BatchId}'.", batchId);
                TempData["ErrorMessage"] = $"A critical error occurred while generating the CSV export for Batch ID '{batchId}'. Please check the logs.";
                return RedirectToPage(new { BatchId = batchId, this.CurrentSort });
            }
        }

        public async Task<IActionResult> OnPostCancelBatchAsync()
        {
            _logger.LogInformation("OnPostCancelBatchAsync called for BatchID: {BatchId}", BatchId);
            if (string.IsNullOrEmpty(BatchId))
            {
                TempData["ErrorMessage"] = "Batch ID is missing. Cannot cancel.";
                return RedirectToPage("./ReviewBatchInvoices");
            }
            var draftInvoicesInBatch = await _context.Invoices
                .Where(i => i.BatchID == BatchId && i.Status == InvoiceStatus.Draft)
                .ToListAsync();
            if (draftInvoicesInBatch.Count == 0)
            {
                TempData["WarningMessage"] = $"No draft invoices found for Batch ID '{BatchId}' to cancel.";
                return RedirectToPage("./ReviewBatchInvoices");
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
                return RedirectToPage("./ReviewBatchInvoices");
            }
            var draftInvoicesInBatch = await _context.Invoices
                .Where(i => i.BatchID == BatchId && i.Status == InvoiceStatus.Draft)
                .ToListAsync();
            if (draftInvoicesInBatch.Count == 0)
            {
                TempData["WarningMessage"] = $"No draft invoices found for Batch ID '{BatchId}' to finalize.";
                return RedirectToPage("./ReviewBatchInvoices");
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

                // START: Apply remaining credit to other due invoices for the user
                var remainingUserCredits = await _context.UserCredits
                    .Where(uc => uc.UserID == invoice.UserID && !uc.IsApplied && uc.Amount > 0)
                    .OrderBy(uc => uc.CreditDate)
                    .ToListAsync();

                if (remainingUserCredits.Count != 0)
                {
                    var otherDueInvoices = await _context.Invoices
                        .Where(i => i.UserID == invoice.UserID &&
                                     i.InvoiceID != invoice.InvoiceID && // Exclude the current invoice
                                     (i.Status == InvoiceStatus.Due || i.Status == InvoiceStatus.Overdue) &&
                                     i.AmountPaid < i.AmountDue)
                        .OrderBy(i => i.DueDate) // Oldest due invoices first
                        .ToListAsync();

                    if (otherDueInvoices.Count != 0)
                    {
                        _logger.LogInformation("User {UserId} has {CreditCount} remaining credit(s). Attempting to apply to {InvoiceCount} other due/overdue invoices.", invoice.UserID, remainingUserCredits.Count, otherDueInvoices.Count);

                        foreach (var otherInvoice in otherDueInvoices)
                        {
                            if (!remainingUserCredits.Any(c => c.Amount > 0)) break; // No more credit left to apply

                            decimal remainingAmountDueOnOtherInvoice = otherInvoice.AmountDue - otherInvoice.AmountPaid;

                            foreach (var credit in remainingUserCredits.Where(c => c.Amount > 0).ToList()) // Iterate over a copy in case of modification
                            {
                                if (remainingAmountDueOnOtherInvoice <= 0) break; // This 'otherInvoice' is now fully paid

                                decimal originalCreditAmountForNotes = credit.Amount; // For logging/notes
                                decimal amountToApplyFromThisCredit = Math.Min(credit.Amount, remainingAmountDueOnOtherInvoice);

                                // Update otherInvoice
                                otherInvoice.AmountPaid += amountToApplyFromThisCredit;
                                remainingAmountDueOnOtherInvoice -= amountToApplyFromThisCredit;
                                otherInvoice.LastUpdated = DateTime.UtcNow;

                                // Update credit
                                credit.Amount -= amountToApplyFromThisCredit;
                                // Note: AppliedToInvoiceID is tricky here if a single credit applies to multiple.
                                // For simplicity, we'll update it to the *last* invoice it was applied to, or append.
                                // A more robust solution might involve a linking table CreditApplication(CreditID, InvoiceID, AmountApplied)
                                credit.AppliedToInvoiceID = otherInvoice.InvoiceID; // Mark this credit as (at least partially) applied to this other invoice
                                credit.LastUpdated = DateTime.UtcNow;
                                credit.AppliedDate = DateTime.UtcNow; // Update applied date

                                string noteSuffix = $"Applied {amountToApplyFromThisCredit:C} to INV-{otherInvoice.InvoiceID:D5} (secondary application during batch finalization). Original credit amount was {originalCreditAmountForNotes:C}.";

                                if (credit.Amount <= 0)
                                {
                                    credit.IsApplied = true;
                                    credit.Amount = 0; // Ensure it doesn't go negative
                                    credit.ApplicationNotes = $"{noteSuffix} No balance remaining on this credit.";
                                    _logger.LogInformation("Credit ID {CreditId} fully applied to INV-{InvoiceId}. Amount: {AmountApplied}", credit.UserCreditID, otherInvoice.InvoiceID, amountToApplyFromThisCredit);
                                }
                                else
                                {
                                    credit.IsApplied = false; // Still has balance
                                    credit.ApplicationNotes = $"{noteSuffix} Remaining balance on this credit: {credit.Amount:C}.";
                                    _logger.LogInformation("Credit ID {CreditId} partially applied to INV-{InvoiceId}. Amount: {AmountApplied}. Remaining on credit: {CreditRemaining}", credit.UserCreditID, otherInvoice.InvoiceID, amountToApplyFromThisCredit, credit.Amount);
                                }
                                _context.UserCredits.Update(credit);
                            }

                            // Update status of otherInvoice
                            if (otherInvoice.AmountPaid >= otherInvoice.AmountDue)
                            {
                                otherInvoice.Status = InvoiceStatus.Paid;
                                otherInvoice.AmountPaid = otherInvoice.AmountDue; // Cap at AmountDue
                            }
                            else if (otherInvoice.Status == InvoiceStatus.Due && otherInvoice.DueDate < DateTime.UtcNow.Date)
                            {
                                otherInvoice.Status = InvoiceStatus.Overdue;
                            }
                            _context.Invoices.Update(otherInvoice);
                        }
                    }
                }
                // END: Apply remaining credit to other due invoices for the user
            }
            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Successfully finalized {finalizedCount} invoices for BatchID: {BatchId}. Additional credit applications may have occurred.", finalizedCount, BatchId);
                TempData["StatusMessage"] = $"{finalizedCount} invoices in batch '{BatchId}' have been finalized. Credits were applied where applicable, including to other outstanding invoices for users with balances.";
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