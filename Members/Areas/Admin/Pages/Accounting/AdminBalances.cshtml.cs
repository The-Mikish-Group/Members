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
    public class AdminBalancesModel(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager,
        ILogger<AdminBalancesModel> logger) : PageModel
    {
        private readonly ApplicationDbContext _context = context;
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly ILogger<AdminBalancesModel> _logger = logger;
        public List<MemberBalanceViewModel> MemberBalances { get; set; } = [];
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
            const string userIdLogTemplate = "OnPostApplyLateFeeAsync called for UserID: {UserId}";
            const string userNotFoundLogTemplate = "User with ID {UserId} not found.";
            const string lateFeeCalculationLogTemplate = "Calculated late fee for {UserName}: {LateFeeAmount:C} based on invoice {InvoiceId}. Description: {FeeDescription}";
            const string defaultLateFeeLogTemplate = "Applying default late fee of $25 for {UserName} as no specific overdue Dues invoice found.";
            const string recentLateFeeLogTemplate = "Recent late fee already exists for {UserName}. No new fee applied by this action.";
            const string lateFeeInvoiceCreatedLogTemplate = "Late fee invoice {InvoiceId} created for {UserName}.";
            _logger.LogInformation(userIdLogTemplate, userId);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "User ID was not provided.";
                return RedirectToPage();
            }
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["ErrorMessage"] = $"User with ID {userId} not found.";
                _logger.LogWarning(userNotFoundLogTemplate, userId);
                return RedirectToPage();
            }
            var latestUnpaidDuesInvoice = await _context.Invoices
                .Where(i => i.UserID == userId &&
                            i.Type == InvoiceType.Dues &&
                            i.Status != InvoiceStatus.Paid &&
                            i.Status != InvoiceStatus.Cancelled &&
                            i.DueDate < DateTime.Today)
                .OrderByDescending(i => i.DueDate)
                .FirstOrDefaultAsync();
            decimal lateFeeAmount;
            string feeCalculationDescription;
            if (latestUnpaidDuesInvoice != null)
            {
                decimal fivePercentOfDues = latestUnpaidDuesInvoice.AmountDue * 0.05m;
                lateFeeAmount = Math.Max(25.00m, fivePercentOfDues);
                feeCalculationDescription = $"Late fee based on overdue dues of {latestUnpaidDuesInvoice.AmountDue:C} from {latestUnpaidDuesInvoice.DueDate:yyyy-MM-dd}. Fee: Max($25, 5% = {fivePercentOfDues:C}) = {lateFeeAmount:C}.";
                _logger.LogInformation(lateFeeCalculationLogTemplate, user.UserName, lateFeeAmount, latestUnpaidDuesInvoice.InvoiceID, feeCalculationDescription);
            }
            else
            {
                lateFeeAmount = 25.00m;
                feeCalculationDescription = "Standard $25 late fee applied.";
                _logger.LogInformation(defaultLateFeeLogTemplate, user.UserName);
            }
            var recentLateFeeExists = await _context.Invoices
                .AnyAsync(i => i.UserID == userId &&
                               i.Type == InvoiceType.LateFee &&
                               i.Description.Contains("Late fee based on overdue dues") &&
                               i.InvoiceDate >= DateTime.Today.AddDays(-7));
            if (recentLateFeeExists)
            {
                TempData["WarningMessage"] = $"A late fee appears to have been applied recently for {user.UserName}. Please check history before applying another.";
                _logger.LogWarning(recentLateFeeLogTemplate, user.UserName);
                TempData["StatusMessage"] = $"Late fee application skipped for {user.UserName} as a recent one already exists.";
                return RedirectToPage(new { sortOrder = CurrentSort, showOnlyOutstanding = ShowOnlyOutstanding });
            }
            var lateFeeInvoice = new Invoice
            {
                UserID = userId,
                InvoiceDate = DateTime.Today,
                DueDate = DateTime.Today.AddDays(15),
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
            _logger.LogInformation(lateFeeInvoiceCreatedLogTemplate, lateFeeInvoice.InvoiceID, user.UserName);
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
            BalanceSort = sortOrder == "balance_desc" ? "balance_asc" : "balance_desc";
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
                if (userProfile != null && userProfile.IsBillingContact)
                {
                    string fullName = user.UserName ?? "N/A"; // Fallback to UserName
                    if (!string.IsNullOrWhiteSpace(userProfile.FirstName) && !string.IsNullOrWhiteSpace(userProfile.LastName))
                    {
                        fullName = $"{userProfile.FirstName} {userProfile.LastName}";
                    }
                    else if (!string.IsNullOrWhiteSpace(userProfile.FirstName))
                    {
                        fullName = userProfile.FirstName;
                    }
                    else if (!string.IsNullOrWhiteSpace(userProfile.LastName))
                    {
                        fullName = userProfile.LastName;
                    }

                    _logger.LogInformation("Calculating balance for: {user.UserName} (ID: {user.Id})", user.UserName, user.Id);

                    decimal totalChargesFromInvoices = await _context.Invoices
                        .Where(i => i.UserID == user.Id && i.Status != InvoiceStatus.Cancelled) // Include Paid invoices in charges
                        .SumAsync(i => i.AmountDue);
                    _logger.LogInformation("User {user.UserName} - Total Charges from Invoices: {totalChargesFromInvoices}", user.UserName, totalChargesFromInvoices);

                    // Fix for the errors in the problematic line
                    decimal totalPaymentsReceived = await _context.Payments
                        .Where(p => p.UserID == user.Id && !p.IsVoided) // Corrected the lambda expression syntax
                        .SumAsync(p => p.Amount);
                    _logger.LogInformation("User {user.UserName} - Total Payments Received: {totalPaymentsReceived}", user.UserName, totalPaymentsReceived);

                    decimal currentBalance = totalChargesFromInvoices - totalPaymentsReceived;
                    _logger.LogInformation("User {user.UserName} - Calculated Current Balance: {currentBalance}", user.UserName, currentBalance);

                    var memberVm = new MemberBalanceViewModel
                    {
                        UserId = user.Id,
                        FullName = fullName,
                        Email = user.Email ?? "N/A",
                        CurrentBalance = currentBalance
                    };

                    if (ShowOnlyOutstanding && memberVm.CurrentBalance <= 0)
                    {
                        continue; // Skip if filtering and balance is not outstanding
                    }

                    memberBalancesTemp.Add(memberVm);
                }
            }            
            
            // Sorting logic
            MemberBalances = sortOrder switch
            {
                "name_desc" => [.. memberBalancesTemp.OrderByDescending(s => s.FullName)],
                "name_asc" => [.. memberBalancesTemp.OrderBy(s => s.FullName)],
                "email_desc" => [.. memberBalancesTemp.OrderByDescending(s => s.Email)],
                "email_asc" => [.. memberBalancesTemp.OrderBy(s => s.Email)],
                "balance_desc" => [.. memberBalancesTemp.OrderByDescending(s => s.CurrentBalance)],
                "balance_asc" => [.. memberBalancesTemp.OrderBy(s => s.CurrentBalance)],
                _ => [.. memberBalancesTemp.OrderBy(s => s.FullName)],// Default sort
            };
            _logger.LogInformation("Populated MemberBalances. Count: {MemberBalances.Count}", MemberBalances.Count);
        }
    }
}
