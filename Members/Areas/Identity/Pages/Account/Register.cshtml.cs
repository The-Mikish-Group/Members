// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;

namespace Members.Areas.Identity.Pages.Account
{
    public class RegisterModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IUserStore<IdentityUser> _userStore;
        private readonly IUserEmailStore<IdentityUser> _emailStore;
        private readonly ILogger<RegisterModel> _logger;
        private readonly IEmailSender _emailSender;
        private readonly Data.ApplicationDbContext _dbContext;

        public RegisterModel(
            UserManager<IdentityUser> userManager,
            IUserStore<IdentityUser> userStore,
            SignInManager<IdentityUser> signInManager,
            ILogger<RegisterModel> logger,
            IEmailSender emailSender,
            ApplicationDbContext dbContext)
        {
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _signInManager = signInManager;
            _logger = logger;
            _emailSender = emailSender;
            _dbContext = dbContext;
            Input = new InputModel
            {
                Email = string.Empty,
                Password = string.Empty,
                ConfirmPassword = string.Empty,
                PhoneNumber = string.Empty,
                FirstName = string.Empty, // Initialize required properties to avoid CS9035 error
                LastName = string.Empty // Initialize required properties to avoid CS9035 error
            };
            ReturnUrl = string.Empty;
            ExternalLogins = [];
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ReturnUrl { get; set; }

        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        public class InputModel
        {
            // Email and Password
            [Required]
            [EmailAddress]
            [Display(Name = "Email")]
            public required string Email { get; set; }

            [Required]
            [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
            [DataType(DataType.Password)]
            [Display(Name = "Password")]
            public required string Password { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "Confirm password")]
            [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
            public required string ConfirmPassword { get; set; }

            // Name - First, Middle, and Last
            [Required]
            [Display(Name = "First Name")]
            public required string FirstName { get; set; }

            [Display(Name = "Middle Name")]
            public string? MiddleName { get; set; }

            [Required]
            [Display(Name = "Last Name")]
            public required string LastName { get; set; }

            // Birthday
            [Display(Name = "Birthday")]
            [DataType(DataType.Date)]
            public DateTime? Birthday { get; set; }

            // PhoneNumber
            [Required]
            [Phone]
            [Display(Name = "Phone Number")]
            [RegularExpression(@"^\(?\d{3}\)?[-. ]?\d{3}[-. ]?\d{4}$", ErrorMessage = "Not a valid format; try ### ###-####")]
            public string? PhoneNumber { get; set; }

            // Address - AddressLine1, AddressLine2, City, State, ZipCode
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

            // Plot Identifier
            [Display(Name = "Plot")]
            public string? Plot { get; set; }
        }

        public async Task OnGetAsync(string? returnUrl = null)
        {
            ReturnUrl = returnUrl ?? string.Empty;
            ExternalLogins = [.. (await _signInManager.GetExternalAuthenticationSchemesAsync())];
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            ExternalLogins = [.. (await _signInManager.GetExternalAuthenticationSchemesAsync())];
            if (ModelState.IsValid)
            {
                var user = CreateUser();

                await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
                await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);

                // Set the PhoneNumber property on the IdentityUser
                user.PhoneNumber = Input.PhoneNumber;

                var result = await _userManager.CreateAsync(user, Input.Password);

                if (result.Succeeded)
                {
                    // Create a new UserProfile
                    var userProfile = new UserProfile
                    {
                        UserId = user.Id,
                        FirstName = Input.FirstName,
                        MiddleName = Input.MiddleName,
                        LastName = Input.LastName,
                        Birthday = Input.Birthday,
                        AddressLine1 = Input.AddressLine1,
                        AddressLine2 = Input.AddressLine2,
                        City = Input.City,
                        State = Input.State,
                        ZipCode = Input.ZipCode,
                        Plot = Input.Plot,
                        User = user // Set the required User property
                    };

                    _dbContext.UserProfile.Add(userProfile);
                    await _dbContext.SaveChangesAsync();

                    if (_userManager.Options.SignIn.RequireConfirmedAccount)
                    {

                        // Send notification email to OaksVillage@oaks-village.com
                        await _emailSender.SendEmailAsync(
                            "OaksVillage@oaks-village.com",
                            "New User Registration",
                            $"{Input.FirstName} {Input.MiddleName} {Input.LastName} with email {Input.Email} has registered."
                        );

                        var userId = await _userManager.GetUserIdAsync(user);
                        var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                        string? callbackUrl = Url.Page(
                            "/Account/ConfirmEmail",
                            pageHandler: null,
                            values: new { area = "Identity", userId, code, returnUrl },
                            protocol: Request.Scheme);

                        // Check if callbackUrl is not null before proceeding
                        if (callbackUrl != null)
                        {
                            // Send the email with the reset link
                            await _emailSender.SendEmailAsync(
                                Input.Email,
                                "Confirm Your Email Address",
                                $"Please confirm your <strong>email address</strong> by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>." +
                                "<br /><br /><br /> After <strong>email confirmation</strong> your new account will be reviewed by the managers." +
                                "<br /><br />That review could take up to 24 hours so please be patient. <br/><br/>When approved, you will receive a <strong>'Welcome Email'</strong>. We are " +
                                "a small volunteer staff, so please be patient." +
                                "<br /><br />Thank you from the team at <strong>Oaks-Village HOA<strong>."
                            );
                        }
                        else
                        {
                            // Log an error or handle the case where the URL could not be generated
                            ModelState.AddModelError(string.Empty, "Error generating password reset link.");
                            return Page();
                        }

                        return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl });
                    }
                    else
                    {
                        await _signInManager.SignInAsync(user, isPersistent: false);
                        return LocalRedirect(returnUrl);
                    }
                }
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            // If we got this far, something failed, redisplay form
            return Page();
        }

        private IdentityUser CreateUser()
        {
            try
            {
                return Activator.CreateInstance<IdentityUser>();
            }
            catch
            {
                throw new InvalidOperationException($"Can't create an instance of '{nameof(IdentityUser)}'. " +
                    $"Ensure that '{nameof(IdentityUser)}' is not an abstract class and has a parameterless constructor, or alternatively " +
                    $"override the register page in /Areas/Identity/Pages/Account/Register.cshtml");
            }
        }

        private IUserEmailStore<IdentityUser> GetEmailStore()
        {
            if (!_userManager.SupportsUserEmail)
            {
                throw new NotSupportedException("The default UI requires a user store with email support.");
            }
            return (IUserEmailStore<IdentityUser>)_userStore;
        }
    }
}
