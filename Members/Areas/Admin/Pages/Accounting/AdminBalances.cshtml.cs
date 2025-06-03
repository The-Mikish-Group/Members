using Members.Data;
using Members.Models; // Assuming UserProfile, Invoice, Payment are here
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
namespace Members.Areas.Admin.Pages.Accounting
{
    [Authorize(Roles = "Admin,Manager")] // Or your specific admin/manager roles
    public class AdminBalancesModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<AdminBalancesModel> _logger;
        public AdminBalancesModel(
            ApplicationDbContext context,
            UserManager<IdentityUser> userManager,
            ILogger<AdminBalancesModel> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }
        public List<MemberBalanceViewModel> MemberBalances { get; set; } = new List<MemberBalanceViewModel>();
        [BindProperty(SupportsGet = true)]
        public string? CurrentSort { get; set; }
        [BindProperty(SupportsGet = true)]
        public string? NameSort { get; set; }
        [BindProperty(SupportsGet = true)]
        public string? EmailSort { get; set; }
        [BindProperty(SupportsGet = true)]
        public string? BalanceSort { get; set; }
        [BindProperty(SupportsGet = true)]
        public bool ShowOnlyOutstanding { get; set; } = true;
        public async Task<IActionResult> OnPostApplyLateFeeAsync(string userId)
        {
            _logger.LogInformation($"OnPostApplyLateFeeAsync called for UserID: {userId}");
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "User ID was not provided.";
                return RedirectToPage(); // Or an error page
            }
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["ErrorMessage"] = $"User with ID {userId} not found.";
                return RedirectToPage();
            }
            // --- Determine the base amount for the 5% calculation ---
            // For simplicity, let's find the most recent, unpaid "Dues" invoice for this user.
            // In a more complex system, you might have specific rules for which invoice(s) trigger late fees.
            var latestUnpaidDuesInvoice = await _context.Invoices
                .Where(i => i.UserID == userId &&
                            i.Type == InvoiceType.Dues && // Assuming 'Dues' is the type for assessments
                            i.Status != InvoiceStatus.Paid &&
                            i.Status != InvoiceStatus.Cancelled &&
                            i.DueDate < DateTime.Today) // Ensure it's actually overdue
                .OrderByDescending(i => i.DueDate)
                .FirstOrDefaultAsync();
            decimal lateFeeAmount;
            string feeCalculationDescription;
            if (latestUnpaidDuesInvoice != null)
            {
                decimal fivePercentOfDues = latestUnpaidDuesInvoice.AmountDue * 0.05m;
                lateFeeAmount = Math.Max(25.00m, fivePercentOfDues);
                feeCalculationDescription = $"Late fee based on overdue dues of {latestUnpaidDuesInvoice.AmountDue:C} from {latestUnpaidDuesInvoice.DueDate:yyyy-MM-dd}. Fee: Max($25, 5% = {fivePercentOfDues:C}) = {lateFeeAmount:C}.";
                _logger.LogInformation($"Calculated late fee for {user.UserName}: {lateFeeAmount:C} based on invoice {latestUnpaidDuesInvoice.InvoiceID}. Description: {feeCalculationDescription}");
            }
            else
            {
                // Default to $25 if no specific overdue Dues invoice is found for the 5% calculation basis
                // Or, you might decide to not allow applying a fee if no clear basis exists.
                // For now, we'll apply the flat $25.
                lateFeeAmount = 25.00m;
                feeCalculationDescription = "Late fee (standard $25 applied as no specific overdue Dues invoice found as primary basis).";
                _logger.LogInformation($"Applying default late fee of $25 for {user.UserName} as no specific overdue Dues invoice found.");
            }
            // Check if a similar late fee was already applied recently to avoid duplicates
            var recentLateFeeExists = await _context.Invoices
                .AnyAsync(i => i.UserID == userId &&
                               i.Type == InvoiceType.LateFee &&
                               i.Description.Contains("Late fee based on overdue dues") && // Make description check more robust if needed
                               i.InvoiceDate >= DateTime.Today.AddDays(-7)); // Check for fees in last 7 days
            if (recentLateFeeExists)
            {
                TempData["WarningMessage"] = $"A late fee appears to have been applied recently for {user.UserName}. Please check history before applying another.";
                // We could redirect or just allow them to proceed if they confirm.
                // For now, we'll let the message show and they can decide if they still want to add one via the UI if we re-show the button.
                // Or, more simply, just don't add it if one was recent. Let's prevent duplicate for now.
                _logger.LogWarning($"Recent late fee already exists for {user.UserName}. No new fee applied by this action.");
                TempData["StatusMessage"] = $"Late fee application skipped for {user.UserName} as a recent one already exists.";
                return RedirectToPage(new { sortOrder = CurrentSort, showOnlyOutstanding = ShowOnlyOutstanding });
            }
            var lateFeeInvoice = new Invoice
            {
                UserID = userId,
                InvoiceDate = DateTime.Today,
                DueDate = DateTime.Today.AddDays(15), // Late fees are typically due relatively soon
                Description = feeCalculationDescription,
                AmountDue = lateFeeAmount,
                AmountPaid = 0,
                Status = InvoiceStatus.Due,
                Type = InvoiceType.LateFee,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };
            _context.Invoices.Add(lateFeeInvoice);
            await _context.SaveChangesAsync();
            TempData["StatusMessage"] = $"Late fee of {lateFeeAmount:C} applied successfully to user {user.UserName}.";
            _logger.LogInformation($"Late fee invoice {lateFeeInvoice.InvoiceID} created for {user.UserName}.");
            // Redirect back to the same page, preserving sort and filter
            return RedirectToPage(new { sortOrder = CurrentSort, showOnlyOutstanding = ShowOnlyOutstanding });
        }
        public class MemberBalanceViewModel
        {
            public string UserId { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            [DataType(DataType.Currency)]
            public decimal CurrentBalance { get; set; }
            public bool HasOutstandingBalance => CurrentBalance > 0;
        }
        public async Task OnGetAsync(string sortOrder, bool? showOnlyOutstanding)
        {
            _logger.LogInformation("OnGetAsync called for AdminBalancesModel.");
            CurrentSort = sortOrder;
            NameSort = string.IsNullOrEmpty(sortOrder) || sortOrder == "name_desc" ? "name_asc" : "name_desc";
            EmailSort = sortOrder == "email_asc" ? "email_desc" : "email_asc";
            BalanceSort = sortOrder == "balance_asc" ? "balance_desc" : "balance_asc";
            if (showOnlyOutstanding.HasValue)
            {
                ShowOnlyOutstanding = showOnlyOutstanding.Value;
            }
            var memberRoleName = "Member";
            var usersInMemberRole = await _userManager.GetUsersInRoleAsync(memberRoleName);
            if (usersInMemberRole == null || !usersInMemberRole.Any())
            {
                _logger.LogWarning("No users found in 'Member' role.");
                return;
            }
            var memberBalancesTemp = new List<MemberBalanceViewModel>();
            foreach (var user in usersInMemberRole)
            {
                var userProfile = await _context.UserProfile.FirstOrDefaultAsync(up => up.UserId == user.Id);
                string fullName = user.UserName ?? "N/A"; // Fallback to UserName
                if (userProfile != null && !string.IsNullOrWhiteSpace(userProfile.FirstName) && !string.IsNullOrWhiteSpace(userProfile.LastName))
                {
                    fullName = $"{userProfile.LastName}, {userProfile.FirstName}";
                }
                else if (userProfile != null && !string.IsNullOrWhiteSpace(userProfile.FirstName))
                {
                    fullName = userProfile.FirstName;
                }
                else if (userProfile != null && !string.IsNullOrWhiteSpace(userProfile.LastName))
                {
                    fullName = userProfile.LastName;
                }
                _logger.LogInformation($"Calculating balance for user: {user.UserName} (ID: {user.Id})");

                decimal totalChargesFromInvoices = await _context.Invoices
                    .Where(i => i.UserID == user.Id && i.Status != InvoiceStatus.Cancelled) // Include Paid invoices in charges
                    .SumAsync(i => i.AmountDue);
                _logger.LogInformation($"User {user.UserName} - Total Charges from Invoices: {totalChargesFromInvoices}");

                decimal totalPaymentsReceived = await _context.Payments
                    .Where(p => p.UserID == user.Id)
                    .SumAsync(p => p.Amount);
                _logger.LogInformation($"User {user.UserName} - Total Payments Received: {totalPaymentsReceived}");

                decimal currentBalance = totalChargesFromInvoices - totalPaymentsReceived;
                _logger.LogInformation($"User {user.UserName} - Calculated Current Balance: {currentBalance}");

                var memberVm = new MemberBalanceViewModel
                {
                    UserId = user.Id,
                    FullName = fullName, // fullName is calculated earlier in your loop
                    Email = user.Email ?? "N/A",
                    CurrentBalance = currentBalance // Use the new, more accurate balance
                };
                if (ShowOnlyOutstanding && memberVm.CurrentBalance <= 0)
                {
                    continue; // Skip if filtering and balance is not outstanding
                }
                memberBalancesTemp.Add(memberVm);
            }
            // Sorting logic
            MemberBalances = sortOrder switch
            {
                "name_desc" => memberBalancesTemp.OrderByDescending(s => s.FullName).ToList(),
                "name_asc" => memberBalancesTemp.OrderBy(s => s.FullName).ToList(),
                "email_desc" => memberBalancesTemp.OrderByDescending(s => s.Email).ToList(),
                "email_asc" => memberBalancesTemp.OrderBy(s => s.Email).ToList(),
                "balance_desc" => memberBalancesTemp.OrderByDescending(s => s.CurrentBalance).ToList(),
                "balance_asc" => memberBalancesTemp.OrderBy(s => s.CurrentBalance).ToList(),
                _ => memberBalancesTemp.OrderBy(s => s.FullName).ToList(),// Default sort
            };
            _logger.LogInformation($"Populated MemberBalances. Count: {MemberBalances.Count}");
        }
    }
}
