using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity.UI.Services;
using Members.Data; 
using Members.Models; 

namespace Members.Areas.Identity.Pages
{
    public class EditUserModel(UserManager<IdentityUser> userManager, IEmailSender emailSender, ApplicationDbContext dbContext) : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly IEmailSender _emailSender = emailSender;
        private readonly ApplicationDbContext _dbContext = dbContext;

        [BindProperty]
        public required InputModel Input { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        public required string StatusMessage { get; set; }

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
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string? searchTerm)
        {
            if (!ModelState.IsValid)
            {
                // Something is wrong with the model state
                foreach (var modelStateValue in ModelState.Values)
                {
                    foreach (var error in modelStateValue.Errors)
                    {
                        Console.WriteLine($"Model Error: {error.ErrorMessage}");
                    }
                }
                return Page();
            }

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

                // Send Emails
                if (result.Succeeded && !string.IsNullOrEmpty(Input.NewPassword))
                {
                    var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                    var passwordResult = await _userManager.ResetPasswordAsync(user, token, Input.NewPassword);

                    // Send new password in email (avoid using...  a last resort passowrd assignment)
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
                        // Something didn't work with the password reset
                        foreach (var error in passwordResult.Errors)
                        {
                            ModelState.AddModelError(string.Empty, error.Description);
                        }
                        return Page();
                    }
                }

                // Send email confirmation if the email was confirmed
                if (result.Succeeded)
                {
                    if (!originalEmailConfirmed && Input.EmailConfirmed)
                    {
                        var roles = await _userManager.GetRolesAsync(user);
                        bool isMember = roles.Contains("Member");
                        string approvalMessage = "";
                        if (!isMember)
                        {
                            approvalMessage = "<p>Please note that we are still waiting for final Manager approval which may take up to 24 hours.</p>";
                        }

                        await _emailSender.SendEmailAsync(
                            Input.Email,
                            "Email Confirmed",
                            $"<html><body><p>Your <strong>email account</strong> has been confirmed...</p>{approvalMessage}</body></html>"
                        );
                    }

                    // Redirect back to the Users page, preserving the search term only if it exists
                    if (!string.IsNullOrEmpty(searchTerm))
                    {
                        return RedirectToPage("./Users", new { SearchTerm = searchTerm });
                    }
                    else
                    {
                        return RedirectToPage("./Users"); // Redirect without the search term.
                    }
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