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
using Members.Data; // Assuming your DbContext is in this namespace
using Members.Models; // Assuming your UserProfile model is in this namespace

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

            // EmailConfirmed
            public bool EmailConfirmed { get; set; }

            // Password
            [DataType(DataType.Password)]
            [Display(Name = "New Password")]
            public string? NewPassword { get; set; }

            // PhoneNumber
            [Phone]
            [Display(Name = "Phone Number")]
            [RegularExpression(@"^\(?\d{3}\)?[-. ]?\d{3}[-. ]?\d{4}$", ErrorMessage = "Not a valid format; try ### ###-####")]
            public string? PhoneNumber { get; set; }

            // PhoneNumberConfirmed
            public bool PhoneNumberConfirmed { get; set; }

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
            var claims = await _userManager.GetClaimsAsync(user);
            var fullNameClaim = claims.FirstOrDefault(c => c.Type == "FullName");
            string fullNameValue = fullNameClaim?.Value ?? string.Empty;

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
                FirstName = userProfile?.FirstName ?? string.Empty,
                MiddleName = userProfile?.MiddleName,
                LastName = userProfile?.LastName ?? string.Empty,
                Birthday = userProfile?.Birthday,
                AddressLine1 = userProfile?.AddressLine1 ?? string.Empty,
                AddressLine2 = userProfile?.AddressLine2,
                City = userProfile?.City ?? string.Empty,
                State = userProfile?.State ?? string.Empty,
                ZipCode = userProfile?.ZipCode ?? string.Empty,
                Plot = userProfile?.Plot ?? string.Empty
            };
        }

        public async Task<IActionResult> OnGetAsync(string id, string? searchTerm)
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

            SearchTerm = searchTerm; // Store the search term from the query string
            await LoadUserAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string? searchTerm)
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user = await _userManager.FindByIdAsync(Input.Id);

            if (user != null)
            {
                bool originalEmailConfirmed = user.EmailConfirmed;
                //bool originalPhoneNumberConfirmed = user.PhoneNumberConfirmed;

                user.UserName = Input.UserName;
                user.Email = Input.Email;
                user.EmailConfirmed = Input.EmailConfirmed;
                user.PhoneNumber = Input.PhoneNumber;
                user.PhoneNumberConfirmed = Input.PhoneNumberConfirmed;

                var result = await _userManager.UpdateAsync(user);

                // Update UserProfile table
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
                userProfile.AddressLine1 = Input.AddressLine1;
                userProfile.AddressLine2 = Input.AddressLine2;
                userProfile.City = Input.City;
                userProfile.State = Input.State;
                userProfile.ZipCode = Input.ZipCode;
                userProfile.Plot = Input.Plot;

                await _dbContext.SaveChangesAsync(); // Save changes to UserProfile

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
                    // Redirect back to the Users page, preserving the search term
                    return RedirectToPage("./Users", new { SearchTerm = searchTerm });
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