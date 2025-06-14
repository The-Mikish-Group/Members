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
using System.Diagnostics.Eventing.Reader;
using System.Threading.Tasks;

namespace Members.Areas.Admin.Pages.Accounting
{
    public class EditInvoiceModel(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager,
        ILogger<EditInvoiceModel> logger) : PageModel
    {
        private readonly ApplicationDbContext _context = context;
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly ILogger<EditInvoiceModel> _logger = logger;

        [BindProperty(SupportsGet = true)]
        public int InvoiceId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }

        [BindProperty]
        public InputModel? Input { get; set; }

        // For display purposes on the form, if needed
        public string? ViewedUserId { get; set; }
        public string? BatchId { get; set; }


        public class InputModel
        {
            [Required]
            [DataType(DataType.Date)]
            [Display(Name = "Due Date")]
            public DateTime DueDate { get; set; }

            [Required]
            [StringLength(200)]
            public string Description { get; set; } = string.Empty;

            [Required]
            [DataType(DataType.Currency)]
            [Range(0.01, 1000000.00, ErrorMessage = "Amount must be greater than 0.")]
            [Display(Name = "Amount Due")]
            public decimal AmountDue { get; set; }

            [Required]
            [Display(Name = "Status")]
            public string Status { get; set; } = string.Empty;
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
                _logger.LogWarning("Invoice with ID {InvoiceId} not found.", InvoiceId);
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

            if (Input == null)
            {
                _logger.LogWarning("Input model was null in OnPostAsync after ModelState validation passed. This should not happen.");
                var originalInvoiceForDisplay = await _context.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.InvoiceID == InvoiceId);
                if (originalInvoiceForDisplay != null)
                {
                    ViewedUserId = originalInvoiceForDisplay.UserID;
                    BatchId = originalInvoiceForDisplay.BatchID;
                }
                return Page();
            }

            if (string.IsNullOrEmpty(ReturnUrl))
            {
                ReturnUrl = Url.Page("./Index"); // Default return URL
            }

            if (!ModelState.IsValid)
            {
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
                _logger.LogWarning("User {Email} attempted to edit invoice {InvoiceId} without authorization.", user.Email, InvoiceId);
                return Forbid();
            }

            var invoiceToUpdate = await _context.Invoices.FindAsync(InvoiceId);

            if (invoiceToUpdate == null)
            {
                _logger.LogWarning("Invoice with ID {InvoiceId} not found during POST.", InvoiceId);
                return NotFound($"Unable to load invoice with ID {InvoiceId}.");
            }

            _logger.LogWarning("Attempt to edit invoice {InvoiceId} with status {Status} denied.", InvoiceId, invoiceToUpdate.Status);

            if (invoiceToUpdate.Status != InvoiceStatus.Draft && invoiceToUpdate.Status != InvoiceStatus.Due)
            {
                _logger.LogWarning("Attempt to edit invoice {InvoiceId} with status {invoiceToUpdate.Status} denied.", invoiceToUpdate.Status, InvoiceId);
                ModelState.AddModelError(string.Empty, $"Invoice cannot be edited because its status is '{invoiceToUpdate.Status}'. Only Draft or Due invoices can be edited.");
                ViewedUserId = invoiceToUpdate.UserID;
                BatchId = invoiceToUpdate.BatchID;
                return Page();
            }

            invoiceToUpdate.DueDate = Input.DueDate;
            invoiceToUpdate.Description = Input.Description;
            invoiceToUpdate.AmountDue = Input.AmountDue;

            if (Enum.TryParse<InvoiceStatus>(Input.Status, out var newStatus))
            {
                invoiceToUpdate.Status = newStatus;
            }
            else
            {
                _logger.LogWarning("Invalid status string '{Input.Status}' provided for invoice {InvoiceId}.", Input.Status, InvoiceId);
                ModelState.AddModelError("Input.Status", "Invalid status value.");
                ViewedUserId = invoiceToUpdate.UserID;
                BatchId = invoiceToUpdate.BatchID;
                return Page();
            }

            invoiceToUpdate.LastUpdated = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Invoice {InvoiceId} updated successfully by {user.Email}.", InvoiceId, user.Email);
                TempData["StatusMessage"] = "Invoice updated successfully.";
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogError(ex, "Concurrency error while updating invoice {InvoiceId}.", InvoiceId);
                ModelState.AddModelError(string.Empty, "The invoice was modified by another user. Please reload and try again.");
                ViewedUserId = invoiceToUpdate.UserID;
                BatchId = invoiceToUpdate.BatchID;
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating invoice {InvoiceId}.", InvoiceId);
                ModelState.AddModelError(string.Empty, "An unexpected error occurred while saving the invoice.");
                ViewedUserId = invoiceToUpdate.UserID;
                BatchId = invoiceToUpdate.BatchID;
                return Page();
            }

            if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            {
                return LocalRedirect(ReturnUrl);
            }

            return RedirectToPage("./Index"); // Ensure a return value for all code paths
        }
    }
}
