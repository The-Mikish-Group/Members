#nullable disable

using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.WebUtilities;
using Members.Data;
using Microsoft.EntityFrameworkCore;

namespace Members.Areas.Identity.Pages.Account
{
    public class ConfirmEmailModel(
        UserManager<IdentityUser> userManager,
        IEmailSender emailSender,
        ApplicationDbContext dbContext,
        RoleManager<IdentityRole> roleManager) : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly IEmailSender _emailSender = emailSender;
        private readonly ApplicationDbContext _dbContext = dbContext;
        private readonly RoleManager<IdentityRole> _roleManager = roleManager;

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
                string emailSubjectUser = "Email Address Confirmed";
                string emailBodyUser;

                // Check if the user has the "Member" role
                bool isMember = await _userManager.IsInRoleAsync(user, "Member");

                if (isMember)
                {
                    emailSubjectUser = "Welcome, your account is Confirmed.";
                    emailBodyUser = "Thank you for confirming your email address. Your account is now <strong>Active</strong>.<br>"+
                        "You can login at https://Oaks-Village.com.<br><br>Thank you from the Oaks Village HOA.";
                }
                else
                {
                    emailBodyUser = "Thank you for confirming your email address. <br><br>"+
                        "A staff member must now authorize your account and this could take up to 24 hours. "+
                        "At that time, you will receive a <strong>Welcome Email</strong>. Please be patient, "+
                        "we are a small staff of volunteers.<br><br>Thank you from the Oaks Village HOA.";
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
                    string emailSubjectAdmin = "Email Confirmed Notification";
                    string emailBodyAdmin;

                    if (isMember)
                    {
                        emailBodyAdmin = $"{userProfile.FirstName} {userProfile.MiddleName} {userProfile.LastName} with email {user.Email} has confirmed their email address and their account is live.";
                    }
                    else
                    {
                        emailBodyAdmin = $"{userProfile.FirstName} {userProfile.MiddleName} {userProfile.LastName} with email {user.Email} has confirmed their email address. Manager attention is needed to set the 'Member' role for this account.";
                    }

                    await _emailSender.SendEmailAsync(
                        "OaksVillage@oaks-village.com",
                        emailSubjectAdmin,
                        emailBodyAdmin
                    );
                }
                else
                {
                    // Handle the case where the UserProfile might be missing
                    var logFactory = LoggerFactory.Create(builder => builder.AddConsole());
                    var logger = logFactory.CreateLogger<ConfirmEmailModel>();
                    logger.LogError("UserProfile not found for user ID: {UserId}", userId);
                }
            }

            return Page();
        }
    }
}