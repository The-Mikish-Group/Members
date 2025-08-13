#nullable disable

using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;

namespace Members.Areas.Identity.Pages
{
    public class AddUserModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IUserStore<IdentityUser> _userStore;
        private readonly IUserEmailStore<IdentityUser> _emailStore;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IEmailSender _emailSender;
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<AddUserModel> _logger;

        public AddUserModel(
            UserManager<IdentityUser> userManager,
            IUserStore<IdentityUser> userStore,
            RoleManager<IdentityRole> roleManager,
            IEmailSender emailSender,
            ApplicationDbContext dbContext,
            ILogger<AddUserModel> logger
            )
        {
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _roleManager = roleManager;
            _emailSender = emailSender;
            _dbContext = dbContext;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new InputModel
        {
            FirstName = string.Empty,
            MiddleName = string.Empty,
            LastName = string.Empty,
            Birthday = null,
            HomePhoneNumber = null,
            Anniversary = null,
            AddressLine1 = string.Empty,
            AddressLine2 = string.Empty,
            City = string.Empty,
            State = string.Empty,
            ZipCode = string.Empty,
            Email = string.Empty,
            EmailConfirmed = true
        };

        public class InputModel
        {
            [BindProperty(SupportsGet = true)]
            public string SearchTerm { get; set; }

            [Required]
            [Display(Name = "FirstName")]
            public required string FirstName { get; set; }

            [Display(Name = "MiddleName")]
            public string MiddleName { get; set; }

            [Required]
            [Display(Name = "LastName")]
            public required string LastName { get; set; }

            [Display(Name = "Birthday")]
            [DataType(DataType.Date)]
            public DateTime? Birthday { get; set; }

            [Display(Name = "Anniversary")]
            [DataType(DataType.Date)]
            public DateTime? Anniversary { get; set; }

            [Required]
            [Display(Name = "AddressLine1")]
            public required string AddressLine1 { get; set; }

            [Display(Name = "AddressLine2")]
            public string AddressLine2 { get; set; }

            [Required]
            [Display(Name = "City")]
            public required string City { get; set; }

            [Required]
            [Display(Name = "State")]
            public required string State { get; set; }

            [Required]
            [Display(Name = "ZipCode")]
            public required string ZipCode { get; set; }

            [Required]
            [EmailAddress]
            [Display(Name = "Email")]

            public required string Email { get; set; }
            [Display(Name = "Email Confirmed")]
            public bool EmailConfirmed { get; set; } = false;

            [Required]
            [Phone]
            [Display(Name = "Cell Phone")]
            [RegularExpression(@"^\(?\d{3}\)?[-. ]?\d{3}[-. ]?\d{4}$", ErrorMessage = "Not a valid format; try ### ###-####")]
            public string PhoneNumber { get; set; }

            [Display(Name = "Cell Confirmed")]
            public bool PhoneNumberConfirmed { get; set; } = false;

            [Phone]
            [Display(Name = "Home Phone")]
            [RegularExpression(@"^\(?\d{3}\)?[-. ]?\d{3}[-. ]?\d{4}$", ErrorMessage = "Not a valid format; try ### ###-####")]
            public string HomePhoneNumber { get; set; }
        }

        public void OnGet()
        {
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

        public async Task<IActionResult> OnPostAsync()
        {
            // Set EmailConfirmed to true before saving
            Input.EmailConfirmed = true;

            if (ModelState.IsValid)
            {
                var user = CreateUser();

                await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
                await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);
                user.PhoneNumber = Input.PhoneNumber;
                user.EmailConfirmed = Input.EmailConfirmed;
                user.PhoneNumberConfirmed = Input.PhoneNumberConfirmed; // Set PhoneNumberConfirmed

                // Create the user without an initial password
                var result = await _userManager.CreateAsync(user);

                if (result.Succeeded)
                {
                    _logger.LogInformation("Successfully created user with ID: {UserId} and Email: {Email}", user.Id, user.Email);

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
                        "/Account/ResetPassword", // Corrected from /Account/AddUser
                        pageHandler: null,
                        values: new { area = "Identity", userId = user.Id, code },
                        protocol: Request.Scheme);

                    if (callbackUrl == null)
                    {
                        _logger.LogError("Failed to generate callback URL for user {UserId}.", user.Id);
                        ModelState.AddModelError(string.Empty, "Error generating password reset link.");
                        return Page();
                    }
                    _logger.LogDebug("Generated password reset token for user {UserId}: {Code}", user.Id, code);
                    _logger.LogDebug("Generated callback URL for user {UserId}: {CallbackUrl}", user.Id, callbackUrl);

                    await _emailSender.SendEmailAsync(
                        Input.Email,
                        $"Welcome to {siteName} HOA - Create Your Password",
                        "<!DOCTYPE html>" +
                        "<html lang=\"en\">" +
                        "<head>" +
                        "    <meta charset=\"UTF-8\">" +
                        "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">" +
                        $"    <title>Create Your Password - {siteName} HOA</title>" +
                        "</head>" +
                        "<body style=\"font-family: sans-serif; line-height: 1.6; margin: 20px;\">" +
                        "    <p style=\"margin-bottom: 1em;\">Dear Member,</p>" +
                        $"    <p style=\"margin-bottom: 1em;\">Welcome to the {siteName} Homeowners Association!</p>" +
                        "    <p style=\"margin-bottom: 1em;\">An account has been created on your behalf. To access your account, please create your password by clicking the button below:</p>" +
                        "    <div style=\"margin: 2em 0;\">" +
                        $"        <a href='{HtmlEncoder.Default.Encode(callbackUrl)}' style=\"background-color:#007bff;color:#fff;padding:10px 15px;text-decoration:none;border-radius:5px;font-weight:bold;display:inline-block;\">" +
                        "            Create Your Password" +
                        "        </a>" +
                        "    </div>" +
                        "    <p style=\"margin-bottom: 1em;\">This step is necessary to confirm your email address and ensure the security of your account. It also prevents unauthorized individuals from setting a password for your account.</p>" +
                        $"    <p style=\"margin-bottom: 1em;\">Once you create your password, you will be able to log in to the {siteName} HOA community portal at <a href=\"https://{siteName}.com\" style=\"color: #007bff; text-decoration: none;\">https://{siteName}.com</a>.</p>" +
                        "    <p style=\"margin-bottom: 0;\">Thank you for being a part of our community.</p>" +
                        "    <p style=\"margin-top: 0;\">Sincerely,</p>" +
                        $"    <p style=\"margin-top: 0;\">The {siteName} HOA Team <img src=\"https://{siteName}.com/Images/LinkImages/SmallLogo.png\" alt=\"{siteName} HOA Logo\" style=\"vertical-align: middle; margin-left: 3px; height: 35px;\"></p>" +
                        "</body>" +
                        "</html>"
                    );
                    _logger.LogInformation("Attempting to send 'Create Your Password' email to: {Email}", Input.Email);

                    var userProfile = new UserProfile
                    {
                        UserId = user.Id,
                        FirstName = Input.FirstName,
                        MiddleName = Input.MiddleName,
                        LastName = Input.LastName,
                        HomePhoneNumber = Input.HomePhoneNumber,
                        Birthday = Input.Birthday,
                        Anniversary = Input.Anniversary,
                        AddressLine1 = Input.AddressLine1,
                        AddressLine2 = Input.AddressLine2,
                        ZipCode = Input.ZipCode,
                        City = Input.City,
                        State = Input.State,
                        User = user
                    };

                    _dbContext.UserProfile.Add(userProfile);
                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("UserProfile created for user {UserId}.", user.Id);

                    if (!await _roleManager.RoleExistsAsync("Member"))
                    {
                        await _roleManager.CreateAsync(new IdentityRole("Member"));
                        _logger.LogInformation("Created 'Member' role.");
                    }
                    await _userManager.AddToRoleAsync(user, "Member");
                    _logger.LogInformation("User {UserId} added to 'Member' role.", user.Id);

                    return RedirectToPage("./Users", new { Input.SearchTerm });
                }
                else
                {
                    _logger.LogError("Failed to create user with email {Email}. Errors: {Errors}", Input.Email, string.Join(", ", result.Errors.Select(e => e.Description)));
                    foreach (var error in result.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }
                    return Page();
                }
            }

            return Page();
        }

        // --- OnPostCancel Handler ---
        public IActionResult OnPostCancel(string returnUrl = null)
        {
            _logger.LogInformation("Cancel button clicked on AddUser page. Returning to: {ReturnUrl}", returnUrl ?? "./Users");

            // Redirect to the ReturnUrl if available, otherwise to the default Users page
            if (!string.IsNullOrEmpty(returnUrl))
            {
                return Redirect(returnUrl);
            }
            else
            {
                return RedirectToPage("./Users");
            }
        }
        // --- OnPostCancel Handler ---

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
    }
}