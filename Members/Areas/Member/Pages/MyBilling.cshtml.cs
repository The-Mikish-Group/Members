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
namespace Members.Areas.Member.Pages
{
    [Authorize] // Can be just [Authorize] if any logged-in user can see their own,
                // or [Authorize(Roles="Member,Admin,Manager")] if admins can also view.
                // The logic inside OnGetAsync will differentiate.
    public class MyBillingModel(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager,
        ILogger<MyBillingModel> logger) : PageModel
    {
        private readonly ApplicationDbContext _context = context;
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly ILogger<MyBillingModel> _logger = logger;
        public List<UserCredit> AvailableCredits { get; set; } = new List<UserCredit>();
        [DataType(DataType.Currency)]
        public decimal TotalAvailableCredit { get; set; }
        public IList<Invoice> Invoices { get; set; } = [];
        public IList<Payment> Payments { get; set; } = [];
        [DataType(DataType.Currency)]
        public decimal CurrentBalance { get; set; }
        public List<BillingTransaction> Transactions { get; set; } = [];
        // New properties for display
        public string DisplayName { get; set; } = "My"; // Default for own view
        public bool IsViewingSelf { get; set; } = true;
        public string? ViewedUserId { get; set; } // To carry UserId if admin is viewing another
        public class BillingTransaction
        {
            public DateTime Date { get; set; }
            public int? InvoiceID { get; set; }
            public string Description { get; set; } = string.Empty;
            [DataType(DataType.Currency)]
            public decimal? ChargeAmount { get; set; }
            [DataType(DataType.Currency)]
            public decimal? PaymentAmount { get; set; }
            public string Type { get; set; } = string.Empty;
            public string StatusOrMethod { get; set; } = string.Empty;
        }
        public async Task<IActionResult> OnGetAsync(string? userId) // Added userId parameter
        {
            _logger.LogInformation("OnGetAsync called for MyBillingModel. Requested UserID: {UserId}", userId);
            IdentityUser? targetUser;
            var currentUser = await _userManager.GetUserAsync(User); // Current logged-in user
            if (currentUser == null)
            {
                return Challenge(); // Should not happen if [Authorize] is effective
            }
            if (!string.IsNullOrEmpty(userId) && (User.IsInRole("Admin") || User.IsInRole("Manager")))
            {
                // Admin/Manager is viewing a specific user's billing
                targetUser = await _userManager.FindByIdAsync(userId);
                if (targetUser == null)
                {
                    _logger.LogWarning("Admin/Manager tried to view billing for non-existent UserID: {UserId}", userId);
                    TempData["ErrorMessage"] = "User not found.";
                    return RedirectToPage("/Index", new { area = "" }); // Or some other appropriate redirect
                }
                IsViewingSelf = false;
                ViewedUserId = targetUser.Id; // Store for link generation if needed
                var targetUserProfile = await _context.UserProfile.FirstOrDefaultAsync(up => up.UserId == targetUser.Id);
                DisplayName = (targetUserProfile != null && !string.IsNullOrWhiteSpace(targetUserProfile.FirstName) && !string.IsNullOrWhiteSpace(targetUserProfile.LastName))
                              ? $"{targetUserProfile.FirstName} {targetUserProfile.LastName} ({targetUser.Email})"
                              : targetUser.UserName ?? targetUser.Email ?? "Selected User's";
            }
            else
            {
                // Member is viewing their own billing, or admin didn't specify a userId
                targetUser = currentUser;
                IsViewingSelf = true;
                ViewedUserId = targetUser.Id;
                var currentUserProfile = await _context.UserProfile.FirstOrDefaultAsync(up => up.UserId == targetUser.Id);
                DisplayName = (currentUserProfile != null && !string.IsNullOrWhiteSpace(currentUserProfile.FirstName) && !string.IsNullOrWhiteSpace(currentUserProfile.LastName))
                              ? $"{currentUserProfile.FirstName} {currentUserProfile.LastName}"
                              : targetUser.UserName ?? targetUser.Email ?? "My";
                if (DisplayName == targetUser.UserName || DisplayName == targetUser.Email) DisplayName += ""; else DisplayName += "";
            }
            _logger.LogInformation("Fetching billing data for user: {UserName} (ID: {UserId})", targetUser.UserName, targetUser.Id);
            Invoices = await _context.Invoices
                                .Where(i => i.UserID == targetUser.Id)
                                .OrderByDescending(i => i.InvoiceDate)
                                .ThenByDescending(i => i.DateCreated)
                                .ToListAsync();
            _logger.LogInformation("Found {InvoiceCount} invoices for user {UserId}.", Invoices.Count, targetUser.Id);
            Payments = await _context.Payments
                                .Where(p => p.UserID == targetUser.Id)
                                .OrderByDescending(p => p.PaymentDate)
                                .ThenByDescending(p => p.DateRecorded)
                                .ToListAsync();
            _logger.LogInformation("Found {PaymentCount} payments for user {UserId}.", Payments.Count, targetUser.Id);
            decimal totalChargesFromInvoices = Invoices
                .Where(i => i.Status != InvoiceStatus.Cancelled) // Exclude Cancelled invoices
                .Sum(i => i.AmountDue);
            _logger.LogInformation("MyBilling: User {UserId} - Total Charges from non-Cancelled Invoices: {TotalCharges}", targetUser.Id, totalChargesFromInvoices);
            decimal totalPaymentsReceived = Payments.Sum(p => p.Amount); // Sum all payments fetched for this user
            _logger.LogInformation("MyBilling: User {UserId} - Total Payments Received: {TotalPayments}", targetUser.Id, totalPaymentsReceived);
            CurrentBalance = totalChargesFromInvoices - totalPaymentsReceived;
            _logger.LogInformation("MyBilling: User {UserId} - Calculated Current Balance: {CurrentBalance}", targetUser.Id, CurrentBalance);
            Transactions.Clear(); // Clear before repopulating
            foreach (var invoice in Invoices)
            {
                Transactions.Add(new BillingTransaction
                {
                    Date = invoice.InvoiceDate,
                    Description = invoice.Description,
                    ChargeAmount = invoice.AmountDue,
                    PaymentAmount = null,
                    Type = "Invoice",
                    StatusOrMethod = invoice.Status.ToString(),
                    InvoiceID = invoice.InvoiceID // <-- POPULATE IT HERE
                });
            }
            foreach (var payment in Payments)
            {
                Transactions.Add(new BillingTransaction
                {
                    Date = payment.PaymentDate,
                    Description = payment.Notes ?? $"Payment (Ref: {payment.ReferenceNumber ?? "N/A"})",
                    ChargeAmount = null,
                    PaymentAmount = payment.Amount,
                    Type = "Payment",
                    StatusOrMethod = payment.Method.ToString(),
                    InvoiceID = payment.InvoiceID // <-- POPULATE IT HERE (it's already on your Payment model)
                });
            }
            Transactions = [.. Transactions.OrderByDescending(t => t.Date).ThenBy(t => t.Type != "Invoice")];
            AvailableCredits = await _context.UserCredits
                .Where(uc => uc.UserID == targetUser.Id && !uc.IsApplied)
                .OrderByDescending(uc => uc.CreditDate)
                .ToListAsync();
            _logger.LogInformation($"Found {AvailableCredits.Count} available (unapplied) credits for user {targetUser.Id}.");
            TotalAvailableCredit = AvailableCredits.Sum(uc => uc.Amount);
            _logger.LogInformation($"Total available credit for user {targetUser.Id}: {TotalAvailableCredit:C}");
            return Page();
        }
    }
}