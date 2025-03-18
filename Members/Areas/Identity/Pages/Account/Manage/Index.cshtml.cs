using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Linq;

namespace Members.Areas.Identity.Pages.Account.Manage
{
    public class IndexModel(
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager) : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly SignInManager<IdentityUser> _signInManager = signInManager;

        public string? Username { get; set; }

        [TempData]
        public required string StatusMessage { get; set; }

        [BindProperty]
        public required InputModel Input { get; set; }

        public class InputModel
        {
            [Phone]
            [Display(Name = "Phone Number")]
            [RegularExpression(@"^\(?([0-9]{3})\)?[-. ]?([0-9]{3})[-. ]?([0-9]{4})$", ErrorMessage = "Not a valid format; try ### ###-###")]
            public string? PhoneNumber { get; set; }

            [Required]
            [Display(Name = "Full Name")]
            public required string FullName { get; set; } // Add FullName property
        }

        private async Task LoadAsync(IdentityUser user)
        {
            var userName = await _userManager.GetUserNameAsync(user);
            var phoneNumber = await _userManager.GetPhoneNumberAsync(user);
            var claims = await _userManager.GetClaimsAsync(user);
            var fullNameClaim = claims.FirstOrDefault(c => c.Type == "FullName");

            Username = userName;

            Input = new InputModel
            {
                PhoneNumber = phoneNumber,
                FullName = fullNameClaim?.Value ?? string.Empty // Load FullName claim with null check
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

            var phoneNumber = await _userManager.GetPhoneNumberAsync(user);
            if (Input.PhoneNumber != phoneNumber)
            {
                var setPhoneResult = await _userManager.SetPhoneNumberAsync(user, Input.PhoneNumber);
                if (!setPhoneResult.Succeeded)
                {
                    StatusMessage = "Unexpected error when trying to set phone number.";
                    return RedirectToPage();
                }
            }

            // Update FullName claim
            var fullNameClaim = new System.Security.Claims.Claim("FullName", Input.FullName ?? string.Empty);
            var existingFullNameClaim = await _userManager.GetClaimsAsync(user);
            var oldFullName = existingFullNameClaim.FirstOrDefault(c => c.Type == "FullName");

            if (oldFullName != null)
            {
                await _userManager.ReplaceClaimAsync(user, oldFullName, fullNameClaim);
            }
            else
            {
                await _userManager.AddClaimAsync(user, fullNameClaim);
            }

            await _signInManager.RefreshSignInAsync(user);
            StatusMessage = "" +
                "Your profile has been updated";
            return RedirectToPage();
        }
    }
}