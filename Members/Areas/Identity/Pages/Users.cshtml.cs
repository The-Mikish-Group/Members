using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Members.Areas.Identity.Pages
{
    public class UsersModel(UserManager<IdentityUser> userManager) : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager = userManager;

        public class UserModel
        {
            public required string Id { get; set; }
            public required string UserName { get; set; }
            public string? FullName { get; set; }
            public required string Email { get; set; }
            public string? PhoneNumber { get; set; }
            public bool EmailConfirmed { get; set; }
            public IList<string>? Roles { get; set; }
        }

        public required List<UserModel> Users { get; set; }

        [BindProperty(SupportsGet = true)]
        public required string? SortColumn { get; set; } = null;

        [BindProperty(SupportsGet = true)]
        public required string? SortOrder { get; set; } = null;

        [BindProperty(SupportsGet = true)]
        public required string? SearchTerm { get; set; } = null;

        public async Task OnGetAsync()
        {
            var users = await _userManager.Users.ToListAsync();
            Users = new List<UserModel>();

            foreach (var user in users)
            {
                var fullNameClaim = (await _userManager.GetClaimsAsync(user)).FirstOrDefault(c => c.Type == "FullName");
                var roles = await _userManager.GetRolesAsync(user);

                Users.Add(new UserModel
                {
                    Id = user.Id,
                    UserName = user.UserName ?? string.Empty,
                    Email = user.Email ?? string.Empty,
                    PhoneNumber = user.PhoneNumber,
                    EmailConfirmed = user.EmailConfirmed,
                    FullName = fullNameClaim?.Value,
                    Roles = roles
                });
            }

            // Search
            if (!string.IsNullOrEmpty(SearchTerm))
            {
                // Trim leading and trailing spaces
                SearchTerm = SearchTerm.Trim();

                if (SearchTerm.Equals("No Role", System.StringComparison.OrdinalIgnoreCase))
                {
                    // Search for empty roles
                    Users = Users.Where(u => u.Roles == null || u.Roles.Count == 0).ToList();
                }
                else if (SearchTerm.Equals("Not Confirmed", System.StringComparison.OrdinalIgnoreCase))
                {
                    // Search for unconfirmed emails
                    Users = Users.Where(u => u.EmailConfirmed == false).ToList();
                }
                else
                {
                    // General Search
                    Users = Users.Where(u =>
                        u.FullName?.Contains(SearchTerm, System.StringComparison.OrdinalIgnoreCase) == true ||
                        u.Email.Contains(SearchTerm, System.StringComparison.OrdinalIgnoreCase) ||
                        u.PhoneNumber?.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) == true ||
                        (u.Roles != null && u.Roles.Any(r => r.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)))
                    ).ToList();
                }
            }

            // Sorting
            if (!string.IsNullOrEmpty(SortColumn))
            {
                switch (SortColumn.ToLower())
                {
                    case "fullname":
                        Users = SortOrder?.ToLower() == "asc" ? Users.OrderBy(u => u.FullName).ToList() : Users.OrderByDescending(u => u.FullName).ToList();
                        break;
                    case "email":
                        Users = SortOrder?.ToLower() == "asc" ? Users.OrderBy(u => u.Email).ToList() : Users.OrderByDescending(u => u.Email).ToList();
                        break;
                    case "emailconfirmed":
                        Users = SortOrder?.ToLower() == "asc" ? Users.OrderBy(u => u.EmailConfirmed).ToList() : Users.OrderByDescending(u => u.EmailConfirmed).ToList();
                        break;
                    case "phonenumber":
                        Users = SortOrder?.ToLower() == "asc" ? Users.OrderBy(u => u.PhoneNumber).ToList() : Users.OrderByDescending(u => u.PhoneNumber).ToList();
                        break;
                    case "roles":
                        Users = SortOrder?.ToLower() == "asc" ? Users.OrderBy(u => u.Roles?.FirstOrDefault()).ToList() : Users.OrderByDescending(u => u.Roles?.FirstOrDefault()).ToList();
                        break;
                    default:
                        Users = Users.OrderBy(u => u.FullName).ThenBy(u => u.Email).ToList();
                        break;
                }
            }
            else
            {
                // Default Sorting
                Users = Users.OrderBy(u => u.FullName).ThenBy(u => u.Email).ToList();
            }
        }
    }
}