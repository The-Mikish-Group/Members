using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Members.Areas.Identity.Pages
{
    public class UsersModel(UserManager<IdentityUser> userManager, ApplicationDbContext dbContext) : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly ApplicationDbContext _dbContext = dbContext; // Inject ApplicationDbContext

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
            Users = [];

            foreach (var user in users)
            {
                var userProfile = await _dbContext.UserProfile.FirstOrDefaultAsync(up => up.UserId == user.Id);
                var roles = await _userManager.GetRolesAsync(user);

                string? fullName = null;
                if (userProfile != null)
                {
                    fullName = $"{userProfile.FirstName} {(string.IsNullOrEmpty(userProfile.MiddleName) ? "" : userProfile.MiddleName + " ")}{userProfile.LastName}".Trim();
                }

                Users.Add(new UserModel
                {
                    Id = user.Id,
                    UserName = user.UserName ?? string.Empty,
                    Email = user.Email ?? string.Empty,
                    PhoneNumber = user.PhoneNumber,
                    EmailConfirmed = user.EmailConfirmed,
                    FullName = fullName ?? "No Info", // Display "No Profile" if UserProfile is missing
                    Roles = roles
                });
            }

            // Search
            if (!string.IsNullOrEmpty(SearchTerm))
            {
                // Trim leading and trailing spaces
                SearchTerm = SearchTerm.Trim();
                if (SearchTerm.Equals("bad", System.StringComparison.OrdinalIgnoreCase))
                {
                    // EmailConfirmed and Role incomplete
                    Users = [.. Users.Where(u => (u.Roles == null || u.Roles.Count == 0) && u.EmailConfirmed == false)];
                }
                else if (SearchTerm.Equals("No Role", System.StringComparison.OrdinalIgnoreCase))
                {
                    // Search for empty roles
                    Users = [.. Users.Where(u => u.Roles == null || u.Roles.Count == 0)];
                }
                else if (SearchTerm.Equals("Not Confirmed", System.StringComparison.OrdinalIgnoreCase))
                {
                    // Search for unconfirmed emails
                    Users = [.. Users.Where(u => u.EmailConfirmed == false)];
                }
                else
                {
                    // General Search
                    Users = [.. Users.Where(u =>
                        u.FullName?.Contains(SearchTerm, System.StringComparison.OrdinalIgnoreCase) == true ||
                        u.Email.Contains(SearchTerm, System.StringComparison.OrdinalIgnoreCase) ||
                        u.PhoneNumber?.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) == true ||
                        (u.Roles != null && u.Roles.Any(r => r.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)))
                    )];
                }
            }

            // Sorting
            if (!string.IsNullOrEmpty(SortColumn))
            {
                Users = SortColumn.ToLower() switch
                {
                    "fullname" => SortOrder?.ToLower() == "asc" ? [.. Users.OrderBy(u => u.FullName)] : [.. Users.OrderByDescending(u => u.FullName)],
                    "email" => SortOrder?.ToLower() == "asc" ? [.. Users.OrderBy(u => u.Email)] : [.. Users.OrderByDescending(u => u.Email)],
                    "emailconfirmed" => SortOrder?.ToLower() == "asc" ? [.. Users.OrderBy(u => u.EmailConfirmed)] : [.. Users.OrderByDescending(u => u.EmailConfirmed)],
                    "phonenumber" => SortOrder?.ToLower() == "asc" ? [.. Users.OrderBy(u => u.PhoneNumber)] : [.. Users.OrderByDescending(u => u.PhoneNumber)],
                    "roles" => SortOrder?.ToLower() == "asc" ? [.. Users.OrderBy(u => u.Roles?.FirstOrDefault())] : [.. Users.OrderByDescending(u => u.Roles?.FirstOrDefault())],
                    _ => [.. Users.OrderBy(u => u.FullName).ThenBy(u => u.Email)],
                };
            }
            else
            {
                // Default Sorting
                Users = [.. Users.OrderBy(u => u.FullName).ThenBy(u => u.Email)];
            }
        }
    }
}