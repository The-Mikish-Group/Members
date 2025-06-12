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
        public class InputModel
        {
            [Required]
            [StringLength(150, MinimumLength = 5)]
            [Display(Name = "Batch Description (e.g., Monthly Assessment)")]
            public string Description { get; set; } = string.Empty;
            [Required]
            [Range(0.01, 1000000.00)]
            [DataType(DataType.Currency)]
            [Display(Name = "Amount Due (per member)")]
            public decimal AmountDue { get; set; }
            [Required]
            [DataType(DataType.Date)]
            [Display(Name = "Invoice Date")]
            public DateTime InvoiceDate { get; set; } = DateTime.Today;
            [Required]
            [DataType(DataType.Date)]
            [Display(Name = "Due Date")]
            public DateTime DueDate { get; set; } = DateTime.Today.AddDays(30);
        }
        public void OnGet()
        {
            _logger.LogInformation("CreateBatchInvoices.OnGet called.");
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
                return Page();
            }
            const string logTemplate = "Attempting to create batch invoices: {Description}";
            _logger.LogInformation(logTemplate, Input.Description);
            var billingContacts = await _context.UserProfile
                .Where(up => up.IsBillingContact)
                .Include(up => up.User) // Include IdentityUser to get UserName/Email if needed for logging
                .ToListAsync();
            if (billingContacts.Count == 0)
            {
                const string warningTemplate = "CreateBatchInvoices: No BillingContacts found.";
                ModelState.AddModelError(string.Empty, "No users found designated as 'Billing Contact'. Cannot create batch.");
                _logger.LogWarning(warningTemplate);
                return Page();
            }
            string newBatchId = $"BATCH-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..4]}";
            const string batchLogTemplate = "Generated BatchID: {BatchID} for {BillingContactsCount} billing contacts.";
            _logger.LogInformation(batchLogTemplate, newBatchId, billingContacts.Count);
            int invoicesCreatedCount = 0;
            foreach (var profile in billingContacts)
            {
                var invoice = new Invoice
                {
                    UserID = profile.UserId,
                    InvoiceDate = Input.InvoiceDate,
                    DueDate = Input.DueDate,
                    Description = Input.Description, // Common description for all in batch
                    AmountDue = Input.AmountDue,     // Common amount for all in batch
                    AmountPaid = 0,
                    Status = InvoiceStatus.Draft, // Initial status for batch invoices
                    Type = InvoiceType.Dues,      // Assuming these are Dues/Assessments
                    BatchID = newBatchId,
                    DateCreated = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                };
                _context.Invoices.Add(invoice);
                invoicesCreatedCount++;
            }
            try
            {
                await _context.SaveChangesAsync();
                const string successTemplate = "Successfully created {InvoicesCreatedCount} draft invoices for BatchID: {BatchID}.";
                _logger.LogInformation(successTemplate, invoicesCreatedCount, newBatchId);
                TempData["StatusMessage"] = $"Draft batch '{newBatchId}' created with {invoicesCreatedCount} invoices for '{Input.Description}'. Please review and finalize.";
                return RedirectToPage("./ReviewBatchInvoices", new { batchId = newBatchId });
            }
            catch (DbUpdateException ex)
            {
                const string errorTemplate = "Error saving batch invoices to database.";
                _logger.LogError(ex, errorTemplate);
                ModelState.AddModelError(string.Empty, "An error occurred while saving the batch invoices. Please check logs.");
                return Page();
            }
        }
    }
}
