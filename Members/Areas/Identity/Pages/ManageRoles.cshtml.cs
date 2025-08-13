using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Encodings.Web;

namespace Members.Areas.Identity.Pages
{
    public class ManageRolesModel(UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager, IEmailSender emailSender, ILogger<ManageRolesModel> logger) : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly RoleManager<IdentityRole> _roleManager = roleManager;
        private readonly IEmailSender _emailSender = emailSender;
        private readonly ILogger<ManageRolesModel> _logger = logger;

        [BindProperty(SupportsGet = true)]
        public string? UserId { get; set; } // To hold the ID of the user being edited

        public IdentityUser? UserToEdit { get; set; }

        [BindProperty] // Bind on POST to get selected roles from the form
        public List<RoleViewModel> AllRoles { get; set; } = [];

        // This property will capture the return URL to go back to the EditUser page
        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }

        [TempData]
        public string? StatusMessage { get; set; }


        public class RoleViewModel
        {
            public required string Value { get; set; } // Role Name (used as value)
            public string? Text { get; set; } // Role Name (used for display)
            public bool Selected { get; set; } // Whether the role is selected for the user
        }

        public async Task<IActionResult> OnGetAsync(string? userId, string? returnUrl)
        {
            if (string.IsNullOrEmpty(userId))
            {
                StatusMessage = "Error: User ID is missing for role management.";
                // Redirect to the default Users page if no user ID is provided
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    // Attempt to go back using the original return URL if available
                    return Redirect(returnUrl);
                }
                return RedirectToPage("./Users");
            }

            UserId = userId;
            ReturnUrl = returnUrl; // Capture the return URL

            UserToEdit = await _userManager.FindByIdAsync(UserId);

            if (UserToEdit == null)
            {
                StatusMessage = $"Error: Unable to load user with ID '{UserId}' for role management.";
                if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
                {
                    return Redirect(ReturnUrl);
                }
                return RedirectToPage("./Users");
            }

            await PopulateRoleViewModelsAsync(UserToEdit);

            return Page();
        }

        private async Task PopulateRoleViewModelsAsync(IdentityUser user)
        {
            var roles = await _roleManager.Roles.ToListAsync();
            var userRoles = await _userManager.GetRolesAsync(user);

            AllRoles = [.. roles.Select(role => new RoleViewModel
            {
                Value = role.Name ?? string.Empty,
                Text = role.Name ?? string.Empty,
                Selected = userRoles.Contains(role.Name ?? string.Empty)
            }).OrderBy(r => r.Text)];
        }


        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(UserId))
            {
                StatusMessage = "Error: User ID is missing on post.";
                if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
                {
                    return Redirect(ReturnUrl);
                }
                return RedirectToPage("./Users");
            }

            UserToEdit = await _userManager.FindByIdAsync(UserId);

            if (UserToEdit == null)
            {
                StatusMessage = $"Error: Unable to find user with ID '{UserId}' to update roles.";
                if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
                {
                    return Redirect(ReturnUrl);
                }
                return RedirectToPage("./Users");
            }

            // Repopulate AllRoles if ModelState is invalid to redisplay the page correctly
            if (!ModelState.IsValid)
            {
                await PopulateRoleViewModelsAsync(UserToEdit);
                StatusMessage = "Error: Please fix validation errors."; // Although role selection usually doesn't have validation errors
                return Page();
            }

            var originalRoles = await _userManager.GetRolesAsync(UserToEdit);
            var selectedRoles = AllRoles.Where(r => r.Selected).Select(r => r.Value ?? string.Empty).ToList();

            var rolesToRemove = originalRoles.Except(selectedRoles).ToList();
            var rolesToAdd = selectedRoles.Except(originalRoles).ToList(); // This list contains the roles that are being newly added

            // --- Apply Role Changes ---

            if (rolesToRemove.Count > 0)
            {
                var removeResult = await _userManager.RemoveFromRolesAsync(UserToEdit, rolesToRemove);
                if (!removeResult.Succeeded)
                {
                    // Handle errors
                    foreach (var error in removeResult.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }
                    await PopulateRoleViewModelsAsync(UserToEdit); // Repopulate roles on error
                    StatusMessage = "Error: Failed to remove roles.";
                    return Page();
                }
            }

            if (rolesToAdd.Count > 0)
            {
                var addResult = await _userManager.AddToRolesAsync(UserToEdit, rolesToAdd);
                if (!addResult.Succeeded)
                {
                    // Handle errors
                    foreach (var error in addResult.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }
                    await PopulateRoleViewModelsAsync(UserToEdit); // Repopulate roles on error
                    StatusMessage = "Error: Failed to add roles.";
                    return Page();
                }
            }

            // --- Conditional Email Sending ---
            // Only send email if the "Member" role was explicitly ADDED in this action.
            if (rolesToAdd.Contains("Member"))
            {
                // Get the site name from environment variable
                string siteName = Environment.GetEnvironmentVariable("SITE_NAME_HOA_CLOUD") ?? "Oaks-Village";

                if (string.IsNullOrEmpty(siteName))
                {
                    _logger.LogError("SITE_NAME_HOA_CLOUD environment variable is not set. Using default value.");
                    siteName = "Oaks-Village"; // Fallback to default if environment variable is not set
                }

                // Now check if the user's email is confirmed to send the appropriate email
                if (UserToEdit.EmailConfirmed)
                {
                    if (!string.IsNullOrEmpty(UserToEdit.Email))
                    {
                        await _emailSender.SendEmailAsync(
                                        UserToEdit.Email,
                                        $"Welcome to {siteName} HOA - Your Account is Ready",
                                        "<!DOCTYPE html>" +
                                        "<html lang=\"en\">" +
                                        "<head>" +
                                        "    <meta charset=\"UTF-8\">" +
                                        "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">" +
                                        $"    <title>Welcome to {siteName} HOA</title>" +
                                        "</head>" +
                                        "<body style=\"font-family: sans-serif; line-height: 1.6; margin: 20px;\">" +
                                        "    <p style=\"margin-bottom: 1em;\">Dear Member,</p>" +
                                        $"    <p style=\"margin-bottom: 1em;\">Welcome! Your {siteName} account has been created and is ready for you to access.</p>" +
                                        $"    <p style=\"margin-bottom: 1em;\">You have been granted Member access and can log in to the HOA community portal at <a href=\"https://{siteName}.com\" style=\"color: #007bff; text-decoration: none;\">https://{siteName}.com</a>.</p>" +
                                        "    <div style=\"margin-bottom: 2em; padding: 15px; border: 1px solid #ddd; border-radius: 5px; background-color: #f9f9f9;\">" +
                                        "        <strong style=\"font-size: 1.1em;\">Important Note:</strong> If this account was automatically generated for you, please click the link below to create your password:" +
                                        "        <p style=\"margin-top: 1em;\">" +
                                        $"            <a href=\"https://{siteName}.com/Identity/Account/ForgotPassword\" style=\"background-color:#007bff;color:#fff;padding:10px 15px;text-decoration:none;border-radius:5px;font-weight:bold;display:inline-block;\">" +
                                        "                Click Here to Create Your Password" +
                                        "            </a>" +
                                        "    </div>" +
                                        "    <p style=\"margin-bottom: 1em;\">You will be directed to enter your email address, and a password reset link will be sent to you. This process ensures the security of your account and verifies your email address, preventing unauthorized password creation attempts.</p>" +
                                        $"    <p style=\"margin-bottom: 1em;\">Thank you for being a part of the {siteName} Homeowners Association.</p>" +
                                        "    <p style=\"margin-bottom: 0;\">Sincerely,</p>" +
                                        $"    <p style=\"margin-top: 0;\">The {siteName} HOA <img src=\"https://{siteName}.com/Images/LinkImages/SmallLogo.png\" alt=\"{siteName} HOA Logo\" style=\"vertical-align: middle; margin-left: 3px; height: 35px;\"></p>" +
                                        "</body>" +
                                        "</html>"
                        );
                    }
                }
                else // Email is NOT confirmed
                {
                    if (!string.IsNullOrEmpty(UserToEdit.Email))
                    {
                        var userId = await _userManager.GetUserIdAsync(UserToEdit);
                        var code = await _userManager.GenerateEmailConfirmationTokenAsync(UserToEdit);
                        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                        var callbackUrl = Url.Page(
                            "/Account/ConfirmEmail",
                            pageHandler: null,
                            values: new { area = "Identity", userId, code },
                            protocol: Request.Scheme);

                        if (callbackUrl != null)
                        {
                            await _emailSender.SendEmailAsync(
                                UserToEdit.Email,
                                $"Please Confirm Your Email Address - {siteName} HOA Registration",
                                $"<!DOCTYPE html>" +
                                "<html lang=\"en\">" +
                                "<head>" +
                                "    <meta charset=\"UTF-8\">" +
                                "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">" +
                                $"    <title>Confirm Your Email - {siteName} HOA</title>" +
                                "</head>" +
                                "<body style=\"font-family: sans-serif; line-height: 1.6; margin: 20px;\">" +
                                "    <p style=\"margin-bottom: 1em;\">Dear Member,</p>" +
                                $"    <p style=\"margin-bottom: 1em;\">Thank you for registering with the {siteName} Homeowners Association!</p>" +
                                "    <p style=\"margin-bottom: 1em;\">To complete your registration and activate your account, please confirm your email address by clicking the button below:</p>" +
                                "    <div style=\"margin: 2em 0;\">" +
                                $"        <a href='{HtmlEncoder.Default.Encode(callbackUrl)}' style=\"background-color:#007bff;color:#fff;padding:10px 15px;text-decoration:none;border-radius:5px;font-weight:bold;display:inline-block;\">" +
                                "            Confirm Your Email Address" +
                                "        </a>" +
                                "    </div>" +
                                "    <p style=\"margin-bottom: 1em;\">By confirming your email, you help us ensure the security of your account and allow us to send you important updates and community information. " +
                                $"If you did not register for an account with {siteName} HOA, please disregard this email.</p>" +
                                "    <p style=\"margin-bottom: 0;\">Thank you for being a part of our community.</p>" +
                                "    <p style=\"margin-top: 0;\">Sincerely,</p>" +
                                $"    <p style=\"margin-top: 0;\">The {siteName} HOA Team<img src=\"https://{siteName}.com/Images/LinkImages/SmallLogo.png\" alt=\"{siteName} HOA Logo\" style=\"vertical-align: middle; margin-left: 3px; height: 40px;\"></p>" +
                                "</body>" +
                                "</html>"
                            );
                        }
                    }
                }
            }

            StatusMessage = $"Roles updated successfully for user '{UserToEdit.UserName}'.";

            if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            {
                // ReturnUrl is now expected to be the URL of the EditUser page 
                // (e.g., /Identity/EditUser?id=USER_ID&returnUrl=URL_TO_USERS_LIST)
                return Redirect(ReturnUrl);
            }
            else
            {
                return RedirectToPage("./EditUser", new { id = UserId });
            }
        }

        // Optional: Add a Cancel button handler to go back without saving changes
        public IActionResult OnPostCancel()
        {
            StatusMessage = "Role changes cancelled.";

            if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            {
                // ReturnUrl should point back to the EditUser page
                return Redirect(ReturnUrl);
            }
            else
            {
                // Fallback if ReturnUrl is missing.                
                return RedirectToPage("./EditUser", new { id = UserId });
            }
        }
    }
}