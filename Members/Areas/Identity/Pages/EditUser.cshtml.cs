using System;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity.UI.Services;
using Members.Data;
using Members.Models;
//using Microsoft.AspNetCore.Routing;
//using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.BlazorIdentity.Pages.Manage;
//using Mono.TextTemplating;
//using System.Reflection.Emit;
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

            AllRoles = roles.Select(role => new RoleViewModel
            {
                Value = role.Name ?? string.Empty,
                Text = role.Name ?? string.Empty,
                Selected = userRoles.Contains(role.Name ?? string.Empty)
            }).ToList();

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
                            "Welcome! Your Oaks-Village Account is ready to use!",
                            "You have been granted Member access and " +
                            "can log in to https://oaks-village.com.<br /><br /><br />" +
                            "If this Account was automatically generated for you, " +
                            "Please use the <a href=https://oaks-village.com/Identity/Account/ForgotPassword></strong>Forgot your Password</strong> " +
                            "link to create your password. <br><br>" +
                            "You will be asked for your email address so a link to " +
                            "complete the process can be sent to you.<br><br>" +
                            "Thank you from the <strong>Oaks-Village HOA</strong>"
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
                                "Confirm Your Email to complete your registration",
                                $"Please confirm your email address by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'><strong>clicking here</strong></a>.<br /><br />Thank you from the team at <strong>Oaks-Village HOA</strong>"
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
