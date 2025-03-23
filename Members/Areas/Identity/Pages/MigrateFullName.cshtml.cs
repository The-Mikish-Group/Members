using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Members.Areas.Identity.Pages
{
    public class MigrateFullNameModel(UserManager<IdentityUser> userManager, ApplicationDbContext dbContext) : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly ApplicationDbContext _dbContext = dbContext;

        public string MigrationResult { get; set; } = "";

        public async Task OnGetAsync()
        {
            int migratedCount = 0;
            var users = await _userManager.Users.ToListAsync();

            foreach (var user in users)
            {
                var fullNameClaim = (await _userManager.GetClaimsAsync(user)).FirstOrDefault(c => c.Type == "FullName");

                if (fullNameClaim != null && !string.IsNullOrWhiteSpace(fullNameClaim.Value))
                {
                    var fullNameParts = fullNameClaim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    string firstName = "";
                    string? middleName = null;
                    string lastName = "";

                    if (fullNameParts.Length > 0)
                    {
                        firstName = fullNameParts[0];
                    }
                    if (fullNameParts.Length > 1)
                    {
                        lastName = fullNameParts[^1]; // Last element
                        if (fullNameParts.Length > 2)
                        {
                            middleName = string.Join(" ", fullNameParts.Skip(1).Take(fullNameParts.Length - 2));
                        }
                    }

                    var userProfile = await _dbContext.UserProfile.FirstOrDefaultAsync(up => up.UserId == user.Id);

                    if (userProfile == null)
                    {
                        userProfile = new UserProfile { UserId = user.Id, User = user };
                        _dbContext.UserProfile.Add(userProfile);
                    }

                    userProfile.FirstName = firstName;
                    userProfile.MiddleName = middleName;
                    userProfile.LastName = lastName;

                    await _dbContext.SaveChangesAsync();
                    migratedCount++;
                }
            }

            MigrationResult = $"Successfully migrated {migratedCount} user profiles.";
        }
    }
}