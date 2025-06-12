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
        public async Task<IActionResult> OnGetAsync(string? userId, string? returnUrl, string? sortOrder)
        {
            _logger.LogInformation($"MyBilling.OnGetAsync START - UserID: {userId}, ReturnUrl: {returnUrl}, SortOrder: {sortOrder}");
            this.BackToEditUserUrl = returnUrl;
            IdentityUser? determinedTargetUser = null;
            var loggedInUser = await _userManager.GetUserAsync(User);
            if (loggedInUser == null)
            {
                _logger.LogWarning("MyBilling.OnGetAsync: Current logged-in user is NULL. Challenging.");
                return Challenge(); // Should not happen if [Authorize] is on the class
            }
            if (!string.IsNullOrEmpty(userId) && (User.IsInRole("Admin") || User.IsInRole("Manager")))
            {
                _logger.LogInformation($"MyBilling.OnGetAsync: Admin/Manager viewing specific user. Attempting to find UserID: {userId}");
                determinedTargetUser = await _userManager.FindByIdAsync(userId);
                if (determinedTargetUser == null)
                {
                    _logger.LogWarning($"MyBilling.OnGetAsync: Admin/Manager provided UserID {userId}, but user was NOT FOUND. Defaulting to logged-in user for safety, but this is an error condition.");
                    // Forcing to load loggedInUser's data instead of erroring out, to see if page renders.
                    // TempData["ErrorMessage"] = $"User with ID '{userId}' not found. Showing your own data if applicable.";
                    determinedTargetUser = loggedInUser; // Fallback to current user
                    IsViewingSelf = true; // Act as if viewing self to avoid null refs on targetUser specific items
                    ViewedUserId = loggedInUser.Id;
                }
                else
                {
                    _logger.LogInformation($"MyBilling.OnGetAsync: Admin/Manager - TargetUser {determinedTargetUser.UserName} (ID: {determinedTargetUser.Id}) FOUND.");
                    IsViewingSelf = false;
                    ViewedUserId = determinedTargetUser.Id;
                }
            }
            else
            {
                _logger.LogInformation($"MyBilling.OnGetAsync: Member viewing self, or Admin/Manager did not provide userId. Using LoggedInUser: {loggedInUser.UserName}");
                determinedTargetUser = loggedInUser;
                IsViewingSelf = true;
                ViewedUserId = determinedTargetUser.Id;
            }
            // Populate DisplayName and TargetUserIsBillingContact based on determinedTargetUser
            var userProfile = await _context.UserProfile.FirstOrDefaultAsync(up => up.UserId == determinedTargetUser.Id);
            if (IsViewingSelf)
            {
                DisplayName = (userProfile != null && !string.IsNullOrWhiteSpace(userProfile.FirstName) && !string.IsNullOrWhiteSpace(userProfile.LastName))
                                ? $"{userProfile.FirstName} {userProfile.LastName}"
                                : determinedTargetUser.UserName ?? determinedTargetUser.Email ?? "My";
                if (DisplayName == determinedTargetUser.UserName || DisplayName == determinedTargetUser.Email) DisplayName += "'s"; else DisplayName += "'s";
            }
            else // Admin viewing another user
            {
                DisplayName = (userProfile != null && !string.IsNullOrWhiteSpace(userProfile.FirstName) && !string.IsNullOrWhiteSpace(userProfile.LastName))
                                ? $"{userProfile.FirstName} {userProfile.LastName} ({determinedTargetUser.Email})"
                                : determinedTargetUser.UserName ?? determinedTargetUser.Email ?? "Selected User's";
            }
            TargetUserIsBillingContact = userProfile?.IsBillingContact ?? false;
            _logger.LogInformation($"MyBilling.OnGetAsync: DisplayName: {DisplayName}. TargetUserIsBillingContact: {TargetUserIsBillingContact}");
            // Fetch data using determinedTargetUser.Id
            _logger.LogInformation($"MyBilling.OnGetAsync: Fetching billing data for user: {determinedTargetUser.UserName} (ID: {determinedTargetUser.Id}).");
            Invoices = await _context.Invoices.Where(i => i.UserID == determinedTargetUser.Id).ToListAsync();
            Payments = await _context.Payments.Where(p => p.UserID == determinedTargetUser.Id).ToListAsync();
            AvailableCredits = await _context.UserCredits.Where(uc => uc.UserID == determinedTargetUser.Id && !uc.IsApplied).ToListAsync();
            _logger.LogInformation($"MyBilling.OnGetAsync: Found {Invoices.Count} invoices, {Payments.Count} payments, {AvailableCredits.Count} available credits for {determinedTargetUser.UserName}");
            decimal totalCharges = Invoices.Where(i => i.Status != InvoiceStatus.Cancelled).Sum(i => i.AmountDue);
            decimal totalPayments = Payments.Sum(p => p.Amount);
            CurrentBalance = totalCharges - totalPayments;
            TotalAvailableCredit = AvailableCredits.Sum(uc => uc.Amount);
            _logger.LogInformation($"MyBilling.OnGetAsync: Balance for {determinedTargetUser.UserName}: {CurrentBalance}, TotalAvailableCredit: {TotalAvailableCredit}");
            // Populate Transactions
            Transactions.Clear();
            foreach (var invoice in Invoices.OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.DateCreated))
            {
                Transactions.Add(new BillingTransaction
                { /* ... your existing mapping ... */
                    Date = invoice.InvoiceDate,
                    InvoiceID = invoice.InvoiceID,
                    Description = invoice.Description,
                    ChargeAmount = invoice.AmountDue,
                    Type = "Invoice",
                    StatusOrMethod = invoice.Status.ToString()
                });
            }
            foreach (var payment in Payments.OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.DateRecorded))
            {
                Transactions.Add(new BillingTransaction
                { /* ... your existing mapping ... */
                    Date = payment.PaymentDate,
                    InvoiceID = payment.InvoiceID,
                    Description = payment.Notes ?? $"Payment (Ref: {payment.ReferenceNumber ?? "N/A"})",
                    PaymentAmount = payment.Amount,
                    Type = "Payment",
                    StatusOrMethod = payment.Method.ToString()
                });
            }
            _logger.LogInformation($"MyBilling.OnGetAsync: Populated {Transactions.Count} total transactions for {determinedTargetUser.UserName}.");
            // Apply Sorting
            string effectiveSort = sortOrder ?? "invoiceid_desc"; // Default to Invoice ID Descending
            this.CurrentSort = effectiveSort;
            DateSort = (effectiveSort == "date_asc") ? "date_desc" : "date_asc";
            InvoiceIdSort = (effectiveSort == "invoiceid_asc") ? "invoiceid_desc" : "invoiceid_asc";
            DescriptionSort = (effectiveSort == "desc_asc") ? "desc_desc" : "desc_asc";
            TypeSort = (effectiveSort == "type_asc") ? "type_desc" : "type_asc";
            ChargeSort = (effectiveSort == "charge_asc") ? "charge_desc" : "charge_asc";
            PaymentSort = (effectiveSort == "payment_asc") ? "payment_desc" : "payment_asc";
            // Ensure the default sort's "next click" state is correctly set
            if (effectiveSort == "invoiceid_desc") InvoiceIdSort = "invoiceid_asc";
            else if (effectiveSort == "date_desc" && sortOrder == null) DateSort = "date_asc"; // If date_desc was default
                                                                                               // Add any other default scenarios if you change the default from invoiceid_desc
            switch (effectiveSort)
            {
                case "date_desc": Transactions = Transactions.OrderByDescending(t => t.Date).ThenBy(t => t.Type != "Invoice").ToList(); break;
                case "date_asc": Transactions = Transactions.OrderBy(t => t.Date).ThenBy(t => t.Type != "Invoice").ToList(); break;
                case "invoiceid_desc": Transactions = Transactions.OrderByDescending(t => t.InvoiceID ?? int.MinValue).ThenByDescending(t => t.Date).ToList(); break;
                case "invoiceid_asc": Transactions = Transactions.OrderBy(t => t.InvoiceID ?? int.MaxValue).ThenByDescending(t => t.Date).ToList(); break;
                case "desc_desc": Transactions = Transactions.OrderByDescending(t => t.Description).ToList(); break;
                case "desc_asc": Transactions = Transactions.OrderBy(t => t.Description).ToList(); break;
                case "type_desc": Transactions = Transactions.OrderByDescending(t => t.Type).ThenByDescending(t => t.Date).ToList(); break;
                case "type_asc": Transactions = Transactions.OrderBy(t => t.Type).ThenByDescending(t => t.Date).ToList(); break;
                case "charge_desc": Transactions = Transactions.OrderByDescending(t => t.ChargeAmount ?? decimal.MinValue).ThenByDescending(t => t.Date).ToList(); break;
                case "charge_asc": Transactions = Transactions.OrderBy(t => t.ChargeAmount ?? decimal.MaxValue).ThenByDescending(t => t.Date).ToList(); break;
                case "payment_desc": Transactions = Transactions.OrderByDescending(t => t.PaymentAmount ?? decimal.MinValue).ThenByDescending(t => t.Date).ToList(); break;
                case "payment_asc": Transactions = Transactions.OrderBy(t => t.PaymentAmount ?? decimal.MaxValue).ThenByDescending(t => t.Date).ToList(); break;
                    // No default needed here as effectiveSort is always set
            }
            _logger.LogInformation($"MyBilling.OnGetAsync: Transactions sorted by {effectiveSort}. Final count: {Transactions.Count}");
            return Page();
        }
        public async Task<IActionResult> OnPostVoidInvoiceAsync(int invoiceId, string voidReason)
        {
            _logger.LogInformation($"OnPostVoidInvoiceAsync called for InvoiceID: {invoiceId} by User: {User.Identity?.Name}. Reason: {voidReason}");
            // Ensure the current user is authorized to perform this action
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                _logger.LogWarning($"User {User.Identity?.Name} attempted to void invoice {invoiceId} without authorization.");
                TempData["ErrorMessage"] = "You are not authorized to perform this action.";
                // Redirect to the current page for the ViewedUserId if available, else to a safe default
                return RedirectToPage(new { userId = ViewedUserId, returnUrl = BackToEditUserUrl });
            }
            if (string.IsNullOrWhiteSpace(voidReason))
            {
                // Although the JavaScript prompt should require a reason, add server-side validation too.
                TempData["WarningMessage"] = "A reason is required to void an invoice.";
                return RedirectToPage(new { userId = ViewedUserId, returnUrl = BackToEditUserUrl });
            }
            // ViewedUserId should be populated in OnGetAsync if an admin is viewing specific user's billing
            var invoiceToVoid = await _context.Invoices
                .FirstOrDefaultAsync(i => i.InvoiceID == invoiceId && i.UserID == ViewedUserId);
            if (invoiceToVoid == null)
            {
                TempData["ErrorMessage"] = $"Invoice with ID {invoiceId} not found for the specified user.";
                _logger.LogWarning($"Void Invoice: InvoiceID {invoiceId} not found for UserID {ViewedUserId}.");
                return RedirectToPage(new { userId = ViewedUserId, returnUrl = BackToEditUserUrl });
            }
            if (invoiceToVoid.Status == InvoiceStatus.Cancelled)
            {
                TempData["WarningMessage"] = $"Invoice INV-{invoiceToVoid.InvoiceID:D5} is already cancelled.";
                return RedirectToPage(new { userId = ViewedUserId, returnUrl = BackToEditUserUrl });
            }
            // Business Rule: For this iteration, we allow voiding an invoice even if it has payments.
            // The balance calculation (which excludes Cancelled invoices from sum of AmountDue)
            // will mean any payments previously applied to this invoice will effectively become
            // unapplied credit for the user. More complex scenarios might involve trying to void
            // linked payments or preventing voiding if payments exist.
            invoiceToVoid.Status = InvoiceStatus.Cancelled;
            invoiceToVoid.ReasonForCancellation = voidReason;
            invoiceToVoid.LastUpdated = DateTime.UtcNow;
            // AmountPaid on the invoice remains as is, to show what was paid before cancellation.
            _context.Invoices.Update(invoiceToVoid);
            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Invoice INV-{invoiceToVoid.InvoiceID:D5} for UserID {ViewedUserId} successfully voided by {User.Identity?.Name}.");
                TempData["StatusMessage"] = $"Invoice INV-{invoiceToVoid.InvoiceID:D5} ('{invoiceToVoid.Description}') has been cancelled.";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, $"Error voiding InvoiceID {invoiceToVoid.InvoiceID} for UserID {ViewedUserId}.");
                TempData["ErrorMessage"] = "Error voiding invoice. Please check logs.";
            }
            return RedirectToPage(new { userId = ViewedUserId, returnUrl = BackToEditUserUrl });
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
                if (!string.IsNullOrEmpty(appliedCreditsSummary))
                {
                    successMessage += appliedCreditsSummary; // This line uses the variable
                }
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