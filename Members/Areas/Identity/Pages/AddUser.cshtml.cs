using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.ComponentModel.DataAnnotations;
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
        private readonly IEmailSender _emailSender; // Add this line

        public AddUserModel(UserManager<IdentityUser> userManager, IUserStore<IdentityUser> userStore, RoleManager<IdentityRole> roleManager, IEmailSender emailSender) // Add emailSender parameter
        {
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _roleManager = roleManager;
            _emailSender = emailSender; // Initialize _emailSender
        }

        [BindProperty]
        public InputModel Input { get; set; } = new InputModel
        {
            FullName = string.Empty, // Initialize FullName to avoid CS9035 error
            Email = string.Empty,
            Password = string.Empty
        };

        public class InputModel
        {
            public bool EmailConfirmed { get; set; } = false;

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

            [Required]
            [DataType(DataType.Password)]
            [Display(Name = "Password")]
            public required string Password { get; set; }
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (ModelState.IsValid)
            {
                var user = CreateUser();

                // Set UserName to Email
                await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
                await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);

                // Set PhoneNumber
                user.PhoneNumber = Input.PhoneNumber;

                // Set EmailConfirmed
                user.EmailConfirmed = Input.EmailConfirmed; // Added this line

                var result = await _userManager.CreateAsync(user, Input.Password);

                if (result.Succeeded && !string.IsNullOrEmpty(Input.Password))
                {
                    var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                    var passwordResult = await _userManager.ResetPasswordAsync(user, token, Input.Password);

                    if (passwordResult.Succeeded)
                    {
                        await _emailSender.SendEmailAsync(
                            Input.Email,
                            "Your New Password",
                            $"Your email is: {Input.Email} and your new Password is: {Input.Password}"
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
                    if (!string.IsNullOrEmpty(Input.FullName))
                    {
                        await _userManager.AddClaimAsync(user, new System.Security.Claims.Claim("FullName", Input.FullName));
                    }

                    // Role Management (Optional)
                    if (!await _roleManager.RoleExistsAsync("Member"))
                    {
                        await _roleManager.CreateAsync(new IdentityRole("Member"));
                    }
                    await _userManager.AddToRoleAsync(user, "Member");

                    return RedirectToPage("./Users"); // Redirect to your users list page
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