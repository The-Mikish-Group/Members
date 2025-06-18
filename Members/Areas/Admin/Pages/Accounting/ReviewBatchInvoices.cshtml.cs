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
using System.Threading.Tasks;
namespace Members.Areas.Admin.Pages.Accounting
{
    [Authorize(Roles = "Admin,Manager")]
    public class ReviewBatchInvoicesModel(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager,
        ILogger<ReviewBatchInvoicesModel> logger) : PageModel
    {
        public class BatchSelectItem        
        {
        public string BatchId { get; set; } = string.Empty;
        public string DisplayText { get; set; } = string.Empty;
        }
        public List<BatchSelectItem> AvailableDraftBatches { get; set; } = [];
        private readonly ApplicationDbContext _context = context;
        private readonly UserManager<IdentityUser> _userManager = userManager; // To get user names for display
        private readonly ILogger<ReviewBatchInvoicesModel> _logger = logger;
        public List<InvoiceViewModel> DraftInvoices { get; set; } = [];
        [BindProperty(SupportsGet = true)] // To capture batchId from route and for post handlers
        public string? BatchId { get; set; }
        [BindProperty(SupportsGet = true)]
        public string? ReturnedFromUserId { get; set; }
        public int TotalInvoiceCount { get; set; }
        [DataType(DataType.Currency)]
        public decimal TotalInvoiceAmount { get; set; }
        public string? BatchDescription { get; set; }
        // Properties for Sort State
        [BindProperty(SupportsGet = true)]
        public string? CurrentSort { get; set; } // Captures current sort order from query string
        public string? UserSort { get; set; }
        public string? EmailSort { get; set; }
        public string? DescriptionSort { get; set; }
        public string? AmountDueSort { get; set; }
        public string? InvoiceDateSort { get; set; }
        public string? DueDateSort { get; set; }
        // ViewModel to include user's name along with invoice details
        public class InvoiceViewModel : Invoice
        {
            public string? UserName { get; set; }
            public string? UserFullName { get; set; }
        }
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
                currentBatchIdToLoad = batchId; // Use batchId from parameter if valid and exists
                _logger.LogInformation("Using provided batchId: {currentBatchIdToLoad}", currentBatchIdToLoad);
            }
            else if (AvailableDraftBatches.Count != 0)
            {
                currentBatchIdToLoad = AvailableDraftBatches.First().BatchId; // Default to the most recent one
                _logger.LogInformation("No valid batchId provided or found, defaulting to most recent: {currentBatchIdToLoad}", currentBatchIdToLoad);
            }
            this.BatchId = currentBatchIdToLoad; // Set the PageModel's BatchId property
            DraftInvoices = []; // Clear previous
            if (string.IsNullOrEmpty(this.BatchId))
            {
                if (AvailableDraftBatches.Count == 0) // No draft batches exist at all
                {
                    TempData["WarningMessage"] = "No active draft batches found.";
                }
                else // Draft batches exist, but none selected to load (e.g. initial visit without batchId)
                {
                    TempData["InfoMessage"] = "Select a batch from the dropdown to review.";
                }
                // Allow page to load empty if no specific batch is to be loaded
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
            if (invoicesInBatch.Count == 0 && this.BatchId != null) // Check this.BatchId too
            {
                _logger.LogWarning("No draft invoices found for selected BatchID: {this.BatchId}. It might have been processed by another session.", this.BatchId);
                TempData["WarningMessage"] = $"No draft invoices found for Batch ID '{this.BatchId}'. It might have been recently processed or an error occurred.";
                // Clear data for display
                TotalInvoiceCount = 0;
                TotalInvoiceAmount = 0;
                BatchDescription = "N/A";
                // Optionally redirect or just show empty state with the message
                return Page(); // Show the page, TempData message will appear
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
        public async Task<IActionResult> OnPostFinalizeBatchAsync() // Removed batchId param, using bound BatchId
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
                // --- OPTIONAL: Auto-apply available credits NOW when finalizing ---
                // (This is the logic from AddInvoice.OnPostAsync, adapted for an existing invoice)
                decimal remainingAmountDueOnInvoice = invoice.AmountDue - invoice.AmountPaid; // Should be full AmountDue if draft
                // string appliedCreditsSummary = ""; // For individual invoice logging if needed
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
                            decimal amountToApplyFromThisCredit;
                            if (credit.Amount >= remainingAmountDueOnInvoice)
                            {
                                amountToApplyFromThisCredit = remainingAmountDueOnInvoice;
                                credit.IsApplied = true;
                                credit.ApplicationNotes = $"Fully used to auto-pay finalized invoice INV-{invoice.InvoiceID:D5} (Batch: {BatchId}). Original credit: {credit.Amount:C}.";
                            }
                            else
                            {
                                amountToApplyFromThisCredit = credit.Amount;
                                credit.IsApplied = true;
                                credit.ApplicationNotes = $"Fully applied to finalized invoice INV-{invoice.InvoiceID:D5} (Batch: {BatchId}).";
                            }
                            credit.AppliedDate = DateTime.UtcNow;
                            credit.AppliedToInvoiceID = invoice.InvoiceID;
                            _context.UserCredits.Update(credit);
                            invoice.AmountPaid += amountToApplyFromThisCredit;
                            remainingAmountDueOnInvoice -= amountToApplyFromThisCredit;
                        }
                        if (invoice.AmountPaid >= invoice.AmountDue) { invoice.Status = InvoiceStatus.Paid; invoice.AmountPaid = invoice.AmountDue; }
                        else if (invoice.DueDate < DateTime.Today.AddDays(-1)) { invoice.Status = InvoiceStatus.Overdue; } // If already past due
                    }
                }
                // --- END CREDIT APPLICATION ---
                _context.Invoices.Update(invoice); // Mark invoice for update
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
            return RedirectToPage("./CreateBatchInvoices"); // Or to an admin dashboard or invoice list
        }
        public async Task<IActionResult> OnPostCancelBatchAsync() // Removed batchId param, using bound BatchId
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
            _context.Invoices.RemoveRange(draftInvoicesInBatch); // Or mark as Cancelled: foreach(var inv in draftInvoicesInBatch) { inv.Status = InvoiceStatus.Cancelled; }
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
            return RedirectToPage("./CreateBatchInvoices"); // Or an admin dashboard
        }
    }
}