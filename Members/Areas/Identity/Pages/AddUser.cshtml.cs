using Azure;
using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;

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
            AddressLine1 = string.Empty,
            AddressLine2 = string.Empty,
            City = string.Empty,
            State = string.Empty,
            ZipCode = string.Empty,
            Plot = string.Empty,
            Email = string.Empty,
            EmailConfirmed = true // Set EmailConfirmed to true by default
        };

        public class InputModel
        {
            [BindProperty(SupportsGet = true)]
            public string? SearchTerm { get; set; }
            public bool EmailConfirmed { get; set; } = false;

            [Required]
            [Display(Name = "FirstName")]
            public required string FirstName { get; set; }

            [Display(Name = "MiddleName")]
            public string? MiddleName { get; set; }

            [Required]
            [Display(Name = "LastName")]
            public required string LastName { get; set; }

            [Display(Name = "Birthday")]
            [DataType(DataType.Date)]
            public DateTime? Birthday { get; set; }

            [Required]
            [Display(Name = "AddressLine1")]
            public required string AddressLine1 { get; set; }

            [Display(Name = "AddressLine2")]
            public string? AddressLine2 { get; set; }

            [Required]
            [Display(Name = "City")]
            public required string City { get; set; }

            [Required]
            [Display(Name = "State")]
            public required string State { get; set; }

            [Required]
            [Display(Name = "ZipCode")]
            public required string ZipCode { get; set; }

            [Display(Name = "Plot")]
            public string? Plot { get; set; }

            [Required]
            [EmailAddress]
            [Display(Name = "Email")]
            public required string Email { get; set; }

            [Required]
            [Phone]
            [Display(Name = "Phone Number")]
            [RegularExpression(@"^\(?\d{3}\)?[-. ]?\d{3}[-. ]?\d{4}$", ErrorMessage = "Not a valid format; try ### ###-####")]
            public string? PhoneNumber { get; set; }

            [Display(Name = "Phone Number Confirmed")]
            public bool PhoneNumberConfirmed { get; set; } = false;
        }

        public void OnGet()
        {
            if (string.IsNullOrEmpty(Input.City))
            {
                Input.City = Environment.GetEnvironmentVariable("DEFAULT_CITY") ?? string.Empty;
            }
            if (string.IsNullOrEmpty(Input.State))
            {
                Input.State = Environment.GetEnvironmentVariable("DEFAULT_STATE") ?? string.Empty;
            }
            if (string.IsNullOrEmpty(Input.ZipCode))
            {
                Input.ZipCode = Environment.GetEnvironmentVariable("DEFAULT_ZIPCODE") ?? string.Empty;
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
                        "Create Your Password",
                        $"An account has been created on your behalf.<br><br>Please create your password by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.<br /><br />Thank you from the team at <strong>Oaks-Village HOA</strong>"
                    );
                    _logger.LogInformation("Attempting to send 'Create Your Password' email to: {Email}", Input.Email);

                    var userProfile = new UserProfile
                    {
                        UserId = user.Id,
                        FirstName = Input.FirstName,
                        MiddleName = Input.MiddleName,
                        LastName = Input.LastName,
                        Birthday = Input.Birthday,
                        AddressLine1 = Input.AddressLine1,
                        AddressLine2 = Input.AddressLine2,
                        ZipCode = Input.ZipCode,
                        Plot = Input.Plot,
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