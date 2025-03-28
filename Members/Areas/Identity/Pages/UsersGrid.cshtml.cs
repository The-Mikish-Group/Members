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
    public class UsersGridModel(UserManager<IdentityUser> userManager, ApplicationDbContext dbContext) : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly ApplicationDbContext _dbContext = dbContext; // Inject ApplicationDbContext

        public class UserModel
        {
            public required string Id { get; set; }
            public required string UserName { get; set; }
            public string? FullName { get; set; }
            public required string Email { get; set; }
            public bool EmailConfirmed { get; set; }
            public string? PhoneNumber { get; set; }
            public bool PhoneNumberConfirmed { get; set; }           
            public string? HomePhoneNumber { get; set; }
            public string? FirstName { get; set; } 
            public string? MiddleName { get; set; }
            public string? LastName { get; set; }
            public string? AddressLine1 { get; set; }
            public string? AddressLine2 { get; set; }
            public string? City { get; set; }
            public string? State { get; set; }
            public string? ZipCode { get; set; }
            public string? Plot { get; set; }
            public string? Birthday { get; set; }
            public string? Anniversary { get; set; }
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
                string? firstName = null;
                string? middleName = null;
                string? lastName = null;
                string? addressLine1 = null;
                string? addressLine2 = null;
                string? city = null;
                string? state = null;
                string? zipCode = null;
                string? plot = null;
                string? birthday = null;
                string? anniversary = null;
                string? homePhoneNumber = null;

                // Check if userProfile is null and handle accordingly
                if (userProfile != null)
                {
                    fullName = $"{userProfile.FirstName} {(string.IsNullOrEmpty(userProfile.MiddleName) ? "" : userProfile.MiddleName + " ")}{userProfile.LastName}".Trim();
                    firstName = userProfile.FirstName;
                    middleName = userProfile.MiddleName;
                    lastName = userProfile.LastName;
                    homePhoneNumber = userProfile.HomePhoneNumber;
                    addressLine1 = userProfile.AddressLine1;
                    addressLine2 = userProfile.AddressLine2;
                    city = userProfile.City;
                    state = userProfile.State;
                    zipCode = userProfile.ZipCode;
                    plot = userProfile.Plot;
                    birthday = userProfile.Birthday?.ToString("yyyy-MM-dd");
                    anniversary = userProfile.Anniversary?.ToString("yyyy-MM-dd");                    
                }

                Users.Add(new UserModel
                {
                    Id = user.Id,
                    UserName = user.UserName ?? string.Empty,
                    Email = user.Email ?? string.Empty,
                    EmailConfirmed = user.EmailConfirmed,
                    PhoneNumber = user.PhoneNumber, // Get the PhoneNumber from IdentityUser
                    PhoneNumberConfirmed = user.PhoneNumberConfirmed,                    
                    Roles = roles,
                    FullName = fullName ?? "No Info",
                    FirstName = firstName,
                    MiddleName = middleName,
                    LastName = lastName,
                    AddressLine1 = addressLine1,
                    AddressLine2 = addressLine2,
                    City = city,
                    State = state,
                    ZipCode = zipCode,
                    Plot = plot,
                    Birthday = birthday,
                    Anniversary = anniversary
                });
            }

            // Search
            if (!string.IsNullOrEmpty(SearchTerm))
            {
                SearchTerm = SearchTerm.Trim();
                if (SearchTerm.Equals("bad", System.StringComparison.OrdinalIgnoreCase))
                {
                    Users = [.. Users.Where(u => (u.Roles == null || u.Roles.Count == 0) && u.EmailConfirmed == false)];
                }
                else if (SearchTerm.Equals("No Role", System.StringComparison.OrdinalIgnoreCase))
                {
                    Users = [.. Users.Where(u => u.Roles == null || u.Roles.Count == 0)];
                }
                else if (SearchTerm.Equals("Not Confirmed", System.StringComparison.OrdinalIgnoreCase))
                {
                    Users = [.. Users.Where(u => u.EmailConfirmed == false)];
                }
                else
                {
                    Users = [.. Users.Where(u =>
                        u.UserName?.Contains(SearchTerm, System.StringComparison.OrdinalIgnoreCase) == true ||
                        u.Email.Contains(SearchTerm, System.StringComparison.OrdinalIgnoreCase) ||
                        u.PhoneNumber?.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) == true || 
                        u.HomePhoneNumber?.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) == true || 
                        u.FullName?.Contains(SearchTerm, System.StringComparison.OrdinalIgnoreCase) == true ||
                        u.FirstName?.Contains(SearchTerm, System.StringComparison.OrdinalIgnoreCase) == true ||
                        u.MiddleName?.Contains(SearchTerm, System.StringComparison.OrdinalIgnoreCase) == true ||
                        u.LastName?.Contains(SearchTerm, System.StringComparison.OrdinalIgnoreCase) == true ||
                        u.AddressLine1?.Contains(SearchTerm, System.StringComparison.OrdinalIgnoreCase) == true ||
                        u.AddressLine2?.Contains(SearchTerm, System.StringComparison.OrdinalIgnoreCase) == true ||
                        u.City?.Contains(SearchTerm, System.StringComparison.OrdinalIgnoreCase) == true ||
                        u.State?.Contains(SearchTerm, System.StringComparison.OrdinalIgnoreCase) == true ||
                        u.ZipCode?.Contains(SearchTerm, System.StringComparison.OrdinalIgnoreCase) == true ||
                        u.Plot?.Contains(SearchTerm, System.StringComparison.OrdinalIgnoreCase) == true ||
                        u.Birthday?.Contains(SearchTerm, System.StringComparison.OrdinalIgnoreCase) == true ||
                        u.Anniversary?.Contains(SearchTerm, System.StringComparison.OrdinalIgnoreCase) == true ||                                              
                        u.Roles != null && u.Roles.Any(r => r.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
                    )];
                }
            }

            // Sorting
            if (!string.IsNullOrEmpty(SortColumn))
            {
                Users = SortColumn.ToLower() switch
                {
                    "fullname" => SortOrder?.ToLower() == "asc" ? [.. Users.OrderBy(u => u.FullName)] : [.. Users.OrderByDescending(u => u.FullName)],
                    "firstname" => SortOrder?.ToLower() == "asc" ? [.. Users.OrderBy(u => u.FirstName)] : [.. Users.OrderByDescending(u => u.FirstName)],
                    "middlename" => SortOrder?.ToLower() == "asc" ? [.. Users.OrderBy(u => u.MiddleName)] : [.. Users.OrderByDescending(u => u.MiddleName)],
                    "lastname" => SortOrder?.ToLower() == "asc" ? [.. Users.OrderBy(u => u.LastName)] : [.. Users.OrderByDescending(u => u.LastName)],
                    "addressline1" => SortOrder?.ToLower() == "asc" ? [.. Users.OrderBy(u => u.AddressLine1)] : [.. Users.OrderByDescending(u => u.AddressLine1)],
                    "addressline2" => SortOrder?.ToLower() == "asc" ? [.. Users.OrderBy(u => u.AddressLine2)] : [.. Users.OrderByDescending(u => u.AddressLine2)],
                    "city" => SortOrder?.ToLower() == "asc" ? [.. Users.OrderBy(u => u.City)] : [.. Users.OrderByDescending(u => u.City)],
                    "state" => SortOrder?.ToLower() == "asc" ? [.. Users.OrderBy(u => u.State)] : [.. Users.OrderByDescending(u => u.State)],
                    "zipcode" => SortOrder?.ToLower() == "asc" ? [.. Users.OrderBy(u => u.ZipCode)] : [.. Users.OrderByDescending(u => u.ZipCode)],
                    "plot" => SortOrder?.ToLower() == "asc" ? [.. Users.OrderBy(u => u.Plot)] : [.. Users.OrderByDescending(u => u.Plot)],
                    "birthday" => SortOrder?.ToLower() == "asc" ? [.. Users.OrderBy(u => u.Birthday)] : [.. Users.OrderByDescending(u => u.Birthday)],
                    "anniversary" => SortOrder?.ToLower() == "asc" ? [.. Users.OrderBy(u => u.Anniversary)] : [.. Users.OrderByDescending(u => u.Anniversary)],
                    "username" => SortOrder?.ToLower() == "asc" ? [.. Users.OrderBy(u => u.UserName)] : [.. Users.OrderByDescending(u => u.UserName)],
                    "email" => SortOrder?.ToLower() == "asc" ? [.. Users.OrderBy(u => u.Email)] : [.. Users.OrderByDescending(u => u.Email)],
                    "emailconfirmed" => SortOrder?.ToLower() == "asc" ? [.. Users.OrderBy(u => u.EmailConfirmed)] : [.. Users.OrderByDescending(u => u.EmailConfirmed)],
                    "phonenumber" => SortOrder?.ToLower() == "asc" ? [.. Users.OrderBy(u => u.PhoneNumber)] : [.. Users.OrderByDescending(u => u.PhoneNumber)], // Sort by IdentityUser's PhoneNumber
                    "phonenumberconfirmed" => SortOrder?.ToLower() == "asc" ? [.. Users.OrderBy(u => u.PhoneNumberConfirmed)] : [.. Users.OrderByDescending(u => u.PhoneNumberConfirmed)],
                    "homephonenumber" => SortOrder?.ToLower() == "asc" ? [.. Users.OrderBy(u => u.HomePhoneNumber)] : [.. Users.OrderByDescending(u => u.HomePhoneNumber)], // Sort by UserProfile's HomePhoneNumber
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