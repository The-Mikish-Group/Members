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
        ILogger<RecordPaymentModel> logger) : PageModel // Assuming you used primary constructor before, I'll use explicit for clarity here too
    {
        private readonly ApplicationDbContext _context = context;
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly ILogger<RecordPaymentModel> _logger = logger;

        [BindProperty]
        public InputModel Input { get; set; } = new InputModel();

        public SelectList? UserSelectList { get; set; }
        public string? TargetUserName { get; set; }
        public bool IsUserPreselected { get; set; } = false;

        // To carry over the returnUrl that leads back to EditUser page
        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }


        public class InputModel
        {
            [Required]
            public string SelectedUserID { get; set; } = string.Empty;

            [Display(Name = "For Invoice (Optional ID)")]
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

        public async Task OnGetAsync(string? userId, string? returnUrl)
        {
            _logger.LogInformation("OnGetAsync called for RecordPaymentModel.");
            ReturnUrl = returnUrl;

            if (!string.IsNullOrEmpty(userId))
            {
                var user = await _userManager.FindByIdAsync(userId);
                if (user != null)
                {
                    var userProfile = await _context.UserProfile.FirstOrDefaultAsync(up => up.UserId == userId);
                    TargetUserName = (userProfile != null && !string.IsNullOrWhiteSpace(userProfile.FirstName) && !string.IsNullOrWhiteSpace(userProfile.LastName))
                                     ? $"{userProfile.LastName}, {userProfile.FirstName} ({user.Email})"
                                     : user.UserName ?? user.Email;
                    Input.SelectedUserID = userId;
                    IsUserPreselected = true;
                    _logger.LogInformation("RecordPayment page loaded for pre-selected user: {TargetUserName} (ID: {UserId})", TargetUserName, userId);
                }
                else
                {
                    _logger.LogWarning("RecordPayment: UserID {UserId} provided but user not found.", userId);
                    await PopulateUserSelectList(); // Fallback to dropdown
                }
            }
            else
            {
                await PopulateUserSelectList(); // No userId, show dropdown
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
                    userListItems.Add(new SelectListItem { Value = user.Id, Text = $"{profile.LastName}, {profile.FirstName} ({user.Email})" });
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
            _logger.LogInformation("OnPostAsync called for RecordPaymentModel.");
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("RecordPayment OnPostAsync: ModelState is invalid.");
                if (!IsUserPreselected && string.IsNullOrEmpty(Input.SelectedUserID))
                {
                    await PopulateUserSelectList();
                }
                else if (!IsUserPreselected && !string.IsNullOrEmpty(Input.SelectedUserID))
                {
                    await PopulateUserSelectList();
                }
                else if (IsUserPreselected && !string.IsNullOrEmpty(Input.SelectedUserID))
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
                await PopulateUserSelectList();
                return Page();
            }

            var payment = new Payment
            {
                UserID = Input.SelectedUserID,
                InvoiceID = Input.SelectedInvoiceID,
                PaymentDate = Input.PaymentDate,
                Amount = Input.Amount,
                Method = Input.Method,
                ReferenceNumber = Input.ReferenceNumber,
                Notes = Input.Notes,
                DateRecorded = DateTime.UtcNow
            };

            _context.Payments.Add(payment);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Successfully saved new payment ID {PaymentID} for user {UserName}.", payment.PaymentID, user.UserName);

            TempData["StatusMessage"] = $"Payment of {payment.Amount:C} recorded successfully for user {TargetUserName ?? user.UserName}.";

            // Redirect logic: If ReturnUrl (to EditUser) is present, use it. Otherwise, back to clean RecordPayment page.
            if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            {
                // This ReturnUrl should be the one leading back to EditUser page for this specific user
                // e.g. /Identity/EditUser?id=USER_ID_HERE&returnUrl=ENCODED_USERS_PAGE_URL
                return Redirect(ReturnUrl);
            }
            return RedirectToPage(new { userId = Input.SelectedUserID }); // Stay on RecordPayment, but keep user context if any
        }
    }
}