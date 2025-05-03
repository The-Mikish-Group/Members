using System;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity.UI.Services;
using Members.Data;
using Members.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;
using System.Text.Encodings.Web;

namespace Members.Areas.Identity.Pages
{
    public class EditUserModel(UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager, IEmailSender emailSender, ApplicationDbContext dbContext) : PageModel
    {
        
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly RoleManager<IdentityRole> _roleManager = roleManager;
        private readonly IEmailSender _emailSender = emailSender;
        private readonly ApplicationDbContext _dbContext = dbContext;

        [BindProperty]
        public required InputModel Input { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }


        public required string StatusMessage { get; set; }        

        [BindProperty]
        public List<RoleViewModel> AllRoles { get; set; } = [];    

        public class RoleViewModel
        {
            public required string Value { get; set; }
            public required string Text { get; set; }
            public bool Selected { get; set; }
        }

        public class InputModel
        {

            // The User ID
            public required string Id { get; set; }

            // Username (should be the same as email)
            [Required]
            [Display(Name = "Username")]
            public required string UserName { get; set; }

            // Email
            [Required]
            [EmailAddress]
            [Display(Name = "Email")]
            public required string Email { get; set; }

            // Email Confirmed
            public bool EmailConfirmed { get; set; }

            // Password
            [DataType(DataType.Password)]
            [Display(Name = "New Password")]
            public string? NewPassword { get; set; }

            // Cell Phone
            [Phone]
            [Display(Name = "Cell Phone")]
            [RegularExpression(@"^\(?\d{3}\)?[-. ]?\d{3}[-. ]?\d{4}$", ErrorMessage = "Not a valid format; try ### ###-####")]
            public string? PhoneNumber { get; set; }

            // Cell Phone Confirmed
            public bool PhoneNumberConfirmed { get; set; }

            // Home Phone
            [Phone]
            [Display(Name = "Home Phone")]
            [RegularExpression(@"^\(?\d{3}\)?[-. ]?\d{3}[-. ]?\d{4}$", ErrorMessage = "Not a valid format; try ### ###-####")]
            public string? HomePhoneNumber { get; set; }

            // Name - First Middle, and Last
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

            // Address - AddressLine1, AddressLine2, City, State, ZipCode
            // [Required]
            [Display(Name = "Address Line 1")]
            public string? AddressLine1 { get; set; }

            [Display(Name = "Address Line 2")]
            public string? AddressLine2 { get; set; }

            // [Required]
            [Display(Name = "City")]
            public string? City { get; set; }

            // [Required]
            [Display(Name = "State")]
            public string? State { get; set; }

            // [Required]
            [Display(Name = "Zip Code")]
            public string? ZipCode { get; set; }

            // Plot Identifier.
            [Display(Name = "Plot")]
            public string? Plot { get; set; }

            public bool ModelState {get; set; }

        }

        private async Task LoadUserAsync(IdentityUser user)
        {
            // Load UserProfile data
            var userProfile = await _dbContext.UserProfile.FindAsync(user.Id);

            Input = new InputModel
            {
                Id = user.Id,
                UserName = user.UserName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                EmailConfirmed = user.EmailConfirmed,
                PhoneNumber = user.PhoneNumber,
                PhoneNumberConfirmed = user.PhoneNumberConfirmed,
                HomePhoneNumber = userProfile?.HomePhoneNumber ?? string.Empty,
                FirstName = userProfile?.FirstName ?? string.Empty,
                MiddleName = userProfile?.MiddleName ?? string.Empty,
                LastName = userProfile?.LastName ?? string.Empty,
                Birthday = userProfile?.Birthday,
                Anniversary = userProfile?.Anniversary,
                AddressLine1 = userProfile?.AddressLine1 ?? string.Empty,
                AddressLine2 = userProfile?.AddressLine2 ?? string.Empty,
                City = userProfile?.City ?? string.Empty,
                State = userProfile?.State ?? string.Empty,
                ZipCode = userProfile?.ZipCode ?? string.Empty,
                Plot = userProfile?.Plot ?? string.Empty
            };
        }

        public async Task<IActionResult> OnGetAsync(string id, string searchTerm)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            // Load the user based on the provided ID
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{id}'.");
            }

            await LoadUserAsync(user);

            // Apply default values from environment variables if the loaded data is empty
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

            // Store the search term from the query string
            SearchTerm = searchTerm;

            var roles = await _roleManager.Roles.ToListAsync();
            var userRoles = await _userManager.GetRolesAsync(user);

            AllRoles = [.. roles.Select(role => new RoleViewModel
            {
                Value = role.Name ?? string.Empty,
                Text = role.Name ?? string.Empty,
                Selected = userRoles.Contains(role.Name ?? string.Empty)
            })];

            return Page();
        }
        public async Task<IActionResult> OnPostAsync(string? searchTerm)
        {
            var user = await _userManager.FindByIdAsync(Input.Id);

            if (user != null)
            {
                // Check if the email is already confirmed
                bool originalEmailConfirmed = user.EmailConfirmed;

                // Update the user properties
                user.UserName = Input.UserName;
                user.Email = Input.Email;
                user.EmailConfirmed = Input.EmailConfirmed;
                user.PhoneNumber = Input.PhoneNumber;
                user.PhoneNumberConfirmed = Input.PhoneNumberConfirmed;

                // Save changes to Users table
                var result = await _userManager.UpdateAsync(user);

                // Update the UserProfile table
                var userProfile = await _dbContext.UserProfile.FindAsync(Input.Id);
                if (userProfile == null)
                {
                    userProfile = new UserProfile { UserId = Input.Id, User = user };
                    _dbContext.UserProfile.Add(userProfile);
                }
                userProfile.FirstName = Input.FirstName;
                userProfile.MiddleName = Input.MiddleName;
                userProfile.LastName = Input.LastName;
                userProfile.Birthday = Input.Birthday;
                userProfile.Anniversary = Input.Anniversary;
                userProfile.AddressLine1 = Input.AddressLine1;
                userProfile.AddressLine2 = Input.AddressLine2;
                userProfile.City = Input.City;
                userProfile.State = Input.State;
                userProfile.ZipCode = Input.ZipCode;
                userProfile.Plot = Input.Plot;
                userProfile.HomePhoneNumber = Input.HomePhoneNumber;

                // Save changes to UserProfile
                await _dbContext.SaveChangesAsync();
            }

            // Update User Roles
            user = await _userManager.FindByIdAsync(Input.Id);
            if (user != null)
            {
                var originalRoles = await _userManager.GetRolesAsync(user);

                var selectedRoles = AllRoles?.Where(r => r.Selected).Select(r => r.Value ?? string.Empty).ToList() ?? [];

                await _userManager.RemoveFromRolesAsync(user, originalRoles);
                var addRolesResult = await _userManager.AddToRolesAsync(user, selectedRoles);

                if (!addRolesResult.Succeeded)
                {
                    foreach (var error in addRolesResult.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }
                    await PopulateUserDataAndRoles(user);
                    return Page(); // Stay on the Edit page with errors
                }

                // --- Integrated Email Sending Logic Here ---
                if (user.EmailConfirmed && selectedRoles.Contains("Member") && !originalRoles.Contains("Member"))
                {
                    if (!string.IsNullOrEmpty(user.Email))
                    {
                        await _emailSender.SendEmailAsync(
                            user.Email,
                            "Welcome to Oaks-Village HOA - Your Account is Ready",
                            "<!DOCTYPE html>" +
                            "<html lang=\"en\">" +
                            "<head>" +
                            "    <meta charset=\"UTF-8\">" +
                            "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">" +
                            "    <title>Welcome to Oaks-Village HOA</title>" +
                            "</head>" +
                            "<body style=\"font-family: sans-serif; line-height: 1.6; margin: 20px;\">" +
                            "    <p style=\"margin-bottom: 1em;\">Dear Member,</p>" +
                            "    <p style=\"margin-bottom: 1em;\">Welcome! Your Oaks-Village account has been created and is ready for you to access.</p>" +
                            "    <p style=\"margin-bottom: 1em;\">You have been granted Member access and can log in to the HOA community portal at <a href=\"https://oaks-village.com\" style=\"color: #007bff; text-decoration: none;\">https://oaks-village.com</a>.</p>" +
                            "    <div style=\"margin-bottom: 2em; padding: 15px; border: 1px solid #ddd; border-radius: 5px; background-color: #f9f9f9;\">" +
                            "        <strong style=\"font-size: 1.1em;\">Important Note:</strong> If this account was automatically generated for you, please click the link below to create your password:" +
                            "        <p style=\"margin-top: 1em;\">" +
                            "            <a href=\"https://oaks-village.com/Identity/Account/ForgotPassword\" style=\"background-color:#007bff;color:#fff;padding:10px 15px;text-decoration:none;border-radius:5px;font-weight:bold;display:inline-block;\">" +
                            "                Click Here to Create Your Password" +
                            "            </a>" +
                            "        </p>" +
                            "    </div>" +
                            "    <p style=\"margin-bottom: 1em;\">You will be directed to enter your email address, and a password reset link will be sent to you. This process ensures the security of your account and verifies your email address, preventing unauthorized password creation attempts.</p>" +
                            "    <p style=\"margin-bottom: 1em;\">Thank you for being a part of the Oaks-Village Homeowners Association.</p>" +
                            "    <p style=\"margin-bottom: 0;\">Sincerely,</p>" +
                            "    <p style=\"margin-top: 0;\">The Oaks-Village HOA</p>" +
                            "</body>" +
                            "</html>"
                        );
                    }
                }
                else if (!user.EmailConfirmed && selectedRoles.Contains("Member") && !originalRoles.Contains("Member"))
                {
                    if (!string.IsNullOrEmpty(user.Email))
                    {
                        var userId = await _userManager.GetUserIdAsync(user);
                        var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                        var callbackUrl = Url.Page(
                            "/Account/ConfirmEmail",
                            pageHandler: null,
                            values: new { area = "Identity", userId, code },
                            protocol: Request.Scheme);

                        if (callbackUrl != null)
                        {
                            await _emailSender.SendEmailAsync(
                                user.Email,
                                "Please Confirm Your Email Address - Oaks-Village HOA Registration",
                                $"<!DOCTYPE html>" +
                                "<html lang=\"en\">" +
                                "<head>" +
                                "    <meta charset=\"UTF-8\">" +
                                "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">" +
                                "    <title>Confirm Your Email - Oaks-Village HOA</title>" +
                                "</head>" +
                                "<body style=\"font-family: sans-serif; line-height: 1.6; margin: 20px;\">" +
                                "    <p style=\"margin-bottom: 1em;\">Dear Member,</p>" +
                                "    <p style=\"margin-bottom: 1em;\">Thank you for registering with the Oaks-Village Homeowners Association!</p>" +
                                "    <p style=\"margin-bottom: 1em;\">To complete your registration and activate your account, please confirm your email address by clicking the button below:</p>" +
                                "    <div style=\"margin: 2em 0;\">" +
                                $"        <a href='{HtmlEncoder.Default.Encode(callbackUrl)}' style=\"background-color:#007bff;color:#fff;padding:10px 15px;text-decoration:none;border-radius:5px;font-weight:bold;display:inline-block;\">" +
                                "            Confirm Your Email Address" +
                                "        </a>" +
                                "    </div>" +
                                "    <p style=\"margin-bottom: 1em;\">By confirming your email, you help us ensure the security of your account and allow us to send you important updates and community information.</p>" +
                                "    <p style=\"margin-bottom: 1em;\">If you did not register for an account with Oaks-Village HOA, please disregard this email.</p>" +
                                "    <p style=\"margin-bottom: 0;\">Thank you for being a part of our community.</p>" +
                                "    <p style=\"margin-top: 0;\">Sincerely,</p>" +
                                "    <p style=\"margin-top: 0;\">The Oaks-Village HOA Team<img src=\"https://Oaks-Village.com/Images/LinkImages/Oaks-Trees.png\" alt=\"Oaks-Village HOA Logo\" style=\"vertical-align: middle; margin-left: 3px; height: 40px;\"></p>" +
                                "</body>" +
                                "</html>"
                            );

                        }
                    }
                }

                // Redirection Logic:
                if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
                {
                    return Redirect(ReturnUrl);

                }
                else
                {
                    // If ReturnUrl is missing or invalid, fall back to the appropriate page
                    if (!string.IsNullOrEmpty(searchTerm))
                    {
                        return RedirectToPage("./Users", new { SearchTerm = searchTerm }); // Redirect to UsersGrid with searchTerm
                    }
                    else
                    {
                        return RedirectToPage("./Users"); // Redirect to UsersGrid without searchTerm
                    }
                }
            }

            //return NotFound($"Unable to load user with ID '{Input.Id}'.");
            return Page();
        }
        
        private async Task PopulateUserDataAndRoles(IdentityUser user)
        {
            var roles = await _roleManager.Roles.ToListAsync();
            var userRoles = await _userManager.GetRolesAsync(user);

            AllRoles = [.. roles.Select(role => new RoleViewModel
            {
                Value = role.Name ?? string.Empty,
                Text = role.Name ?? string.Empty,
                Selected = userRoles.Contains(role.Name ?? string.Empty)
            })];
        }
    }
}
