using Members.Data;
using Members.Models; // Assuming UserProfile, Invoice, Payment are here
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text; // Added for StringBuilder and Encoding
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
        private const int RecentFeeDaysThreshold = 7;
        // Inner class for results
        public class LateFeeApplicationResult
        {
            public bool Success { get; private set; }
            public string Message { get; private set; } = string.Empty;
            public string UserId { get; private set; } = string.Empty;
            public string UserName { get; private set; } = string.Empty;
            public decimal? FeeAmount { get; private set; }
            public decimal? CreditsApplied { get; private set; }
            public int? InvoiceId { get; private set; }
            public InvoiceStatus? FinalInvoiceStatus { get; private set; }
            // Private constructor to enforce factory method usage
            private LateFeeApplicationResult() { }
            public static LateFeeApplicationResult UserNotFound(string userId) =>
                new() { Success = false, UserId = userId, UserName = "N/A", Message = $"User with ID {userId} not found." };
            public static LateFeeApplicationResult ProfileNotFound(string userId, string userName) =>
                new() { Success = false, UserId = userId, UserName = userName, Message = $"User profile for {userName} (ID: {userId}) not found." };
            public static LateFeeApplicationResult NotBillingContact(string userName, string userId) =>
                new() { Success = false, UserId = userId, UserName = userName, Message = $"User {userName} (ID: {userId}) is not a billing contact." };
            public static LateFeeApplicationResult SkippedNoOutstandingBalance(string userName, string userId, decimal balance) =>
                new() { Success = false, UserId = userId, UserName = userName, Message = $"User {userName} (ID: {userId}) has no outstanding balance ({balance:C}). Skipped." };
            public static LateFeeApplicationResult SkippedRecentFeeExists(string userName, string userId) =>
                new() { Success = false, UserId = userId, UserName = userName, Message = $"User {userName} (ID: {userId}) already has a recent late fee. Skipped." };
            public static LateFeeApplicationResult Error(string userName, string userId, string errorMessage, Exception? ex = null) =>
                new() { Success = false, UserId = userId, UserName = userName, Message = $"Error applying late fee to {userName} (ID: {userId}): {errorMessage}{(ex != null ? " Details: " + ex.Message : "")}" };
            public static LateFeeApplicationResult FeeApplied(string userName, string userId, decimal feeAmount, int invoiceId, decimal creditsApplied, InvoiceStatus status) =>
                new() { Success = true, UserId = userId, UserName = userName, FeeAmount = feeAmount, InvoiceId = invoiceId, CreditsApplied = creditsApplied, FinalInvoiceStatus = status, Message = $"User {userName} (ID: {userId}): Late fee of {feeAmount:C} applied. Invoice INV-{invoiceId:D5}. Credits applied: {creditsApplied:C}. Status: {status}." };
        }
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
        public string? CreditBalanceSort { get; set; }
        [BindProperty(SupportsGet = true)]
        public bool ShowOnlyOutstanding { get; set; } = true;
        [DataType(DataType.Currency)]
        public decimal TotalCurrentBalance { get; set; }
        [DataType(DataType.Currency)]
        public decimal TotalCreditBalance { get; set; }
        [BindProperty(SupportsGet = true)]
        public string? ReturnedFromUserId { get; set; }
        private async Task<LateFeeApplicationResult> ApplyLateFeeToUserAsync(string userId, string? knownUserName = null)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("ApplyLateFeeToUserAsync: User with ID {UserId} not found.", userId);
                return LateFeeApplicationResult.UserNotFound(userId);
            }
            var userNameForDisplay = knownUserName ?? user.UserName ?? user.Email ?? userId;
            var userProfile = await _context.UserProfile.FirstOrDefaultAsync(up => up.UserId == userId);
            if (userProfile == null)
            {
                _logger.LogWarning("ApplyLateFeeToUserAsync: User profile for {UserName} (ID: {UserId}) not found.", userNameForDisplay, userId);
                return LateFeeApplicationResult.ProfileNotFound(userId, userNameForDisplay);
            }
            if (!userProfile.IsBillingContact)
            {
                _logger.LogInformation("ApplyLateFeeToUserAsync: User {UserName} (ID: {UserId}) is not a billing contact.", userNameForDisplay, userId);
                return LateFeeApplicationResult.NotBillingContact(userNameForDisplay, userId);
            }
            decimal totalChargesFromInvoices = await _context.Invoices
                .Where(i => i.UserID == userId && i.Status != InvoiceStatus.Cancelled)
                .SumAsync(i => i.AmountDue);
            decimal totalAmountPaidOnInvoices = await _context.Invoices
                .Where(i => i.UserID == userId && i.Status != InvoiceStatus.Cancelled)
                .SumAsync(i => i.AmountPaid);
            decimal currentBalance = totalChargesFromInvoices - totalAmountPaidOnInvoices;
            if (currentBalance <= 0)
            {
                _logger.LogInformation("ApplyLateFeeToUserAsync: User {UserName} (ID: {UserId}) has no outstanding balance ({CurrentBalance:C}).", userNameForDisplay, userId, currentBalance);
                return LateFeeApplicationResult.SkippedNoOutstandingBalance(userNameForDisplay, userId, currentBalance);
            }
            var latestOverdueDuesInvoice = await _context.Invoices
                .Where(i => i.UserID == userId &&
                            i.Type == InvoiceType.Dues &&
                            i.Status != InvoiceStatus.Paid &&
                            i.Status != InvoiceStatus.Cancelled &&
                            i.DueDate < DateTime.Today)
                .OrderByDescending(i => i.DueDate)
                .FirstOrDefaultAsync();
            bool skipForRecentFee = false;
            if (latestOverdueDuesInvoice != null)
            {
                string expectedDescPart = $"INV-{latestOverdueDuesInvoice.InvoiceID:D5}";
                if (await _context.Invoices.AnyAsync(i => i.UserID == userId &&
                                                          i.Type == InvoiceType.LateFee &&
                                                          i.Description.Contains(expectedDescPart) &&
                                                          i.InvoiceDate >= DateTime.Today.AddDays(-RecentFeeDaysThreshold)))
                {
                    skipForRecentFee = true;
                }
            }
            else
            {
                if (await _context.Invoices.AnyAsync(i => i.UserID == userId &&
                                                          i.Type == InvoiceType.LateFee &&
                                                          i.InvoiceDate >= DateTime.Today.AddDays(-RecentFeeDaysThreshold)))
                {
                    skipForRecentFee = true;
                }
            }
            if (skipForRecentFee)
            {
                _logger.LogInformation("ApplyLateFeeToUserAsync: User {UserName} (ID: {UserId}) already has a recent late fee. Skipped.", userNameForDisplay, userId);
                return LateFeeApplicationResult.SkippedRecentFeeExists(userNameForDisplay, userId);
            }
            decimal lateFeeAmount;
            string feeCalculationDescription;
            if (latestOverdueDuesInvoice != null)
            {
                decimal fivePercentOfDues = latestOverdueDuesInvoice.AmountDue * 0.05m;
                lateFeeAmount = Math.Max(25.00m, fivePercentOfDues);
                feeCalculationDescription = $"Late fee on overdue INV-{latestOverdueDuesInvoice.InvoiceID:D5} ({latestOverdueDuesInvoice.AmountDue:C} due {latestOverdueDuesInvoice.DueDate:yyyy-MM-dd})."; /*Fee: Max($25, 5 % = { fivePercentOfDues: C}) = { lateFeeAmount: C}.*/
            }
            else
            {
                lateFeeAmount = 25.00m;
                feeCalculationDescription = "Standard $25.00 late fee applied.";
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
            decimal creditsAppliedToThisFee = 0;
            List<UserCredit> availableCredits = await _context.UserCredits
                .Where(uc => uc.UserID == userId && !uc.IsApplied && !uc.IsVoided)
                .OrderBy(uc => uc.CreditDate)
                .ToListAsync();
            if (availableCredits.Count > 0) // CA1860
            {
                foreach (var credit in availableCredits) // UserCredit objects that are !IsApplied and !IsVoided
                {
                    if (lateFeeInvoice.AmountPaid >= lateFeeInvoice.AmountDue) break; // Stop if fee invoice is fully paid
                    // Calculate how much of this credit can be applied to the remaining fee amount
                    decimal amountToApplyFromThisCredit = Math.Min(credit.Amount, lateFeeInvoice.AmountDue - lateFeeInvoice.AmountPaid);
                    if (amountToApplyFromThisCredit <= 0) continue; // Should not happen if credit.Amount > 0 and fee not paid
                    // Mark the entire credit as applied, as per the simplified logic without AmountUsed
                    credit.IsApplied = true;
                    credit.AppliedDate = DateTime.UtcNow;
                    credit.AppliedToInvoiceID = 0; // Placeholder, will be updated after invoice is saved
                    // Note: ApplicationNotes should reflect that the *entire* credit is considered "used" or "allocated" by this application,
                    // even if only a portion is applied to this specific invoice. Or, state only the amount applied.
                    // For clarity, let's state what was applied to *this* invoice.
                    credit.ApplicationNotes = (credit.ApplicationNotes ?? "").Trim() + $" Applied {amountToApplyFromThisCredit:C} to Late Fee INV-0. Original credit amount: {credit.Amount:C}.";
                    credit.LastUpdated = DateTime.UtcNow;
                    _context.UserCredits.Update(credit);
                    lateFeeInvoice.AmountPaid += amountToApplyFromThisCredit;
                    creditsAppliedToThisFee += amountToApplyFromThisCredit;
                }
                if (lateFeeInvoice.AmountPaid >= lateFeeInvoice.AmountDue)
                {
                    lateFeeInvoice.Status = InvoiceStatus.Paid;
                    lateFeeInvoice.AmountPaid = lateFeeInvoice.AmountDue;
                }
            }
            await _context.SaveChangesAsync(); // Save Invoice and initial Credit updates
            bool neededCreditInvoiceIdUpdate = false;
            foreach (var credit in availableCredits.Where(c => c.AppliedToInvoiceID == 0 && c.ApplicationNotes != null && c.ApplicationNotes.Contains("Auto-applied portion to Late Fee INV-0")))
            {
                credit.AppliedToInvoiceID = lateFeeInvoice.InvoiceID;
                credit.ApplicationNotes = credit.ApplicationNotes?.Replace("INV-0", $"INV-{lateFeeInvoice.InvoiceID:D5}");
                _context.UserCredits.Update(credit);
                neededCreditInvoiceIdUpdate = true;
            }
            if (neededCreditInvoiceIdUpdate)
            {
                await _context.SaveChangesAsync(); // Save Credit updates with correct InvoiceID
            }
            _logger.LogInformation("ApplyLateFeeToUserAsync: Fee applied for {UserName} (ID: {UserId}). Invoice INV-{InvoiceId}, Amount: {FeeAmount}, Credits: {CreditsApplied}, Status: {Status}",
                userNameForDisplay, userId, lateFeeInvoice.InvoiceID, lateFeeAmount, creditsAppliedToThisFee, lateFeeInvoice.Status);
            return LateFeeApplicationResult.FeeApplied(userNameForDisplay, userId, lateFeeAmount, lateFeeInvoice.InvoiceID, creditsAppliedToThisFee, lateFeeInvoice.Status);
        }
        public async Task<IActionResult> OnPostApplyLateFeeAsync(string userId)
        {
            _logger.LogInformation("OnPostApplyLateFeeAsync called for UserID: {UserId}", userId);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "User ID was not provided.";
                return RedirectToPage(new { sortOrder = CurrentSort, showOnlyOutstanding = ShowOnlyOutstanding });
            }
            try
            {
                var result = await ApplyLateFeeToUserAsync(userId);
                if (result.Success)
                {
                    TempData["StatusMessage"] = result.Message;
                }
                else
                {
                    // Distinguish between skips and actual errors for TempData type
                    if (result.Message.Contains("Error") || result.Message.Contains("not found"))
                        TempData["ErrorMessage"] = result.Message;
                    else // Skips like no balance, recent fee, not billing contact
                        TempData["WarningMessage"] = result.Message;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error in OnPostApplyLateFeeAsync for UserID: {UserId}", userId);
                TempData["ErrorMessage"] = "A critical error occurred while applying the late fee.";
            }
            return RedirectToPage(new { sortOrder = CurrentSort, showOnlyOutstanding = ShowOnlyOutstanding });
        }
        public class MemberBalanceViewModel
        {
            public string UserId { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            [DataType(DataType.Currency)]
            public decimal CurrentBalance { get; set; }
            [DataType(DataType.Currency)]
            public decimal CreditBalance { get; set; } = 0;
            public bool HasOutstandingBalance => CurrentBalance > 0;
        }
        public async Task OnGetAsync(string? sortOrder, bool? showOnlyOutstanding) // Made parameters nullable to match typical OnGetAsync patterns if they can be optional
        {
            _logger.LogInformation("OnGetAsync called for AdminBalancesModel. SortOrder: {SortOrder}, ShowOnlyOutstanding: {ShowFilter}, ReturnedFromUserId: {ReturnedUserId}",
                sortOrder, showOnlyOutstanding, ReturnedFromUserId);
            if (!string.IsNullOrEmpty(ReturnedFromUserId))
            {
                _logger.LogInformation("Returned to AdminBalances from MyBilling, last viewed user ID: {ReturnedUserId}", ReturnedFromUserId);
                // Placeholder for potential future use:
                // TempData["HighlightUserId"] = ReturnedFromUserId; 
            }
            CurrentSort = sortOrder;
            NameSort = string.IsNullOrEmpty(sortOrder) || sortOrder == "name_desc" ? "name_asc" : "name_desc";
            EmailSort = sortOrder == "email_asc" ? "email_desc" : "email_asc";
            BalanceSort = sortOrder == "balance_desc" ? "balance_asc" : "balance_desc";
            CreditBalanceSort = sortOrder == "credit_asc" ? "credit_desc" : "credit_asc";
            if (showOnlyOutstanding.HasValue)
            {
                ShowOnlyOutstanding = showOnlyOutstanding.Value;
            }
            var memberRoleName = "Member";
            var usersInMemberRole = await _userManager.GetUsersInRoleAsync(memberRoleName);
            if (usersInMemberRole == null || usersInMemberRole.Count == 0) // CA1860
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
                    string fullName;
                    if (!string.IsNullOrWhiteSpace(userProfile.LastName) && !string.IsNullOrWhiteSpace(userProfile.FirstName))
                    {
                        fullName = $"{userProfile.LastName}, {userProfile.FirstName}";
                    }
                    else if (!string.IsNullOrWhiteSpace(userProfile.LastName))
                    {
                        fullName = userProfile.LastName; // Only LastName available
                    }
                    else if (!string.IsNullOrWhiteSpace(userProfile.FirstName))
                    {
                        fullName = userProfile.FirstName; // Only FirstName available
                    }
                    else
                    {
                        fullName = user.UserName ?? "N/A"; // Fallback to UserName
                    }
                    _logger.LogInformation("Calculating balance for: {user.UserName} (ID: {user.Id})", user.UserName, user.Id);
                    // Log all invoices for the user
                    var userInvoices = await _context.Invoices
                        .Where(i => i.UserID == user.Id)
                        .Select(i => new { i.InvoiceID, i.AmountDue, i.Status })
                        .ToListAsync();
                    foreach (var inv in userInvoices)
                    {
                        _logger.LogInformation("User {UserName} - Invoice Detail: ID={InvoiceID}, AmountDue={AmountDue}, Status={Status}", user.UserName, inv.InvoiceID, inv.AmountDue, inv.Status);
                    }
                    decimal totalChargesFromInvoices = await _context.Invoices
                        .Where(i => i.UserID == user.Id && i.Status != InvoiceStatus.Cancelled) // Include Paid invoices in charges
                        .SumAsync(i => i.AmountDue);
                    _logger.LogInformation("User {UserName} - Total Charges from Invoices: {TotalCharges}", user.UserName, totalChargesFromInvoices);
                    decimal totalAmountPaidOnInvoices = await _context.Invoices
                        .Where(i => i.UserID == user.Id && i.Status != InvoiceStatus.Cancelled)
                        .SumAsync(i => i.AmountPaid);
                    _logger.LogInformation("User {UserName} - Total Amount Paid (from Invoices): {TotalAmountPaid}", user.UserName, totalAmountPaidOnInvoices);
                    decimal currentBalance = totalChargesFromInvoices - totalAmountPaidOnInvoices;
                    _logger.LogInformation("User {UserName} - Calculated Current Balance (Charges - Invoice.AmountPaid): {CurrentBalance}", user.UserName, currentBalance);
                    decimal unappliedCredits = await _context.UserCredits
                        .Where(uc => uc.UserID == user.Id && !uc.IsApplied && !uc.IsVoided)
                        .SumAsync(uc => uc.Amount);
                    _logger.LogInformation("User {user.UserName} - Fetched Unapplied Credit Balance: {unappliedCredits}", user.UserName, unappliedCredits);
                    var memberVm = new MemberBalanceViewModel
                    {
                        UserId = user.Id,
                        FullName = fullName,
                        Email = user.Email ?? "N/A",
                        CurrentBalance = currentBalance,
                        CreditBalance = unappliedCredits
                    };
                    if (ShowOnlyOutstanding && memberVm.CurrentBalance <= 0 && memberVm.CreditBalance <= 0)
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
                "credit_desc" => [.. memberBalancesTemp.OrderByDescending(s => s.CreditBalance)],
                "credit_asc" => [.. memberBalancesTemp.OrderBy(s => s.CreditBalance)],
                _ => [.. memberBalancesTemp.OrderBy(s => s.FullName)],// Default sort
            };
            _logger.LogInformation("Populated MemberBalances. Count: {MemberBalances.Count}", MemberBalances.Count);
            TotalCurrentBalance = MemberBalances.Sum(mb => mb.CurrentBalance);
            TotalCreditBalance = MemberBalances.Sum(mb => mb.CreditBalance);
            _logger.LogInformation("Calculated totals: TotalCurrentBalance = {TotalCurrentBalance}, TotalCreditBalance = {TotalCreditBalance}", TotalCurrentBalance, TotalCreditBalance);
        }
        public async Task<IActionResult> OnGetExportCsvAsync(string? sortOrder, bool? showOnlyOutstanding)
        {
            _logger.LogInformation("OnGetExportCsvAsync called. SortOrder: {SortOrder}, ShowOnlyOutstanding: {ShowOnlyOutstanding}", sortOrder, showOnlyOutstanding);
            var memberRoleName = "Member";
            var usersInMemberRole = await _userManager.GetUsersInRoleAsync(memberRoleName);
            var dataToExport = new List<MemberBalanceViewModel>();
            if (usersInMemberRole != null)
            {
                foreach (var user in usersInMemberRole)
                {
                    var userProfile = await _context.UserProfile.FirstOrDefaultAsync(up => up.UserId == user.Id);
                    if (userProfile != null && userProfile.IsBillingContact)
                    {
                        string fullName;
                        if (!string.IsNullOrWhiteSpace(userProfile.LastName) && !string.IsNullOrWhiteSpace(userProfile.FirstName))
                        {
                            fullName = $"{userProfile.LastName}, {userProfile.FirstName}";
                        }
                        else if (!string.IsNullOrWhiteSpace(userProfile.LastName))
                        {
                            fullName = userProfile.LastName; // Only LastName available
                        }
                        else if (!string.IsNullOrWhiteSpace(userProfile.FirstName))
                        {
                            fullName = userProfile.FirstName; // Only FirstName available
                        }
                        else
                        {
                            fullName = user.UserName ?? "N/A"; // Fallback to UserName
                        }
                        decimal totalChargesFromInvoices = await _context.Invoices
                            .Where(i => i.UserID == user.Id && i.Status != InvoiceStatus.Cancelled)
                            .SumAsync(i => i.AmountDue);
                        decimal totalAmountPaidOnInvoices = await _context.Invoices
                            .Where(i => i.UserID == user.Id && i.Status != InvoiceStatus.Cancelled)
                            .SumAsync(i => i.AmountPaid);
                        decimal currentBalance = totalChargesFromInvoices - totalAmountPaidOnInvoices;
                        decimal userCreditBalance = await _context.UserCredits
                            .Where(uc => uc.UserID == user.Id && !uc.IsApplied && !uc.IsVoided)
                            .SumAsync(uc => uc.Amount);
                        var memberVm = new MemberBalanceViewModel
                        {
                            UserId = user.Id,
                            FullName = fullName,
                            Email = user.Email ?? "N/A",
                            CurrentBalance = currentBalance,
                            CreditBalance = userCreditBalance
                        };
                        bool effectiveShowOnlyOutstanding = showOnlyOutstanding ?? ShowOnlyOutstanding; // Use model's ShowOnlyOutstanding if parameter is null
                        if (effectiveShowOnlyOutstanding && memberVm.CurrentBalance <= 0 && memberVm.CreditBalance <= 0)
                        {
                            continue;
                        }
                        dataToExport.Add(memberVm);
                    }
                }
            }
            string currentSortOrder = sortOrder ?? CurrentSort ?? "name_asc"; // Default or use passed/model sortOrder
            dataToExport = currentSortOrder switch
            {
                "name_desc" => [.. dataToExport.OrderByDescending(s => s.FullName)],
                "name_asc" => [.. dataToExport.OrderBy(s => s.FullName)],
                "email_desc" => [.. dataToExport.OrderByDescending(s => s.Email)],
                "email_asc" => [.. dataToExport.OrderBy(s => s.Email)],
                "balance_desc" => [.. dataToExport.OrderByDescending(s => s.CurrentBalance)],
                "balance_asc" => [.. dataToExport.OrderBy(s => s.CurrentBalance)],
                "credit_desc" => [.. dataToExport.OrderByDescending(s => s.CreditBalance)],
                "credit_asc" => [.. dataToExport.OrderBy(s => s.CreditBalance)],
                _ => [.. dataToExport.OrderBy(s => s.FullName)],
            };
            _logger.LogInformation("Data prepared for CSV export. Count: {Count}", dataToExport.Count);
            var sb = new StringBuilder();
            sb.AppendLine("\"Full Name\",\"Email\",\"Current Balance\",\"Credit Balance\"");
            foreach (var memberVm in dataToExport)
            {
                sb.AppendLine($"\"{EscapeCsvField(memberVm.FullName)}\",\"{EscapeCsvField(memberVm.Email)}\",{memberVm.CurrentBalance.ToString("F2")},{memberVm.CreditBalance.ToString("F2")}");
            }
            _logger.LogInformation("CSV string generated. Length: {Length}", sb.Length);
            byte[] csvBytes = Encoding.UTF8.GetBytes(sb.ToString());
            return File(csvBytes, "text/csv", $"member_balances_export_{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
        }
        private static string EscapeCsvField(string? field)
        {
            if (string.IsNullOrEmpty(field))
                return string.Empty;
            return field.Replace("\"", "\"\"");
        }
        public async Task<IActionResult> OnPostBulkApplyLateFeesAsync()
        {
            _logger.LogInformation("OnPostBulkApplyLateFeesAsync START - Attempting to apply late fees to all eligible members.");
            int processedCount = 0;
            int successCount = 0;
            int skippedNoOutstandingBalance = 0;
            int skippedRecentFeeExists = 0;
            // int skippedNotBillingContact = 0; // Removed as per plan
            int errorCount = 0;
            var detailedErrorMessages = new List<string>();
            var successMessages = new List<string>();
            var memberRoleName = "Member";
            var allUsersInMemberRole = await _userManager.GetUsersInRoleAsync(memberRoleName);
            if (allUsersInMemberRole == null || allUsersInMemberRole.Count == 0)
            {
                _logger.LogWarning("OnPostBulkApplyLateFeesAsync: No users found in '{MemberRoleName}' role to begin processing.", memberRoleName);
                TempData["WarningMessage"] = "No users found in the member role to process.";
                return RedirectToPage(new { sortOrder = CurrentSort, showOnlyOutstanding = ShowOnlyOutstanding });
            }
            _logger.LogInformation("Fetched {AllUsersCount} users in member role. Filtering for billing contacts.", allUsersInMemberRole.Count);
            var billingContactsToProcess = new List<IdentityUser>();
            foreach (var user in allUsersInMemberRole)
            {
                var userProfile = await _context.UserProfile.FirstOrDefaultAsync(up => up.UserId == user.Id);
                if (userProfile != null && userProfile.IsBillingContact)
                {
                    billingContactsToProcess.Add(user);
                }
            }
            _logger.LogInformation("Found {BillingContactsCount} billing contacts. Starting late fee application process.", billingContactsToProcess.Count); // Refined log
            if (billingContactsToProcess.Count == 0)
            {
                TempData["WarningMessage"] = "No billing contacts found among users in the member role to process for late fees.";
                return RedirectToPage(new { sortOrder = CurrentSort, showOnlyOutstanding = ShowOnlyOutstanding });
            }
            processedCount = billingContactsToProcess.Count; // Number of users for whom fee application will be attempted
            foreach (var user in billingContactsToProcess)
            {
                LateFeeApplicationResult result;
                try
                {
                    // Pass user.UserName to avoid another DB hit if user is already fetched by _userManager
                    result = await ApplyLateFeeToUserAsync(user.Id, user.UserName);
                }
                catch (Exception ex) // Catch unexpected errors from the helper or surrounding logic
                {
                    errorCount++;
                    string errorMsg = $"Critical error during bulk processing for user {user.UserName ?? user.Id}: {ex.Message}";
                    detailedErrorMessages.Add(errorMsg);
                    _logger.LogError(ex, "Critical error in OnPostBulkApplyLateFeesAsync loop for UserID {UserId}", user.Id);
                    continue; // Skip to next user
                }
                if (result.Success)
                {
                    successCount++;
                    successMessages.Add(result.Message);
                    // Logging is done within ApplyLateFeeToUserAsync for success
                }
                else
                {
                    // Update counters based on result type/message
                    // Note: ApplyLateFeeToUserAsync now returns more specific results, so we can categorize skips better.
                    // Logging of these skips/errors is also done within ApplyLateFeeToUserAsync.
                    // A "not found" or "not a billing contact" message here is unexpected due to pre-filtering.
                    if (result.Message.Contains("not found") || result.Message.Contains("not a billing contact"))
                    {
                        errorCount++;
                        detailedErrorMessages.Add($"UserID: {result.UserId}, Name: {result.UserName} - Unexpectedly failed pre-filter checks: {result.Message}");
                        _logger.LogWarning("Unexpected issue for UserID {UserId} ({UserName}) after pre-filtering: {ResultMessage}", result.UserId, result.UserName, result.Message);
                    }
                    else if (result.Message.Contains("no outstanding balance")) { skippedNoOutstandingBalance++; }
                    else if (result.Message.Contains("recent late fee")) { skippedRecentFeeExists++; }
                    else // General error from ApplyLateFeeToUserAsync or other unexpected non-success
                    {
                        errorCount++;
                        detailedErrorMessages.Add($"UserID: {result.UserId}, Name: {result.UserName} - {result.Message}");
                    }
                }
            }
            // Note: processedCount is set before the loop based on billingContactsToProcess.Count
            _logger.LogInformation("OnPostBulkApplyLateFeesAsync COMPLETE. Billing Contacts Targeted: {ProcessedCount}, Successful Applications: {SuccessCount}, Skipped (No Balance): {SkippedNoBalance}, Skipped (Recent Fee): {SkippedRecentFee}, Errors: {ErrorCount}",
                processedCount, successCount, skippedNoOutstandingBalance, skippedRecentFeeExists, errorCount);
            var summaryMessage = new StringBuilder();
            summaryMessage.AppendLine($"Bulk late fee process summary:");
            summaryMessage.AppendLine($"- Billing contacts targeted for processing: {processedCount}");
            summaryMessage.AppendLine($"- Late fees successfully applied: {successCount}");
            summaryMessage.AppendLine($"- Skipped (No outstanding balance): {skippedNoOutstandingBalance}");
            summaryMessage.AppendLine($"- Skipped (Recent fee already exists): {skippedRecentFeeExists}");
            summaryMessage.AppendLine($"- Errors encountered (includes unexpected issues or profile mismatches): {errorCount}");
            if (successMessages.Count > 0)
            {
                summaryMessage.AppendLine("\nSuccessful applications (first 5):");
                foreach (var msg in successMessages.Take(5))
                {
                    summaryMessage.AppendLine($"- {msg}");
                }
                if (successMessages.Count > 5) summaryMessage.AppendLine($"...and {successMessages.Count - 5} more.");
            }
            if (detailedErrorMessages.Count > 0) // CA1860
            {
                summaryMessage.AppendLine("\nError details (first 5):");
                foreach (var err in detailedErrorMessages.Take(5))
                {
                    summaryMessage.AppendLine($"- {err}");
                }
                if (detailedErrorMessages.Count > 5) summaryMessage.AppendLine($"...and {detailedErrorMessages.Count - 5} more errors.");
            }
            if (errorCount > 0)
            {
                TempData["ErrorMessage"] = summaryMessage.ToString();
            }
            else if (successCount > 0 || processedCount > 0 || skippedNoOutstandingBalance > 0 || skippedRecentFeeExists > 0)
            {
                TempData["StatusMessage"] = summaryMessage.ToString();
            }
            else // This case means processedCount was 0 (already handled) or no successes, no skips, no errors.
            {
                TempData["WarningMessage"] = "Bulk late fee process ran, but no fees were applied or applicable to the targeted billing contacts.";
            }
            return RedirectToPage(new { sortOrder = CurrentSort, showOnlyOutstanding = ShowOnlyOutstanding });
        }
    }
}
