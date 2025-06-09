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
        public List<UserCredit> AvailableCredits { get; set; } = [];
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
        // Add the missing property definition for 'BackToEditUserUrl'
        [BindProperty(SupportsGet = true)]
        public string? BackToEditUserUrl { get; set; }
        public bool TargetUserIsBillingContact { get; set; }
        // Properties for Sort State
        [BindProperty(SupportsGet = true)] // Bind sortOrder from query string
        public string? CurrentSort { get; set; }
        public string? DateSort { get; private set; }
        public string? DescriptionSort { get; private set; }
        public string? TypeSort { get; private set; }
        public string? ChargeSort { get; private set; }
        public string? PaymentSort { get; private set; }
        public string? InvoiceIdSort { get; private set; }
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
        public async Task<IActionResult> OnGetAsync(string? userId, string? returnUrl, string? sortOrder) // Added sortOrder
        {
            _logger.LogInformation($"OnGetAsync called for MyBillingModel. UserID: {userId}, ReturnUrl: {returnUrl}, SortOrder: {sortOrder}");
            this.BackToEditUserUrl = returnUrl;
            sortOrder ??= InvoiceIdSort;
            this.CurrentSort = sortOrder;
            // Setup next sort states for column headers
            DateSort = (sortOrder == "date_asc") ? "date_desc" : "date_asc";
            InvoiceIdSort = (sortOrder == "invoiceid_desc") ? "invoiceid_asc" : "invoiceid_desc";
            DescriptionSort = (sortOrder == "desc_asc") ? "desc_desc" : "desc_asc";
            TypeSort = (sortOrder == "type_asc") ? "type_desc" : "type_asc";
            ChargeSort = (sortOrder == "charge_asc") ? "charge_desc" : "charge_asc";
            PaymentSort = (sortOrder == "payment_asc") ? "payment_desc" : "payment_asc";
            IdentityUser? targetUser;
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) { return Challenge(); }
            if (!string.IsNullOrEmpty(userId) && (User.IsInRole("Admin") || User.IsInRole("Manager")))
            {
                targetUser = await _userManager.FindByIdAsync(userId);
                if (targetUser == null)
                {
                    _logger.LogWarning($"Admin/Manager tried to view billing for non-existent UserID: {userId}");
                    TempData["ErrorMessage"] = "User not found.";
                    return RedirectToPage("/Index", new { area = "" });
                }
                IsViewingSelf = false;
                ViewedUserId = targetUser.Id;
                var targetUserProfile = await _context.UserProfile.FirstOrDefaultAsync(up => up.UserId == targetUser.Id);
                DisplayName = (targetUserProfile != null && !string.IsNullOrWhiteSpace(targetUserProfile.FirstName) && !string.IsNullOrWhiteSpace(targetUserProfile.LastName))
                              ? $"{targetUserProfile.LastName}, {targetUserProfile.FirstName} ({targetUser.Email})"
                              : targetUser.UserName ?? targetUser.Email ?? "Selected User's";
                TargetUserIsBillingContact = targetUserProfile?.IsBillingContact ?? false;
            }
            else
            {
                targetUser = currentUser;
                IsViewingSelf = true;
                ViewedUserId = targetUser.Id;
                var currentUserProfile = await _context.UserProfile.FirstOrDefaultAsync(up => up.UserId == targetUser.Id);
                DisplayName = (currentUserProfile != null && !string.IsNullOrWhiteSpace(currentUserProfile.FirstName) && !string.IsNullOrWhiteSpace(currentUserProfile.LastName))
                              ? $"{currentUserProfile.FirstName} {currentUserProfile.LastName}"
                              : targetUser.UserName ?? targetUser.Email ?? "My";
                if (DisplayName == targetUser.UserName || DisplayName == targetUser.Email) DisplayName += "'s"; else DisplayName += "'s";
                TargetUserIsBillingContact = currentUserProfile?.IsBillingContact ?? false;
            }
            _logger.LogInformation($"Fetching billing data for user: {targetUser.UserName} (ID: {targetUser.Id}). IsBillingContact: {TargetUserIsBillingContact}");
            Invoices = await _context.Invoices
                                .Where(i => i.UserID == targetUser.Id)
                                //.OrderByDescending(i => i.InvoiceDate) // Initial sort for combining, will be re-sorted later
                                //.ThenByDescending(i => i.DateCreated)
                                .ToListAsync();
            _logger.LogInformation($"Found {Invoices.Count} invoices for user {targetUser.Id}.");
            Payments = await _context.Payments
                                .Where(p => p.UserID == targetUser.Id)
                                // .OrderByDescending(p => p.PaymentDate) // Initial sort for combining, will be re-sorted later
                                // .ThenByDescending(p => p.DateRecorded)
                                .ToListAsync();
            _logger.LogInformation($"Found {Payments.Count} payments for user {targetUser.Id}.");
            decimal totalChargesFromInvoices = Invoices
                .Where(i => i.Status != InvoiceStatus.Cancelled)
                .Sum(i => i.AmountDue);
            decimal totalPaymentsReceived = Payments.Sum(p => p.Amount);
            CurrentBalance = totalChargesFromInvoices - totalPaymentsReceived;
            _logger.LogInformation($"MyBilling: User {targetUser.Id} - Calculated Current Balance: {CurrentBalance}");
            Transactions.Clear();
            foreach (var invoice in Invoices)
            {
                Transactions.Add(new BillingTransaction
                {
                    Date = invoice.InvoiceDate,
                    InvoiceID = invoice.InvoiceID,
                    Description = invoice.Description,
                    ChargeAmount = invoice.AmountDue,
                    PaymentAmount = null, // Explicitly null
                    Type = "Invoice",
                    StatusOrMethod = invoice.Status.ToString()                    
                });
            }
            foreach (var payment in Payments)
            {
                Transactions.Add(new BillingTransaction
                {
                    Date = payment.PaymentDate,
                    InvoiceID = payment.InvoiceID,
                    Description = payment.Notes ?? $"Payment (Ref: {payment.ReferenceNumber ?? "N/A"})",
                    ChargeAmount = null, // Explicitly null
                    PaymentAmount = payment.Amount,
                    Type = "Payment",
                    StatusOrMethod = payment.Method.ToString()                    
                });
            }
            // --- NEW SORTING LOGIC FOR Transactions ---
            switch (sortOrder)
            {
                case "date_desc": Transactions = Transactions.OrderByDescending(t => t.Date).ThenBy(t => t.Type != "Invoice").ToList(); break;
                case "date_asc": Transactions = Transactions.OrderBy(t => t.Date).ThenBy(t => t.Type != "Invoice").ToList(); break;
                case "desc_desc": Transactions = Transactions.OrderByDescending(t => t.Description).ToList(); break;
                case "desc_asc": Transactions = Transactions.OrderBy(t => t.Description).ToList(); break;
                case "type_desc": Transactions = Transactions.OrderByDescending(t => t.Type).ThenByDescending(t => t.Date).ToList(); break;
                case "type_asc": Transactions = Transactions.OrderBy(t => t.Type).ThenByDescending(t => t.Date).ToList(); break;
                case "charge_desc": Transactions = Transactions.OrderByDescending(t => t.ChargeAmount ?? decimal.MinValue).ThenByDescending(t => t.Date).ToList(); break;
                case "charge_asc": Transactions = Transactions.OrderBy(t => t.ChargeAmount ?? decimal.MaxValue).ThenByDescending(t => t.Date).ToList(); break;
                case "payment_desc": Transactions = Transactions.OrderByDescending(t => t.PaymentAmount ?? decimal.MinValue).ThenByDescending(t => t.Date).ToList(); break;
                case "payment_asc": Transactions = Transactions.OrderBy(t => t.PaymentAmount ?? decimal.MaxValue).ThenByDescending(t => t.Date).ToList(); break;
                case "invoiceid_desc": Transactions = Transactions.OrderByDescending(t => t.InvoiceID ?? int.MinValue).ThenByDescending(t => t.Date).ToList(); break;
                case "invoiceid_asc": Transactions = Transactions.OrderBy(t => t.InvoiceID ?? int.MaxValue).ThenByDescending(t => t.Date).ToList(); break;
                default: Transactions = Transactions.OrderByDescending(t => t.InvoiceID).ToList();
                if (string.IsNullOrEmpty(sortOrder)) InvoiceIdSort = "invoiceid_desc"; break;
            }
            // --- END NEW SORTING LOGIC ---
            AvailableCredits = await _context.UserCredits
                .Where(uc => uc.UserID == targetUser.Id && !uc.IsApplied)
                .OrderByDescending(uc => uc.CreditDate)
                .ToListAsync();
            TotalAvailableCredit = AvailableCredits.Sum(uc => uc.Amount);
            _logger.LogInformation($"Total available credit for user {targetUser.Id}: {TotalAvailableCredit:C}");
            return Page();
        }
        public async Task<IActionResult> OnPostApplyLateFeeAsync(string userId)
        {
            _logger.LogInformation($"OnPostApplyLateFeeAsync called for UserID: {userId}. Current viewing user (admin/manager): {User.Identity?.Name}");
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "User ID was not provided to apply late fee.";
                return RedirectToPage("/Index", new { area = "Admin" });
            }
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                _logger.LogWarning($"User {User.Identity?.Name} attempted to apply late fee without authorization.");
                TempData["ErrorMessage"] = "You are not authorized to perform this action.";
                return RedirectToPage();
            }
            var targetUser = await _userManager.FindByIdAsync(userId);
            if (targetUser == null)
            {
                TempData["ErrorMessage"] = $"Target user with ID {userId} not found.";
                return RedirectToPage();
            }
            var targetUserProfile = await _context.UserProfile.FirstOrDefaultAsync(up => up.UserId == targetUser.Id);
            if (targetUserProfile == null || !targetUserProfile.IsBillingContact)
            {
                TempData["WarningMessage"] = $"Late fee cannot be applied: User {targetUser.UserName} is not designated as a Billing Contact.";
                return RedirectToPage(new { userId = userId, returnUrl = BackToEditUserUrl });
            }
            var latestOverdueDuesInvoice = await _context.Invoices
                .Where(i => i.UserID == userId &&
                            i.Type == InvoiceType.Dues &&
                            i.Status != InvoiceStatus.Paid &&
                            i.Status != InvoiceStatus.Cancelled &&
                            i.DueDate < DateTime.Today)
                .OrderByDescending(i => i.DueDate)
                .FirstOrDefaultAsync();
            if (latestOverdueDuesInvoice == null)
            {
                TempData["WarningMessage"] = $"No overdue Dues/Assessment invoice found for {targetUser.UserName} to apply a late fee to.";
                return RedirectToPage(new { userId = userId, returnUrl = BackToEditUserUrl });
            }
            string expectedLateFeeDescriptionPart = $"INV-{latestOverdueDuesInvoice.InvoiceID:D5}";
            var existingLateFeeForThisInvoice = await _context.Invoices
                .AnyAsync(i => i.UserID == userId &&
                               i.Type == InvoiceType.LateFee &&
                               i.Description.Contains(expectedLateFeeDescriptionPart));
            if (existingLateFeeForThisInvoice)
            {
                TempData["WarningMessage"] = $"A late fee for the overdue assessment (INV-{latestOverdueDuesInvoice.InvoiceID:D5}) appears to have already been applied for {targetUser.UserName}.";
                return RedirectToPage(new { userId = userId, returnUrl = BackToEditUserUrl });
            }
            decimal fivePercentOfDues = latestOverdueDuesInvoice.AmountDue * 0.05m;
            decimal lateFeeAmount = Math.Max(25.00m, fivePercentOfDues);
            string feeReason = $"Late Fee for overdue assessment (INV-{latestOverdueDuesInvoice.InvoiceID:D5} due {latestOverdueDuesInvoice.DueDate:yyyy-MM-dd}). Basis: Max($25, 5% of {latestOverdueDuesInvoice.AmountDue:C})";
            var lateFeeInvoice = new Invoice
            {
                UserID = userId,
                InvoiceDate = DateTime.Today,
                DueDate = DateTime.Today.AddDays(15),
                Description = feeReason,
                AmountDue = lateFeeAmount,
                AmountPaid = 0,
                Status = InvoiceStatus.Due,
                Type = InvoiceType.LateFee,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };
            _context.Invoices.Add(lateFeeInvoice);
            decimal remainingAmountDueOnLateFee = lateFeeInvoice.AmountDue;
            string appliedCreditsSummary = "";
            bool creditsWereUpdatedForLateFee = false;
            List<UserCredit> availableCredits = await _context.UserCredits
                .Where(uc => uc.UserID == userId && !uc.IsApplied)
                .OrderBy(uc => uc.CreditDate)
                .ToListAsync();
            if (availableCredits.Any())
            {
                _logger.LogInformation($"User {targetUser.UserName} has {availableCredits.Count} available credits. Attempting to apply to new late fee invoice.");
                foreach (var credit in availableCredits)
                {
                    if (remainingAmountDueOnLateFee <= 0) break;
                    decimal amountToApplyFromThisCredit;
                    if (credit.Amount >= remainingAmountDueOnLateFee)
                    {
                        amountToApplyFromThisCredit = remainingAmountDueOnLateFee;
                        credit.IsApplied = true;
                        credit.ApplicationNotes = $"Fully used to auto-pay new late fee invoice INV-{lateFeeInvoice.InvoiceID:D5} (original credit: {credit.Amount:C}).";
                    }
                    else
                    {
                        amountToApplyFromThisCredit = credit.Amount;
                        credit.IsApplied = true;
                        credit.ApplicationNotes = $"Fully applied to new late fee invoice INV-{lateFeeInvoice.InvoiceID:D5}.";
                    }
                    credit.AppliedDate = DateTime.UtcNow;
                    credit.AppliedToInvoiceID = lateFeeInvoice.InvoiceID;
                    _context.UserCredits.Update(credit);
                    creditsWereUpdatedForLateFee = true;
                    lateFeeInvoice.AmountPaid += amountToApplyFromThisCredit;
                    remainingAmountDueOnLateFee -= amountToApplyFromThisCredit;
                    if (string.IsNullOrEmpty(appliedCreditsSummary)) appliedCreditsSummary = "\nCredits applied to late fee: ";
                    appliedCreditsSummary += $"{amountToApplyFromThisCredit:C} (from Credit #{credit.UserCreditID}); ";
                }
            }
            if (lateFeeInvoice.AmountPaid >= lateFeeInvoice.AmountDue)
            {
                lateFeeInvoice.Status = InvoiceStatus.Paid;
                lateFeeInvoice.AmountPaid = lateFeeInvoice.AmountDue;
            }
            try
            {
                await _context.SaveChangesAsync();
                if (creditsWereUpdatedForLateFee && lateFeeInvoice.InvoiceID > 0)
                {
                    bool needSecondSave = false;
                        foreach (var cred in availableCredits.Where(c => c.ApplicationNotes != null && c.ApplicationNotes.Contains($"INV-0")))
                        {
                            if (cred.ApplicationNotes!.Contains("auto-pay new late fee invoice INV-0")) // Use null-forgiving operator (!) to suppress CS8602
                            {
                                cred.AppliedToInvoiceID = lateFeeInvoice.InvoiceID;
                                cred.ApplicationNotes = cred.ApplicationNotes.Replace("INV-0", $"INV-{lateFeeInvoice.InvoiceID:D5}");
                                _context.UserCredits.Update(cred);
                                needSecondSave = true;
                            }
                        }                        
                    if (needSecondSave) await _context.SaveChangesAsync();
                }
                string successMessage = $"Late fee of {lateFeeInvoice.AmountDue:C} applied to {targetUser.UserName}. Invoice INV-{lateFeeInvoice.InvoiceID:D5} created, Status: {lateFeeInvoice.Status}.";
                if (!string.IsNullOrEmpty(appliedCreditsSummary)) successMessage += appliedCreditsSummary;
                TempData["StatusMessage"] = successMessage;
                _logger.LogInformation($"Late fee invoice INV-{lateFeeInvoice.InvoiceID:D5} created for {targetUser.UserName}.");
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, $"Error applying late fee for UserID {userId}.");
                TempData["ErrorMessage"] = "Error applying late fee. Check logs.";
            }
            return RedirectToPage(new { userId = userId, returnUrl = BackToEditUserUrl });
        }
    }
}