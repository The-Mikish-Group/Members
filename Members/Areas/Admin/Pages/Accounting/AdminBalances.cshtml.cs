using Members.Data;
using Members.Models; // Assuming UserProfile, Invoice, Payment are here
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
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
                decimal totalDues = await _context.Invoices
                    .Where(i => i.UserID == user.Id && i.Status != InvoiceStatus.Paid && i.Status != InvoiceStatus.Cancelled)
                    .SumAsync(i => i.AmountDue - i.AmountPaid); // Sum of remaining amounts on unpaid/partially paid invoices
                // For a simpler balance, you could also do:
                // decimal totalCharges = await _context.Invoices.Where(i => i.UserID == user.Id).SumAsync(i => i.AmountDue);
                // decimal totalPaymentsMade = await _context.Payments.Where(p => p.UserID == user.Id).SumAsync(p => p.Amount);
                // decimal balance = totalCharges - totalPaymentsMade;
                // For this iteration, totalDues (outstanding on active invoices) is a good representation of current balance due.
                var memberVm = new MemberBalanceViewModel
                {
                    UserId = user.Id,
                    FullName = fullName,
                    Email = user.Email ?? "N/A",
                    CurrentBalance = totalDues
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
