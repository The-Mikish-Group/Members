using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.BlazorIdentity.Shared;
using System.Text.Encodings.Web;
using System.Text;

namespace Members.Areas.Identity.Pages
{
    public class EditUserModel(UserManager<IdentityUser> userManager, IEmailSender emailSender) : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly IEmailSender _emailSender = emailSender;

        [BindProperty]
        public required InputModel Input { get; set; }

        public required string StatusMessage { get; set; } // Add this property to store status messages

        public class InputModel
        {
            public required string Id { get; set; }

            [Required]
            [Display(Name = "Username")]
            public required string UserName { get; set; }

            [Required]
            [Display(Name = "FullName")]
            public required string FullName { get; set; }

            [Required]
            [EmailAddress]
            [Display(Name = "Email")]
            public required string Email { get; set; }

            [Phone]
            [Display(Name = "Phone Number")]
            [RegularExpression(@"^\(?([0-9]{3})\)?[-. ]?([0-9]{3})[-. ]?([0-9]{4})$", ErrorMessage = "Not a valid format; try ### ###-###")]
            public string? PhoneNumber { get; set; }
            public bool EmailConfirmed { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "New Password")]
            public string? NewPassword { get; set; }

            [EmailAddress]
            [Display(Name = "New Email")]
            public string? NewEmail { get; set; }
        }
        public async Task<IActionResult> OnPostChangeEmailAsync(string? callbackUrl)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            if (!ModelState.IsValid)
            {
                if (!string.IsNullOrEmpty(Input.NewEmail))
                {
                    await _emailSender.SendEmailAsync(
                        Input.NewEmail,
                        "Confirm your email",
                        $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl ?? string.Empty)}'>clicking here</a>.");
                }
                await LoadUserAsync(user);
                return Page();
            }

            var email = await _userManager.GetEmailAsync(user);
            if (!string.IsNullOrEmpty(Input.NewEmail) && Input.NewEmail != email)
            {
                var userId = await _userManager.GetUserIdAsync(user);
                var code = await _userManager.GenerateChangeEmailTokenAsync(user, Input.NewEmail);
                code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                var newCallbackUrl = Url.Page(
                    "/Account/ConfirmEmailChange",
                    pageHandler: null,
                    values: new { area = "Identity", userId, email = Input.NewEmail, code },
                    protocol: Request.Scheme);
                await _emailSender.SendEmailAsync(
                    Input.NewEmail,
                    "Confirm your email",
                    $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(newCallbackUrl ?? string.Empty)}'>clicking here</a>.");

                StatusMessage = "Confirmation link to change email sent. Please check your email.";
                return RedirectToPage();
            }

            StatusMessage = "Your email is unchanged.";
            return RedirectToPage();
        }

        private async Task LoadUserAsync(IdentityUser user)
        {
            var claims = await _userManager.GetClaimsAsync(user);
            var fullNameClaim = claims.FirstOrDefault(c => c.Type == "FullName");
            string value = fullNameClaim?.Value ?? string.Empty;
            Input = new InputModel
            {
                Id = user.Id,
                UserName = user.UserName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                PhoneNumber = user.PhoneNumber,
                EmailConfirmed = user.EmailConfirmed,
                FullName = value
            };
        }

        public async Task<IActionResult> OnGetAsync(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            var claims = await _userManager.GetClaimsAsync(user);
            var fullNameClaim = claims.FirstOrDefault(c => c.Type == "FullName");
            string value = fullNameClaim?.Value ?? string.Empty;
            Input = new InputModel
            {
                Id = user.Id,
                UserName = user.UserName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                PhoneNumber = user.PhoneNumber,
                EmailConfirmed = user.EmailConfirmed,
                FullName = value
            };

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user = await _userManager.FindByIdAsync(Input.Id);

            if (user != null)
            {
                // Track the original EmailConfirmed status
                bool originalEmailConfirmed = user.EmailConfirmed;

                user.UserName = Input.UserName;
                user.Email = Input.Email;
                user.PhoneNumber = Input.PhoneNumber;
                user.EmailConfirmed = Input.EmailConfirmed;

                var existingFullNameClaim = await _userManager.GetClaimsAsync(user);
                var oldFullName = existingFullNameClaim.FirstOrDefault(c => c.Type == "FullName");
                var fullNameClaim = new System.Security.Claims.Claim("FullName", Input.FullName);

                if (oldFullName != null)
                {
                    await _userManager.ReplaceClaimAsync(user, oldFullName, fullNameClaim);
                }
                else
                {
                    await _userManager.AddClaimAsync(user, fullNameClaim);
                }

                var result = await _userManager.UpdateAsync(user);

                if (result.Succeeded && !string.IsNullOrEmpty(Input.NewPassword))
                {
                    var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                    var passwordResult = await _userManager.ResetPasswordAsync(user, token, Input.NewPassword);

                    if (passwordResult.Succeeded)
                    {
                        await _emailSender.SendEmailAsync(
                            Input.Email,
                            "Your New Password",
                            $"Your email is: {Input.Email} and your new Password is: {Input.NewPassword}"
                            );
                    }
                    else
                    {
                        foreach (var error in passwordResult.Errors)
                        {
                            ModelState.AddModelError(string.Empty, error.Description);
                        }
                        return Page();
                    }
                }

                if (result.Succeeded)
                {
                    // Check if EmailConfirmed status changed to true
                    if (!originalEmailConfirmed && Input.EmailConfirmed)
                    {
                        await _emailSender.SendEmailAsync(
                        Input.Email,
                        "Email Confirmed",
                        "<html><body>" +
                        "<p>Your <strong>email account</strong> has been confirmed and you can log " +
                        "into <a href=\"https://Oaks-Village.com\">Oaks-Village.com</a>.</p>" +
                        "<p><strong>However,</strong> you won't have <strong>*Member access*</strong> " +
                        "until a Manager sets your account role to Member. " +                        
                        "This step may take 24 hours, so please be patient. " +
                        "It is a manual process and we are a small team of volunteers.</p><br>" +
                        "<p>You will be notified again when your account is ready.</p>" +
                        "<br>" +
                        "Thank you from the staff at Oaks-Village!" +
                        "</body></html>"
                        );                                                
                    }

                    return RedirectToPage("./Users");
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return Page();
        }
    }
}




