using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
namespace Members.Areas.Member.Pages // Or adjust if your folder structure leads to a different namespace
{
    [Authorize(Roles = "Admin,Manager,Member")] // Only accessible to users in the "Member" role
    public class MyBillingModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<MyBillingModel> _logger;
        public MyBillingModel(
            ApplicationDbContext context,
            UserManager<IdentityUser> userManager,
            ILogger<MyBillingModel> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }
        public IList<Invoice> Invoices { get; set; } = new List<Invoice>();
        public IList<Payment> Payments { get; set; } = new List<Payment>();
        [DataType(DataType.Currency)]
        public decimal CurrentBalance { get; set; }
        // Combined list for display
        public List<BillingTransaction> Transactions { get; set; } = new List<BillingTransaction>();
        public class BillingTransaction
        {
            public DateTime Date { get; set; }
            public string Description { get; set; } = string.Empty;
            [DataType(DataType.Currency)]
            public decimal? ChargeAmount { get; set; } // Debit
            [DataType(DataType.Currency)]
            public decimal? PaymentAmount { get; set; } // Credit
            [DataType(DataType.Currency)]
            public decimal RunningBalance { get; set; } // Optional: if you want to show this
            public string Type { get; set; } = string.Empty; // "Invoice" or "Payment"
            public string StatusOrMethod { get; set; } = string.Empty; // Invoice Status or Payment Method
        }
        public async Task<IActionResult> OnGetAsync()
        {
            _logger.LogInformation("OnGetAsync called for MyBillingModel.");
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                _logger.LogWarning("User not found for MyBillingModel.");
                return NotFound("Unable to load user.");
            }
            Invoices = await _context.Invoices
                                .Where(i => i.UserID == user.Id)
                                .OrderByDescending(i => i.InvoiceDate)
                                .ThenByDescending(i => i.DateCreated)
                                .ToListAsync();
            _logger.LogInformation($"Found {Invoices.Count} invoices for user {user.Id}.");
            Payments = await _context.Payments
                                .Where(p => p.UserID == user.Id)
                                .OrderByDescending(p => p.PaymentDate)
                                .ThenByDescending(p => p.DateRecorded)
                                .ToListAsync();
            _logger.LogInformation($"Found {Payments.Count} payments for user {user.Id}.");
            decimal totalCharges = Invoices.Sum(i => i.AmountDue);
            decimal totalPayments = Payments.Sum(p => p.Amount);
            CurrentBalance = totalCharges - totalPayments;
            _logger.LogInformation($"Calculated balance for user {user.Id}: {CurrentBalance}");
            // Combine and sort transactions for display
            foreach (var invoice in Invoices)
            {
                Transactions.Add(new BillingTransaction
                {
                    Date = invoice.InvoiceDate,
                    Description = invoice.Description,
                    ChargeAmount = invoice.AmountDue,
                    PaymentAmount = null,
                    Type = "Invoice",
                    StatusOrMethod = invoice.Status.ToString()
                });
            }
            foreach (var payment in Payments)
            {
                Transactions.Add(new BillingTransaction
                {
                    Date = payment.PaymentDate,
                    Description = payment.Notes ?? $"Payment Ref: {payment.ReferenceNumber ?? "N/A"}",
                    ChargeAmount = null,
                    PaymentAmount = payment.Amount,
                    Type = "Payment",
                    StatusOrMethod = payment.Method.ToString()
                });
            }
            Transactions = Transactions.OrderByDescending(t => t.Date).ThenBy(t => t.Type != "Invoice").ToList();
            // Optional: Calculate running balance if you want to display it in the table
            decimal runningBal = 0;
            var reversedTransactions = Transactions.OrderBy(t => t.Date).ThenBy(t => t.Type == "Invoice").ToList(); // oldest first
            foreach (var trans in reversedTransactions)
            {
                if (trans.ChargeAmount.HasValue) runningBal += trans.ChargeAmount.Value;
                if (trans.PaymentAmount.HasValue) runningBal -= trans.PaymentAmount.Value;
            }
            // Now iterate again to set running balance in the display order (most recent first)
            // This is a bit complex for a simple list; often running balance is calculated from oldest to newest.
            // For display, you might just show current balance at top and list transactions.
            // The below is a simplified way to show running balance as of that transaction if list is oldest to newest.
            // For newest to oldest, it's more like a statement.
            // Let's recalculate running balance for display in descending date order (statement style)
            decimal currentStatementBalance = CurrentBalance; // Start with the overall current balance
            var statementTransactions = new List<BillingTransaction>();
            // To show a running balance on a statement that lists recent items first,
            // you'd typically start with an opening balance, list transactions, and end with a closing balance.
            // The `Transactions` list is already sorted newest first.
            // For simplicity, we'll assign the running balance after each transaction based on this order.
            // This isn't a true "running balance from the beginning of time" for each row, but rather a "balance after this transaction in the context of the statement".
            // A more accurate running balance in the table would require calculating from the start of time for each row, or sorting by date ascending.
            // For now, we'll keep Transactions sorted by Date Descending and omit per-transaction running balance to keep it simple.
            // The CurrentBalance property will show the overall balance.
            return Page();
        }
    }
}
