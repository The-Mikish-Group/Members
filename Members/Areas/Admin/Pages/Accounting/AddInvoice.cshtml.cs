using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering; // Keep for SelectList if used as fallback
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging; // Assuming you still have logger
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
namespace Members.Areas.Admin.Pages.Accounting
{
    [Authorize(Roles = "Admin,Manager")]
    public class AddInvoiceModel(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager,
        ILogger<AddInvoiceModel> logger) : PageModel // Primary constructor removed for clarity if not strictly needed by your current version
    {
        private readonly ApplicationDbContext _context = context;
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly ILogger<AddInvoiceModel> _logger = logger;
        [BindProperty]
        public InputModel Input { get; set; } = new InputModel();
        // To display the name of the user if userId is passed
        public string? TargetUserName { get; set; }
        public bool IsUserPreselected { get; set; } = false;
        // UserSelectList is kept in case this page is ever accessed directly without a userId
        public SelectList? UserSelectList { get; set; }
        // ReturnUrl to get back to EditUser or Users page
        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }
        public class InputModel
        {
            // SelectedUserID will be set from query string if provided, or from dropdown if not
            [Required]
            public string SelectedUserID { get; set; } = string.Empty;
            [Required]
            [DataType(DataType.Date)]
            [Display(Name = "Invoice Date")]
            public DateTime InvoiceDate { get; set; } = DateTime.Today;
            [Required]
            [DataType(DataType.Date)]
            [Display(Name = "Due Date")]
            public DateTime DueDate { get; set; } = DateTime.Today.AddDays(30);
            [Required]
            [StringLength(200)]
            [Display(Name = "Description")]
            public string Description { get; set; } = string.Empty;
            [Required]
            [Range(0.00, 1000000.00, ErrorMessage = "Amount must be 0 or greater.")] // Changed 0.01 to 0.00
            [DataType(DataType.Currency)]
            [Display(Name = "Amount Due")]
            public decimal AmountDue { get; set; }
            [Required]
            [Display(Name = "Invoice Type")]
            public InvoiceType Type { get; set; } = InvoiceType.MiscCharge;
        }
        public async Task OnGetAsync(string? userId, string? returnUrl)
        {
            ReturnUrl = returnUrl; // Capture the return URL to pass back to EditUser
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
                    _logger.LogInformation("AddInvoice page loaded for pre-selected user: {TargetUserName} (ID: {UserId})", TargetUserName, userId);
                }
                else
                {
                    _logger.LogWarning("AddInvoice: UserID {UserId} provided but user not found.", userId);
                    // UserID provided but not valid, fall back to showing dropdown
                    await PopulateUserSelectList();
                }
            }
            else
            {
                // No userId passed, populate dropdown for manual selection
                await PopulateUserSelectList();
            }
        }
        private async Task PopulateUserSelectList()
        {
            var memberRoleName = "Member";
            var usersInMemberRole = await _userManager.GetUsersInRoleAsync(memberRoleName);
            _logger.LogInformation("PopulateUserSelectList: Found {UserCount} users in role '{RoleName}'.", usersInMemberRole?.Count ?? 0, memberRoleName);
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
            _logger.LogInformation("OnPostAsync called for AddInvoiceModel.");
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("AddInvoice OnPostAsync: ModelState is invalid.");
                if (!IsUserPreselected) await PopulateUserSelectList();
                else if (!string.IsNullOrEmpty(Input.SelectedUserID)) // Repopulate TargetUserName if preselected
                {
                    var userForDisplay = await _userManager.FindByIdAsync(Input.SelectedUserID);
                    if (userForDisplay != null)
                    {
                        var userProfile = await _context.UserProfile.FirstOrDefaultAsync(up => up.UserId == Input.SelectedUserID);
                        TargetUserName = (userProfile != null && !string.IsNullOrWhiteSpace(userProfile.FirstName) && !string.IsNullOrWhiteSpace(userProfile.LastName))
                                         ? $"{userProfile.LastName}, {userProfile.FirstName} ({userForDisplay.Email})"
                                         : userForDisplay.UserName ?? userForDisplay.Email;
                    }
                }
                return Page();
            }
            var user = await _userManager.FindByIdAsync(Input.SelectedUserID);
            if (user == null)
            {
                ModelState.AddModelError("Input.SelectedUserID", "Selected user not found.");
                if (!IsUserPreselected) await PopulateUserSelectList();
                return Page();
            }
            var invoice = new Invoice
            {
                UserID = Input.SelectedUserID,
                InvoiceDate = Input.InvoiceDate,
                DueDate = Input.DueDate,
                Description = Input.Description,
                AmountDue = Input.AmountDue,
                AmountPaid = 0, // Initial AmountPaid
                Status = InvoiceStatus.Due, // Initial status
                Type = Input.Type,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };
            _context.Invoices.Add(invoice);
            try
            {
                // --- FIRST SAVE: Get the InvoiceID for the new invoice ---
                await _context.SaveChangesAsync();
                _logger.LogInformation("Saved new invoice ID {invoice.InvoiceID} for user {user.UserName} before credit application.", invoice.InvoiceID, user.UserName);
                // --- APPLY AVAILABLE CREDITS ---
                decimal remainingAmountDueOnNewInvoice = invoice.AmountDue; // Should be full AmountDue at this point
                string appliedCreditsSummary = "";
                bool creditsWereUpdated = false;
                if (remainingAmountDueOnNewInvoice > 0)
                {
                    var availableCredits = await _context.UserCredits
                        .Where(uc => uc.UserID == Input.SelectedUserID && !uc.IsApplied && uc.Amount > 0)
                        .OrderBy(uc => uc.CreditDate) // Apply oldest credits first
                        .ToListAsync();
                    if (availableCredits.Count != 0)
                    {
                        _logger.LogInformation("User {user.UserName} has {availableCredits.Count} available credits. Attempting to apply to new invoice ID {invoice.InvoiceID}.", user.UserName, availableCredits.Count, invoice.InvoiceID);
                        foreach (var credit in availableCredits)
                        {
                            if (remainingAmountDueOnNewInvoice <= 0) break; // Invoice is fully paid by credits
                            //decimal amountToApplyFromThisCredit;
                            decimal amountActuallyApplied = Math.Min(remainingAmountDueOnNewInvoice, credit.Amount);

                            if (amountActuallyApplied > 0) // Only proceed if there's an amount to apply
                            {
                                // Create CreditApplication record
                                var creditApplication = new CreditApplication
                                {
                                    UserCreditID = credit.UserCreditID,
                                    InvoiceID = invoice.InvoiceID,
                                    AmountApplied = amountActuallyApplied,
                                    ApplicationDate = DateTime.UtcNow,
                                    Notes = $"Auto-applied during new invoice creation (INV-{invoice.InvoiceID:D5}). Original UserCredit Reason: {credit.Reason}"
                                };
                                _context.CreditApplications.Add(creditApplication);
                                _logger.LogInformation("CreditApplication created: UCID {UserCreditID} to INV {InvoiceID}, Amount {AmountApplied}, during new invoice creation.", credit.UserCreditID, invoice.InvoiceID, amountActuallyApplied);

                                decimal creditAmountBeforeThisApplication = credit.Amount;

                                credit.Amount -= amountActuallyApplied;
                                credit.LastUpdated = DateTime.UtcNow;
                                // string applicationNoteForCredit = $"Utilized {amountActuallyApplied:C} for new INV-{invoice.InvoiceID:D5} (CA_ID {creditApplication.CreditApplicationID}) on {DateTime.UtcNow:yyyy-MM-dd}.";

                                if (credit.Amount <= 0)
                                {
                                    credit.IsApplied = true;
                                    credit.Amount = 0;
                                    credit.AppliedDate = DateTime.UtcNow;
                                    // credit.AppliedToInvoiceID = invoice.InvoiceID; // Less critical with CreditApplications table
                                    // applicationNoteForCredit += " Credit fully utilized."; // Not needed for UserCredit.ApplicationNotes
                                    _logger.LogInformation("UserCredit UCID#{UserCreditID} fully utilized by new invoice INV-{InvoiceID}. Prev Bal: {PrevBal}, Applied: {AppliedAmount}", credit.UserCreditID, invoice.InvoiceID, creditAmountBeforeThisApplication, amountActuallyApplied);
                                }
                                else
                                {
                                    credit.IsApplied = false;
                                     _logger.LogInformation("UserCredit UCID#{UserCreditID} partially utilized by new invoice INV-{InvoiceID}. Prev Bal: {PrevBal}, Applied: {AppliedAmount}, Rem Bal: {RemBal}", credit.UserCreditID, invoice.InvoiceID, creditAmountBeforeThisApplication, amountActuallyApplied, credit.Amount);
                                }
                                // UserCredit.ApplicationNotes should retain its original creation reason.
                                // No longer appending detailed application notes here.
                                // credit.ApplicationNotes = (string.IsNullOrEmpty(credit.ApplicationNotes) ? "" : credit.ApplicationNotes + "; ") + applicationNoteForCredit;
                                _context.UserCredits.Update(credit);

                                invoice.AmountPaid += amountActuallyApplied;
                                remainingAmountDueOnNewInvoice -= amountActuallyApplied;
                                // invoice.LastUpdated will be set before SaveChangesAsync below

                                creditsWereUpdated = true;
                                if (string.IsNullOrEmpty(appliedCreditsSummary)) appliedCreditsSummary = "\nCredits applied: ";
                                appliedCreditsSummary += $"{amountActuallyApplied:C} (from Credit UCID#{credit.UserCreditID} via CA_ID#{creditApplication.CreditApplicationID}); ";
                            }
                        }
                    }
                    // Update invoice status after applying credits
                    if (creditsWereUpdated || invoice.AmountPaid > 0) // ensure LastUpdated is set if AmountPaid changed
                    {
                        invoice.LastUpdated = DateTime.UtcNow;
                    }

                    if (invoice.AmountPaid >= invoice.AmountDue)
                    {
                        invoice.Status = InvoiceStatus.Paid;
                        invoice.AmountPaid = invoice.AmountDue; // Ensure it doesn't exceed AmountDue
                    }
                    else if (invoice.AmountPaid > 0 && invoice.DueDate < DateTime.Today.AddDays(-1) && invoice.Status == InvoiceStatus.Due) // Partially paid and overdue
                    {
                        invoice.Status = InvoiceStatus.Overdue;
                    }
                    else if (invoice.AmountPaid <= 0 && invoice.DueDate < DateTime.Today.AddDays(-1)) // Not paid at all and overdue
                    {
                        invoice.Status = InvoiceStatus.Overdue;
                    }
                    // If credits were updated OR if the invoice's AmountPaid/Status changed due to credits
                    if (creditsWereUpdated || invoice.AmountPaid > 0)
                    {
                        invoice.LastUpdated = DateTime.UtcNow;
                        _context.Invoices.Update(invoice); // Ensure invoice is marked for update if AmountPaid changed
                        await _context.SaveChangesAsync(); // --- SECOND SAVE: For credit updates and invoice payment/status updates ---
                        _logger.LogInformation("Updated invoice ID {invoice.InvoiceID} and any applied UserCredits.", invoice.InvoiceID);
                    }
                    string successMessage = $"Invoice '{invoice.Description}' (INV-{invoice.InvoiceID:D5}) created. Status: {invoice.Status}. Amount Paid: {invoice.AmountPaid:C}.";
                    if (!string.IsNullOrEmpty(appliedCreditsSummary))
                    {
                        successMessage += appliedCreditsSummary;
                    }
                    TempData["StatusMessage"] = successMessage;
                }
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error saving new invoice or applying credits.");
                ModelState.AddModelError(string.Empty, "An error occurred while saving data. See logs for details.");
                if (!IsUserPreselected) await PopulateUserSelectList();
                else if (!string.IsNullOrEmpty(Input.SelectedUserID))
                {
                    var userForDisplay = await _userManager.FindByIdAsync(Input.SelectedUserID);
                    if (userForDisplay != null)
                    {
                        var userProfile = await _context.UserProfile.FirstOrDefaultAsync(up => up.UserId == Input.SelectedUserID);
                        TargetUserName = (userProfile != null && !string.IsNullOrWhiteSpace(userProfile.FirstName) && !string.IsNullOrWhiteSpace(userProfile.LastName))
                                            ? $"{userProfile.LastName}, {userProfile.FirstName} ({userForDisplay.Email})"
                                            : userForDisplay.UserName ?? userForDisplay.Email;
                    }
                }
                return Page();
            }
            if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            {
                return Redirect(ReturnUrl);
            }
            return RedirectToPage();
        }
    }
}
