using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using TempDataAttribute = Microsoft.AspNetCore.Mvc.TempDataAttribute;
namespace Members.Areas.Identity.Pages
{
    public class EditUserModel(UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager, IEmailSender emailSender, ApplicationDbContext dbContext) : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly RoleManager<IdentityRole> _roleManager = roleManager;
        private readonly IEmailSender _emailSender = emailSender;
        private readonly ApplicationDbContext _dbContext = dbContext;
        [BindProperty]
        public InputModel Input { get; set; } = new InputModel();
        // This property will capture the full return URL from the query string
        // It's also marked for binding on POST
        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }
        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }
        [BindProperty(SupportsGet = true)]
        public string? ShowExtraFields { get; set; }
        [TempData] // Use TempData to display status messages after redirect
        public string? StatusMessage { get; set; }
        [BindProperty] // Bind on POST to get selected roles from the form
        public List<RoleViewModel> AllRoles { get; set; } = [];
        public class RoleViewModel
        {
            public required string Value { get; set; }
            public string? Text { get; set; }
            public bool Selected { get; set; }
        }
        public class InputModel
        {
            public string? Id { get; set; }
            [Display(Name = "Username")]
            public string? UserName { get; set; }
            [EmailAddress]
            [Display(Name = "Email")]
            public string? Email { get; set; }
            [Display(Name = "Email Confirmed")]
            public bool EmailConfirmed { get; set; }
            [DataType(DataType.Password)]
            [Display(Name = "New Password")]
            public string? NewPassword { get; set; }
            [Phone]
            [Display(Name = "Cell Phone")]
            [RegularExpression(@"^\(?\d{3}\)?[-. ]?\d{3}[-. ]?\d{4}$", ErrorMessage = "Not a valid format; try ### ###-####")]
            public string? PhoneNumber { get; set; }
            [Display(Name = "Cell Confirmed")]
            public bool PhoneNumberConfirmed { get; set; }
            [Phone]
            [Display(Name = "Home Phone")]
            [RegularExpression(@"^\(?\d{3}\)?[-. ]?\d{3}[-. ]?\d{4}$", ErrorMessage = "Not a valid format; try ### ###-####")]
            public string? HomePhoneNumber { get; set; }
            [Required]
            [Display(Name = "First Name")]
            public string? FirstName { get; set; }
            [Display(Name = "Middle Name")]
            public string? MiddleName { get; set; }
            [Required]
            [Display(Name = "Last Name")]
            public string? LastName { get; set; }
            [Display(Name = "Birthday")]
            [DataType(DataType.Date)]
            public DateTime? Birthday { get; set; }
            [Display(Name = "Anniversary")]
            [DataType(DataType.Date)]
            public DateTime? Anniversary { get; set; }
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
            // [Display(Name = "Plot")] // Removed
            // public string? Plot { get; set; } // Removed
            [Display(Name = "Is Billing Contact")] // DisplayName might need update if "for Plot" is no longer relevant
            public bool IsBillingContact { get; set; }
            [Display(Name = "Is Two Factor On")]
            public bool TwoFactorEnabled { get; set; }
        }
        private async Task LoadUserAsync(IdentityUser user)
        {
            var userProfile = await _dbContext.UserProfile.FindAsync(user.Id);
            Input = new InputModel
            {
                Id = user.Id,
                UserName = user.UserName,
                Email = user.Email,
                EmailConfirmed = user.EmailConfirmed,
                PhoneNumber = user.PhoneNumber,
                PhoneNumberConfirmed = user.PhoneNumberConfirmed,
                TwoFactorEnabled = user.TwoFactorEnabled,
                HomePhoneNumber = userProfile?.HomePhoneNumber,
                FirstName = userProfile?.FirstName,
                MiddleName = userProfile?.MiddleName,
                LastName = userProfile?.LastName,
                Birthday = userProfile?.Birthday,
                Anniversary = userProfile?.Anniversary,
                AddressLine1 = userProfile?.AddressLine1,
                AddressLine2 = userProfile?.AddressLine2,
                City = userProfile?.City,
                State = userProfile?.State,
                ZipCode = userProfile?.ZipCode,
                // Plot = userProfile?.Plot, // Removed
                IsBillingContact = userProfile?.IsBillingContact ?? false
            };
            await PopulateRoleViewModelsAsync(user);
        }
        private async Task PopulateRoleViewModelsAsync(IdentityUser user)
        {
            var roles = await _roleManager.Roles.ToListAsync();
            var userRoles = await _userManager.GetRolesAsync(user);
            AllRoles = [.. roles.Select(role => new RoleViewModel
            {
                Value = role.Name ?? string.Empty,
                Text = role.Name ?? string.Empty,
                Selected = userRoles.Contains(role.Name ?? string.Empty)
            }).OrderBy(r => r.Text)];
        }
        public async Task<IActionResult> OnGetAsync(string id, string? returnUrl)
        {
            // Optionally capture SearchTerm and ShowExtraFields from returnUrl if needed for display
            if (!string.IsNullOrEmpty(returnUrl))
            {
                var uri = new Uri("http://dummyurl" + returnUrl); // Use a dummy base URI for parsing
                var queryParameters = QueryHelpers.ParseQuery(uri.Query);
                if (queryParameters.TryGetValue("SearchTerm", out var searchTermValue))
                {
                    SearchTerm = searchTermValue.ToString();
                }
                if (queryParameters.TryGetValue("ShowExtraFields", out var showExtraFieldsValue))
                {
                    ShowExtraFields = showExtraFieldsValue.ToString();
                }
            }
            if (string.IsNullOrEmpty(id))
            {
                StatusMessage = "Error: User ID is missing.";
                if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
                {
                    return Redirect(ReturnUrl);
                }
                return RedirectToPage("./Users");
            }
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                StatusMessage = $"Error: Unable to load user with ID '{id}'.";
                if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
                {
                    return Redirect(ReturnUrl);
                }
                return RedirectToPage("./Users");
            }
            await LoadUserAsync(user);
            Input.City ??= Environment.GetEnvironmentVariable("DEFAULT_CITY_HOA_CLOUD");
            Input.State ??= Environment.GetEnvironmentVariable("DEFAULT_STATE_HOA_CLOUD");
            Input.ZipCode ??= Environment.GetEnvironmentVariable("DEFAULT_ZIPCODE_HOA_CLOUD");
            return Page();
        }
        public async Task<IActionResult> OnPostAsync()
        {
            var userOnPost = await _userManager.FindByIdAsync(Input.Id ?? string.Empty);
            if (userOnPost != null)
            {
                await PopulateRoleViewModelsAsync(userOnPost);
            }
            if (!ModelState.IsValid)
            {
                StatusMessage = "Error: Please fix the validation errors.";
                return Page();
            }
            var user = await _userManager.FindByIdAsync(Input.Id ?? string.Empty);
            if (user == null)
            {
                StatusMessage = $"Error: Unable to find user with ID '{Input.Id}' to update.";
                if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
                {
                    return Redirect(ReturnUrl);
                }
                return RedirectToPage("./Users");
            }
            user.UserName = Input.UserName;
            user.Email = Input.Email;
            user.EmailConfirmed = Input.EmailConfirmed;
            user.PhoneNumber = Input.PhoneNumber;
            user.PhoneNumberConfirmed = Input.PhoneNumberConfirmed;
            user.TwoFactorEnabled = Input.TwoFactorEnabled;
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                StatusMessage = "Error: User update failed.";
                return Page();
            }
            var userProfile = await _dbContext.UserProfile.FindAsync(Input.Id);
            if (userProfile == null)
            {
                userProfile = new UserProfile { UserId = Input.Id ?? string.Empty, User = user };
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
            // userProfile.Plot = Input.Plot; // Removed
            userProfile.HomePhoneNumber = Input.HomePhoneNumber;

            // Simplified IsBillingContact assignment
            userProfile.IsBillingContact = Input.IsBillingContact;

            await _dbContext.SaveChangesAsync(); // Consolidated SaveChanges
            StatusMessage = "User updated successfully.";

            if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            {
                return Redirect(ReturnUrl);
            }
            else
            {
                // Fallback: Redirect to the default Users page if ReturnUrl is invalid or missing
                return RedirectToPage("./Users");
            }
        }
        public IActionResult OnPostCancel()
        {
            StatusMessage = "User Cancelled.";
            if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            {
                return Redirect(ReturnUrl);
            }
            else
            {
                // Fallback: Redirect to the default Users page if ReturnUrl is invalid or missing
                return RedirectToPage("./Users");
            }
        }
        public async Task<IActionResult> OnPostDeleteAsync()
        {
            // Find the user to delete using the Id from the bound Input model
            var userToDelete = await _userManager.FindByIdAsync(Input.Id ?? string.Empty);
            if (userToDelete == null)
            {
                StatusMessage = $"Error: Unable to find user with ID '{Input.Id}' to delete.";
                // On error after post, if ReturnUrl is valid, go back there. Otherwise, base Users.
                if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
                {
                    return Redirect(ReturnUrl);
                }
                return RedirectToPage("./Users");
            }
            // Prevent deleting the currently logged-in user (optional but recommended)
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser != null)
            {
                if (userToDelete.Id == currentUser.Id)
                {
                    StatusMessage = "Error: You cannot delete your own account.";
                    if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
                    {
                        return Redirect(ReturnUrl);
                    }
                    return RedirectToPage("./Users");
                }
            }
            // Delete the user
            var result = await _userManager.DeleteAsync(userToDelete);
            if (!result.Succeeded)
            {
                // If deletion fails, add errors to ModelState and return to the page
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                // Need to repopulate the user and roles if returning to the page on error
                var userOnPost = await _userManager.FindByIdAsync(Input.Id ?? string.Empty);
                if (userOnPost != null)
                {
                    await LoadUserAsync(userOnPost); // Load user data and roles
                }
                StatusMessage = "Error: User deletion failed.";
                return Page(); // Stay on the Edit page with errors
            }
            // Delete the associated UserProfile as well
            var userProfileToDelete = await _dbContext.UserProfile.FindAsync(userToDelete.Id);
            if (userProfileToDelete != null)
            {
                _dbContext.UserProfile.Remove(userProfileToDelete);
                await _dbContext.SaveChangesAsync();
            }
            StatusMessage = $"User '{userToDelete.UserName}' deleted successfully.";
            if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            {
                return Redirect(ReturnUrl);
            }
            else
            {
                // Fallback: Redirect to the default Users page if ReturnUrl is invalid or missing
                return RedirectToPage("./Users");
            }
        }
    }
}