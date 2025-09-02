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
namespace Members.Areas.Admin.Pages.AccountsReceivable
{
    public class UserCreditViewModel
    {
        public int UserCreditID { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime CreditDate { get; set; }
    }
    [Authorize(Roles = "Admin,Manager,DataEntry")]
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
            [StringLength(1000)]
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
                                    i.Status != InvoiceStatus.Draft && // Exclude Draft invoices
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
            if (invoiceToApplyTo.Status == InvoiceStatus.Draft)
            {
                ModelState.AddModelError("Input.SelectedInvoiceID", $"Credits cannot be applied to Draft invoices (INV-{invoiceToApplyTo.InvoiceID}). Please finalize the invoice first.");
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

            // Create CreditApplication record
            var creditApplication = new CreditApplication
            {
                UserCreditID = creditToApply.UserCreditID,
                InvoiceID = invoiceToApplyTo.InvoiceID,
                AmountApplied = actualAmountToApply,
                ApplicationDate = DateTime.UtcNow,
                Notes = $"Applied by admin from RecordPayment page. UserCredit original reason: {creditToApply.Reason}"
            };
            _context.CreditApplications.Add(creditApplication);

            decimal originalCreditAmountForLog = creditToApply.Amount;
            creditToApply.Amount -= actualAmountToApply;
            creditToApply.LastUpdated = DateTime.UtcNow;
             
            if (creditToApply.Amount <= 0)
            {
                creditToApply.IsApplied = true; // Mark as fully applied if balance is zero or less
                creditToApply.Amount = 0;       // Ensure amount doesn't go negative
                creditToApply.AppliedDate = DateTime.UtcNow; // Optional: can signify date of full application
                // creditToApply.AppliedToInvoiceID = invoiceToApplyTo.InvoiceID; // This field becomes less critical
                _logger.LogInformation("CreditID {CreditID} fully applied. Original amount: {OriginalAmount}, Applied now: {AppliedNow}, Remaining amount: {RemainingAmount}",

                    creditToApply.UserCreditID, originalCreditAmountForLog, actualAmountToApply, creditToApply.Amount);
            }
            else
            {
                creditToApply.IsApplied = false; // Ensure IsApplied is false if there's a remaining balance
                _logger.LogInformation("CreditID {CreditID} partially applied. Original amount: {OriginalAmount}, Applied now: {AppliedNow}, Remaining amount: {RemainingAmount}",
                    creditToApply.UserCreditID, originalCreditAmountForLog, actualAmountToApply, creditToApply.Amount);
            }


            // UserCredit.ApplicationNotes will primarily store the original reason or voiding notes.
            // Details of this specific application are in CreditApplication.Notes.
            // We don't need to append detailed application notes to UserCredit.ApplicationNotes anymore.
            // If a general note about its current state is needed, it can be set here, but not appended cumulatively.
            // For now, we'll preserve the existing ApplicationNotes (which should be its creation reason)
            // unless the credit is fully utilized or voided later.

            _context.UserCredits.Update(creditToApply);

            try
            {
                await _context.SaveChangesAsync();

                TempData["StatusMessage"] = $"{actualAmountToApply:C} from Credit ID {creditToApply.UserCreditID} (New App. ID: {creditApplication.CreditApplicationID}) applied to Invoice INV-{invoiceToApplyTo.InvoiceID:D5}. Invoice status: {invoiceToApplyTo.Status}.";
                if (creditToApply.IsVoided) // Should not happen as we fetch non-voided credits
                {
                     TempData["StatusMessage"] += $" Credit ID {creditToApply.UserCreditID} is VOIDED.";
                }
                else if (creditToApply.Amount <= 0)
                {
                    TempData["StatusMessage"] += $" Credit ID {creditToApply.UserCreditID} is now fully utilized.";
                }
                else
                {
                    TempData["StatusMessage"] += $" Credit ID {creditToApply.UserCreditID} has {creditToApply.Amount:C} remaining.";
                }
                _logger.LogInformation("Credit application successful. CreditApplicationID: {CreditAppID}, UserCreditID: {UserCreditID}, InvoiceID: {InvoiceID}, AmountApplied: {AmountApplied}",
                    creditApplication.CreditApplicationID, creditToApply.UserCreditID, invoiceToApplyTo.InvoiceID, actualAmountToApply);
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
            if (invoiceToPay.Status == InvoiceStatus.Draft)
            {
                ModelState.AddModelError("Input.SelectedInvoiceID", $"Payments cannot be recorded for Draft invoices (INV-{invoiceToPay.InvoiceID}). Please finalize the invoice first.");
                await OnGetAsync(Input.SelectedUserID, ReturnUrl); // Repopulate needed page data
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

            UserCredit? overpaymentEventCredit = null;
            List<CreditApplication> newCreditApplications = [];

            // Save the payment first to get its ID, needed for SourcePaymentID on UserCredit
            _context.Invoices.Update(invoiceToPay); // Ensure invoiceToPay is tracked for update

            try
            {
                await _context.SaveChangesAsync(); // Save Payment and updated primary Invoice
                _logger.LogInformation("Payment {PaymentId} and primary Invoice {InvoiceId} saved. Overpayment amount: {OverpaymentAmount}",
                                       payment.PaymentID, invoiceToPay.InvoiceID, overpaymentAmount);

                if (overpaymentAmount > 0)
                {
                    _logger.LogInformation("Overpayment of {OverpaymentAmount} occurred for User {UserId} from Payment {PaymentId}. Will create a UserCredit and attempt to apply to other due invoices.",
                                           overpaymentAmount, Input.SelectedUserID, payment.PaymentID);

                    overpaymentEventCredit = new UserCredit
                    {
                        UserID = Input.SelectedUserID,
                        CreditDate = Input.PaymentDate!.Value,
                        Amount = overpaymentAmount, // Full overpayment amount initially
                        SourcePaymentID = payment.PaymentID,
                        Reason = $"Overpayment from Payment P{payment.PaymentID} on Invoice INV-{invoiceToPay.InvoiceID:D5}.",
                        IsApplied = false, // Will be set to true if fully consumed by other invoices
                        DateCreated = DateTime.UtcNow,
                        LastUpdated = DateTime.UtcNow
                    };
                    _context.UserCredits.Add(overpaymentEventCredit);
                    await _context.SaveChangesAsync(); // Save UserCredit to get its ID
                    _logger.LogInformation("Created UserCredit {UserCreditId} for overpayment amount {OverpaymentAmount}.",
                                           overpaymentEventCredit.UserCreditID, overpaymentAmount);

                    var otherDueInvoices = await _context.Invoices
                        .Where(i => i.UserID == Input.SelectedUserID &&
                                     i.InvoiceID != invoiceToPay.InvoiceID &&
                                     (i.Status == InvoiceStatus.Due || i.Status == InvoiceStatus.Overdue) &&
                                     i.AmountPaid < i.AmountDue)
                        .OrderBy(i => i.DueDate)
                        .ToListAsync();

                    if (otherDueInvoices.Count != 0)
                    {
                        _logger.LogInformation("Found {OtherInvoiceCount} other due/overdue invoices for User {UserId} to apply overpayment from UserCredit {UserCreditId}.",
                                               otherDueInvoices.Count, Input.SelectedUserID, overpaymentEventCredit.UserCreditID);

                        foreach (var otherInvoice in otherDueInvoices)
                        {
                            if (overpaymentEventCredit.Amount <= 0) break; // All of the overpayment credit has been applied

                            decimal amountNeededForOtherInvoice = otherInvoice.AmountDue - otherInvoice.AmountPaid;
                            decimal amountToApplyToOtherInvoice = Math.Min(overpaymentEventCredit.Amount, amountNeededForOtherInvoice);

                            if (amountToApplyToOtherInvoice > 0)
                            {
                                otherInvoice.AmountPaid += amountToApplyToOtherInvoice;
                                otherInvoice.LastUpdated = DateTime.UtcNow;
                                if (otherInvoice.AmountPaid >= otherInvoice.AmountDue)
                                {
                                    otherInvoice.Status = InvoiceStatus.Paid;
                                    otherInvoice.AmountPaid = otherInvoice.AmountDue; // Cap
                                }
                                else if (otherInvoice.DueDate < DateTime.Today.AddDays(-1) && otherInvoice.Status == InvoiceStatus.Due)
                                {
                                    otherInvoice.Status = InvoiceStatus.Overdue;
                                }
                                _context.Invoices.Update(otherInvoice);

                                var creditApplication = new CreditApplication
                                {
                                    UserCreditID = overpaymentEventCredit.UserCreditID,
                                    InvoiceID = otherInvoice.InvoiceID,
                                    AmountApplied = amountToApplyToOtherInvoice,
                                    ApplicationDate = DateTime.UtcNow,
                                    Notes = $"Applied from overpayment (Payment P{payment.PaymentID}, UserCredit UC{overpaymentEventCredit.UserCreditID})"
                                };
                                newCreditApplications.Add(creditApplication); // Add to list, will batch add later
                                _context.CreditApplications.Add(creditApplication);


                                overpaymentEventCredit.Amount -= amountToApplyToOtherInvoice;
                                payment.Notes = (payment.Notes ?? "") + $" Applied ${amountToApplyToOtherInvoice:F2} from UC{overpaymentEventCredit.UserCreditID} to INV-{otherInvoice.InvoiceID:D5}.";
                                _logger.LogInformation("Recorded CreditApplication: {AmountApplied} from UserCredit {UserCreditId} to Invoice {OtherInvoiceId}. UserCredit remaining: {UserCreditRemaining}",
                                                       amountToApplyToOtherInvoice, overpaymentEventCredit.UserCreditID, otherInvoice.InvoiceID, overpaymentEventCredit.Amount);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogInformation("No other due/overdue invoices found for User {UserId} to apply overpayment from UserCredit {UserCreditId}.",

                                               Input.SelectedUserID, overpaymentEventCredit.UserCreditID);
                    }

                    // Update the overpaymentEventCredit state after applications
                    if (overpaymentEventCredit.Amount <= 0)
                    {
                        overpaymentEventCredit.IsApplied = true;
                        overpaymentEventCredit.Amount = 0; // Ensure it's not negative
                        overpaymentEventCredit.AppliedDate = DateTime.UtcNow;
                        _logger.LogInformation("UserCredit {UserCreditId} fully consumed by applying to other invoices.", overpaymentEventCredit.UserCreditID);
                    }
                    overpaymentEventCredit.LastUpdated = DateTime.UtcNow;
                    _context.UserCredits.Update(overpaymentEventCredit);
                }

                // Final truncation safeguard for payment notes (might have been updated)
                const int dbColumnMaxLength = 1000;
                if (payment.Notes != null && payment.Notes.Length > dbColumnMaxLength)
                {
                    _logger.LogWarning("Payment.Notes for UserID {UserId} (PaymentID {PaymentId}) was truncated from {OriginalLength} to {MaxLength} characters.",
                                       payment.UserID, payment.PaymentID, payment.Notes.Length, dbColumnMaxLength);
                    payment.Notes = payment.Notes[..dbColumnMaxLength];
                }
                _context.Payments.Update(payment); // Ensure payment notes changes are saved

                await _context.SaveChangesAsync(); // Save CreditApplications, updated UserCredit, and other Invoices
                _logger.LogInformation("Successfully saved credit applications and updated overpayment UserCredit {UserCreditId}.", overpaymentEventCredit?.UserCreditID);

                // Build Status Message
                var statusMessageBuilder = new System.Text.StringBuilder();
                statusMessageBuilder.Append($"Payment of {Input.Amount!.Value:C} (P{payment.PaymentID}) processed for Invoice INV-{invoiceToPay.InvoiceID:D5} (Status: {invoiceToPay.Status}).");

                if (overpaymentEventCredit != null)
                {
                    decimal totalAppliedToOthersFromOverpayment = overpaymentAmount - overpaymentEventCredit.Amount;
                    if (totalAppliedToOthersFromOverpayment > 0)
                    {
                        statusMessageBuilder.Append($" {totalAppliedToOthersFromOverpayment:C} of the overpayment (from UC{overpaymentEventCredit.UserCreditID}) was automatically applied to other due invoices.");
                    }

                    if (overpaymentEventCredit.Amount > 0) // If there's still a balance on the overpayment credit
                    {
                        statusMessageBuilder.Append($" Remaining overpayment of ${overpaymentEventCredit.Amount:C} credited to account (UC{overpaymentEventCredit.UserCreditID}).");
                    }
                    else if (overpaymentAmount > 0 && overpaymentEventCredit.Amount <= 0) // Overpayment occurred and was fully used
                    {
                         if(totalAppliedToOthersFromOverpayment == 0 && overpaymentAmount > 0) {
                            // This case means overpayment occurred but for some reason nothing was applied to others, and no credit remains.
                            // This implies the overpaymentEventCredit was created for the full amount and still exists with that amount.
                            // This should not happen if the logic above is correct; if overpaymentAmount > 0 and totalAppliedToOthers is 0, then overpaymentEventCredit.Amount should be > 0.
                            // However, to be safe, if overpaymentEventCredit.Amount is indeed 0 here, it means it was consumed.
                             statusMessageBuilder.Append($" The overpayment (UC{overpaymentEventCredit.UserCreditID}) was fully utilized.");
                         } else if (totalAppliedToOthersFromOverpayment > 0) {
                            // This is covered by the first part of the if-else if.
                         }
                    }
                }
                TempData["StatusMessage"] = statusMessageBuilder.ToString();
                _logger.LogInformation("Successfully processed payment ID {PaymentID}. Final status message: {StatusMessage}",
                                       payment.PaymentID, TempData["StatusMessage"]);
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

        // Simplified payment method for DataEntry role - auto-applies to oldest invoice
        public async Task<IActionResult> OnPostSimplePaymentAsync()
        {
            _logger.LogInformation("OnPostSimplePaymentAsync called. UserID: {SelectedUserID}, Amount: {Amount}", Input.SelectedUserID, Input.Amount);
            
            // Basic validation
            if (!Input.PaymentDate.HasValue)
            {
                ModelState.AddModelError("Input.PaymentDate", "Payment Date is required.");
            }
            if (!Input.Amount.HasValue || Input.Amount.Value <= 0)
            {
                ModelState.AddModelError("Input.Amount", "Payment Amount is required and must be greater than 0.");
            }
            if (!Input.Method.HasValue)
            {
                ModelState.AddModelError("Input.Method", "Payment Method is required.");
            }
            
            if (!ModelState.IsValid)
            {
                await OnGetAsync(Input.SelectedUserID, ReturnUrl);
                return Page();
            }

            var user = await _userManager.FindByIdAsync(Input.SelectedUserID);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "User not found.");
                await OnGetAsync(Input.SelectedUserID, ReturnUrl);
                return Page();
            }

            // Get the oldest unpaid invoice automatically (ordered by InvoiceDate, then by InvoiceID for tie-breaking)
            var oldestInvoice = await _context.Invoices
                .Where(i => i.UserID == Input.SelectedUserID &&
                           i.Status != InvoiceStatus.Cancelled &&
                           i.Status != InvoiceStatus.Draft &&
                           i.AmountPaid < i.AmountDue)
                .OrderBy(i => i.InvoiceDate)
                .ThenBy(i => i.InvoiceID)
                .FirstOrDefaultAsync();

            if (oldestInvoice == null)
            {
                TempData["StatusMessage"] = "No open invoices found for this member. Payment could not be processed.";
                return RedirectToPage(new { userId = Input.SelectedUserID, returnUrl = ReturnUrl });
            }

            // Auto-assign the oldest invoice
            Input.SelectedInvoiceID = oldestInvoice.InvoiceID;

            // Calculate amounts
            decimal paymentAmount = Input.Amount!.Value;
            decimal amountRemaining = oldestInvoice.AmountDue - oldestInvoice.AmountPaid;
            decimal totalOutstanding = await _context.Invoices
                .Where(i => i.UserID == Input.SelectedUserID &&
                           i.Status != InvoiceStatus.Cancelled &&
                           i.Status != InvoiceStatus.Draft &&
                           i.AmountPaid < i.AmountDue)
                .SumAsync(i => i.AmountDue - i.AmountPaid);

            // Create notification messages
            string notificationMessage = "";
            if (paymentAmount < amountRemaining)
            {
                notificationMessage = $"Payment of {paymentAmount:C} applied to invoice INV-{oldestInvoice.InvoiceID:D5}. " +
                                    $"This invoice still has {(amountRemaining - paymentAmount):C} remaining. " +
                                    $"Total outstanding balance: {(totalOutstanding - paymentAmount):C}.";
            }
            else if (paymentAmount > totalOutstanding)
            {
                notificationMessage = $"Payment of {paymentAmount:C} exceeds total outstanding balance of {totalOutstanding:C}. " +
                                    $"Payment will be applied to all open invoices starting with the oldest, " +
                                    $"and any remaining amount will be credited to the account.";
            }
            else if (paymentAmount > amountRemaining)
            {
                decimal remainingAfterFirst = paymentAmount - amountRemaining;
                notificationMessage = $"Payment of {paymentAmount:C} exceeds the oldest invoice amount of {amountRemaining:C}. " +
                                    $"The remaining {remainingAfterFirst:C} will be automatically applied to other open invoices.";
            }

            // Store notification for display after processing
            TempData["PaymentNotification"] = notificationMessage;

            // Call the existing payment processing method
            return await OnPostRecordNewPaymentAsync();
        }
    }
}