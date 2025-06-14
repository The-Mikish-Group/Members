using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;

namespace Members.Areas.Identity.Pages.Account.Manage
{
    public class EmailModel(
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager,
        IEmailSender emailSender) : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly SignInManager<IdentityUser> _signInManager = signInManager;
        private readonly IEmailSender _emailSender = emailSender;

        public string? Email { get; set; }
        public bool IsEmailConfirmed { get; set; }

        [TempData]
        public string? StatusMessage { get; set; }

        [BindProperty]
        public InputModel? Input { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            [Display(Name = "New email")]
            public string? NewEmail { get; set; }
        }

        private async Task LoadAsync(IdentityUser user)
        {
            var email = await _userManager.GetEmailAsync(user);
            Email = email ?? string.Empty;

            Input = new InputModel
            {
                NewEmail = email,
            };

            IsEmailConfirmed = await _userManager.IsEmailConfirmedAsync(user);
        }

        public async Task<IActionResult> OnGet()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            await LoadAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostChangeEmailAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            if (!ModelState.IsValid)
            {
                await LoadAsync(user);
                return Page();
            }

            var email = await _userManager.GetEmailAsync(user);
            if (Input?.NewEmail != null && Input.NewEmail != email)
            {
                var userId = await _userManager.GetUserIdAsync(user);
                var code = await _userManager.GenerateChangeEmailTokenAsync(user, Input.NewEmail);
                code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                var callbackUrl = Url.Page(
                    "/Account/ConfirmEmailChange",
                    pageHandler: null,
                    values: new { area = "Identity", userId, email = Input.NewEmail, code },
                    protocol: Request.Scheme);

                // Add this null check to address the warning
                if (callbackUrl != null)
                {
                    await _emailSender.SendEmailAsync(
                        Input.NewEmail,
                        "Oaks-Village HOA - Confirm Your Email Address Change",
                        $"<!DOCTYPE html>" +
                        "<html lang=\"en\">" +
                        "<head>" +
                        "    <meta charset=\"UTF-8\">" +
                        "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">" +
                        "    <title>Confirm Your New Email - Oaks-Village HOA</title>" +
                        "</head>" +
                        "<body style=\"font-family: sans-serif; line-height: 1.6; margin: 20px;\">" +
                        "    <p style=\"margin-bottom: 1em;\">Dear Member,</p>" +
                        "    <p style=\"margin-bottom: 1em;\">You are receiving this email because you requested to change the email address associated with your Oaks-Village HOA account.</p>" +
                        "    <p style=\"margin-bottom: 1em;\">Please confirm your new email address by clicking the button below:</p>" +
                        "    <div style=\"margin: 2em 0;\">" +
                        $"        <a href='{HtmlEncoder.Default.Encode(callbackUrl)}' style=\"background-color:#007bff;color:#fff;padding:10px 15px;text-decoration:none;border-radius:5px;font-weight:bold;display:inline-block;\">" +
                        "            Confirm Email Address Change" +
                        "        </a>" +
                        "    </div>" +
                        "    <p style=\"margin-bottom: 1em;\">This email confirmation link is valid for a limited time. If you did not request to change your email address, you can ignore this email. Your email address will not be updated.</p>" +
                        "    <p style=\"margin-bottom: 0;\">Thank you,</p>" +
                        "    <p style=\"margin-top: 0;\">The Oaks-Village HOA Team<img src=\"https://Oaks-Village.com/Images/LinkImages/SmallLogo.png\" alt=\"Oaks-Village HOA Logo\" style=\"vertical-align: middle; margin-left: 3px; height: 40px;\"></p>" +
                        "</body>" +
                        "</html>"
                    );

                    StatusMessage = "Confirmation link to change email sent. Please check your email.";
                    return RedirectToPage();
                }
                else
                {
                    StatusMessage = "Error generating confirmation link.";
                    return Page(); // Or RedirectToPage with an error message
                }
            }

            StatusMessage = "Your email is unchanged.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostSendVerificationEmailAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            if (!ModelState.IsValid)
            {
                await LoadAsync(user);
                return Page();
            }

            var userId = await _userManager.GetUserIdAsync(user);
            var email = await _userManager.GetEmailAsync(user);
            if (email == null)
            {
                StatusMessage = "Error: Email not found.";
                return RedirectToPage();
            }
            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var callbackUrl = Url.Page(
                "/Account/ConfirmEmail",
                pageHandler: null,
                values: new { area = "Identity", userId, code },
                protocol: Request.Scheme);

            // If the user has changed their email, we need to send the confirmation link to the new email address
            if (callbackUrl == null)
            {
                StatusMessage = "Error: Callback URL not found.";

                return RedirectToPage();
            }

            // Send an email with this link
            await _emailSender.SendEmailAsync(
                email,
                "Oaks-Village HOA - Verify Your Email Address",
                $"<!DOCTYPE html>" +
                "<html lang=\"en\">" +
                "<head>" +
                "    <meta charset=\"UTF-8\">" +
                "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">" +
                "    <title>Verify Your Email - Oaks-Village HOA</title>" +
                "</head>" +
                "<body style=\"font-family: sans-serif; line-height: 1.6; margin: 20px;\">" +
                "    <p style=\"margin-bottom: 1em;\">Dear Member,</p>" +
                "    <p style=\"margin-bottom: 1em;\">Thank you for registering with the Oaks-Village Homeowners Association!</p>" +
                "    <p style=\"margin-bottom: 1em;\">Please verify your email address to activate your account by clicking the button below:</p>" +
                "    <div style=\"margin: 2em 0;\">" +
                $"        <a href='{HtmlEncoder.Default.Encode(callbackUrl)}' style=\"background-color:#007bff;color:#fff;padding:10px 15px;text-decoration:none;border-radius:5px;font-weight:bold;display:inline-block;\">" +
                "            Verify Your Email Address" +
                "        </a>" +
                "    </div>" +
                "    <p style=\"margin-bottom: 1em;\">This email verification link is valid for a limited time. If you did not register for an account with Oaks-Village HOA, you can disregard this email.</p>" +
                "    <p style=\"margin-bottom: 0;\">Thank you,</p>" +
                "    <p style=\"margin-top: 0;\">The Oaks-Village HOA Team<img src=\"https://Oaks-Village.com/Images/LinkImages/SmallLogo.png\" alt=\"Oaks-Village HOA Logo\" style=\"vertical-align: middle; margin-left: 3px; height: 40px;\"></p>" +
                "</body>" +
                "</html>"
            );

            StatusMessage = "Verification email sent.";
            return RedirectToPage();
        }
    }
}