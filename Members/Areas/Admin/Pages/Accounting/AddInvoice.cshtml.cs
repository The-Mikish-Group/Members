using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Authorization; // Ensure this is present
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging; // Ensure this is present
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema; // For [Column] attribute, if you keep it in InputModel
using System.Linq;
using System.Threading.Tasks;
namespace Members.Areas.Admin.Pages.Accounting
{
    [Authorize(Roles = "Admin,Manager")] // Or your specific admin roles
    public class AddInvoiceModel(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager,
        ILogger<AddInvoiceModel> logger) : PageModel // Corrected Primary Constructor
    {
        // private readonly ApplicationDbContext _context = context; // No longer needed with primary constructors like this
        // private readonly UserManager<IdentityUser> _userManager = userManager; // No longer needed
        // private readonly ILogger<AddInvoiceModel> _logger = logger; // No longer needed
        [BindProperty]
        public InputModel Input { get; set; } = new InputModel();
        public SelectList? UserSelectList { get; set; }
        public class InputModel
        {
            [Required]
            [Display(Name = "User")]
            public string SelectedUserID { get; set; } = string.Empty;

            [Required]
            [DataType(DataType.Date)]
            [Display(Name = "Invoice Date")]
            public DateTime InvoiceDate { get; set; } = DateTime.Today;

            [Required]
            [DataType(DataType.Date)]
            [Display(Name = "Due Date")]
            public DateTime DueDate { get; set; } = DateTime.Today.AddDays(30);

            [Required]
            [StringLength(200)]
            [Display(Name = "Description")] // <<-- ADDED THIS ATTRIBUTE
            public string Description { get; set; } = string.Empty;

            [Required]
            [Range(0.01, 1000000.00, ErrorMessage = "Amount must be greater than 0.")]
            [DataType(DataType.Currency)]
            [Display(Name = "Amount Due")]
            public decimal AmountDue { get; set; }

            [Required]
            [Display(Name = "Invoice Type")]
            public InvoiceType Type { get; set; } = InvoiceType.MiscCharge;
        }
        public async Task OnGetAsync()
        {
            var memberRoleName = "Member";
            logger.LogInformation("OnGetAsync called for AddInvoiceModel.");
            var usersInMemberRole = await userManager.GetUsersInRoleAsync(memberRoleName);
            logger.LogInformation($"Found {usersInMemberRole?.Count ?? 0} users in role '{memberRoleName}'.");
            if (usersInMemberRole == null || !usersInMemberRole.Any())
            {
                UserSelectList = new SelectList(Enumerable.Empty<SelectListItem>());
                logger.LogWarning("UserSelectList will be empty because no users were found in the role.");
                return;
            }
            var userIdsInMemberRole = usersInMemberRole.Select(u => u.Id).ToList();
            // logger.LogInformation($"First few UserIDs in role: {string.Join(", ", userIdsInMemberRole.Take(5))}");
            var userProfiles = await context.UserProfile // Use context directly from primary constructor
                                        .Where(up => userIdsInMemberRole.Contains(up.UserId))
                                        .ToDictionaryAsync(up => up.UserId);
            logger.LogInformation($"Found {userProfiles?.Count ?? 0} UserProfile records for these users.");
            var userListItems = new List<SelectListItem>();
            int profilesMatched = 0;
            foreach (var user in usersInMemberRole.OrderBy(u => u.UserName))
            {
                if (userProfiles != null)
                {
                    if (userProfiles.TryGetValue(user.Id, out UserProfile? profile) && profile != null && !string.IsNullOrEmpty(profile.LastName))
                    {
                        profilesMatched++;
                        userListItems.Add(new SelectListItem
                        {
                            Value = user.Id,
                            Text = $"{profile.LastName}, {profile.FirstName} ({user.Email})"
                        });
                    }
                }
                else
                {
                    userListItems.Add(new SelectListItem
                    {
                        Value = user.Id,
                        Text = $"{user.UserName} ({user.Email}) - Profile Incomplete"
                    });
                    // if (userProfiles.ContainsKey(user.Id))
                    //    logger.LogInformation($"User {user.UserName} has profile but LastName is missing.");
                    // else
                    //    logger.LogInformation($"User {user.UserName} profile not found in fetched profiles for ID {user.Id}.");
                }
            }
            logger.LogInformation($"Total items prepared for UserSelectList: {userListItems.Count}. Profiles matched with LastName: {profilesMatched}.");
            UserSelectList = new SelectList(userListItems.OrderBy(item => item.Text), "Value", "Text");
            logger.LogInformation("UserSelectList created.");
        }
        public async Task<IActionResult> OnPostAsync()
        {
            logger.LogInformation("OnPostAsync called for AddInvoiceModel."); // Added log
            if (!ModelState.IsValid)
            {
                logger.LogWarning("OnPostAsync: ModelState is invalid."); // Added log
                await OnGetAsync(); // Repopulate UserSelectList
                return Page();
            }
            var user = await userManager.FindByIdAsync(Input.SelectedUserID);
            if (user == null)
            {
                logger.LogWarning($"OnPostAsync: Selected user with ID {Input.SelectedUserID} not found."); // Added log
                ModelState.AddModelError(string.Empty, "Selected user not found.");
                await OnGetAsync();
                return Page();
            }
            var invoice = new Invoice
            {
                UserID = Input.SelectedUserID,
                InvoiceDate = Input.InvoiceDate,
                DueDate = Input.DueDate,
                Description = Input.Description,
                AmountDue = Input.AmountDue,
                AmountPaid = 0,
                Status = InvoiceStatus.Due,
                Type = Input.Type,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };
            context.Invoices.Add(invoice);
            await context.SaveChangesAsync();
            logger.LogInformation($"Successfully saved new invoice ID {invoice.InvoiceID} for user {user.UserName}."); // Added log
            TempData["StatusMessage"] = $"Invoice '{invoice.Description}' created successfully for user {user.UserName}.";
            return RedirectToPage();
        }
    }
}