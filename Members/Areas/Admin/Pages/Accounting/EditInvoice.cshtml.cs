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
using System.Threading.Tasks;

namespace Members.Areas.Admin.Pages.Accounting
{
    public class EditInvoiceModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<EditInvoiceModel> _logger;

        public EditInvoiceModel(
            ApplicationDbContext context,
            UserManager<IdentityUser> userManager,
            ILogger<EditInvoiceModel> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        [BindProperty(SupportsGet = true)]
        public int InvoiceId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string ReturnUrl { get; set; }

        [BindProperty]
        public InputModel Input { get; set; }

        // For display purposes on the form, if needed
        public string? ViewedUserId { get; set; }
        public int? BatchId { get; set; }


        public class InputModel
        {
            [Required]
            [DataType(DataType.Date)]
            [Display(Name = "Due Date")]
            public DateTime DueDate { get; set; }

            [Required]
            [StringLength(200)]
            public string Description { get; set; }

            [Required]
            [DataType(DataType.Currency)]
            [Range(0.01, 1000000.00, ErrorMessage = "Amount must be greater than 0.")]
            [Display(Name = "Amount Due")]
            public decimal AmountDue { get; set; }

            [Required]
            [Display(Name = "Status")]
            public string Status { get; set; } // TODO: Consider making this an Enum: InvoiceStatus.Draft, InvoiceStatus.Due, InvoiceStatus.Paid, InvoiceStatus.Cancelled
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (string.IsNullOrEmpty(ReturnUrl))
            {
                ReturnUrl = Url.Page("./Index"); // Default return URL
            }

            var invoice = await _context.Invoices.FindAsync(InvoiceId);

            if (invoice == null)
            {
                _logger.LogWarning($"Invoice with ID {InvoiceId} not found.");
                return NotFound($"Unable to load invoice with ID {InvoiceId}.");
            }

            Input = new InputModel
            {
                DueDate = invoice.DueDate,
                Description = invoice.Description,
                AmountDue = invoice.AmountDue,
                Status = invoice.Status.ToString() // Assuming Invoice.Status is an enum
            };

            ViewedUserId = invoice.UserID;
            BatchId = invoice.BatchID;

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            if (string.IsNullOrEmpty(ReturnUrl))
            {
                ReturnUrl = Url.Page("./Index"); // Default return URL
            }

            if (!ModelState.IsValid)
            {
                // If model state is invalid, we might need to re-populate ViewedUserId and BatchId
                // if they are used in the form display when validation errors occur.
                var originalInvoiceForDisplay = await _context.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.InvoiceID == InvoiceId);
                if (originalInvoiceForDisplay != null)
                {
                    ViewedUserId = originalInvoiceForDisplay.UserID;
                    BatchId = originalInvoiceForDisplay.BatchID;
                }
                return Page();
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge(); // Or RedirectToPage("/Account/Login")
            }

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            var isManager = await _userManager.IsInRoleAsync(user, "Manager");

            if (!isAdmin && !isManager)
            {
                _logger.LogWarning($"User {user.Email} attempted to edit invoice {InvoiceId} without authorization.");
                return Forbid();
            }

            var invoiceToUpdate = await _context.Invoices.FindAsync(InvoiceId);

            if (invoiceToUpdate == null)
            {
                _logger.LogWarning($"Invoice with ID {InvoiceId} not found during POST.");
                return NotFound($"Unable to load invoice with ID {InvoiceId}.");
            }

            // Status Check
            if (invoiceToUpdate.Status != InvoiceStatus.Draft && invoiceToUpdate.Status != InvoiceStatus.Due)
            {
                _logger.LogInformation($"Attempt to edit invoice {InvoiceId} with status {invoiceToUpdate.Status} denied.");
                ModelState.AddModelError(string.Empty, $"Invoice cannot be edited because its status is '{invoiceToUpdate.Status}'. Only Draft or Due invoices can be edited.");
                // Re-populate non-input model properties for display
                ViewedUserId = invoiceToUpdate.UserID;
                BatchId = invoiceToUpdate.BatchID;
                return Page();
            }

            // Update properties
            invoiceToUpdate.DueDate = Input.DueDate;
            invoiceToUpdate.Description = Input.Description;
            invoiceToUpdate.AmountDue = Input.AmountDue;

            if (Enum.TryParse<InvoiceStatus>(Input.Status, out var newStatus))
            {
                invoiceToUpdate.Status = newStatus;
            }
            else
            {
                _logger.LogWarning($"Invalid status string '{Input.Status}' provided for invoice {InvoiceId}.");
                ModelState.AddModelError("Input.Status", "Invalid status value.");
                // Re-populate non-input model properties for display
                ViewedUserId = invoiceToUpdate.UserID;
                BatchId = invoiceToUpdate.BatchID;
                return Page();
            }

            invoiceToUpdate.LastUpdated = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Invoice {InvoiceId} updated successfully by {user.Email}.");
                TempData["StatusMessage"] = "Invoice updated successfully.";
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogError(ex, $"Concurrency error while updating invoice {InvoiceId}.");
                ModelState.AddModelError(string.Empty, "The invoice was modified by another user. Please reload and try again.");
                // Re-populate non-input model properties for display
                ViewedUserId = invoiceToUpdate.UserID; // or from a fresh fetch
                BatchId = invoiceToUpdate.BatchID;   // or from a fresh fetch
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating invoice {InvoiceId}.");
                ModelState.AddModelError(string.Empty, "An unexpected error occurred while saving the invoice.");
                // Re-populate non-input model properties for display
                ViewedUserId = invoiceToUpdate.UserID;
                BatchId = invoiceToUpdate.BatchID;
                return Page();
            }

            return LocalRedirect(ReturnUrl);
        }
    }
}
