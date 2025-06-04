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
            [Range(0.01, 1000000.00, ErrorMessage = "Amount must be greater than 0.")]
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
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("AddInvoice OnPostAsync: ModelState is invalid.");
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
                                     ? $"{userProfile.FirstName} {userProfile.LastName} ({userForDisplay.Email})"
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

            var invoice = new Invoice
            {
                UserID = Input.SelectedUserID,
                InvoiceDate = Input.InvoiceDate,
                DueDate = Input.DueDate,
                Description = Input.Description,
                AmountDue = Input.AmountDue,
                AmountPaid = 0,
                Status = InvoiceStatus.Due,
                Type = Input.Type,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };

            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Successfully saved new invoice ID {InvoiceId} for user {UserName}.", invoice.InvoiceID, user.UserName);

            TempData["StatusMessage"] = $"Invoice '{invoice.Description}' created successfully for user {TargetUserName ?? user.UserName}.";

            if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            {
                return RedirectToPage("/EditUser", new { area = "Identity", id = Input.SelectedUserID, returnUrl = ReturnUrl });
            }
            return RedirectToPage();
        }
    }
}
