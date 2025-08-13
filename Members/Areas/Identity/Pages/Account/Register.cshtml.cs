using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.WebUtilities;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;

namespace Members.Areas.Identity.Pages.Account
{
    public class RegisterModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IUserStore<IdentityUser> _userStore;
        private readonly IUserEmailStore<IdentityUser> _emailStore;
        private readonly ILogger<RegisterModel> _logger;
        private readonly IEmailSender _emailSender;
        private readonly Data.ApplicationDbContext _dbContext;

        public RegisterModel(
            UserManager<IdentityUser> userManager,
            IUserStore<IdentityUser> userStore,
            SignInManager<IdentityUser> signInManager,
            ILogger<RegisterModel> logger,
            IEmailSender emailSender,
            ApplicationDbContext dbContext)
        {
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _signInManager = signInManager;
            _logger = logger;
            _emailSender = emailSender;
            _dbContext = dbContext;

            Input = new InputModel
            {
                Email = string.Empty,
                Password = string.Empty,
                ConfirmPassword = string.Empty,
                PhoneNumber = string.Empty,
                HomePhoneNumber = string.Empty,
                FirstName = string.Empty,
                MiddleName = string.Empty,
                LastName = string.Empty,
                AddressLine1 = string.Empty,
                AddressLine2 = string.Empty,
                City = string.Empty,
                State = string.Empty,
                ZipCode = string.Empty,
                Birthday = null,
                Anniversary = null
            };

            ReturnUrl = string.Empty;
            ExternalLogins = [];
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ReturnUrl { get; set; }

        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        public class InputModel
        {
            // Email and Password
            [Required]
            [EmailAddress]
            [Display(Name = "Email")]
            public required string Email { get; set; }

            [Required]
            [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
            [DataType(DataType.Password)]
            [Display(Name = "Password")]
            public required string Password { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "Confirm password")]
            [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
            public required string ConfirmPassword { get; set; }

            // Name - First, Middle, and Last
            [Required]
            [Display(Name = "First Name")]
            public required string FirstName { get; set; }

            [Display(Name = "Middle Name")]
            public string? MiddleName { get; set; }

            [Required]
            [Display(Name = "Last Name")]
            public required string LastName { get; set; }

            // Birthday
            [Display(Name = "Birthday")]
            [DataType(DataType.Date)]
            public DateTime? Birthday { get; set; }

            // Anniversary
            [Display(Name = "Anniversary")]
            [DataType(DataType.Date)]
            public DateTime? Anniversary { get; set; }

            // Cell Phone
            [Required]
            [Phone]
            [Display(Name = "Cell Phone")]
            [RegularExpression(@"^\(?\d{3}\)?[-. ]?\d{3}[-. ]?\d{4}$", ErrorMessage = "Not a valid format; try ### ###-####")]
            public string? PhoneNumber { get; set; }

            // Home Phone
            [Phone]
            [Display(Name = "Home Phone")]
            [RegularExpression(@"^\(?\d{3}\)?[-. ]?\d{3}[-. ]?\d{4}$", ErrorMessage = "Not a valid format; try ### ###-####")]
            public string? HomePhoneNumber { get; set; }

            // Address - AddressLine1, AddressLine2, City, State, ZipCode
            [Required]
            [Display(Name = "Address Line 1")]
            public string? AddressLine1 { get; set; }

            [Display(Name = "Address Line 2")]
            public string? AddressLine2 { get; set; }

            [Required]
            [Display(Name = "City")]
            public string? City { get; set; }

            [Required]
            [Display(Name = "State")]
            public string? State { get; set; }

            [Required]
            [Display(Name = "Zip Code")]
            public string? ZipCode { get; set; }
        }

        public async Task OnGetAsync(string? returnUrl = null)
        {
            ReturnUrl = returnUrl ?? string.Empty;
            ExternalLogins = [.. (await _signInManager.GetExternalAuthenticationSchemesAsync())];

            // Apply default values from environment variables if the Input properties are empty
            if (string.IsNullOrEmpty(Input.City))
            {
                Input.City = Environment.GetEnvironmentVariable("DEFAULT_CITY_HOA_CLOUD") ?? string.Empty;
            }
            if (string.IsNullOrEmpty(Input.State))
            {
                Input.State = Environment.GetEnvironmentVariable("DEFAULT_STATE_HOA_CLOUD") ?? string.Empty;
            }
            if (string.IsNullOrEmpty(Input.ZipCode))
            {
                Input.ZipCode = Environment.GetEnvironmentVariable("DEFAULT_ZIPCODE_HOA_CLOUD") ?? string.Empty;
            }
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            returnUrl ??= new UrlHelper(new ActionContext(HttpContext, new RouteData(), new PageActionDescriptor())).Content("~/");
            ExternalLogins = [.. (await _signInManager.GetExternalAuthenticationSchemesAsync())];

            if (ModelState.IsValid)
            {
                var user = CreateUser();

                await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
                await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);
                user.PhoneNumber = Input.PhoneNumber;

                var result = await _userManager.CreateAsync(user, Input.Password);

                if (result.Succeeded)
                {
                    _logger.LogInformation("User created a new account with password.");

                    // Create UserProfile
                    var userProfile = new UserProfile
                    {
                        UserId = user.Id,
                        FirstName = Input.FirstName,
                        MiddleName = Input.MiddleName,
                        LastName = Input.LastName,
                        Birthday = Input.Birthday,
                        Anniversary = Input.Anniversary,
                        HomePhoneNumber = Input.HomePhoneNumber,
                        AddressLine1 = Input.AddressLine1,
                        AddressLine2 = Input.AddressLine2,
                        City = Input.City,
                        State = Input.State,
                        ZipCode = Input.ZipCode,
                        User = user
                    };

                    _dbContext.UserProfile.Add(userProfile);
                    await _dbContext.SaveChangesAsync();

                    var userId = await _userManager.GetUserIdAsync(user);
                    var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                    code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

                    var callbackUrl = Url.Page(
                        "/Account/ConfirmEmail",
                        pageHandler: null,
                        values: new { area = "Identity", userId, code, returnUrl },
                        protocol: Request.Scheme);

                    // Send confirmation email with error handling
                    bool emailSentSuccessfully = await SendConfirmationEmailSafely(Input.Email, callbackUrl, userProfile);

                    // Send admin notification email with error handling  
                    bool adminEmailSentSuccessfully = await SendAdminNotificationEmailSafely(userProfile, user);

                    // Log email sending results but don't fail registration
                    if (!emailSentSuccessfully)
                    {
                        _logger.LogWarning("Failed to send confirmation email to {Email} for user {UserId}, but registration completed successfully", Input.Email, user.Id);
                    }

                    if (!adminEmailSentSuccessfully)
                    {
                        _logger.LogWarning("Failed to send admin notification email for user {UserId}, but registration completed successfully", user.Id);
                    }

                    if (_userManager.Options.SignIn.RequireConfirmedAccount)
                    {
                        return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl });
                    }
                    else
                    {
                        await _signInManager.SignInAsync(user, isPersistent: false);
                        return LocalRedirect(returnUrl);
                    }
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return Page();
        }

        private IdentityUser CreateUser()
        {
            try
            {
                return Activator.CreateInstance<IdentityUser>();
            }
            catch
            {
                throw new InvalidOperationException($"Can't create an instance of '{nameof(IdentityUser)}'. " +
                    $"Ensure that '{nameof(IdentityUser)}' is not an abstract class and has a parameterless constructor, or alternatively " +
                    $"override the register page in /Areas/Identity/Pages/Account/Register.cshtml");
            }
        }

        private IUserEmailStore<IdentityUser> GetEmailStore()
        {
            if (!_userManager.SupportsUserEmail)
            {
                throw new NotSupportedException("The default UI requires a user store with email support.");
            }
            return (IUserEmailStore<IdentityUser>)_userStore;
        }

        private async Task<bool> SendConfirmationEmailSafely(string email, string? callbackUrl, UserProfile userProfile)
        {
            try
            {
                // Get environment variables for dynamic content
                string siteName = Environment.GetEnvironmentVariable("SITE_NAME_HOA_CLOUD") ?? "Oaks-Village";
                string siteUrl = Environment.GetEnvironmentVariable("SITE_URL_HOA_CLOUD") ?? string.Empty;

                await _emailSender.SendEmailAsync(
                    email,
                    $"{siteName} - Confirm Your Email Address",
                    $"<!DOCTYPE html>" +
                    "<html lang=\"en\">" +
                    "<head>" +
                    "    <meta charset=\"UTF-8\">" +
                    "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">" +
                    $"    <title>Confirm Your Email Address - {siteName}</title>" +
                    "</head>" +
                    "<body style=\"font-family: sans-serif; line-height: 1.6; margin: 20px;\">" +
                    "    <p style=\"margin-bottom: 1em;\">Dear Member,</p>" +
                    $"   <p style=\"margin-bottom: 1em;\">Thank you for registering with the {siteName} Homeowners Association!</p>" +
                    "    <p style=\"margin-bottom: 1em;\">Please confirm your email address by clicking the button below:</p>" +
                    "    <div style=\"margin: 2em 0;\">" +
                    $"        <a href='{HtmlEncoder.Default.Encode(callbackUrl!)}' style=\"background-color:#007bff;color:#fff;padding:10px 15px;text-decoration:none;border-radius:5px;font-weight:bold;display:inline-block;\">" +
                    "            Confirm Your Email Address" +
                    "        </a>" +
                    "    </div>" +
                    "    <p style=\"margin-bottom: 1em;\">Please note that a staff member must now authorize your account, and this process could take up to <strong>24 hours</strong>. At that time, you will receive a <strong>Welcome Email</strong>. We appreciate your patience as we are a small team of volunteers.</p>" +
                    "    <p style=\"margin-bottom: 0;\">Thank you for your understanding.</p>" +
                    "    <p style=\"margin-top: 0;\">Sincerely,</p>" +
                    $"   <p style=\"margin-top: 0;\">The {siteName} HOA Team <img src=\"https://{siteName}.com/Images/LinkImages/SmallLogo.png\" alt=\"{siteName} HOA Logo\" style=\"vertical-align: middle; margin-left: 3px; height: 35px;\"></p>" +
                    "</body>" +
                    "</html>"
                );

                _logger.LogInformation("Confirmation email sent successfully to {Email}", email);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send confirmation email to {Email}. Registration will continue.", email);
                return false;
            }
        }

        private async Task<bool> SendAdminNotificationEmailSafely(UserProfile userProfile, IdentityUser user)
        {
            try
            {
                string? adminEmail = Environment.GetEnvironmentVariable("SMTP_USERNAME");

                if (string.IsNullOrEmpty(adminEmail))
                {
                    _logger.LogError("SMTP_USERNAME environment variable is not set. Cannot send admin notification for new registration.");
                    return false;
                }

                // Get environment variables for dynamic content
                string siteName = Environment.GetEnvironmentVariable("SITE_NAME_HOA_CLOUD") ?? "Oaks-Village";

                string emailSubject = $"{siteName} - New Member Registration";
                string emailBody = $"<!DOCTYPE html>" +
                                  "<html lang=\"en\">" +
                                  "<head>" +
                                  "    <meta charset=\"UTF-8\">" +
                                  "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">" +
                                  "    <title>New Member Registration</title>" +
                                  "</head>" +
                                  "<body style=\"font-family: sans-serif; line-height: 1.6; margin: 20px;\">" +
                                  $"    <p style=\"margin-bottom: 1em;\">Dear {siteName} Administrator,</p>" +
                                  $"    <p style=\"margin-bottom: 1em;\">A new member has registered on the {siteName} portal and requires their role to be assigned.</p>" +
                                  "    <p style=\"margin-bottom: 1em;\"><strong>New Member Information:</strong></p>" +
                                  "    <ul style=\"margin-left: 20px; margin-bottom: 1em;\">" +
                                  $"        <li><strong>Name:</strong> {userProfile.FirstName} {userProfile.MiddleName} {userProfile.LastName}</li>" +
                                  $"        <li><strong>Email:</strong> {user.Email}</li>" +
                                  "    </ul>" +
                                  "    <p style=\"margin-bottom: 1em;\">Please log in to the administration panel to review the new member's profile and assign the appropriate role.</p>" +
                                  "    <p style=\"margin-bottom: 0;\">Sincerely,</p>" +
                                  $"    <p style=\"margin-top: 0;\">{siteName} System</p>" +
                                  "</body>" +
                                  "</html>";

                await _emailSender.SendEmailAsync(adminEmail, emailSubject, emailBody);

                _logger.LogInformation("Admin notification email sent successfully to {AdminEmail}", adminEmail);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send admin notification email. Registration will continue.");
                return false;
            }
        }
    }
}