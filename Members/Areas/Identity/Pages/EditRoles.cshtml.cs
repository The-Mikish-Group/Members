using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace Members.Areas.Identity.Pages
{
    public class EditRolesModel(UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager, IEmailSender emailSender) : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly RoleManager<IdentityRole> _roleManager = roleManager;
        private readonly IEmailSender _emailSender = emailSender; // Add this field

        [BindProperty]
        public required string UserId { get; set; }

        public required string UserName { get; set; }

        [BindProperty]
        public List<RoleViewModel> AllRoles { get; set; } = [];

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        public class RoleViewModel
        {
            public required string Value { get; set; }
            public required string Text { get; set; }
            public bool Selected { get; set; }
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

            UserId = user.Id;
            UserName = user.UserName ?? string.Empty;
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

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.FindByIdAsync(UserId);
            if (user == null)
            {
                return NotFound();
            }

            var originalRoles = await _userManager.GetRolesAsync(user);
            var selectedRoles = AllRoles?.Where(r => r.Selected).Select(r => r.Value ?? string.Empty).ToList() ?? [];

            await _userManager.RemoveFromRolesAsync(user, originalRoles);
            await _userManager.AddToRolesAsync(user, selectedRoles);

            // Check if the "Member" role was added
            if (selectedRoles.Contains("Member") && !originalRoles.Contains("Member"))
            {
                // Send the email
                if (!string.IsNullOrEmpty(user.Email))
                {
                    await _emailSender.SendEmailAsync(
                        user.Email,
                        "Welcome! Oaks-Village Account is Ready",
                        "You have been granted Member access and " +
                        "can log in to https://oaks-village.com."
                    );
                }
            }

            // Redirect back to the EditUser page, passing the id and searchTerm
            return RedirectToPage("./EditUser", new { id = UserId, SearchTerm });
        }
    }
}