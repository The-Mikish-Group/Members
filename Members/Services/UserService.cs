using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Members.Services 
{
    public class UserService(UserManager<IdentityUser> userManager, ApplicationDbContext dbContext)
    {
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly ApplicationDbContext _dbContext = dbContext; 

        public async Task ImportUsersFromImportFileAsync()
        {
            var importRecords = await _dbContext.ImportFile.ToListAsync();

            foreach (var record in importRecords)
            {
                if (record.Email == null)
                {
                    Console.WriteLine("Skipping record with null email.");
                    continue;
                }

                var existingUser = await _userManager.FindByEmailAsync(record.Email);

                if (existingUser == null)
                {
                    var newUser = new IdentityUser
                    {
                        UserName = record.Email,
                        Email = record.Email,
                        PhoneNumber = record.PhoneNumber,
                        EmailConfirmed = true,
                        PhoneNumberConfirmed = false
                    };

                    var result = await _userManager.CreateAsync(newUser);

                    if (result.Succeeded)
                    {
                        var userProfile = new UserProfile
                        {
                            UserId = newUser.Id,
                            FirstName = record.FirstName,
                            LastName = record.LastName,
                            HomePhoneNumber = record.HomePhoneNumber,
                            AddressLine1 = record.AddressLine1,
                            City = record.City,
                            State = record.ZipCode, // Corrected spelling here
                            Plot = record.Plot,
                            User = newUser // Set the required User property
                        };

                        _dbContext.UserProfile.Add(userProfile);
                        await _dbContext.SaveChangesAsync();

                        Console.WriteLine($"User created: {record.Email}");
                    }
                    else
                    {
                        Console.WriteLine($"Error creating user {record.Email}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
                    }
                }
                else
                {
                    Console.WriteLine($"User with email {record.Email} already exists.");
                }
            }

            Console.WriteLine("User import process completed.");
        }
    }
}