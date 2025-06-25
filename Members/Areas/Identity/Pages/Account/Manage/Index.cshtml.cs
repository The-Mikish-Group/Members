using Members.Data; // Make sure this namespace is correct for your DbContext
using Members.Models; // Make sure this namespace is correct for your UserProfile model
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace Members.Areas.Identity.Pages.Account.Manage
{
    public class IndexModel(
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager,
        ApplicationDbContext dbContext) : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly SignInManager<IdentityUser> _signInManager = signInManager;
        private readonly ApplicationDbContext _dbContext = dbContext;

        public string? Username { get; set; }

        [TempData]
        public required string StatusMessage { get; set; }

        [BindProperty]
        public required InputModel Input { get; set; }

        public class InputModel
        {
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

            [Required]
            [Display(Name = "First Name")]
            public required string FirstName { get; set; }

            [Display(Name = "Middle Name")]
            public string? MiddleName { get; set; }

            [Required]
            [Display(Name = "Last Name")]
            public required string LastName { get; set; }

            [Display(Name = "Birthday")]
            [DataType(DataType.Date)]
            public string? Birthday { get; set; }

            [Display(Name = "Anniversary")]
            [DataType(DataType.Date)]
            public string? Anniversary { get; set; }

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
            [Display(Name = "Zip Code")] // Corrected the Display Name
            public string? ZipCode { get; set; }

            //[Display(Name = "Plot")]
            //public string? Plot { get; set; }
        }

        private async Task LoadAsync(IdentityUser user)
        {
            var userName = await _userManager.GetUserNameAsync(user);
            var phoneNumber = await _userManager.GetPhoneNumberAsync(user);

            var userProfile = await _dbContext.UserProfile.FindAsync(user.Id);

            Username = userName;

            Input = new InputModel
            {
                PhoneNumber = phoneNumber, // Add this line to load the Cell Phone number
                FirstName = userProfile?.FirstName ?? string.Empty,
                MiddleName = userProfile?.MiddleName,
                LastName = userProfile?.LastName ?? string.Empty,
                Birthday = userProfile?.Birthday?.ToString("yyyy-MM-dd") ?? string.Empty,
                Anniversary = userProfile?.Anniversary?.ToString("yyyy-MM-dd") ?? string.Empty,
                AddressLine1 = userProfile?.AddressLine1,
                AddressLine2 = userProfile?.AddressLine2,
                City = userProfile?.City,
                State = userProfile?.State,
                ZipCode = userProfile?.ZipCode,
                //Plot = userProfile?.Plot,
                HomePhoneNumber = userProfile?.HomePhoneNumber
            };
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            await LoadAsync(user);

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

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            if (!ModelState.IsValid)
            {
                await LoadAsync(user);
                return Page();
            }

            // Update UserProfile (This part is independent of the PhoneNumber update)
            var userProfile = await _dbContext.UserProfile.FindAsync(user.Id);
            if (userProfile == null)
            {
                userProfile = new UserProfile { UserId = user.Id, User = user };
                _dbContext.UserProfile.Add(userProfile);
            }

            userProfile.FirstName = Input.FirstName;
            userProfile.MiddleName = Input.MiddleName;
            userProfile.LastName = Input.LastName;
            userProfile.Birthday = string.IsNullOrEmpty(Input.Birthday) ? (DateTime?)null : DateTime.Parse(Input.Birthday);
            userProfile.Anniversary = string.IsNullOrEmpty(Input.Anniversary) ? (DateTime?)null : DateTime.Parse(Input.Anniversary);
            userProfile.AddressLine1 = Input.AddressLine1;
            userProfile.AddressLine2 = Input.AddressLine2;
            userProfile.City = Input.City;
            userProfile.State = Input.State;
            userProfile.ZipCode = Input.ZipCode;
            //userProfile.Plot = Input.Plot;
            userProfile.HomePhoneNumber = Input.HomePhoneNumber;

            await _dbContext.SaveChangesAsync();
            await _signInManager.RefreshSignInAsync(user);

            StatusMessage = "Your profile has been updated";
            return RedirectToPage();
        }
    }
}