using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.WebUtilities;
using System.Text.Encodings.Web;
using System.Text;

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
            var selectedRoles = AllRoles?.Where(r => r.Selected).Select(r => r.Value ?? string.Empty).ToList() ??[];

            await _userManager.RemoveFromRolesAsync(user, originalRoles);
            await _userManager.AddToRolesAsync(user, selectedRoles);

            // Check if the user's email is confirmed
            if (user.EmailConfirmed)
            {
                // If email is confirmed, send the welcome email if the "Member" role was added
                if (selectedRoles.Contains("Member") && !originalRoles.Contains("Member"))
                {
                    if (!string.IsNullOrEmpty(user.Email))
                    {
                        await _emailSender.SendEmailAsync(
                            user.Email,
                            "Welcome! Your Oaks-Village Account is ready to use!",
                            "You have been granted Member access and " +
                            "can log in to https://oaks-village.com.<br /><br />"+
                            "If this Account was automatically generated for you, "+
                            "Please use the <a href=https://oaks-village.com/Identity/Account/ForgotPassword><strong>Forgot your Password</strong> "+
                            "link to create your password. <br><br>"+
                            "You will be asked for your email address so a link to "+
                            "complete the process can be sent to you.<br><br>" +
                            "Thank you from the <strong>Oaks-Village HOA</strong>"

                        );
                    }
                }
            }
            else
            {
                // If email is NOT confirmed, send the email confirmation link
                if (!string.IsNullOrEmpty(user.Email))
                {
                    var userId = await _userManager.GetUserIdAsync(user);
                    var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                    code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                    var callbackUrl = Url.Page(
                        "/Account/ConfirmEmail",
                        pageHandler: null,
                        values: new { area = "Identity", userId, code },
                        protocol: Request.Scheme);

                    if (callbackUrl != null)
                    {
                        await _emailSender.SendEmailAsync(
                            user.Email,
                            "Confirm Your Email to complete your registration",
                            $"Please confirm your email address by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'><strong>clicking here</strong></a>.<br /><br />Thank you from the team at <strong>Oaks-Village HOA</strong>"
                        );
                    }
                }
            }

            // Redirect back to the EditUser page, passing the id and searchTerm
            return RedirectToPage("./EditUser", new { id = UserId, SearchTerm });
        }
    }
}