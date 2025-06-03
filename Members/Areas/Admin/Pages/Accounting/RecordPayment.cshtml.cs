using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
namespace Members.Areas.Admin.Pages.Accounting
{
    [Authorize(Roles = "Admin,Manager")] // Or your specific admin roles
    public class RecordPaymentModel(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager,
        ILogger<RecordPaymentModel> logger) : PageModel
    {
        [BindProperty]
        public InputModel Input { get; set; } = new InputModel();
        public SelectList? UserSelectList { get; set; }
        // Optional: We can add a list of unpaid invoices for the selected user later.
        // public SelectList? UnpaidInvoiceSelectList { get; set; } 
        public class InputModel
        {
            [Required]
            [Display(Name = "User")]
            public string SelectedUserID { get; set; } = string.Empty;
            // Optional: If linking directly to an invoice
            [Display(Name = "For Invoice (Optional)")]
            public int? SelectedInvoiceID { get; set; }
            [Required]
            [DataType(DataType.Date)]
            [Display(Name = "Payment Date")]
            public DateTime PaymentDate { get; set; } = DateTime.Today;
            [Required]
            [Range(0.01, 1000000.00, ErrorMessage = "Amount must be greater than 0.")]
            [DataType(DataType.Currency)]
            [Display(Name = "Payment Amount")]
            public decimal Amount { get; set; }
            [Required]
            [Display(Name = "Payment Method")]
            public PaymentMethod Method { get; set; } = PaymentMethod.Check;
            [StringLength(100)]
            [Display(Name = "Reference Number (e.g., Check #)")]
            public string? ReferenceNumber { get; set; }
            [StringLength(200)]
            [Display(Name = "Notes (Optional)")]
            public string? Notes { get; set; }
        }
        public async Task OnGetAsync(string? userId)
        {
            logger.LogInformation("OnGetAsync called for RecordPaymentModel.");
            await PopulateUserSelectList();
            if (!string.IsNullOrEmpty(userId))
            {
                Input.SelectedUserID = userId;
                // TODO: Optionally load unpaid invoices for this user if SelectedUserID is pre-filled
                // For now, this part is manual. We can enhance later to show invoices for the selected user.
                logger.LogInformation($"Pre-selected UserID: {userId}");
            }
        }
        private async Task PopulateUserSelectList()
        {
            var memberRoleName = "Member";
            var usersInMemberRole = await userManager.GetUsersInRoleAsync(memberRoleName);
            logger.LogInformation($"Found {usersInMemberRole?.Count ?? 0} users in role '{memberRoleName}' for UserSelectList.");
            if (usersInMemberRole == null || !usersInMemberRole.Any())
            {
                UserSelectList = new SelectList(Enumerable.Empty<SelectListItem>());
                return;
            }
            var userIdsInMemberRole = usersInMemberRole.Select(u => u.Id).ToList();
            var userProfiles = await context.UserProfile
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
                }
            }
            logger.LogInformation($"Total items prepared for UserSelectList: {userListItems.Count}. Profiles matched with LastName: {profilesMatched}.");
            UserSelectList = new SelectList(userListItems.OrderBy(item => item.Text), "Value", "Text");
        }
        public async Task<IActionResult> OnPostAsync()
        {
            logger.LogInformation("OnPostAsync called for RecordPaymentModel.");
            if (!ModelState.IsValid)
            {
                logger.LogWarning("OnPostAsync: ModelState is invalid.");
                await PopulateUserSelectList(); // Repopulate UserSelectList if returning to page
                return Page();
            }
            var user = await userManager.FindByIdAsync(Input.SelectedUserID);
            if (user == null)
            {
                logger.LogWarning($"OnPostAsync: Selected user with ID {Input.SelectedUserID} not found.");
                ModelState.AddModelError("Input.SelectedUserID", "Selected user not found.");
                await PopulateUserSelectList();
                return Page();
            }
            var payment = new Payment
            {
                UserID = Input.SelectedUserID,
                InvoiceID = Input.SelectedInvoiceID, // Can be null
                PaymentDate = Input.PaymentDate,
                Amount = Input.Amount,
                Method = Input.Method,
                ReferenceNumber = Input.ReferenceNumber,
                Notes = Input.Notes,
                DateRecorded = DateTime.UtcNow
            };
            context.Payments.Add(payment);
            // If an invoice was selected and payment fully covers it, update invoice status.
            // For simplicity now, we're not automatically updating invoice status here.
            // That would require fetching the invoice, checking amounts, etc.
            // We can add that logic as an enhancement.
            // For now, an admin would manually reconcile payments to invoices if needed.
            await context.SaveChangesAsync();
            logger.LogInformation($"Successfully saved new payment ID {payment.PaymentID} for user {user.UserName}.");
            TempData["StatusMessage"] = $"Payment of {payment.Amount:C} recorded successfully for user {user.UserName}.";
            return RedirectToPage(); // Redirect back to a clean form
        }
    }
}
