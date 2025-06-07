using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
namespace Members.Areas.Admin.Pages.Accounting
{
    [Authorize(Roles = "Admin,Manager")]
    public class RecordPaymentModel(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager,
        ILogger<RecordPaymentModel> logger) : PageModel
    {
        private readonly ApplicationDbContext _context = context ?? throw new ArgumentNullException(nameof(context));
        private readonly UserManager<IdentityUser> _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        private readonly ILogger<RecordPaymentModel> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        [BindProperty]
        public InputModel Input { get; set; } = new InputModel();
        public List<OpenInvoiceViewModel> OpenInvoicesForUser { get; set; } = [];
        public SelectList? UserSelectList { get; set; }
        public string? TargetUserName { get; set; }
        public bool IsUserPreselected { get; set; } = false;
        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }
        public class InputModel
        {
            [Required]
            [Display(Name = "User")] // This DisplayName is for the label if dropdown is shown
            public string SelectedUserID { get; set; } = string.Empty;
            [Display(Name = "Apply to Invoice")]
            [Required(ErrorMessage = "You must select an invoice to apply this payment to.")]
            public int? SelectedInvoiceID { get; set; }
            [Required]
            [DataType(DataType.Date)]
            [Display(Name = "Payment Date")]
            public DateTime PaymentDate { get; set; } = DateTime.Today;
            [Required]
            [Range(0.01, 1000000.00, ErrorMessage = "Amount must be greater than 0.")]
            [DataType(DataType.Currency)]
            [Display(Name = "Payment Amount")]
            public decimal Amount { get; set; }
            [Required]
            [Display(Name = "Payment Method")]
            public PaymentMethod Method { get; set; } = PaymentMethod.Check;
            [StringLength(100)]
            [Display(Name = "Reference Number (e.g., Check #)")]
            public string? ReferenceNumber { get; set; }
            [StringLength(200)]
            [Display(Name = "Notes (Optional)")]
            public string? Notes { get; set; }
        }
        public class OpenInvoiceViewModel
        {
            public int InvoiceID { get; set; }
            public DateTime InvoiceDate { get; set; }
            public string Description { get; set; } = string.Empty;
            [DataType(DataType.Currency)]
            public decimal AmountDue { get; set; }
            [DataType(DataType.Currency)]
            public decimal AmountPaid { get; set; }
            [DataType(DataType.Currency)]
            public decimal AmountRemaining => AmountDue - AmountPaid;
        }
        public async Task OnGetAsync(string? userId, string? returnUrl)
        {
            const string logTemplate = "OnGetAsync called for RecordPaymentModel. UserID: {UserId}, ReturnUrl: {ReturnUrl}";
            _logger.LogInformation(logTemplate, userId, returnUrl);
            ReturnUrl = returnUrl;
            if (!string.IsNullOrEmpty(userId))
            {
                var user = await _userManager.FindByIdAsync(userId);
                if (user != null)
                {
                    var userProfile = await _context.UserProfile.FirstOrDefaultAsync(up => up.UserId == userId);
                    TargetUserName = (userProfile != null && !string.IsNullOrWhiteSpace(userProfile.FirstName) && !string.IsNullOrWhiteSpace(userProfile.LastName))
                                     ? $"{userProfile.FirstName} {userProfile.LastName} ({user.Email})"
                                     : user.UserName ?? user.Email;
                    Input.SelectedUserID = userId;
                    IsUserPreselected = true;
                    _logger.LogInformation("RecordPayment page loaded for pre-selected user: {TargetUserName} (ID: {UserId})", TargetUserName, userId);
                    OpenInvoicesForUser = await _context.Invoices
                        .Where(i => i.UserID == userId &&
                                    i.Status != InvoiceStatus.Cancelled &&
                                    i.AmountPaid < i.AmountDue)
                        .Select(i => new OpenInvoiceViewModel
                        {
                            InvoiceID = i.InvoiceID,
                            InvoiceDate = i.InvoiceDate,
                            Description = i.Description,
                            AmountDue = i.AmountDue,
                            AmountPaid = i.AmountPaid
                        })
                        .OrderBy(i => i.InvoiceDate)
                        .ToListAsync();
                    _logger.LogInformation("Found {OpenInvoicesCount} open invoices for user {UserId}.", OpenInvoicesForUser.Count, userId);
                }
                else
                {
                    _logger.LogWarning("RecordPayment: UserID {UserId} provided but user not found. Falling back to user selection list.", userId);
                    await PopulateUserSelectList();
                }
            }
            else
            {
                _logger.LogInformation("RecordPayment: No UserID provided. Populating user selection list.");
                await PopulateUserSelectList();
            }
        }
        private async Task PopulateUserSelectList()
        {
            var memberRoleName = "Member";
            var usersInMemberRole = await _userManager.GetUsersInRoleAsync(memberRoleName);
            _logger.LogInformation($"PopulateUserSelectList: Found {usersInMemberRole?.Count ?? 0} users in role '{memberRoleName}'.");
            if (usersInMemberRole == null || !usersInMemberRole.Any())
            {
                UserSelectList = new SelectList(Enumerable.Empty<SelectListItem>());
                return;
            }
            var userIdsInMemberRole = usersInMemberRole.Select(u => u.Id).ToList();
            var userProfiles = await _context.UserProfile
                                        .Where(up => userIdsInMemberRole.Contains(up.UserId))
                                        .ToDictionaryAsync(up => up.UserId);
            var userListItems = new List<SelectListItem>();
            foreach (var user in usersInMemberRole.OrderBy(u => u.UserName))
            {
                if (userProfiles.TryGetValue(user.Id, out UserProfile? profile) && profile != null && !string.IsNullOrEmpty(profile.LastName))
                {
                    userListItems.Add(new SelectListItem { Value = user.Id, Text = $"{profile.FirstName} {profile.LastName} ({user.Email})" });
                }
                else
                {
                    userListItems.Add(new SelectListItem { Value = user.Id, Text = $"{user.UserName} ({user.Email}) - Profile Incomplete" });
                }
            }
            UserSelectList = new SelectList(userListItems.OrderBy(item => item.Text), "Value", "Text");
        }
        public async Task<IActionResult> OnPostAsync()
        {
            const string logTemplate = "OnPostAsync called for RecordPaymentModel. SelectedUserID: {SelectedUserID}, SelectedInvoiceID: {SelectedInvoiceID}";
            _logger.LogInformation(logTemplate, Input.SelectedUserID, Input.SelectedInvoiceID);
            if (!string.IsNullOrEmpty(Input.SelectedUserID))
            {
                var userForDisplayTest = await _userManager.FindByIdAsync(Input.SelectedUserID);
                if (userForDisplayTest != null) IsUserPreselected = true;
            }
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("RecordPayment OnPostAsync: ModelState is invalid.");
                if (!string.IsNullOrEmpty(Input.SelectedUserID))
                {
                    var userForDisplay = await _userManager.FindByIdAsync(Input.SelectedUserID);
                    if (userForDisplay != null)
                    {
                        var userProfile = await _context.UserProfile.FirstOrDefaultAsync(up => up.UserId == Input.SelectedUserID);
                        TargetUserName = (userProfile != null && !string.IsNullOrWhiteSpace(userProfile.FirstName) && !string.IsNullOrWhiteSpace(userProfile.LastName))
                                         ? $"{userProfile.FirstName} {userProfile.LastName} ({userForDisplay.Email})"
                                         : userForDisplay.UserName ?? userForDisplay.Email;
                        OpenInvoicesForUser = await _context.Invoices
                            .Where(i => i.UserID == Input.SelectedUserID &&
                                        i.Status != InvoiceStatus.Cancelled &&
                                        i.AmountPaid < i.AmountDue)
                            .Select(i => new OpenInvoiceViewModel
                            {
                                InvoiceID = i.InvoiceID,
                                InvoiceDate = i.InvoiceDate,
                                Description = i.Description,
                                AmountDue = i.AmountDue,
                                AmountPaid = i.AmountPaid
                            })
                            .OrderBy(i => i.InvoiceDate)
                            .ToListAsync();
                    }
                }
                if (!IsUserPreselected)
                {
                    await PopulateUserSelectList();
                }
                return Page();
            }
            var user = await _userManager.FindByIdAsync(Input.SelectedUserID);
            if (user == null) { /* This case should ideally be caught by model validation or earlier checks */ }
            var invoiceToPay = await _context.Invoices.FirstOrDefaultAsync(i => i.InvoiceID == Input.SelectedInvoiceID.GetValueOrDefault() && i.UserID == Input.SelectedUserID);
            if (invoiceToPay == null)
            {
                _logger.LogWarning("OnPostAsync: Selected invoice ID {SelectedInvoiceID} not found for user {SelectedUserID}.", Input.SelectedInvoiceID, Input.SelectedUserID);
                ModelState.AddModelError("Input.SelectedInvoiceID", "The selected invoice was not found or does not belong to this user.");
                if (!string.IsNullOrEmpty(Input.SelectedUserID))
                {
                    var userForDisplay = await _userManager.FindByIdAsync(Input.SelectedUserID);
                    if (userForDisplay != null)
                    {
                        var userProfile = await _context.UserProfile.FirstOrDefaultAsync(up => up.UserId == Input.SelectedUserID);
                        TargetUserName = (userProfile != null && !string.IsNullOrWhiteSpace(userProfile.FirstName) && !string.IsNullOrWhiteSpace(userProfile.LastName))
                                         ? $"{userProfile.FirstName} {userProfile.LastName} ({userForDisplay.Email})"
                                         : userForDisplay.UserName ?? userForDisplay.Email;
                        OpenInvoicesForUser = await _context.Invoices
                            .Where(i => i.UserID == Input.SelectedUserID &&
                                        i.Status != InvoiceStatus.Cancelled &&
                                        i.AmountPaid < i.AmountDue)
                            .Select(i => new OpenInvoiceViewModel { /* ... populate ... */ })
                            .OrderBy(i => i.InvoiceDate)
                            .ToListAsync();
                    }
                }
                if (!IsUserPreselected) await PopulateUserSelectList();
                return Page();
            }
            if (invoiceToPay.Status == InvoiceStatus.Cancelled || invoiceToPay.Status == InvoiceStatus.Paid)
            {
                ModelState.AddModelError("Input.SelectedInvoiceID", $"Invoice {invoiceToPay.InvoiceID} is already {invoiceToPay.Status} and cannot receive further payments.");
                await OnGetAsync(Input.SelectedUserID, ReturnUrl);
                return Page();
            }
            decimal amountToApplyToInvoice = Input.Amount;
            decimal overpaymentAmount = 0;
            decimal amountRemainingOnInvoice = invoiceToPay.AmountDue - invoiceToPay.AmountPaid;
            if (Input.Amount > amountRemainingOnInvoice)
            {
                amountToApplyToInvoice = amountRemainingOnInvoice; // Only apply what's remaining to the invoice
                overpaymentAmount = Input.Amount - amountRemainingOnInvoice;
                _logger.LogInformation($"Overpayment detected. Payment: {Input.Amount:C}, Remaining on Invoice {invoiceToPay.InvoiceID}: {amountRemainingOnInvoice:C}. Overpayment: {overpaymentAmount:C}");
            }
            // Create the Payment record for the full amount received
            var payment = new Payment
            {
                UserID = Input.SelectedUserID,
                InvoiceID = invoiceToPay.InvoiceID, // Link payment to the invoice it's primarily applied to
                PaymentDate = Input.PaymentDate,
                Amount = Input.Amount, // Record the actual amount paid by the user
                Method = Input.Method,
                ReferenceNumber = Input.ReferenceNumber,
                Notes = Input.Notes, // You might want to add "Overpayment processed" to notes if overpaymentAmount > 0
                DateRecorded = DateTime.UtcNow
            };           
            _context.Payments.Add(payment);
            // Note: We need payment.PaymentID for SourcePaymentID in UserCredit.
            // We will call SaveChangesAsync once after adding all entities.
            // Update the selected invoice
            invoiceToPay.AmountPaid += amountToApplyToInvoice; // Apply the calculated amount (could be partial or full)
            invoiceToPay.LastUpdated = DateTime.UtcNow;
            if (invoiceToPay.AmountPaid >= invoiceToPay.AmountDue)
            {
                invoiceToPay.Status = InvoiceStatus.Paid;
                // Ensure AmountPaid does not exceed AmountDue on the invoice itself
                invoiceToPay.AmountPaid = invoiceToPay.AmountDue;
            }
            else if (invoiceToPay.DueDate < DateTime.Today.AddDays(-1) && invoiceToPay.Status == InvoiceStatus.Due)
            {
                // If partially paid but past due, mark Overdue
                invoiceToPay.Status = InvoiceStatus.Overdue;
            }
            // If partially paid but not yet overdue, it remains Due.
            // Handle UserCredit if there was an overpayment
            if (overpaymentAmount > 0)
            {
                // We need to save the payment first to get its ID if SourcePaymentID is not nullable
                // or if you prefer to link it immediately.
                // However, if SourcePaymentID in UserCredit is nullable, we can do it in one SaveChanges.
                // Assuming SourcePaymentID is nullable in UserCredit.cs as per my model suggestion.
                // If not, you'd call SaveChanges after adding payment, then create UserCredit with payment.PaymentID.
                var userCredit = new UserCredit
                {
                    UserID = Input.SelectedUserID,
                    CreditDate = Input.PaymentDate, // Or DateTime.Today
                    Amount = overpaymentAmount,
                    SourcePaymentID = null, // Will be set after payment is saved if needed, see below.
                    Reason = $"Overpayment on Invoice INV-{invoiceToPay.InvoiceID:D5}.",
                    IsApplied = false,
                    DateCreated = DateTime.UtcNow
                };
                _context.UserCredits.Add(userCredit);
                _logger.LogInformation($"UserCredit record created for {overpaymentAmount:C} for user {Input.SelectedUserID}.");
                // To link UserCredit to the Payment, you might need to save Payment first,
                // then assign payment.PaymentID to userCredit.SourcePaymentID, then save UserCredit.
                // For simplicity with a single SaveChanges, we'll make SourcePaymentID on UserCredit nullable
                // and assume you can add it later or that this initial record is enough.
                // A more robust way if SourcePaymentID is critical immediately:
                // 1. _context.Payments.Add(payment);
                // 2. await _context.SaveChangesAsync(); // Save payment to get its ID
                // 3. userCredit.SourcePaymentID = payment.PaymentID;
                // 4. _context.UserCredits.Add(userCredit);
                // 5. await _context.SaveChangesAsync(); // Save invoice update and user credit
                // For now, the provided UserCredit model has nullable SourcePaymentID.
                // We will save all changes together at the end.
            }
            try
            {
                await _context.SaveChangesAsync(); // Saves Payment, updated Invoice, and new UserCredit (if any)
                // If UserCredit was created and you want to link its SourcePaymentID now
                if (overpaymentAmount > 0 && payment.PaymentID > 0)
                {
                    var savedCredit = await _context.UserCredits.OrderByDescending(uc => uc.DateCreated).FirstOrDefaultAsync(uc => uc.UserID == Input.SelectedUserID && uc.Amount == overpaymentAmount && uc.SourcePaymentID == null);
                    if (savedCredit != null)
                    {
                        savedCredit.SourcePaymentID = payment.PaymentID;
                        savedCredit.Reason = $"Overpayment on Invoice INV-{invoiceToPay.InvoiceID:D5} from Payment #{payment.PaymentID}.";
                        await _context.SaveChangesAsync(); // Save the update to UserCredit
                    }
                }
                _logger.LogInformation($"Successfully processed payment ID {payment.PaymentID}, updated invoice ID {invoiceToPay.InvoiceID}, and potentially created UserCredit for user."); 
                TempData["StatusMessage"] = $"Payment of {Input.Amount:C} applied. Invoice INV-{invoiceToPay.InvoiceID:D5} status: {invoiceToPay.Status}.";
                if (overpaymentAmount > 0)
                {
                    TempData["StatusMessage"] += $"\nOverpayment of {overpaymentAmount:C} credited to account.";
                }
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error saving payment/invoice/credit.");
                ModelState.AddModelError(string.Empty, "An error occurred while saving data. See logs for details.");
                await OnGetAsync(Input.SelectedUserID, ReturnUrl); // Repopulate page data
                return Page();
            }
            //payment = new Payment
            //{
            //    UserID = Input.SelectedUserID,
            //    InvoiceID = invoiceToPay.InvoiceID,
            //    PaymentDate = Input.PaymentDate,
            //    Amount = Input.Amount,
            //    Method = Input.Method,
            //    ReferenceNumber = Input.ReferenceNumber,
            //    Notes = Input.Notes,
            //    DateRecorded = DateTime.UtcNow
            //};
            //_context.Payments.Add(payment);
            invoiceToPay.AmountPaid += Input.Amount;
            if (invoiceToPay.AmountPaid >= invoiceToPay.AmountDue)
            {
                invoiceToPay.Status = InvoiceStatus.Paid;
            }
            else if (invoiceToPay.DueDate < DateTime.Today.AddDays(-1) && invoiceToPay.Status == InvoiceStatus.Due)
            {
                invoiceToPay.Status = InvoiceStatus.Overdue;
            }
            invoiceToPay.LastUpdated = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            _logger.LogInformation("Successfully saved payment ID {PaymentID} and updated invoice ID {InvoiceID} for user {UserName}.", payment.PaymentID, invoiceToPay.InvoiceID, user?.UserName);
            TempData["StatusMessage"] = $"Payment of {payment.Amount:C} applied to invoice INV-{invoiceToPay.InvoiceID:D5} successfully for user {TargetUserName ?? user?.UserName}. Invoice status: {invoiceToPay.Status}.";
            if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            {
                return Redirect(ReturnUrl);
            }
            return RedirectToPage(new { userId = Input.SelectedUserID, returnUrl = ReturnUrl });
        }
    }
}