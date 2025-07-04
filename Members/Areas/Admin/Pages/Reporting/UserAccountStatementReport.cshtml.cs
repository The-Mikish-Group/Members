using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Members.Data;
using Members.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc.Rendering; // For SelectList

namespace Members.Areas.Admin.Pages.Reporting
{
    [Authorize(Roles = "Admin,Manager")]
    public class UserAccountStatementReportModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<UserAccountStatementReportModel> _logger;

        public UserAccountStatementReportModel(ApplicationDbContext context,
                                               UserManager<IdentityUser> userManager,
                                               ILogger<UserAccountStatementReportModel> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        [BindProperty(SupportsGet = true)]
        [Display(Name = "Select Member")]
        public string? SelectedUserId { get; set; }
        public SelectList? UserSelectList { get; set; }

        [BindProperty(SupportsGet = true)]
        [DataType(DataType.Date)]
        [Display(Name = "Start Date")]
        public DateTime StartDate { get; set; } = DateTime.Today.AddMonths(-1).AddDays(1 - DateTime.Today.Day); // First day of last month

        [BindProperty(SupportsGet = true)]
        [DataType(DataType.Date)]
        [Display(Name = "End Date")]
        public DateTime EndDate { get; set; } = DateTime.Today;

        public AccountStatementViewModel? StatementData { get; set; }

        public async Task OnGetAsync()
        {
            await PopulateUserSelectListAsync();
            if (!string.IsNullOrEmpty(SelectedUserId))
            {
                await GenerateStatementDataAsync();
            }
            else
            {
                StatementData = new AccountStatementViewModel {
                    Transactions = new List<StatementTransactionItemViewModel>(),
                    SelectedUserName = "Please select a member.", // Updated text
                    ReportStartDate = StartDate.Date,
                    ReportEndDate = EndDate.Date
                };
            }
        }

        private async Task GenerateStatementDataAsync()
        {
            if (string.IsNullOrEmpty(SelectedUserId))
            {
                StatementData = new AccountStatementViewModel { Transactions = new List<StatementTransactionItemViewModel>(), SelectedUserName = "Please select a member." }; // Ensure consistency
                return;
            }

            var selectedUserIdentity = await _userManager.FindByIdAsync(SelectedUserId);
            var userProfile = await _context.UserProfile.FirstOrDefaultAsync(up => up.UserId == SelectedUserId);
            string currentSelectedUserName = userProfile != null ? $"{userProfile.LastName}, {userProfile.FirstName} ({selectedUserIdentity?.Email})" : (selectedUserIdentity?.Email ?? SelectedUserId);

            _logger.LogInformation("Generating statement for User: {UserId}, Period: {StartDate} to {EndDate}", SelectedUserId, StartDate, EndDate);

            var transactions = new List<StatementTransactionItemViewModel>();
            DateTime reportEffectiveStartDate = StartDate.Date;
            DateTime reportEffectiveEndDate = EndDate.Date.AddDays(1).AddTicks(-1); // Inclusive end date for transactions within period

            // --- Calculate Opening Balance (as of StartDate.Date, so transactions BEFORE this date) ---
            decimal openingBalance = 0;
            DateTime openingBalanceCutoffDate = reportEffectiveStartDate;

            var invoicesBeforePeriod = await _context.Invoices
                .Where(i => i.UserID == SelectedUserId && i.InvoiceDate < openingBalanceCutoffDate && i.Status != InvoiceStatus.Cancelled)
                .ToListAsync();
            openingBalance += invoicesBeforePeriod.Sum(i => i.AmountDue);

            var paymentsBeforePeriod = await _context.Payments
                .Where(p => p.UserID == SelectedUserId && p.PaymentDate < openingBalanceCutoffDate)
                .ToListAsync();
            openingBalance -= paymentsBeforePeriod.Sum(p => p.Amount);

            var userCreditsCreatedBeforePeriod = await _context.UserCredits
                .Where(uc => uc.UserID == SelectedUserId && uc.CreditDate < openingBalanceCutoffDate && !uc.IsVoided)
                // .Include(uc => uc.CreditApplications) // Removed incorrect include
                .ToListAsync();
            foreach (var uc in userCreditsCreatedBeforePeriod)
            {
                var applicationsForUc = await _context.CreditApplications
                    .Where(ca => ca.UserCreditID == uc.UserCreditID && !ca.IsReversed)
                    .ToListAsync();
                decimal calculatedOriginalAmount = uc.Amount + applicationsForUc.Sum(ca => ca.AmountApplied);
                openingBalance -= calculatedOriginalAmount;
            }

            _logger.LogInformation("Calculated Opening Balance for {UserId}: {OpeningBalance}", SelectedUserId, openingBalance);

            // --- Fetch Transactions within Period ---
            var invoicesInPeriod = await _context.Invoices
                .Where(i => i.UserID == SelectedUserId && i.InvoiceDate >= reportEffectiveStartDate && i.InvoiceDate <= reportEffectiveEndDate && i.Status != InvoiceStatus.Cancelled)
                .ToListAsync();
            transactions.AddRange(invoicesInPeriod.Select(i => new StatementTransactionItemViewModel
            {
                TransactionDate = i.InvoiceDate,
                TransactionType = "Invoice",
                Reference = $"INV-{i.InvoiceID:D5}",
                Description = $"{i.Type} - {i.Description}",
                DebitAmount = i.AmountDue,
                SortOrder = 1
            }));

            var paymentsInPeriod = await _context.Payments
                .Where(p => p.UserID == SelectedUserId && p.PaymentDate >= reportEffectiveStartDate && p.PaymentDate <= reportEffectiveEndDate)
                .ToListAsync();
            transactions.AddRange(paymentsInPeriod.Select(p => new StatementTransactionItemViewModel
            {
                TransactionDate = p.PaymentDate,
                TransactionType = "Payment",
                Reference = $"P-{p.PaymentID:D5}",
                Description = $"Method: {p.Method}{(string.IsNullOrWhiteSpace(p.ReferenceNumber) ? "" : $" Ref: {p.ReferenceNumber}")}. {p.Notes}",
                CreditAmount = p.Amount,
                SortOrder = 2
            }));

            var creditsIssuedInPeriod = await _context.UserCredits
                .Where(uc => uc.UserID == SelectedUserId && uc.CreditDate >= reportEffectiveStartDate && uc.CreditDate <= reportEffectiveEndDate && !uc.IsVoided)
                // .Include(uc => uc.CreditApplications) // Removed incorrect include
                .ToListAsync();

            foreach (var uc in creditsIssuedInPeriod) // Changed from .Select to foreach to handle async call for applications
            {
                var applicationsForUc = await _context.CreditApplications
                    .Where(ca => ca.UserCreditID == uc.UserCreditID && !ca.IsReversed)
                    .ToListAsync();
                decimal calculatedOriginalAmount = uc.Amount + applicationsForUc.Sum(ca => ca.AmountApplied);
                transactions.Add(new StatementTransactionItemViewModel
                {
                    TransactionDate = uc.CreditDate,
                    TransactionType = "Credit Issued",
                    Reference = $"UC-{uc.UserCreditID:D5}",
                    Description = uc.Reason,
                    CreditAmount = calculatedOriginalAmount,
                    SortOrder = 3
                });
            }

            var creditApplicationsInPeriod = await _context.CreditApplications
                .Where(ca => ca.UserCredit != null && ca.UserCredit.UserID == SelectedUserId &&
                               ca.ApplicationDate >= reportEffectiveStartDate && ca.ApplicationDate <= reportEffectiveEndDate && !ca.IsReversed)
                .Include(ca => ca.Invoice)
                // .Include(ca => ca.UserCredit) // UserCredit object itself is not needed here, UserCreditID is on 'ca'
                .ToListAsync();
            transactions.AddRange(creditApplicationsInPeriod.Select(ca => new StatementTransactionItemViewModel
            {
                TransactionDate = ca.ApplicationDate,
                TransactionType = "Credit Applied",
                Reference = $"CA-{ca.CreditApplicationID:D5}",
                Description = $"Applied UC-{ca.UserCreditID} to {(ca.Invoice != null ? $"INV-{ca.Invoice.InvoiceID:D5}" : "N/A")}. {ca.Notes}",
                CreditAmount = ca.AmountApplied,
                SortOrder = 4
            }));

            var sortedTransactions = transactions.OrderBy(t => t.TransactionDate).ThenBy(t => t.SortOrder).ThenBy(t => t.Reference).ToList();

            decimal currentBalance = openingBalance;
            foreach (var tx in sortedTransactions)
            {
                if (tx.DebitAmount.HasValue) currentBalance += tx.DebitAmount.Value;
                if (tx.CreditAmount.HasValue) currentBalance -= tx.CreditAmount.Value;
                tx.RunningBalance = currentBalance;
            }

            StatementData = new AccountStatementViewModel
            {
                SelectedUserName = currentSelectedUserName,
                ReportStartDate = StartDate.Date,
                ReportEndDate = EndDate.Date,
                OpeningBalance = openingBalance,
                Transactions = sortedTransactions,
                ClosingBalance = currentBalance
            };
            _logger.LogInformation("Statement generated for {UserId}. OB: {OpeningBalance}, Transactions: {TxCount}, CB: {ClosingBalance}", SelectedUserId, openingBalance, sortedTransactions.Count, currentBalance);
        }

        public async Task<IActionResult> OnGetExportToCsvAsync()
        {
            // Ensure data is generated if a user is selected
            if (string.IsNullOrEmpty(SelectedUserId))
            {
                 // Optionally, return an empty CSV or a message, or redirect.
                 // For now, let's assume UI prevents export if no user selected.
                 // Or, we can just produce an empty CSV.
                return new EmptyResult(); // Or appropriate response
            }
            await PopulateUserSelectListAsync(); // Ensure user name is available for CSV if needed
            await GenerateStatementDataAsync();

            var builder = new System.Text.StringBuilder();
            if (StatementData == null) return new EmptyResult();

            builder.AppendLine($"\"Member Account Statement for: {EscapeCsvField(StatementData.SelectedUserName)}\""); // Updated text
            builder.AppendLine($"\"Report Period: {StatementData.ReportStartDate:yyyy-MM-dd} to {StatementData.ReportEndDate:yyyy-MM-dd}\"");
            builder.AppendLine();
            builder.AppendLine($"\"Opening Balance\",,,,,\"{StatementData.OpeningBalance:F2}\"");
            builder.AppendLine();
            builder.AppendLine("Date,Type,Reference,Description,Debit,Credit,Balance");

            foreach (var item in StatementData.Transactions)
            {
                builder.Append($"{item.TransactionDate:yyyy-MM-dd},");
                builder.Append($"\"{EscapeCsvField(item.TransactionType)}\",");
                builder.Append($"\"{EscapeCsvField(item.Reference)}\",");
                builder.Append($"\"{EscapeCsvField(item.Description)}\",");
                builder.Append($"{(item.DebitAmount.HasValue ? item.DebitAmount.Value.ToString("F2") : "")},");
                builder.Append($"{(item.CreditAmount.HasValue ? item.CreditAmount.Value.ToString("F2") : "")},");
                builder.AppendLine($"{item.RunningBalance:F2}");
            }
            builder.AppendLine();
            builder.AppendLine($"\"Closing Balance\",,,,,\"{StatementData.ClosingBalance:F2}\"");

            string safeUserName = StatementData.SelectedUserName?.Replace(", ", "_").Replace("(", "").Replace(")", "").Replace(" ", "") ?? "Member";
            string fileName = $"MemberAccountStatement_{safeUserName}_{StatementData.ReportStartDate:yyyyMMdd}_{StatementData.ReportEndDate:yyyyMMdd}.csv";
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(builder.ToString());
            return File(buffer, "text/csv", fileName);
        }

        private static string EscapeCsvField(string? field)
        {
            if (string.IsNullOrEmpty(field))
                return string.Empty;
            return field.Replace("\"", "\"\"");
        }

        private async Task PopulateUserSelectListAsync()
        {
            _logger.LogInformation("PopulateUserSelectListAsync started - Filtering for IsBillingContact=true.");

            var billingContactProfiles = await _context.UserProfile
                .Where(up => up.IsBillingContact)
                .OrderBy(up => up.LastName)
                .ThenBy(up => up.FirstName)
                .Select(up => new { up.UserId, up.FirstName, up.LastName }) // Select only needed fields
                .ToListAsync();

            _logger.LogInformation("Fetched {ProfileCount} billing contact UserProfiles.", billingContactProfiles.Count);

            var userList = new List<SelectListItem>();
            if (billingContactProfiles.Any())
            {
                var userIds = billingContactProfiles.Select(p => p.UserId).ToList();
                var identityUsers = await _userManager.Users
                                          .Where(u => userIds.Contains(u.Id))
                                          .ToDictionaryAsync(u => u.Id);
                _logger.LogInformation("Fetched {IdentityUserCount} IdentityUser records for the billing contact profiles.", identityUsers.Count);

                foreach (var profile in billingContactProfiles)
                {
                    _logger.LogInformation("Processing profile for UserId: {UserId}", profile.UserId);
                    if (identityUsers.TryGetValue(profile.UserId, out var user))
                    {
                        string displayText = $"{profile.LastName}, {profile.FirstName} ({user.Email})";
                        _logger.LogInformation("Generated displayText: '{DisplayText}' for UserId: {UserId}", displayText, profile.UserId);
                        userList.Add(new SelectListItem { Value = user.Id, Text = displayText });
                    }
                    else
                    {
                        _logger.LogWarning("No IdentityUser found for UserProfile UserId: {UserId} (FirstName: {FirstName}, LastName: {LastName}), though profile IsBillingContact=true. Skipping.", profile.UserId, profile.FirstName, profile.LastName);
                    }
                }
            }

            _logger.LogInformation("userList contains {UserListCount} items before creating SelectList.", userList.Count);
            UserSelectList = new SelectList(userList.OrderBy(u => u.Text), "Value", "Text", SelectedUserId); // OrderBy(u=>u.Text) is redundant due to earlier OrderBy on profiles, but harmless.
            _logger.LogInformation("UserSelectList created. Item count: {SelectListCount}", UserSelectList?.Count());
        }


        public class AccountStatementViewModel
        {
            public string SelectedUserName { get; set; } = string.Empty;
            public DateTime ReportStartDate { get; set; }
            public DateTime ReportEndDate { get; set; }
            [DataType(DataType.Currency)]
            public decimal OpeningBalance { get; set; }
            [DataType(DataType.Currency)]
            public decimal ClosingBalance { get; set; }
            public List<StatementTransactionItemViewModel> Transactions { get; set; } = new List<StatementTransactionItemViewModel>();
        }

        public class StatementTransactionItemViewModel
        {
            public DateTime TransactionDate { get; set; }
            public string TransactionType { get; set; } = string.Empty; // "Invoice", "Payment", "Credit Issued", "Credit Applied"
            public string Reference { get; set; } = string.Empty;       // e.g., Inv #, Payment ID, Credit ID, App ID
            public string Description { get; set; } = string.Empty;
            [DataType(DataType.Currency)]
            public decimal? DebitAmount { get; set; } // Nullable for display
            [DataType(DataType.Currency)]
            public decimal? CreditAmount { get; set; } // Nullable for display
            [DataType(DataType.Currency)]
            public decimal RunningBalance { get; set; }
            public int SortOrder { get; set; } // For secondary sorting on the same date (e.g. Invoice=1, Payment=2, CreditIssued=3, CreditApplied=4)
        }
    }
}
