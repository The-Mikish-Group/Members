using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Members.Areas.Identity.Pages
{
    public class ViewPdfModel(SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> userManager) : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager = signInManager;
        private readonly UserManager<IdentityUser> _userManager = userManager;

        public string FileName { get; set; } = string.Empty;
        public bool IsAuthorized { get; set; }

        public void OnGet()
        {
            if (_signInManager.IsSignedIn(User) && (IsUserInRole("Member") || IsUserInRole("Admin") || IsUserInRole("Manager")))
            {
                IsAuthorized = true;
                FileName = Request.Query["fileName"].FirstOrDefault() ?? "";
            }
            else
            {
                IsAuthorized = false;
            }
        }

        private bool IsUserInRole(string role)
        {
            var user = _userManager.GetUserAsync(User).Result;
            return user != null && _userManager.IsInRoleAsync(user, role).Result;
        }
    }
}