using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Members.Areas.Admin.Pages.AccountsReceivable
{
    [Authorize(Roles = "Admin,Manager")]
    public class ApplyCreditsModel(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager,
        ILogger<ApplyCreditsModel> logger) : PageModel
    {
        private readonly ApplicationDbContext _context = context;
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly ILogger<ApplyCreditsModel> _logger = logger;

        [BindProperty]
        public InputModel Input { get; set; } = new InputModel();

        public SelectList? UserSelectList { get; set; }
        public string? TargetUserName { get; set; }
        public bool IsUserPreselected { get; set; } = false;
        
        public List<UserCreditViewModel> AvailableCredits { get; set; } = [];
        public List<OpenInvoiceViewModel> OpenInvoices { get; set; } = [];
        public decimal TotalAvailableCredit { get; set; }
        public decimal TotalAmountDue { get; set; }

        public class InputModel
        {
            [Required]
            [Display(Name = "Member")]
            public string SelectedUserID { get; set; } = string.Empty;
        }

        public class UserCreditViewModel
        {
            public int UserCreditID { get; set; }
            public decimal Amount { get; set; }
            public string Reason { get; set; } = string.Empty;
            public DateTime CreditDate { get; set; }
        }

        public class OpenInvoiceViewModel
        {
            public int InvoiceID { get; set; }
            public DateTime InvoiceDate { get; set; }
            public string Description { get; set; } = string.Empty;
            public decimal AmountDue { get; set; }
            public decimal AmountPaid { get; set; }
            public decimal AmountRemaining => AmountDue - AmountPaid;
        }

        public async Task OnGetAsync(string? userId)
        {
            if (!string.IsNullOrEmpty(userId))
            {
                await LoadUserData(userId);
            }
            else
            {
                await PopulateUserSelectList();
            }
        }

        public async Task<IActionResult> OnPostLoadUserDataAsync()
        {
            if (!ModelState.IsValid || string.IsNullOrEmpty(Input.SelectedUserID))
            {
                await PopulateUserSelectList();
                return Page();
            }

            return RedirectToPage(new { userId = Input.SelectedUserID });
        }

        public async Task<IActionResult> OnPostApplyAllCreditsAsync()
        {
            _logger.LogInformation("ApplyAllCredits called for User: {SelectedUserID}", Input.SelectedUserID);

            if (string.IsNullOrEmpty(Input.SelectedUserID))
            {
                TempData["ErrorMessage"] = "User ID is required.";
                return RedirectToPage();
            }

            // Get all available credits for the user
            var availableCredits = await _context.UserCredits
                .Where(uc => uc.UserID == Input.SelectedUserID && !uc.IsApplied && !uc.IsVoided && uc.Amount > 0)
                .OrderBy(uc => uc.CreditDate)
                .ToListAsync();

            if (!availableCredits.Any())
            {
                TempData["ErrorMessage"] = "No available credits found for this user.";
                return RedirectToPage(new { userId = Input.SelectedUserID });
            }

            // Get all open invoices for the user
            var openInvoices = await _context.Invoices
                .Where(i => i.UserID == Input.SelectedUserID &&
                           i.Status != InvoiceStatus.Cancelled &&
                           i.Status != InvoiceStatus.Draft &&
                           i.Status != InvoiceStatus.Paid &&
                           i.AmountPaid < i.AmountDue)
                .OrderBy(i => i.InvoiceDate)
                .ThenBy(i => i.InvoiceID)
                .ToListAsync();

            if (!openInvoices.Any())
            {
                TempData["ErrorMessage"] = $"No open invoices found. User has {availableCredits.Sum(c => c.Amount):C} in unused credits.";
                return RedirectToPage(new { userId = Input.SelectedUserID });
            }

            decimal totalCreditsApplied = 0;
            int invoicesPaid = 0;
            var detailMessages = new List<string>();

            try
            {
                foreach (var credit in availableCredits)
                {
                    if (credit.Amount <= 0) continue;

                    foreach (var invoice in openInvoices.Where(i => i.AmountPaid < i.AmountDue))
                    {
                        if (credit.Amount <= 0) break;

                        decimal amountNeeded = invoice.AmountDue - invoice.AmountPaid;
                        decimal amountToApply = Math.Min(credit.Amount, amountNeeded);

                        if (amountToApply > 0)
                        {
                            // Update invoice
                            invoice.AmountPaid += amountToApply;
                            invoice.LastUpdated = DateTime.UtcNow;
                            
                            if (invoice.AmountPaid >= invoice.AmountDue)
                            {
                                invoice.Status = InvoiceStatus.Paid;
                                invoice.AmountPaid = invoice.AmountDue;
                                invoicesPaid++;
                            }
                            else if (invoice.DueDate < DateTime.Today.AddDays(-1) && invoice.Status == InvoiceStatus.Due)
                            {
                                invoice.Status = InvoiceStatus.Overdue;
                            }

                            // Create credit application record
                            var creditApplication = new CreditApplication
                            {
                                UserCreditID = credit.UserCreditID,
                                InvoiceID = invoice.InvoiceID,
                                AmountApplied = amountToApply,
                                ApplicationDate = DateTime.UtcNow,
                                Notes = $"Applied via Admin Credit Utility. Original credit: {credit.Reason}"
                            };
                            _context.CreditApplications.Add(creditApplication);

                            // Update credit
                            credit.Amount -= amountToApply;
                            credit.LastUpdated = DateTime.UtcNow;
                            totalCreditsApplied += amountToApply;

                            detailMessages.Add($"• Applied {amountToApply:C} from UC{credit.UserCreditID} to INV-{invoice.InvoiceID:D5}");
                            _logger.LogInformation("Applied {Amount} from UserCredit {CreditId} to Invoice {InvoiceId}", 
                                amountToApply, credit.UserCreditID, invoice.InvoiceID);
                        }
                    }

                    // Mark credit as applied if fully used
                    if (credit.Amount <= 0)
                    {
                        credit.IsApplied = true;
                        credit.Amount = 0;
                        credit.AppliedDate = DateTime.UtcNow;
                    }
                }

                await _context.SaveChangesAsync();

                // Build comprehensive status message
                var statusMessage = $"<strong>Successfully applied {totalCreditsApplied:C} in credits!</strong><br/>";
                
                if (invoicesPaid > 0)
                {
                    statusMessage += $"✓ {invoicesPaid} invoice(s) fully paid<br/>";
                }
                
                var remainingCredit = availableCredits.Where(c => c.Amount > 0).Sum(c => c.Amount);
                if (remainingCredit > 0)
                {
                    statusMessage += $"• {remainingCredit:C} in credits remaining<br/>";
                }

                var remainingDue = openInvoices.Sum(i => Math.Max(0, i.AmountDue - i.AmountPaid));
                if (remainingDue > 0)
                {
                    statusMessage += $"• {remainingDue:C} still due on open invoices<br/>";
                }

                if (detailMessages.Count <= 10)
                {
                    statusMessage += "<br/><strong>Details:</strong><br/>";
                    statusMessage += string.Join("<br/>", detailMessages);
                }

                TempData["StatusMessage"] = statusMessage;
                _logger.LogInformation("Apply All Credits completed. Applied: {TotalApplied}, Invoices Paid: {InvoicesPaid}", 
                    totalCreditsApplied, invoicesPaid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying credits for user {UserId}", Input.SelectedUserID);
                TempData["ErrorMessage"] = $"Error applying credits: {ex.Message}";
                return RedirectToPage(new { userId = Input.SelectedUserID });
            }

            return RedirectToPage(new { userId = Input.SelectedUserID });
        }

        private async Task LoadUserData(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                var userProfile = await _context.UserProfile.FirstOrDefaultAsync(up => up.UserId == userId);
                TargetUserName = (userProfile != null && !string.IsNullOrWhiteSpace(userProfile.FirstName) && !string.IsNullOrWhiteSpace(userProfile.LastName))
                                 ? $"{userProfile.FirstName} {userProfile.LastName} ({user.Email})"
                                 : user.UserName ?? user.Email;
                
                Input.SelectedUserID = userId;
                IsUserPreselected = true;

                // Load available credits
                var credits = await _context.UserCredits
                    .Where(uc => uc.UserID == userId && !uc.IsApplied && !uc.IsVoided && uc.Amount > 0)
                    .OrderBy(uc => uc.CreditDate)
                    .ToListAsync();
                
                AvailableCredits = credits.Select(c => new UserCreditViewModel
                {
                    UserCreditID = c.UserCreditID,
                    Amount = c.Amount,
                    Reason = c.Reason,
                    CreditDate = c.CreditDate
                }).ToList();
                
                TotalAvailableCredit = AvailableCredits.Sum(c => c.Amount);

                // Load open invoices
                var invoices = await _context.Invoices
                    .Where(i => i.UserID == userId &&
                               i.Status != InvoiceStatus.Cancelled &&
                               i.Status != InvoiceStatus.Draft &&
                               i.Status != InvoiceStatus.Paid &&
                               i.AmountPaid < i.AmountDue)
                    .OrderBy(i => i.InvoiceDate)
                    .ThenBy(i => i.InvoiceID)
                    .ToListAsync();
                
                OpenInvoices = invoices.Select(i => new OpenInvoiceViewModel
                {
                    InvoiceID = i.InvoiceID,
                    InvoiceDate = i.InvoiceDate,
                    Description = i.Description,
                    AmountDue = i.AmountDue,
                    AmountPaid = i.AmountPaid
                }).ToList();
                
                TotalAmountDue = OpenInvoices.Sum(i => i.AmountRemaining);
            }
            else
            {
                await PopulateUserSelectList();
            }
        }

        private async Task PopulateUserSelectList()
        {
            var memberRoleName = "Member";
            var usersInMemberRole = await _userManager.GetUsersInRoleAsync(memberRoleName);
            
            if (usersInMemberRole == null || !usersInMemberRole.Any())
            {
                UserSelectList = new SelectList(Enumerable.Empty<SelectListItem>());
                return;
            }
            
            var userIdsInMemberRole = usersInMemberRole.Select(u => u.Id).ToList();
            var userProfiles = await _context.UserProfile
                                        .Where(up => userIdsInMemberRole.Contains(up.UserId))
                                        .ToDictionaryAsync(up => up.UserId);
            
            var userListItems = new List<SelectListItem>();
            foreach (var user in usersInMemberRole.OrderBy(u => u.UserName))
            {
                if (userProfiles.TryGetValue(user.Id, out UserProfile? profile) && profile != null && !string.IsNullOrEmpty(profile.LastName))
                {
                    userListItems.Add(new SelectListItem { Value = user.Id, Text = $"{profile.FirstName} {profile.LastName} ({user.Email})" });
                }
                else
                {
                    userListItems.Add(new SelectListItem { Value = user.Id, Text = $"{user.UserName} ({user.Email})" });
                }
            }
            
            UserSelectList = new SelectList(userListItems.OrderBy(item => item.Text), "Value", "Text");
        }
    }
}