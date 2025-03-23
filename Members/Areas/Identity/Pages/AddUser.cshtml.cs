using Azure;
using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.ComponentModel.DataAnnotations;
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

        public AddUserModel(
            UserManager<IdentityUser> userManager,
            IUserStore<IdentityUser> userStore,
            RoleManager<IdentityRole> roleManager,
            IEmailSender emailSender,
            ApplicationDbContext dbContext
            )
        {
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _roleManager = roleManager;
            _emailSender = emailSender;
            _dbContext = dbContext;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new InputModel
        {
            FirstName = string.Empty,
            MiddleName = string.Empty,
            LastName = string.Empty,
            Birthday = null, // Initialize Birthday
            AddressLine1 = string.Empty,
            AddressLine2 = string.Empty,
            City = string.Empty,
            State = string.Empty,
            ZipCode = string.Empty,
            Plot = string.Empty,
            Email = string.Empty
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
            public required string Plot { get; set; }

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
        }

        public async Task<IActionResult> OnPostAsync()
        {
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
                    // Generate password reset token
                    var token = await _userManager.GeneratePasswordResetTokenAsync(user);

                    // Construct the reset password link
                    var callbackUrl = Url.Page(
                        "/Account/ResetPassword",
                        pageHandler: null,
                        values: new { area = "Identity", userId = user.Id, code = token },
                        protocol: Request.Scheme);

                    // Check if callbackUrl is not null before proceeding
                    if (callbackUrl != null)
                    {
                        // Send the email with the reset link
                        await _emailSender.SendEmailAsync(
                            Input.Email,
                            "Create Your Password",
                            $"Please create your password by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>."
                        );
                    }
                    else
                    {
                        // Log an error or handle the case where the URL could not be generated
                        ModelState.AddModelError(string.Empty, "Error generating password reset link.");
                        return Page();
                    }

                    var userProfile = new UserProfile
                    {
                        UserId = user.Id,
                        FirstName = Input.FirstName,
                        MiddleName = Input.MiddleName,
                        LastName = Input.LastName,
                        Birthday = Input.Birthday, // Add Birthday to UserProfile
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

                    if (!await _roleManager.RoleExistsAsync("Member"))
                    {
                        await _roleManager.CreateAsync(new IdentityRole("Member"));
                    }
                    await _userManager.AddToRoleAsync(user, "Member");

                    return RedirectToPage("./Users", new { Input.SearchTerm });
                }
                else
                {
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