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
        [BindProperty]
        public string? ViewedUserId { get; set; } // To carry UserId if admin is viewing another
                                                  // Add the missing property definition for 'BackToEditUserUrl'
        [BindProperty]
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
            public int? PaymentID { get; set; } 
            public string Description { get; set; } = string.Empty;
            [DataType(DataType.Currency)]
            public decimal? ChargeAmount { get; set; }
            [DataType(DataType.Currency)]
            public decimal? PaymentAmount { get; set; }
            public string Type { get; set; } = string.Empty;
            public string StatusOrMethod { get; set; } = string.Empty;
            public bool IsVoided { get; set; } = false;
        }
        public async Task<IActionResult> OnGetAsync(string? userId, string? returnUrl, string? sortOrder)
        {
            // Enhanced initial logging
            _logger.LogInformation("MyBilling.OnGetAsync START - Received UserID: {ReceivedUserId}, ReturnUrl: {ReceivedReturnUrl}, SortOrder: {ReceivedSortOrder}", userId, returnUrl, sortOrder);
            this.BackToEditUserUrl = returnUrl;
            IdentityUser? determinedTargetUser = null;
            var loggedInUser = await _userManager.GetUserAsync(User);
            if (loggedInUser == null)
            {
                _logger.LogWarning("MyBilling.OnGetAsync: Current logged-in user is NULL. Challenging.");
                return Challenge(); 
            }
            _logger.LogInformation("MyBilling.OnGetAsync: LoggedInUser: {LoggedInUserName} (ID: {LoggedInUserId})", loggedInUser.UserName, loggedInUser.Id);
            if (!string.IsNullOrEmpty(userId) && (User.IsInRole("Admin") || User.IsInRole("Manager")))
            {
                _logger.LogInformation("MyBilling.OnGetAsync: Admin/Manager viewing specific user. Attempting to find UserID: {UserIdToFind}", userId);
                determinedTargetUser = await _userManager.FindByIdAsync(userId);
                if (determinedTargetUser == null)
                {
                    _logger.LogWarning("MyBilling.OnGetAsync: Admin/Manager provided UserID {ProvidedUserId}, but user was NOT FOUND. Defaulting to logged-in user for safety.", userId);
                    determinedTargetUser = loggedInUser; 
                    IsViewingSelf = true; 
                    ViewedUserId = loggedInUser.Id;
                }
                else
                {
                    _logger.LogInformation("MyBilling.OnGetAsync: Admin/Manager - TargetUser {TargetUserName} (ID: {TargetUserId}) FOUND.", determinedTargetUser.UserName, determinedTargetUser.Id);
                    IsViewingSelf = false;
                    ViewedUserId = determinedTargetUser.Id;
                }
            }
            else
            {
                _logger.LogInformation("MyBilling.OnGetAsync: Member viewing self, or Admin/Manager did not provide userId. Using LoggedInUser: {UserNameToUse}", loggedInUser.UserName);
                determinedTargetUser = loggedInUser;
                IsViewingSelf = true;
                ViewedUserId = determinedTargetUser.Id;
            }
            // This line was already present from previous step, ensuring correct logging placement.
            _logger.LogInformation("MyBilling.OnGetAsync: Determined Target User: {DeterminedUserName} (ID: {DeterminedUserId}), IsViewingSelf: {IsViewingSelfFlag}", determinedTargetUser.UserName, determinedTargetUser.Id, IsViewingSelf);
            _logger.LogInformation("MyBilling.OnGetAsync: BackToEditUserUrl initially set to: {InitialReturnUrl}", this.BackToEditUserUrl);
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
            _logger.LogInformation("MyBilling.OnGetAsync: DisplayName: {DisplayName}. TargetUserIsBillingContact: {TargetUserIsBillingContact}", DisplayName, TargetUserIsBillingContact);
            // Fetch data using determinedTargetUser.Id
            _logger.LogInformation("MyBilling.OnGetAsync: Fetching billing data for user: {determinedTargetUser.UserName} (ID: {determinedTargetUser.Id}).", determinedTargetUser.UserName, determinedTargetUser.Id);
            Invoices = await _context.Invoices.Where(i => i.UserID == determinedTargetUser.Id).ToListAsync();
            Payments = await _context.Payments.Where(p => p.UserID == determinedTargetUser.Id).ToListAsync();
            AvailableCredits = await _context.UserCredits.Where(uc => uc.UserID == determinedTargetUser.Id && !uc.IsApplied).ToListAsync();
            _logger.LogInformation("MyBilling.OnGetAsync: Found {Invoices.Count} invoices, {Payments.Count} payments, {AvailableCredits.Count} available credits for {determinedTargetUser.UserName}", Invoices.Count, Payments.Count, AvailableCredits.Count, determinedTargetUser.UserName);
            decimal totalCharges = Invoices.Where(i => i.Status != InvoiceStatus.Cancelled).Sum(i => i.AmountDue);
            decimal currentTotalAmountPaidOnInvoices = Invoices.Where(i => i.Status != InvoiceStatus.Cancelled).Sum(i => i.AmountPaid);
            CurrentBalance = totalCharges - currentTotalAmountPaidOnInvoices;
            TotalAvailableCredit = AvailableCredits.Sum(uc => uc.Amount);
            _logger.LogInformation("MyBilling.OnGetAsync: Balance for {determinedTargetUser.UserName}: {CurrentBalance}, TotalAvailableCredit: {TotalAvailableCredit}", determinedTargetUser.UserName, CurrentBalance, TotalAvailableCredit);
            // Populate Transactions
            Transactions.Clear();
            foreach (var invoice in Invoices.OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.DateCreated))
            {
                Transactions.Add(new BillingTransaction
                {
                    Date = invoice.InvoiceDate,
                    InvoiceID = invoice.InvoiceID,
                    PaymentID = null, // Invoices don't have a PaymentID here
                    Description = invoice.Description,
                    ChargeAmount = invoice.AmountDue,
                    Type = "Invoice",
                    StatusOrMethod = invoice.Status.ToString(),
                    IsVoided = (invoice.Status == InvoiceStatus.Cancelled) // Or similar logic for invoices
                });
            }
            foreach (var payment in Payments.OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.DateRecorded))
            {
                Transactions.Add(new BillingTransaction
                {
                    Date = payment.PaymentDate,
                    InvoiceID = payment.InvoiceID, // Linked invoice for payment
                    PaymentID = payment.PaymentID,
                    Description = payment.Notes ?? $"Payment (Ref: {payment.ReferenceNumber ?? "N/A"})",
                    PaymentAmount = payment.Amount,
                    Type = "Payment",
                    StatusOrMethod = payment.Method.ToString(),
                    IsVoided = payment.IsVoided
                });
            }
            _logger.LogInformation("MyBilling.OnGetAsync: Populated {Transactions.Count} total transactions for {determinedTargetUser.UserName}.", Transactions.Count, determinedTargetUser.UserName);
            // Apply Sorting
            string effectiveSort = sortOrder ?? "invoiceid_desc"; // Default to Invoice ID Descending
            this.CurrentSort = effectiveSort;
            // Modify BackToEditUserUrl if conditions are met
            if (!string.IsNullOrEmpty(this.BackToEditUserUrl) && 
                !string.IsNullOrEmpty(this.ViewedUserId) && 
                (this.BackToEditUserUrl.Contains("/Admin/Accounting/AdminBalances") || 
                 this.BackToEditUserUrl.Contains("/Admin/Accounting/CurrentBalances") || // Future name
                 this.BackToEditUserUrl.Contains("/Admin/Accounting/ReviewBatchInvoices") ||
                 this.BackToEditUserUrl.Contains("/Admin/Accounting/ManageBillableAssets") ))
            {
                string separator = this.BackToEditUserUrl.Contains('?') ? "&" : "?";
                this.BackToEditUserUrl = $"{this.BackToEditUserUrl}{separator}returnedFromUserId={this.ViewedUserId}";
                _logger.LogInformation("Appended returnedFromUserId to BackToEditUserUrl for relevant admin page. New URL: {NewUrl}", this.BackToEditUserUrl);
            }
            DateSort = (effectiveSort == "date_asc") ? "date_desc" : "date_asc";
            InvoiceIdSort = (effectiveSort == "invoiceid_asc") ? "invoiceid_desc" : "invoiceid_asc";
            DescriptionSort = (effectiveSort == "desc_asc") ? "desc_desc" : "desc_asc";
            TypeSort = (effectiveSort == "type_asc") ? "type_desc" : "type_asc";
            ChargeSort = (effectiveSort == "charge_asc") ? "charge_desc" : "charge_asc";
            PaymentSort = (effectiveSort == "payment_asc") ? "payment_desc" : "payment_asc";
            // Ensure the default sort's "next click" state is correctly set
            if (effectiveSort == "invoiceid_desc") InvoiceIdSort = "invoiceid_asc";
            else if (effectiveSort == "date_desc" && sortOrder == null) DateSort = "date_asc"; // If date_desc was default
            switch (effectiveSort)
            {
                case "date_desc": Transactions = [.. Transactions.OrderByDescending(t => t.Date).ThenBy(t => t.Type != "Invoice")]; break;
                case "date_asc": Transactions = [.. Transactions.OrderBy(t => t.Date).ThenBy(t => t.Type != "Invoice")]; break;
                case "invoiceid_desc": Transactions = [.. Transactions.OrderByDescending(t => t.InvoiceID ?? int.MinValue).ThenByDescending(t => t.Date)]; break;
                case "invoiceid_asc": Transactions = [.. Transactions.OrderBy(t => t.InvoiceID ?? int.MaxValue).ThenByDescending(t => t.Date)]; break;
                case "desc_desc": Transactions = [.. Transactions.OrderByDescending(t => t.Description)]; break;
                case "desc_asc": Transactions = [.. Transactions.OrderBy(t => t.Description)]; break;
                case "type_desc": Transactions = [.. Transactions.OrderByDescending(t => t.Type).ThenByDescending(t => t.Date)]; break;
                case "type_asc": Transactions = [.. Transactions.OrderBy(t => t.Type).ThenByDescending(t => t.Date)]; break;
                case "charge_desc": Transactions = [.. Transactions.OrderByDescending(t => t.ChargeAmount ?? decimal.MinValue).ThenByDescending(t => t.Date)]; break;
                case "charge_asc": Transactions = [.. Transactions.OrderBy(t => t.ChargeAmount ?? decimal.MaxValue).ThenByDescending(t => t.Date)]; break;
                case "payment_desc": Transactions = [.. Transactions.OrderByDescending(t => t.PaymentAmount ?? decimal.MinValue).ThenByDescending(t => t.Date)]; break;
                case "payment_asc": Transactions = [.. Transactions.OrderBy(t => t.PaymentAmount ?? decimal.MaxValue).ThenByDescending(t => t.Date)]; break;                    
            }
            _logger.LogInformation("MyBilling.OnGetAsync: Transactions sorted by {effectiveSort}. Final count: {Transactions.Count}", effectiveSort, Transactions.Count);
            return Page();
        }
        public async Task<IActionResult> OnPostVoidInvoiceAsync(int invoiceId, string voidReason)
        {
            _logger.LogInformation("OnPostVoidInvoiceAsync called for InvoiceID: {invoiceId} by User: {User.Identity?.Name}. Reason: {voidReason}", invoiceId, User.Identity?.Name, voidReason);
            // Ensure the current user is authorized to perform this action
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                _logger.LogWarning("User {User.Identity?.Name} attempted to void invoice {invoiceId} without authorization.", User.Identity?.Name, invoiceId);
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
                // ... (handle not found) ...
                return RedirectToPage(new { userId = ViewedUserId, returnUrl = BackToEditUserUrl });
            }
            if (invoiceToVoid.Status == InvoiceStatus.Cancelled)
            {
                // ... (handle already cancelled) ...
                return RedirectToPage(new { userId = ViewedUserId, returnUrl = BackToEditUserUrl });
            }
            decimal originalAmountPaidOnInvoice = invoiceToVoid.AmountPaid; // Capture before any changes
            string successMessage = $"Invoice INV-{invoiceToVoid.InvoiceID:D5} ('{invoiceToVoid.Description}') has been cancelled.";
            List<string> creditReversalSummaries = [];

            // --- Step 1: Reverse any CreditApplications that paid this invoice ---
            var applicationsToReverse = await _context.CreditApplications
                .Include(ca => ca.UserCredit) // Include the UserCredit to update it
                .Where(ca => ca.InvoiceID == invoiceToVoid.InvoiceID && !ca.IsReversed)
                .ToListAsync();

            decimal totalAmountUnpaidFromCreditApplications = 0;

            if (applicationsToReverse.Count != 0)
            {
                _logger.LogInformation("Found {Count} active credit applications for cancelled InvoiceID {InvoiceId}. Reversing them.", applicationsToReverse.Count, invoiceToVoid.InvoiceID);
                foreach (var appToReverse in applicationsToReverse)
                {
                    appToReverse.IsReversed = true;
                    appToReverse.ReversedDate = DateTime.UtcNow;
                    appToReverse.Notes = (appToReverse.Notes ?? "") + $"; Reversed due to Invoice INV-{invoiceToVoid.InvoiceID:D5} cancellation on {DateTime.UtcNow:yyyy-MM-dd}.";
                    _context.CreditApplications.Update(appToReverse);

                    if (appToReverse.UserCredit != null)
                    {
                        var linkedCredit = appToReverse.UserCredit;
                        linkedCredit.Amount += appToReverse.AmountApplied; // Restore amount to the credit
                        linkedCredit.IsApplied = false; // It's no longer (fully) applied
                        // Optionally clear AppliedDate if you use it to signify date of full application

                        // linkedCredit.AppliedDate = null; 
                        linkedCredit.LastUpdated = DateTime.UtcNow;
                        linkedCredit.ApplicationNotes = (string.IsNullOrEmpty(linkedCredit.ApplicationNotes) ? "" : linkedCredit.ApplicationNotes + "; ") +
                                                        $"Reversed application of {appToReverse.AmountApplied:C} from INV-{invoiceToVoid.InvoiceID:D5} (CA_ID {appToReverse.CreditApplicationID}) due to invoice cancellation.";
                        _context.UserCredits.Update(linkedCredit);
                        totalAmountUnpaidFromCreditApplications += appToReverse.AmountApplied;
                        creditReversalSummaries.Add($"Restored {appToReverse.AmountApplied:C} to UserCredit UCID#{linkedCredit.UserCreditID} (from CA_ID#{appToReverse.CreditApplicationID}).");
                        _logger.LogInformation("Reversed CreditApplicationID {CreditAppId}: Restored {AmountApplied} to UserCreditID {UserCreditId}.", appToReverse.CreditApplicationID, appToReverse.AmountApplied, linkedCredit.UserCreditID);
                    }
                    else
                    {
                        _logger.LogWarning("CreditApplicationID {CreditAppId} linked to InvoiceID {InvoiceId} has a null UserCredit navigation property. Cannot automatically restore amount to UserCredit.", appToReverse.CreditApplicationID, invoiceToVoid.InvoiceID);
                    }
                }
                successMessage += "\nApplied credits reversed: " + string.Join(" ", creditReversalSummaries);
            }

            // --- Step 2: Update the invoice itself ---
            invoiceToVoid.Status = InvoiceStatus.Cancelled;
            invoiceToVoid.ReasonForCancellation = voidReason;
            invoiceToVoid.LastUpdated = DateTime.UtcNow;
            // AmountPaid on the invoice should reflect that credit applications are reversed.
            // The actual direct payments are handled next by creating a new credit.
            invoiceToVoid.AmountPaid -= totalAmountUnpaidFromCreditApplications;
            if (invoiceToVoid.AmountPaid < 0) invoiceToVoid.AmountPaid = 0;

            _context.Invoices.Update(invoiceToVoid);

            // --- Step 3: Create a new UserCredit for any amount that was paid by direct payments (not by other credits) ---
            // This is the originalAmountPaidOnInvoice MINUS what we just reversed from credit applications.
            decimal netAmountPaidByDirectMeans = originalAmountPaidOnInvoice - totalAmountUnpaidFromCreditApplications;

            if (netAmountPaidByDirectMeans > 0)
            {
                var newCreditForDirectPayments = new UserCredit
                {
                    UserID = invoiceToVoid.UserID,
                    CreditDate = DateTime.UtcNow,
                    Amount = netAmountPaidByDirectMeans,
                    Reason = $"Credit from cancelled (and previously directly paid portion of) Invoice INV-{invoiceToVoid.InvoiceID:D5}. Original cancel reason: {voidReason}",
                    IsApplied = false,
                    IsVoided = false,
                    DateCreated = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow,
                    SourcePaymentID = null // Cannot easily determine this if multiple payments were involved
                };
                _context.UserCredits.Add(newCreditForDirectPayments);
                _logger.LogInformation("New UserCredit UCID#{UserCreditId} for {Amount} created due to directly paid portion of cancelled InvoiceID {InvoiceId}.", newCreditForDirectPayments.UserCreditID, newCreditForDirectPayments.Amount, invoiceToVoid.InvoiceID);
                successMessage += $"\nA new credit of {newCreditForDirectPayments.Amount:C} has been generated for the directly paid portion.";
            }
            else if (originalAmountPaidOnInvoice > 0 && netAmountPaidByDirectMeans <= 0)
            {
                _logger.LogInformation("Invoice INV-{InvoiceId} was fully paid by credits that have now been reversed. No new credit generated for direct payments.", invoiceToVoid.InvoiceID);
                successMessage += "\nInvoice was previously paid by credits; those credits have been restored.";
            }


            try
            {
                await _context.SaveChangesAsync();
                TempData["StatusMessage"] = successMessage;
                _logger.LogInformation("Invoice INV-{InvoiceId} cancelled. Credit applications reversed and new credit generated if applicable.", invoiceToVoid.InvoiceID);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error cancelling InvoiceID {InvoiceId}.", invoiceToVoid.InvoiceID);
                TempData["ErrorMessage"] = "Error cancelling invoice. Please check logs.";
            }
            return RedirectToPage(new { userId = ViewedUserId, returnUrl = BackToEditUserUrl });
        }
        public async Task<IActionResult> OnPostApplyLateFeeAsync(string userId)
        {
            _logger.LogInformation("OnPostApplyLateFeeAsync called for UserID: {userId}. Current viewing user (admin/manager): {User.Identity?.Name}", userId, User.Identity?.Name);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "User ID was not provided to apply late fee.";
                return RedirectToPage("/Index", new { area = "Admin" });
            }
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                _logger.LogWarning("User {User.Identity?.Name} attempted to apply late fee without authorization.", User.Identity?.Name);
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
                return RedirectToPage(new { userId, returnUrl = BackToEditUserUrl });
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
                return RedirectToPage(new { userId, returnUrl = BackToEditUserUrl });
            }
            string expectedLateFeeDescriptionPart = $"INV-{latestOverdueDuesInvoice.InvoiceID:D5}";
            var existingLateFeeForThisInvoice = await _context.Invoices
                .AnyAsync(i => i.UserID == userId &&
                               i.Type == InvoiceType.LateFee &&
                               i.Description.Contains(expectedLateFeeDescriptionPart));
            if (existingLateFeeForThisInvoice)
            {
                TempData["WarningMessage"] = $"A late fee for the overdue assessment (INV-{latestOverdueDuesInvoice.InvoiceID:D5}) appears to have already been applied for {targetUser.UserName}.";
                return RedirectToPage(new { userId, returnUrl = BackToEditUserUrl });
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
            if (availableCredits.Count != 0)
            {
                _logger.LogInformation("User {targetUser.UserName} has {availableCredits.Count} available credits. Attempting to apply to new late fee invoice.", targetUser.UserName, availableCredits.Count);
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
                _logger.LogInformation("Late fee invoice INV-{lateFeeInvoice.InvoiceID:D5} created for {targetUser.UserName}.", lateFeeInvoice.InvoiceID, targetUser.UserName);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error applying late fee for UserID {userId}.", userId);
                TempData["ErrorMessage"] = "Error applying late fee. Check logs.";
            }
            return RedirectToPage(new { userId, returnUrl = BackToEditUserUrl });
        }
        public async Task<IActionResult> OnPostVoidPaymentAsync(int paymentId, string voidReason)
        {
            _logger.LogInformation("ENTERING OnPostVoidPaymentAsync for PaymentID: {PaymentId}, Reason: {Reason}", paymentId, voidReason);
            _logger.LogInformation("ViewedUserId from PageModel property: {ModelViewedUserId}", this.ViewedUserId);
            if (string.IsNullOrEmpty(ViewedUserId))
            {
                var currentUserForSafety = await _userManager.GetUserAsync(User);
                if (currentUserForSafety == null)
                {
                    _logger.LogWarning("OnPostVoidPaymentAsync: Current user not found. Challenging.");
                    return Challenge();
                }
                ViewedUserId = currentUserForSafety.Id;
                _logger.LogWarning("OnPostVoidPaymentAsync: ViewedUserId was null/empty. Defaulted to current user ID: {DefViewedUserId}.", ViewedUserId);
            }
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                _logger.LogWarning("User {UserName} attempted to void payment {PaymentId} for user {TargetUserId} without authorization.", User.Identity?.Name, paymentId, ViewedUserId);
                TempData["ErrorMessage"] = "You are not authorized to perform this action.";
                return RedirectToPage(new { userId = ViewedUserId, returnUrl = BackToEditUserUrl });
            }
            if (string.IsNullOrWhiteSpace(voidReason))
            {
                TempData["WarningMessage"] = "A reason is required to void a payment.";
                _logger.LogWarning("Void Payment: Reason not provided for PaymentID {PaymentId} by User {UserName}.", paymentId, User.Identity?.Name);
                return RedirectToPage(new { userId = ViewedUserId, returnUrl = BackToEditUserUrl });
            }
            var paymentToVoid = await _context.Payments
                                    .Include(p => p.Invoice) // Include the directly linked invoice
                                    .FirstOrDefaultAsync(p => p.PaymentID == paymentId && p.UserID == ViewedUserId);
            if (paymentToVoid == null)
            {
                _logger.LogWarning("Void Payment: PaymentID {PaymentId} not found for UserID {UserId}.", paymentId, ViewedUserId);
                TempData["ErrorMessage"] = "Payment not found or not accessible for this user.";
                return RedirectToPage(new { userId = ViewedUserId, returnUrl = BackToEditUserUrl });
            }
            if (paymentToVoid.IsVoided)
            {
                _logger.LogInformation("Void Payment: PaymentID {PaymentId} is already voided.", paymentId);
                TempData["WarningMessage"] = $"Payment (ID: {paymentToVoid.PaymentID}) is already voided.";
                return RedirectToPage(new { userId = ViewedUserId, returnUrl = BackToEditUserUrl });
            }
            paymentToVoid.IsVoided = true;
            paymentToVoid.VoidedDate = DateTime.UtcNow;
            paymentToVoid.ReasonForVoiding = voidReason.Trim();
            paymentToVoid.LastUpdated = DateTime.UtcNow;
            _context.Payments.Update(paymentToVoid);
            _logger.LogInformation("PaymentID {PaymentId} marked as voided.", paymentToVoid.PaymentID);
            // --- Step 1: Adjust the directly linked invoice (if any) ---
            // This part handles the portion of the payment that directly paid an invoice, *not* the overpayment part.
            if (paymentToVoid.InvoiceID.HasValue && paymentToVoid.Invoice != null)
            {
                var directlyLinkedInvoice = paymentToVoid.Invoice;
                _logger.LogInformation("Voiding Payment P{PaymentId}: Adjusting directly linked Invoice INV-{InvoiceID}. Current AmountPaid: {AmountPaid}",
                    paymentToVoid.PaymentID, directlyLinkedInvoice.InvoiceID, directlyLinkedInvoice.AmountPaid);

                // Determine how much of *this specific payment* was applied to *this invoice*.
                // This is complex if multiple payments hit one invoice or one payment hit multiple.
                // For simplicity, assume the payment amount up to the invoice's original (AmountDue - AmountPaid_before_this_payment) was from this payment.
                // A more accurate way would be to look at Payment.Amount vs Invoice.AmountDue if this was the only payment.
                // The current logic: Math.Min(directlyLinkedInvoice.AmountPaid, paymentToVoid.Amount)
                // If payment was $500 for $100 invoice, AmountPaid became $100. reduction is Min(100, 500) = $100. Correct for this invoice.
                // If payment was $50 for $100 invoice, AmountPaid became $50. reduction is Min(50, 50) = $50. Correct.
                decimal reductionAmountForDirectApplication = Math.Min(directlyLinkedInvoice.AmountPaid, paymentToVoid.Amount);
                // This reductionAmountForDirectApplication should not exceed the portion of the payment that was NOT an overpayment.
                // Example: Payment $500, Invoice $100. Overpayment $400. Portion applied to this invoice = $100.
                // So, reductionAmountForDirectApplication should be $100.
                decimal nonOverpaymentPortion = paymentToVoid.Amount; // Assume full payment initially applied to this invoice if no overpayment
                if (paymentToVoid.Amount > directlyLinkedInvoice.AmountDue - (directlyLinkedInvoice.AmountPaid - reductionAmountForDirectApplication)) { // A bit circular, need original state
                     // More simply: if payment amount > what was needed for THIS invoice, then only what was needed is reversed from direct application.
                     var amountNeededForThisInvoiceInitially = directlyLinkedInvoice.AmountDue - (directlyLinkedInvoice.AmountPaid - reductionAmountForDirectApplication); // Amount paid before this payment's effect is hard to get here.
                     // Let's assume `reductionAmount` as calculated by Min is the best guess for what this payment contributed to this invoice's AmountPaid directly.
                }


                directlyLinkedInvoice.AmountPaid -= reductionAmountForDirectApplication;
                if (directlyLinkedInvoice.AmountPaid < 0) directlyLinkedInvoice.AmountPaid = 0;

                if (directlyLinkedInvoice.Status != InvoiceStatus.Cancelled) // Respect if already cancelled
                {
                    if (directlyLinkedInvoice.AmountPaid < directlyLinkedInvoice.AmountDue)
                    {
                        directlyLinkedInvoice.Status = (directlyLinkedInvoice.DueDate < DateTime.Today.AddDays(-1)) ? InvoiceStatus.Overdue : InvoiceStatus.Due;
                    }
                }
                directlyLinkedInvoice.LastUpdated = DateTime.UtcNow;
                _context.Invoices.Update(directlyLinkedInvoice);
                _logger.LogInformation("Voiding Payment P{PaymentId}: Directly linked Invoice INV-{InvoiceID} updated. New AmountPaid: {NewAmountPaid}, New Status: {NewStatus}",
                    paymentToVoid.PaymentID, directlyLinkedInvoice.InvoiceID, directlyLinkedInvoice.AmountPaid, directlyLinkedInvoice.Status);
            }

            // --- Step 2: Handle UserCredits that were created BY this payment (i.e., overpayment credits) ---
            var creditsSourcedFromThisPayment = await _context.UserCredits
                .Where(uc => uc.SourcePaymentID == paymentToVoid.PaymentID && !uc.IsVoided)
                .ToListAsync();

            if (creditsSourcedFromThisPayment.Count != 0)
            {
                _logger.LogInformation("Voiding Payment P{PaymentId}: Found {Count} UserCredits sourced from this payment. Voiding them and reversing their applications.",
                    paymentToVoid.PaymentID, creditsSourcedFromThisPayment.Count);

                foreach (var sourcedCredit in creditsSourcedFromThisPayment)
                {
                    _logger.LogInformation("Voiding Payment P{PaymentId}: Processing sourced UserCredit UCID#{SourcedCreditId} (Amount: {Amount}).",
                        paymentToVoid.PaymentID, sourcedCredit.UserCreditID, sourcedCredit.Amount);

                    sourcedCredit.IsVoided = true;

                    // Append to Reason, as this is a significant event for the credit's lifecycle.
                    sourcedCredit.Reason = $"{sourcedCredit.Reason ?? "Credit"}; VOIDED: Source Payment P{paymentToVoid.PaymentID} was voided on {DateTime.UtcNow:yyyy-MM-dd}.";
                    // Set ApplicationNotes to a concise final status, rather than appending.
                    sourcedCredit.ApplicationNotes = $"VOIDED due to source Payment P{paymentToVoid.PaymentID} void on {DateTime.UtcNow:yyyy-MM-dd}. Original reason: {sourcedCredit.Reason}";
                    sourcedCredit.LastUpdated = DateTime.UtcNow;

                    // Find all active applications of this specific sourcedCredit
                    var applicationsToReverse = await _context.CreditApplications
                        .Include(ca => ca.Invoice) // Include the Invoice to update it
                        .Where(ca => ca.UserCreditID == sourcedCredit.UserCreditID && !ca.IsReversed)
                        .ToListAsync();

                    if (applicationsToReverse.Count != 0)
                    {
                        _logger.LogInformation("Voiding Payment P{PaymentId}: UserCredit UCID#{SourcedCreditId} has {AppCount} active applications. Reversing them.",
                            paymentToVoid.PaymentID, sourcedCredit.UserCreditID, applicationsToReverse.Count);
                        foreach (var appToReverse in applicationsToReverse)
                        {
                            appToReverse.IsReversed = true;
                            appToReverse.ReversedDate = DateTime.UtcNow;
                            appToReverse.Notes = (appToReverse.Notes ?? "") + $"; Reversed due to source UserCredit UCID#{sourcedCredit.UserCreditID} (from Payment P{paymentToVoid.PaymentID}) voiding on {DateTime.UtcNow:yyyy-MM-dd}.";
                            _context.CreditApplications.Update(appToReverse);

                            if (appToReverse.Invoice != null)
                            {
                                var invoicePaidByApplication = appToReverse.Invoice;
                                invoicePaidByApplication.AmountPaid -= appToReverse.AmountApplied;
                                if (invoicePaidByApplication.AmountPaid < 0) invoicePaidByApplication.AmountPaid = 0;

                                if (invoicePaidByApplication.Status != InvoiceStatus.Cancelled) // Respect if already cancelled
                                {
                                    if (invoicePaidByApplication.AmountPaid < invoicePaidByApplication.AmountDue)
                                    {
                                        invoicePaidByApplication.Status = (invoicePaidByApplication.DueDate < DateTime.Today.AddDays(-1)) ? InvoiceStatus.Overdue : InvoiceStatus.Due;
                                    }
                                }
                                invoicePaidByApplication.LastUpdated = DateTime.UtcNow;
                                _context.Invoices.Update(invoicePaidByApplication);
                                _logger.LogInformation("Voiding Payment P{PaymentId}: Reversed CA_ID#{AppId} ({AmountApplied:C} from INV-{InvoiceId}). Invoice new AmountPaid: {NewAmountPaid}, Status: {NewStatus}",
                                    paymentToVoid.PaymentID, appToReverse.CreditApplicationID, appToReverse.AmountApplied, invoicePaidByApplication.InvoiceID, invoicePaidByApplication.AmountPaid, invoicePaidByApplication.Status);
                            }
                            else
                            {
                                _logger.LogWarning("Voiding Payment P{PaymentId}: CreditApplication CA_ID#{AppId} for UserCredit UCID#{SourcedCreditId} has a null Invoice navigation property. Cannot update invoice.",
                                    paymentToVoid.PaymentID, appToReverse.CreditApplicationID, sourcedCredit.UserCreditID);
                            }
                        }
                    }
                    // After reversing applications, set the sourced credit's amount to 0 and mark as applied (as it's now voided)
                    sourcedCredit.Amount = 0;
                    sourcedCredit.IsApplied = true; // A voided credit is effectively "applied" in the sense it's no longer available
                    sourcedCredit.AppliedDate = DateTime.UtcNow; // Date it was effectively zeroed out/voided
                    _context.UserCredits.Update(sourcedCredit);
                }
            }
            else
            {
                 _logger.LogInformation("Voiding Payment P{PaymentId}: No UserCredits were directly sourced from this payment.", paymentToVoid.PaymentID);
            }
            try
            {
                await _context.SaveChangesAsync();
                TempData["StatusMessage"] = $"Payment (ID: {paymentToVoid.PaymentID}, Amount: {paymentToVoid.Amount:C}) voided successfully. Reason: {paymentToVoid.ReasonForVoiding}. Related records updated.";
                _logger.LogInformation("PaymentID {PaymentId} and related entities saved successfully after voiding.", paymentToVoid.PaymentID);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error voiding PaymentID {PaymentId} for UserID {UserId}. Details: {ErrorMessage}",
                    paymentToVoid.PaymentID, ViewedUserId, ex.InnerException?.Message ?? ex.Message);
                TempData["ErrorMessage"] = "Error voiding payment. Please check logs.";
            }
            return RedirectToPage(new { userId = ViewedUserId, returnUrl = BackToEditUserUrl });
        }
    }
}