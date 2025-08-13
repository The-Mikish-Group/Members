// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

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
    public class ForgotPasswordModel(UserManager<IdentityUser> userManager, IEmailSender emailSender, ILogger<ForgotPasswordModel> logger) : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly IEmailSender _emailSender = emailSender;
        private readonly ILogger<ForgotPasswordModel> _logger = logger;

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (ModelState.IsValid)
            {
                var user = await _userManager.FindByEmailAsync(Input.Email);
                if (user == null)
                {
                    // Don't reveal that the user does not exist or is not confirmed
                    return RedirectToPage("./ForgotPasswordConfirmation");
                }

                // Get the site name from environment variable
                string siteName = Environment.GetEnvironmentVariable("SITE_NAME_HOA_CLOUD");

                if (string.IsNullOrEmpty(siteName))
                {
                    _logger.LogError("SITE_NAME_HOA_CLOUD environment variable is not set. Using default value.");
                    siteName = "Oaks-Village"; // Fallback to default if environment variable is not set
                }

                var code = await _userManager.GeneratePasswordResetTokenAsync(user);
                code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                var callbackUrl = Url.Page(
                    "/Account/ResetPassword",
                    pageHandler: null,
                    values: new { area = "Identity", code },
                    protocol: Request.Scheme);

                await _emailSender.SendEmailAsync(
                    Input.Email,
                    $"{siteName} - Reset Your Password",
                    $"<!DOCTYPE html>" +
                    "<html lang=\"en\">" +
                    "<head>" +
                    "    <meta charset=\"UTF-8\">" +
                    "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">" +
                    $"    <title>Reset Your Password - {siteName} HOA</title>" +
                    "</head>" +
                    "<body style=\"font-family: sans-serif; line-height: 1.6; margin: 20px;\">" +
                    "    <p style=\"margin-bottom: 1em;\">Dear Member,</p>" +
                    $"    <p style=\"margin-bottom: 1em;\">You are receiving this email because you requested to reset your password for your {siteName} HOA account.</p>" +
                    "    <p style=\"margin-bottom: 1em;\">Please click the button below to reset your password:</p>" +
                    "    <div style=\"margin: 2em 0;\">" +
                    $"        <a href='{HtmlEncoder.Default.Encode(callbackUrl)}' style=\"background-color:#007bff;color:#fff;padding:10px 15px;text-decoration:none;border-radius:5px;font-weight:bold;display:inline-block;\">" +
                    "            Reset Your Password" +
                    "        </a>" +
                    "    </div>" +
                    "    <p style=\"margin-bottom: 1em;\">This password reset link is valid for a limited time. If you did not request a password reset, you can ignore this email. Your password will not be changed.</p>" +
                    "    <p style=\"margin-bottom: 0;\">Thank you,</p>" +
                    $"    <p style=\"margin-top: 0;\">The {siteName} HOA Team <img src=\"https://{siteName}.com/Images/LinkImages/SmallLogo.png\" alt=\"{siteName} HOA Logo\" style=\"vertical-align: middle; margin-left: 3px; height: 35px;\"></p>" +
                    "</body>" +
                    "</html>"
                );

                return RedirectToPage("./ForgotPasswordConfirmation");
            }

            return Page();
        }
    }
}