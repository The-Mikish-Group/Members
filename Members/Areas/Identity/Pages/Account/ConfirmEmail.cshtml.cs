#nullable disable

using Members.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Members.Areas.Identity.Pages.Account
{
    public class ConfirmEmailModel(
                UserManager<IdentityUser> userManager,
                IEmailSender emailSender,
                ApplicationDbContext dbContext,
                RoleManager<IdentityRole> roleManager,
                ILogger<ConfirmEmailModel> logger) : PageModel // Add logger parameter
    {
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly IEmailSender _emailSender = emailSender;
        private readonly ApplicationDbContext _dbContext = dbContext;
        private readonly RoleManager<IdentityRole> _roleManager = roleManager;
        private readonly ILogger<ConfirmEmailModel> _logger = logger; // Add this line

        [TempData]
        public string StatusMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(string userId, string code)
        {
            if (userId == null || code == null)
            {
                return RedirectToPage("/Index");
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{userId}'.");
            }

            code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            var result = await _userManager.ConfirmEmailAsync(user, code);
            StatusMessage = result.Succeeded ? "Thank you for confirming your email." : "Error confirming your email.";

            if (result.Succeeded)
            {
                string emailSubjectUser;
                string emailBodyUser;

                // Check if the user has the "Member" role
                bool isMember = await _userManager.IsInRoleAsync(user, "Member");

                if (isMember)
                {
                    emailSubjectUser = "Welcome to Oaks-Village HOA - Your Account is Confirmed";
                    emailBodyUser = $"<!DOCTYPE html>" +
                      "<html lang=\"en\">" +
                      "<head>" +
                      "    <meta charset=\"UTF-8\">" +
                      "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">" +
                      "    <title>Welcome to Oaks-Village HOA</title>" +
                      "</head>" +
                      "<body style=\"font-family: sans-serif; line-height: 1.6; margin: 20px;\">" +
                      "    <p style=\"margin-bottom: 1em;\">Dear Member,</p>" +
                      "    <p style=\"margin-bottom: 1em;\">Welcome to the Oaks-Village Homeowners Association!</p>" +
                      "    <p style=\"margin-bottom: 1em;\">Thank you for confirming your email address. Your account is now <strong style=\"font-weight: bold;\">Active</strong>.</p>" +
                      "    <p style=\"margin-bottom: 1em;\">You can log in to the community portal at <a href=\"https://Oaks-Village.com\" style=\"color: #007bff; text-decoration: none;\">https://Oaks-Village.com</a>.</p>" +
                      "    <p style=\"margin-bottom: 0;\">Thank you for being a part of our community.</p>" +
                      "    <p style=\"margin-top: 0;\">Sincerely,</p>" +
                      "    <p style=\"margin-top: 0;\">The Oaks-Village HOA Team<img src=\"https://Oaks-Village.com/Images/LinkImages/SmallLogo.png\" alt=\"Oaks-Village HOA Logo\" style=\"vertical-align: middle; margin-left: 3px; height: 40px;\"></p>" +
                      "</body>" +
                      "</html>"; ;
                }
                else
                {
                    emailSubjectUser = "Email Address Confirmed - Account Pending Authorization";
                    emailBodyUser = $"<!DOCTYPE html>" +
                        "<html lang=\"en\">" +
                        "<head>" +
                        "    <meta charset=\"UTF-8\">" +
                        "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">" +
                        "    <title>Email Address Confirmed - Oaks-Village HOA</title>" +
                        "</head>" +
                        "<body style=\"font-family: sans-serif; line-height: 1.6; margin: 20px;\">" +
                        "    <p style=\"margin-bottom: 1em;\">Dear Member,</p>" +
                        "    <p style=\"margin-bottom: 1em;\">Thank you for confirming your email address.</p>" +
                        "    <p style=\"margin-bottom: 1em;\">Your account registration is now complete. However, a staff member must authorize your account, and this process could take up to 24 hours.</p>" +
                        "    <p style=\"margin-bottom: 1em;\">Once your account has been authorized, you will receive a separate <strong style=\"font-weight: bold;\">Welcome Email</strong> with login instructions. Please be patient as we are a small team of volunteers.</p>" +
                        "    <p style=\"margin-bottom: 0;\">Thank you for your understanding.</p>" +
                        "    <p style=\"margin-top: 0;\">Sincerely,</p>" +
                        "    <p style=\"margin-top: 0;\">The Oaks-Village HOA Team<img src=\"https://Oaks-Village.com/Images/LinkImages/SmallLogo.png\" alt=\"Oaks-Village HOA Logo\" style=\"vertical-align: middle; margin-left: 3px; height: 40px;\"></p>" +
                        "</body>" +
                        "</html>";
                }

                // Send confirmation email to the user
                await _emailSender.SendEmailAsync(
                    user.Email,
                    emailSubjectUser,
                    emailBodyUser
                );

                // Send notification email to OaksVillage@Oaks-village.com
                var userProfile = await _dbContext.UserProfile.FirstOrDefaultAsync(up => up.UserId == user.Id);
                if (userProfile != null)
                {
                    string emailSubjectAdmin = "Oaks-Village HOA - Email Confirmation Notification";
                    string emailBodyAdmin;
                    string adminEmail = Environment.GetEnvironmentVariable("SMTP_USERNAME_OAKS_VILLAGE");

                    if (string.IsNullOrEmpty(adminEmail))
                    {
                        // Handle the case where the environment variable is not set
                        _logger.LogError("SMTP_USERNAME_OAKS_VILLAGE environment variable is not set. Cannot send admin notification.");
                        return Page(); // Or handle this error as appropriate for your application
                    }

                    if (isMember)
                    {
                        emailBodyAdmin = $"<!DOCTYPE html>" +
                                         "<html lang=\"en\">" +
                                         "<head>" +
                                         "    <meta charset=\"UTF-8\">" +
                                         "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">" +
                                         "    <title>Email Confirmation Notification</title>" +
                                         "</head>" +
                                         "<body style=\"font-family: sans-serif; line-height: 1.6; margin: 20px;\">" +
                                         "    <p style=\"margin-bottom: 1em;\">Dear Oaks-Village HOA Administrator,</p>" +
                                         "    <p style=\"margin-bottom: 1em;\">This is a notification that a user has confirmed their email address.</p>" +
                                         "    <p style=\"margin-bottom: 1em;\"><strong>Member Account Activated:</strong></p>" +
                                         "    <ul style=\"margin-left: 20px; margin-bottom: 1em;\">" +
                                         $"        <li><strong>Name:</strong> {userProfile.FirstName} {userProfile.MiddleName} {userProfile.LastName}</li>" +
                                         $"        <li><strong>Email:</strong> {user.Email}</li>" +
                                         "    </ul>" +
                                         "    <p style=\"margin-bottom: 1em;\">The user's email address has been verified, and their account is now live with Member access.</p>" +
                                         "    <p style=\"margin-bottom: 0;\">Sincerely,</p>" +
                                         "    <p style=\"margin-top: 0;\">Oaks-Village HOA System<img src=\"https://Oaks-Village.com/Images/LinkImages/SmallLogo.png\" alt=\"Oaks-Village HOA Logo\" style=\"vertical-align: middle; margin-left: 3px; height: 40px;\"></p>" +
                                         "</body>" +
                                         "</html>";
                    }
                    else
                    {
                        emailBodyAdmin = $"<!DOCTYPE html>" +
                                         "<html lang=\"en\">" +
                                         "<head>" +
                                         "    <meta charset=\"UTF-8\">" +
                                         "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">" +
                                         "    <title>Email Confirmation Notification - Action Required</title>" +
                                         "</head>" +
                                         "<body style=\"font-family: sans-serif; line-height: 1.6; margin: 20px;\">" +
                                         "    <p style=\"margin-bottom: 1em;\">Dear Oaks-Village HOA Administrator,</p>" +
                                         "    <p style=\"margin-bottom: 1em;\">This is a notification that a user has confirmed their email address.</p>" +
                                         "    <p style=\"margin-bottom: 1em;\"><strong>Account Requires Member Role Assignment:</strong></p>" +
                                         "    <ul style=\"margin-left: 20px; margin-bottom: 1em;\">" +
                                         $"        <li><strong>Name:</strong> {userProfile.FirstName} {userProfile.MiddleName} {userProfile.LastName}</li>" +
                                         $"        <li><strong>Email:</strong> {user.Email}</li>" +
                                         "    </ul>" +
                                         "    <p style=\"margin-bottom: 1em;\">The user with the email address above has confirmed their email. Please review their account and assign the 'Member' role as appropriate.</p>" +
                                         "    <p style=\"margin-bottom: 0;\">Sincerely,</p>" +
                                         "    <p style=\"margin-top: 0;\">Oaks-Village HOA System<img src=\"https://Oaks-Village.com/Images/LinkImages/SmallLogo.png\" alt=\"Oaks-Village HOA Logo\" style=\"vertical-align: middle; margin-left: 3px; height: 40px;\"></p>" +
                                         "</body>" +
                                         "</html>";
                    }

                    await _emailSender.SendEmailAsync(
                        adminEmail,
                        emailSubjectAdmin,
                        emailBodyAdmin
                    );
                }
                else
                {
                    // Handle the case where the UserProfile might be missing
                    _logger.LogError("UserProfile not found for user ID: {UserId}", userId);
                }
            }

            return Page();
        }
    }
}