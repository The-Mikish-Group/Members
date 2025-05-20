using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;

namespace Members.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ResendEmailConfirmationModel(UserManager<IdentityUser> userManager, IEmailSender emailSender) : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly IEmailSender _emailSender = emailSender;

        [BindProperty]
        public InputModel Input { get; set; } = new InputModel();

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; } = string.Empty;
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Verification email sent.");
                return Page();
            }

            var userId = await _userManager.GetUserIdAsync(user);
            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var callbackUrl = Url.Page(
                "/Account/ConfirmEmail",
                pageHandler: null,
                values: new { userId, code },
                protocol: Request.Scheme);

            if (string.IsNullOrEmpty(callbackUrl))
            {
                await _emailSender.SendEmailAsync(
                    Input.Email,
                    "Reminder: Confirm Your Oaks-Village HOA Email",
                    $"<!DOCTYPE html>" +
                    "<html lang=\"en\">" +
                    "<head>" +
                    "    <meta charset=\"UTF-8\">" +
                    "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">" +
                    "    <title>Reminder: Confirm Your Email - Oaks-Village HOA</title>" +
                    "</head>" +
                    "<body style=\"font-family: sans-serif; line-height: 1.6; margin: 20px;\">" +
                    "    <p style=\"margin-bottom: 1em;\">Dear Member,</p>" +
                    "    <p style=\"margin-bottom: 1em;\">We noticed you haven't confirmed your email address for your Oaks-Village Homeowners Association account yet. To activate your account, please click the button below:</p>" +
                    "    <div style=\"margin: 2em 0;\">" +
                    $"        <a href='{HtmlEncoder.Default.Encode(callbackUrl ?? string.Empty)}' style=\"background-color:#007bff;color:#fff;padding:10px 15px;text-decoration:none;border-radius:5px;font-weight:bold;display:inline-block;\">" +
                    "            Confirm Your Email Address" +
                    "        </a>" +
                    "    </div>" +
                    "    <p style=\"margin-bottom: 1em;\">Please click the link to verify your email and complete your registration. This step is important to ensure you receive important updates and can access all features of the portal.</p>" +
                    "    <p style=\"margin-bottom: 1em;\">If you have already confirmed your email, you can disregard this message.</p>" +
                    "    <p style=\"margin-bottom: 0;\">Thank you,</p>" +
                    "    <p style=\"margin-top: 0;\">The Oaks-Village HOA Team<img src=\"https://Oaks-Village.com/Images/LinkImages/SmallLogo.png\" alt=\"Oaks-Village HOA Logo\" style=\"vertical-align: middle; margin-left: 3px; height: 40px;\"></p>" +
                    "</body>" +
                    "</html>"
                );
                ModelState.AddModelError(string.Empty, "Verification email sent.");
                return Page();
            }
            ;

            ModelState.AddModelError(string.Empty, "Verification email sent.");
            return Page();
        }
    }
}
