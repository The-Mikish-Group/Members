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
    public class UserCreditViewModel
    {
        public int UserCreditID { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime CreditDate { get; set; }
    }
    [Authorize(Roles = "Admin,Manager")]
    public class RecordPaymentModel(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager,
        ILogger<RecordPaymentModel> logger) : PageModel
    {        
        private readonly ApplicationDbContext _context = context ?? throw new ArgumentNullException(nameof(context));
        private readonly UserManager<IdentityUser> _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        private readonly ILogger<RecordPaymentModel> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        public List<UserCreditViewModel> AvailableUserCredits { get; set; } = [];
        [DataType(DataType.Currency)]
        public decimal TotalAvailableUserCreditAmount { get; set; }
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
            // ... (Your existing SelectedUserID and SelectedInvoiceID properties) ...
            [Required]
            [Display(Name = "User")]
            public string SelectedUserID { get; set; } = string.Empty;
            [Display(Name = "Apply to Invoice")]
            [Required(ErrorMessage = "You must select an invoice to apply this payment or credit to.")]
            public int? SelectedInvoiceID { get; set; }
            // --- MODIFIED: Make these nullable ---
            [DataType(DataType.Date)]
            [Display(Name = "Payment Date (if new payment)")]
            public DateTime? PaymentDate { get; set; } = DateTime.Today;
            [Range(0.01, 1000000.00, ErrorMessage = "Amount must be greater than 0 if entered.")]
            [DataType(DataType.Currency)]
            [Display(Name = "Payment Amount (if new payment)")]
            public decimal? Amount { get; set; }
            [Display(Name = "Payment Method (if new payment)")]
            public PaymentMethod? Method { get; set; } = PaymentMethod.Check;
            // --- END MODIFIED ---
            // ... (Your existing ReferenceNumber and Notes properties) ...
            [StringLength(100)]
            [Display(Name = "Reference Number (e.g., Check #)")]
            public string? ReferenceNumber { get; set; }
            [StringLength(200)]
            [Display(Name = "Notes (Optional for new payment)")]
            public string? Notes { get; set; }
            // --- NEW PROPERTIES FOR CREDIT APPLICATION ---
            [Display(Name = "Select Credit to Apply (Optional)")]
            public int? SelectedUserCreditID { get; set; }
            [DataType(DataType.Currency)]
            [Range(0.01, 1000000.00, ErrorMessage = "Amount to apply from credit must be greater than 0 if entered.")]
            [Display(Name = "Amount from Credit to Apply")]
            public decimal? AmountToApplyFromCredit { get; set; }
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
                    const string userPreselectedLogTemplate = "RecordPayment page loaded for pre-selected user: {TargetUserName} (ID: {UserId})";
                    _logger.LogInformation(userPreselectedLogTemplate, TargetUserName, userId);
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
                    const string openInvoicesLogTemplate = "Found {OpenInvoicesCount} open invoices for user {UserId}.";
                    _logger.LogInformation(openInvoicesLogTemplate, OpenInvoicesForUser.Count, userId);
                }
                else
                {
                    const string userNotFoundLogTemplate = "RecordPayment: UserID {UserId} provided but user not found. Falling back to user selection list.";
                    _logger.LogWarning(userNotFoundLogTemplate, userId);
                    await PopulateUserSelectList();
                }
                var unappliedCredits = await _context.UserCredits
                    .Where(uc => uc.UserID == userId && !uc.IsApplied && uc.Amount > 0) // Use the 'userId' method parameter
                    .OrderBy(uc => uc.CreditDate)
                    .ToListAsync();
                if (unappliedCredits.Count != 0)
                {
                    AvailableUserCredits = [.. unappliedCredits.Select(uc => new UserCreditViewModel
                    {
                        UserCreditID = uc.UserCreditID,
                        Amount = uc.Amount,
                        Reason = uc.Reason,
                        CreditDate = uc.CreditDate
                    })];
                    TotalAvailableUserCreditAmount = AvailableUserCredits.Sum(c => c.Amount);
                    _logger.LogInformation("Found {CreditCount} available credits totaling {TotalCreditAmount} for user {UserId}.", AvailableUserCredits.Count, TotalAvailableUserCreditAmount, userId);
                }
                else
                {
                    _logger.LogInformation("No available credits found for user {UserId}.", userId);
                }
            }
            else
            {
                const string noUserIdLogTemplate = "RecordPayment: No UserID provided. Populating user selection list.";
                _logger.LogInformation(noUserIdLogTemplate);
                await PopulateUserSelectList();
            }
        }
        private async Task PopulateUserSelectList()
        {                       
            var memberRoleName = "Member";
            var usersInMemberRole = await _userManager.GetUsersInRoleAsync(memberRoleName);
            const string populationUserSelectListTemplate = "RecordPayment: {UserCount} users found in role {RoleName}.";
            _logger.LogInformation(populationUserSelectListTemplate, usersInMemberRole?.Count ?? 0, memberRoleName);
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
        public async Task<IActionResult> OnPostApplyCreditAsync()
        {
            _logger.LogInformation("OnPostApplyCreditAsync called. User: {SelectedUserID}, Invoice: {SelectedInvoiceID}, Credit: {SelectedUserCreditID}, Amount: {AmountToApplyFromCredit}",
                Input.SelectedUserID, Input.SelectedInvoiceID, Input.SelectedUserCreditID, Input.AmountToApplyFromCredit);
            // Basic Validation for required inputs for this specific action
            if (!Input.SelectedUserCreditID.HasValue || Input.SelectedUserCreditID.Value <= 0)
            {
                ModelState.AddModelError("Input.SelectedUserCreditID", "A credit must be selected to apply.");
            }
            if (!Input.AmountToApplyFromCredit.HasValue || Input.AmountToApplyFromCredit.Value <= 0)
            {
                ModelState.AddModelError("Input.AmountToApplyFromCredit", "Please enter a positive amount from the credit to apply.");
            }
            if (!Input.SelectedInvoiceID.HasValue)
            {
                ModelState.AddModelError("Input.SelectedInvoiceID", "An invoice must be selected to apply the credit to.");
            }
            // Input.SelectedUserID is already [Required] in InputModel
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("OnPostApplyCreditAsync: ModelState is invalid at initial check.");
                await OnGetAsync(Input.SelectedUserID, ReturnUrl); // Repopulate page data for re-render
                return Page();
            }
            // Fetch entities - Null forgiving operator (!) is used because ModelState.IsValid should ensure these have values.
            var creditToApply = await _context.UserCredits.FirstOrDefaultAsync(uc =>
                uc.UserCreditID == Input.SelectedUserCreditID!.Value &&
                uc.UserID == Input.SelectedUserID &&
                !uc.IsApplied &&
                !uc.IsVoided);
            if (creditToApply == null)
            {
                ModelState.AddModelError("Input.SelectedUserCreditID", "Selected credit is not available, already used, voided, or does not belong to this user.");
                await OnGetAsync(Input.SelectedUserID, ReturnUrl);
                return Page();
            }
            var invoiceToApplyTo = await _context.Invoices.FirstOrDefaultAsync(i =>
                i.InvoiceID == Input.SelectedInvoiceID!.Value &&
                i.UserID == Input.SelectedUserID);
            if (invoiceToApplyTo == null)
            {
                ModelState.AddModelError("Input.SelectedInvoiceID", "Selected invoice not found for this user.");
                await OnGetAsync(Input.SelectedUserID, ReturnUrl);
                return Page();
            }
            if (invoiceToApplyTo.Status == InvoiceStatus.Paid || invoiceToApplyTo.Status == InvoiceStatus.Cancelled)
            {
                ModelState.AddModelError(string.Empty, $"Invoice {invoiceToApplyTo.InvoiceID} is already {invoiceToApplyTo.Status} and cannot have credits applied.");
                await OnGetAsync(Input.SelectedUserID, ReturnUrl);
                return Page();
            }
            decimal amountFromCreditInput = Input.AmountToApplyFromCredit!.Value;
            decimal invoiceAmountRemaining = invoiceToApplyTo.AmountDue - invoiceToApplyTo.AmountPaid;
            if (amountFromCreditInput > creditToApply.Amount)
            {
                ModelState.AddModelError("Input.AmountToApplyFromCredit", "Amount to apply exceeds the available balance of the selected credit.");
            }
            decimal actualAmountToApply = Math.Min(amountFromCreditInput, invoiceAmountRemaining);
            if (actualAmountToApply <= 0)
            {
                if (invoiceAmountRemaining <= 0)
                {
                    ModelState.AddModelError("Input.AmountToApplyFromCredit", "The selected invoice is already fully paid.");
                }
                else
                {
                    // This case (e.g. user entered 0 or negative) should also be caught by Range attribute on AmountToApplyFromCredit if model validation is robust
                    ModelState.AddModelError("Input.AmountToApplyFromCredit", "Amount to apply from credit must be a positive value.");
                }
            }
            if (!ModelState.IsValid) // Re-check ModelState after custom validations
            {
                _logger.LogWarning("OnPostApplyCreditAsync: ModelState invalid after amount/entity validation.");
                await OnGetAsync(Input.SelectedUserID, ReturnUrl);
                return Page();
            }
            // Perform Application
            _logger.LogInformation("Applying {ActualAmountToApply} from CreditID {CreditID} to InvoiceID {InvoiceID}",
                actualAmountToApply, creditToApply.UserCreditID, invoiceToApplyTo.InvoiceID);
            invoiceToApplyTo.AmountPaid += actualAmountToApply;
            invoiceToApplyTo.LastUpdated = DateTime.UtcNow;
            if (invoiceToApplyTo.AmountPaid >= invoiceToApplyTo.AmountDue)
            {
                invoiceToApplyTo.Status = InvoiceStatus.Paid;
                invoiceToApplyTo.AmountPaid = invoiceToApplyTo.AmountDue; // Cap at AmountDue
            }
            else if (invoiceToApplyTo.DueDate < DateTime.Today.AddDays(-1) && invoiceToApplyTo.Status == InvoiceStatus.Due)
            {
                invoiceToApplyTo.Status = InvoiceStatus.Overdue;
            }
            _context.Invoices.Update(invoiceToApplyTo);
            creditToApply.Amount -= actualAmountToApply; // This updates creditToApply.Amount to the new remaining balance
            creditToApply.LastUpdated = DateTime.UtcNow;

            string originalCreditAmountBeforeThisApplication = (creditToApply.Amount + actualAmountToApply).ToString("C");
            string remainingCreditAmountAfterThisApplication = creditToApply.Amount.ToString("C");
            creditToApply.ApplicationNotes = $"Applied {actualAmountToApply:C} to INV-{invoiceToApplyTo.InvoiceID:D5} on {DateTime.UtcNow:yyyy-MM-dd}. Original credit bal before this app: {originalCreditAmountBeforeThisApplication}. Remaining bal on this credit: {remainingCreditAmountAfterThisApplication}.";
            
            if (creditToApply.Amount <= 0)
            {
                creditToApply.IsApplied = true;
                creditToApply.Amount = 0;
                creditToApply.AppliedDate = DateTime.UtcNow;
                creditToApply.AppliedToInvoiceID = invoiceToApplyTo.InvoiceID;
                _logger.LogInformation("CreditID {CreditID} fully applied. Remaining amount: {Amount}", creditToApply.UserCreditID, creditToApply.Amount);
            }
            else
            {
                _logger.LogInformation("CreditID {CreditID} partially applied. Remaining amount: {Amount}", creditToApply.UserCreditID, creditToApply.Amount);
            }
            _context.UserCredits.Update(creditToApply);
            try
            {
                await _context.SaveChangesAsync();
                TempData["StatusMessage"] = $"{actualAmountToApply:C} from Credit ID {creditToApply.UserCreditID} applied to Invoice INV-{invoiceToApplyTo.InvoiceID:D5}. Invoice status: {invoiceToApplyTo.Status}.";
                if (creditToApply.IsVoided || creditToApply.Amount <= 0)
                {
                    TempData["StatusMessage"] += $" Credit ID {creditToApply.UserCreditID} is now fully utilized.";
                }
                else
                {
                    TempData["StatusMessage"] += $" Credit ID {creditToApply.UserCreditID} has {creditToApply.Amount:C} remaining.";
                }
                _logger.LogInformation("Credit application successful for CreditID {CreditID} to InvoiceID {InvoiceID}", creditToApply.UserCreditID, invoiceToApplyTo.InvoiceID);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error saving credit application for CreditID {CreditID} to InvoiceID {InvoiceID}", creditToApply.UserCreditID, invoiceToApplyTo.InvoiceID);
                ModelState.AddModelError(string.Empty, "An error occurred while saving the credit application. See logs.");
                await OnGetAsync(Input.SelectedUserID, ReturnUrl);
                return Page();
            }
            return RedirectToPage(new { userId = Input.SelectedUserID, returnUrl = ReturnUrl });
        }
        public async Task<IActionResult> OnPostRecordNewPaymentAsync()
        {
            _logger.LogInformation("OnPostRecordNewPaymentAsync called. UserID: {SelectedUserID}, InvoiceID: {SelectedInvoiceID}", Input.SelectedUserID, Input.SelectedInvoiceID);
            if (!Input.PaymentDate.HasValue)
            {
                ModelState.AddModelError("Input.PaymentDate", "Payment Date is required.");
            }
            // Ensure you check .HasValue before accessing .Value for Amount
            if (!Input.Amount.HasValue || Input.Amount.Value <= 0)
            {
                ModelState.AddModelError("Input.Amount", "Payment Amount is required and must be greater than 0.");
            }
            if (!Input.Method.HasValue)
            {
                ModelState.AddModelError("Input.Method", "Payment Method is required.");
            }
            if (!Input.SelectedUserCreditID.HasValue || Input.AmountToApplyFromCredit.GetValueOrDefault() <= 0)
            {
                // This block means we are likely processing a NEW payment, so these fields are required.
                if (!Input.PaymentDate.HasValue) ModelState.AddModelError("Input.PaymentDate", "Payment Date is required if not applying a credit.");
                if (!Input.Amount.HasValue || Input.Amount.Value <= 0) ModelState.AddModelError("Input.Amount", "Payment Amount is required and must be greater than 0 if not applying a credit.");
                if (!Input.Method.HasValue) ModelState.AddModelError("Input.Method", "Payment Method is required if not applying a credit.");
            }
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("RecordPayment OnPostAsync: ModelState is invalid.");
                await OnGetAsync(Input.SelectedUserID, ReturnUrl); // Call OnGetAsync to repopulate ALL page data
                return Page();
            }
            const string logTemplateOnPost = "OnPostAsync called for RecordPaymentModel. SelectedUserID: {SelectedUserID}, SelectedInvoiceID: {SelectedInvoiceID}";
            _logger.LogInformation(logTemplateOnPost, Input.SelectedUserID, Input.SelectedInvoiceID);
            if (!string.IsNullOrEmpty(Input.SelectedUserID))
            {
                var userForDisplayTest = await _userManager.FindByIdAsync(Input.SelectedUserID);
                if (userForDisplayTest != null) IsUserPreselected = true;
            }
            if (!ModelState.IsValid)
            {
                const string logTemplateInvalidModelState = "RecordPayment OnPostAsync: ModelState is invalid.";
                _logger.LogWarning(logTemplateInvalidModelState);
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
                if (!string.IsNullOrEmpty(Input.SelectedUserID)) // Ensure SelectedUserID is available
                {
                    var unappliedCreditsOnError = await _context.UserCredits
                       .Where(uc => uc.UserID == Input.SelectedUserID && !uc.IsApplied && uc.Amount > 0)
                       .OrderBy(uc => uc.CreditDate).ToListAsync();
                    if (unappliedCreditsOnError.Count != 0)
                    {
                        AvailableUserCredits = [.. unappliedCreditsOnError.Select(uc => new UserCreditViewModel
                        {
                            UserCreditID = uc.UserCreditID,
                            Amount = uc.Amount,
                            Reason = uc.Reason,
                            CreditDate = uc.CreditDate
                        })];
                        TotalAvailableUserCreditAmount = AvailableUserCredits.Sum(c => c.Amount);
                    }
                }
                return Page();
            }
            var user = await _userManager.FindByIdAsync(Input.SelectedUserID);
            if (user == null) { /* This case should ideally be caught by model validation or earlier checks */ }
            var invoiceToPay = await _context.Invoices.FirstOrDefaultAsync(i => i.InvoiceID == Input.SelectedInvoiceID.GetValueOrDefault() && i.UserID == Input.SelectedUserID);
            if (invoiceToPay == null)
            {
                const string logTemplateInvoiceNotFound = "OnPostAsync: Selected invoice ID {SelectedInvoiceID} not found for user {SelectedUserID}.";
                _logger.LogWarning(logTemplateInvoiceNotFound, Input.SelectedInvoiceID, Input.SelectedUserID);
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
                if (!IsUserPreselected) await PopulateUserSelectList();
                return Page();
            }
            if (invoiceToPay.Status == InvoiceStatus.Cancelled || invoiceToPay.Status == InvoiceStatus.Paid)
            {
                ModelState.AddModelError("Input.SelectedInvoiceID", $"Invoice {invoiceToPay.InvoiceID} is already {invoiceToPay.Status} and cannot receive further payments.");
                await OnGetAsync(Input.SelectedUserID, ReturnUrl);
                return Page();
            }
            decimal amountToApplyToInvoice = Input.Amount!.Value;            
            decimal overpaymentAmount = 0;
            decimal amountRemainingOnInvoice = invoiceToPay.AmountDue - invoiceToPay.AmountPaid;
            if (Input.Amount!.Value > amountRemainingOnInvoice)
            {
                amountToApplyToInvoice = amountRemainingOnInvoice; // Only apply what's remaining to the invoice
                overpaymentAmount = Input.Amount.Value - amountRemainingOnInvoice;
                const string overpaymentLogTemplate = "Overpayment detected. Payment: {PaymentAmount}, Remaining on Invoice {InvoiceID}: {RemainingAmount}. Overpayment: {OverpaymentAmount}";
                _logger.LogInformation(overpaymentLogTemplate, Input.Amount, invoiceToPay.InvoiceID, amountRemainingOnInvoice, overpaymentAmount);
            }
            // Create the Payment record for the full amount received
            var payment = new Payment
            {
                UserID = Input.SelectedUserID,
                InvoiceID = invoiceToPay.InvoiceID, // Link payment to the invoice it's primarily applied to
                PaymentDate = Input.PaymentDate!.Value,
                Amount = Input.Amount!.Value, // Record the actual amount paid by the user
                Method = Input.Method!.Value,
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
                    CreditDate = Input.PaymentDate!.Value, // Or DateTime.Today
                    Amount = overpaymentAmount,
                    SourcePaymentID = null, // Will be set after payment is saved if needed, see below.
                    Reason = $"Overpayment on Invoice INV-{invoiceToPay.InvoiceID:D5}.",
                    IsApplied = false,
                    DateCreated = DateTime.UtcNow
                };
                _context.UserCredits.Add(userCredit);
                const string userCreditLogTemplate = "UserCredit record created for {CreditAmount} for user {UserID}.";
                _logger.LogInformation(userCreditLogTemplate, overpaymentAmount, Input.SelectedUserID);
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
            _context.Invoices.Update(invoiceToPay); // Ensure invoice is marked for update

            UserCredit? newCreditForOverpayment = null; // Renamed from userCredit for clarity in this block
            if (overpaymentAmount > 0)
            {
                newCreditForOverpayment = new UserCredit
                {                    
                    UserID = Input.SelectedUserID,
                    CreditDate = Input.PaymentDate!.Value, 
                    Amount = overpaymentAmount,
                    SourcePaymentID = null, 
                    Reason = $"Overpayment on Invoice INV-{invoiceToPay.InvoiceID:D5}.", // Initial reason
                    IsApplied = false,
                    DateCreated = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                };
                _context.UserCredits.Add(newCreditForOverpayment);
            }

            try
            {
                await _context.SaveChangesAsync(); // First main save (Payment, Invoice, potential UserCredit without SourcePaymentID)
                _logger.LogInformation("Initial save successful for payment {PaymentAmount} to invoice {InvoiceId}. PaymentID: {PaymentId}", payment.Amount, invoiceToPay.InvoiceID, payment.PaymentID);

                if (newCreditForOverpayment != null && payment.PaymentID > 0) // payment.PaymentID is now available
                {
                    newCreditForOverpayment.SourcePaymentID = payment.PaymentID;
                    newCreditForOverpayment.Reason = $"Overpayment on Invoice INV-{invoiceToPay.InvoiceID:D5} from Payment ID: {payment.PaymentID}.";
                    // EF Core should be tracking newCreditForOverpayment as it was just Added.
                    // Explicitly marking it as Modified can be done if necessary, but often not if just updated.
                    // _context.UserCredits.Update(newCreditForOverpayment); // Usually not needed if tracked
                    await _context.SaveChangesAsync(); // Second save, specifically for UserCredit.SourcePaymentID and updated Reason
                    _logger.LogInformation("Successfully linked UserCredit {UserCreditId} to PaymentID {PaymentId}", newCreditForOverpayment.UserCreditID, payment.PaymentID);
                }

                // All database operations successful, now set TempData
                TempData["StatusMessage"] = $"Payment of {Input.Amount!.Value:C} applied. Invoice INV-{invoiceToPay.InvoiceID:D5} status: {invoiceToPay.Status}.";
                if (overpaymentAmount > 0)
                {
                    TempData["StatusMessage"] += $"\nOverpayment of {overpaymentAmount:C} credited to account (Credit ID: {newCreditForOverpayment?.UserCreditID}).";
                }
                _logger.LogInformation("Successfully processed payment ID {PaymentID}, updated invoice ID {InvoiceID}.", payment.PaymentID, invoiceToPay.InvoiceID);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error saving payment/invoice/credit data for user {UserId}, invoice {InvoiceId}.", Input.SelectedUserID, Input.SelectedInvoiceID);
                ModelState.AddModelError(string.Empty, "An error occurred while saving payment data. See logs for details.");
                await OnGetAsync(Input.SelectedUserID, ReturnUrl); // Repopulate page data
                return Page();
            }
            // Redundant SaveChangesAsync and TempData setting are removed from here.
            // The status of invoiceToPay was already set before the main SaveChangesAsync.
            
            if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            {
                return Redirect(ReturnUrl);
            }
            return RedirectToPage(new { userId = Input.SelectedUserID, returnUrl = ReturnUrl });
        }
    }
}