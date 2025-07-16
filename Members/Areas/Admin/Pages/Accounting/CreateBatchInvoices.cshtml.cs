using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
namespace Members.Areas.Admin.Pages.Accounting
{
    [Authorize(Roles = "Admin,Manager")]
    public class CreateBatchInvoicesModel(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager,
        ILogger<CreateBatchInvoicesModel> logger) : PageModel
    {
        private readonly ApplicationDbContext _context = context;
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly ILogger<CreateBatchInvoicesModel> _logger = logger;
        [BindProperty]
        public InputModel Input { get; set; } = new InputModel();
        public int ActionableBillableAssetsCount { get; set; }
        public class InputModel
        {
            [Required]
            [StringLength(150, MinimumLength = 5)]
            [Display(Name = "Batch Description:")]
            public string Description { get; set; } = string.Empty;
            // AmountDue removed from InputModel
            [Required]
            [DataType(DataType.Date)]
            [Display(Name = "Invoice Date:")]
            public DateTime InvoiceDate { get; set; } = DateTime.Today;
            [Required]
            [DataType(DataType.Date)]
            [Display(Name = "Due Date:")]
            public DateTime DueDate { get; set; } = DateTime.Today.AddDays(30);
        }
        public async Task OnGetAsync()
        {
            _logger.LogInformation("CreateBatchInvoices.OnGetAsync called.");
            ActionableBillableAssetsCount = await _context.BillableAssets
                                             .CountAsync(ba => ba.UserID != null && ba.UserID != "");
            _logger.LogInformation("Found {Count} actionable billable assets (with assigned users).", ActionableBillableAssetsCount);
            // Default to creating assessments for the first of next month
            DateTime firstOfNextMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1);
            Input.Description = $"Monthly Assessment {firstOfNextMonth:MMMM yyyy}";
            Input.InvoiceDate = firstOfNextMonth;
            Input.DueDate = firstOfNextMonth; // Payable on the 1st, in advance
                                              // Input.AmountDue can be left for the admin to fill in, or you could have a system setting for default monthly dues.
        }
        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                await OnGetAsync(); // Repopulate counts for display
                return Page();
            }
            _logger.LogInformation("Attempting to create batch invoices for description: {Description}", Input.Description);
            var billableAssetsToInvoice = await _context.BillableAssets
                .Where(ba => ba.UserID != null && ba.UserID != "")
                .ToListAsync();
            if (billableAssetsToInvoice.Count == 0)
            {
                ModelState.AddModelError(string.Empty, "No billable assets found with assigned billing contacts. Cannot create batch.");
                _logger.LogWarning("CreateBatchInvoices: No billable assets with assigned users found.");
                await OnGetAsync(); // Repopulate counts
                return Page();
            }
            string newBatchId = $"BATCH-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..4]}";
            // Log message will be updated after loop when total amount is known.
            int invoicesCreatedCount = 0;
            decimal batchTotalAmount = 0; // Initialize batch total
            foreach (var asset in billableAssetsToInvoice)
            {
                var invoice = new Invoice
                {
                    UserID = asset.UserID!,
                    InvoiceDate = Input.InvoiceDate,
                    DueDate = Input.DueDate,
                    Description = $"{Input.Description} - Plot: {asset.PlotID}",
                    AmountDue = asset.AssessmentFee, // Use asset's specific fee
                    AmountPaid = 0,
                    Status = InvoiceStatus.Draft,
                    Type = InvoiceType.Dues,      // Assuming these are Dues/Assessments
                    BatchID = newBatchId,
                    DateCreated = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                };
                _context.Invoices.Add(invoice);
                invoicesCreatedCount++;
                batchTotalAmount += asset.AssessmentFee; // Accumulate total amount
            }
            _logger.LogInformation("Generated BatchID: {BatchID} for {BillableAssetsCount} billable assets, total amount {BatchTotalAmountC}.", newBatchId, billableAssetsToInvoice.Count, batchTotalAmount.ToString("C"));
            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Successfully created {InvoicesCreatedCount} draft invoices (totaling {BatchTotalAmountC}) for BatchID: {BatchID}.", invoicesCreatedCount, batchTotalAmount.ToString("C"), newBatchId);
                TempData["StatusMessage"] = $"Draft batch '{newBatchId}' created with {invoicesCreatedCount} invoices (for '{Input.Description}') totaling {batchTotalAmount:C}. One invoice per assigned billable asset. Please review and finalize.";
                return RedirectToPage("./ReviewBatchInvoices", new { batchId = newBatchId });
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error saving batch invoices to database for BatchID {BatchId}", newBatchId);
                ModelState.AddModelError(string.Empty, "An error occurred while saving the batch invoices. Please check logs.");
                await OnGetAsync(); // Repopulate counts
                return Page();
            }
        }
    }
}
