﻿using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Linq;
using Members.Data; // Make sure this namespace is correct for your DbContext
using Members.Models; // Make sure this namespace is correct for your UserProfile model

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
            [Phone]
            [Display(Name = "Phone Number")]
            [RegularExpression(@"^\(?\d{3}\)?[-. ]?\d{3}[-. ]?\d{4}$", ErrorMessage = "Not a valid format; try ### ###-####")]
            public string? PhoneNumber { get; set; }

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
            [Display(Name = "State")]
            public string? ZipCode { get; set; }

            [Display(Name = "Plot")]
            public string? Plot { get; set; }
        }

        private async Task LoadAsync(IdentityUser user)
        {
            var userName = await _userManager.GetUserNameAsync(user);
            var phoneNumber = await _userManager.GetPhoneNumberAsync(user);

            var userProfile = await _dbContext.UserProfile.FindAsync(user.Id);

            Username = userName;

            Input = new InputModel
            {
                PhoneNumber = phoneNumber,
                FirstName = userProfile?.FirstName ?? string.Empty,
                MiddleName = userProfile?.MiddleName,
                LastName = userProfile?.LastName ?? string.Empty,                
                Birthday = userProfile?.Birthday?.ToString("yyyy-MM-dd") ?? string.Empty,
                AddressLine1 = userProfile?.AddressLine1,
                AddressLine2 = userProfile?.AddressLine2,
                City = userProfile?.City,
                State = userProfile?.State,
                ZipCode = userProfile?.ZipCode,
                Plot = userProfile?.Plot
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

            // Update UserProfile
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
            userProfile.AddressLine1 = Input.AddressLine1;
            userProfile.AddressLine2 = Input.AddressLine2;
            userProfile.City = Input.City;
            userProfile.State = Input.State;
            userProfile.ZipCode = Input.ZipCode;
            userProfile.Plot = Input.Plot;

            await _dbContext.SaveChangesAsync();
            await _signInManager.RefreshSignInAsync(user);

            StatusMessage = "Your profile has been updated";
            return RedirectToPage();
        }
    }
}