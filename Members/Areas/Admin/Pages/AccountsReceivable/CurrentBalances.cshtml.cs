using Members.Data;
using Members.Models; // Assuming UserProfile, Invoice, Payment are here
using Members.Services; // Add this using statement
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text; // Added for StringBuilder and Encoding

namespace Members.Areas.Admin.Pages.AccountsReceivable
{
    [Authorize(Roles = "Admin,Manager")] // Or your specific admin/manager roles
    public class CurrentBalancesModel(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager,
        ILogger<CurrentBalancesModel> logger,
        Microsoft.AspNetCore.Identity.UI.Services.IEmailSender emailSender,
        ITaskManagementService taskService) : PageModel // Add task service parameter
    {
        private readonly ApplicationDbContext _context = context;
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly ILogger<CurrentBalancesModel> _logger = logger;
        private readonly Microsoft.AspNetCore.Identity.UI.Services.IEmailSender _emailSender = emailSender;
        private readonly ITaskManagementService _taskService = taskService; // Add this field
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
            public static LateFeeApplicationResult SkippedNoOverdueInvoice(string userName, string userId) =>
                new() { Success = false, UserId = userId, UserName = userName, Message = $"User {userName} (ID: {userId}) has an outstanding balance but no overdue invoices. No late fee applied." };
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

            var latestOverdueInvoice = await _context.Invoices
                .Where(i => i.UserID == userId &&
                            //i.Type == InvoiceType.Dues &&
                            i.Status != InvoiceStatus.Paid &&
                            i.Status != InvoiceStatus.Cancelled &&
                            i.DueDate < DateTime.Today)
                .OrderByDescending(i => i.DueDate)
                .FirstOrDefaultAsync();

            bool skipForRecentFee = false;
            if (latestOverdueInvoice != null)
            {
                // Check if a late fee specifically for this overdue invoice was recently applied
                string expectedDescPart = $"INV-{latestOverdueInvoice.InvoiceID:D5}";
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
                // If there's no specific overdue invoice, check if ANY type of late fee was applied recently.
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

            if (latestOverdueInvoice == null)
            {
                _logger.LogInformation("ApplyLateFeeToUserAsync: User {UserName} (ID: {UserId}) has an outstanding balance but no overdue invoices. No late fee applied.", userNameForDisplay, userId);
                return LateFeeApplicationResult.SkippedNoOverdueInvoice(userNameForDisplay, userId);
            }

            // If we reach here, latestOverdueInvoice is NOT null, and no recent fee for it exists.
            // Proceed to calculate and apply the late fee.
            decimal fivePercentOfDues = latestOverdueInvoice.AmountDue * 0.05m;
            decimal lateFeeAmount = Math.Max(25.00m, fivePercentOfDues);
            string feeCalculationDescription = $"Late fee on overdue INV-{latestOverdueInvoice.InvoiceID:D5} ({latestOverdueInvoice.AmountDue:C} due {latestOverdueInvoice.DueDate:yyyy-MM-dd}).";

            var lateFeeInvoice = new Invoice
            {
                UserID = userId,
                InvoiceDate = DateTime.Today,
                DueDate = DateTime.Today.AddDays(15), // Standard due date for late fees (15 days)
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

            if (availableCredits.Count > 0)
            {
                foreach (var credit in availableCredits)
                {
                    if (lateFeeInvoice.AmountPaid >= lateFeeInvoice.AmountDue) break;

                    decimal amountToApplyFromThisCredit = Math.Min(credit.Amount, lateFeeInvoice.AmountDue - lateFeeInvoice.AmountPaid);
                    if (amountToApplyFromThisCredit <= 0) continue;

                    credit.IsApplied = true;
                    credit.AppliedDate = DateTime.UtcNow;
                    credit.AppliedToInvoiceID = 0; // Placeholder, updated after invoice save
                    credit.ApplicationNotes = (credit.ApplicationNotes ?? "").Trim() + $" Applied {amountToApplyFromThisCredit:C} to Late Fee INV-0. Original credit amount: {credit.Amount:C}.";
                    credit.LastUpdated = DateTime.UtcNow;
                    _context.UserCredits.Update(credit);

                    lateFeeInvoice.AmountPaid += amountToApplyFromThisCredit;
                    creditsAppliedToThisFee += amountToApplyFromThisCredit;
                }
                if (lateFeeInvoice.AmountPaid >= lateFeeInvoice.AmountDue)
                {
                    lateFeeInvoice.Status = InvoiceStatus.Paid;
                    lateFeeInvoice.AmountPaid = lateFeeInvoice.AmountDue; // Ensure it doesn't exceed AmountDue
                }
            }

            await _context.SaveChangesAsync(); // Save Invoice and initial Credit updates

            // Update credits with the actual InvoiceID now that it's generated
            bool neededCreditInvoiceIdUpdate = false;

            // Ensure we only update credits that were just applied in this session (marked with INV-0)
            foreach (var credit in availableCredits.Where(c => c.IsApplied && c.AppliedToInvoiceID == 0 && c.ApplicationNotes != null && c.ApplicationNotes.Contains("to Late Fee INV-0")))
            {
                credit.AppliedToInvoiceID = lateFeeInvoice.InvoiceID; // Set the actual invoice ID
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
            public string FullName { get; set; } = string.Empty; // Used for display in table (LastName, FirstName)
            public string FirstName { get; set; } = string.Empty; // For email salutation
            public string LastName { get; set; } = string.Empty; // For email salutation
            public string Email { get; set; } = string.Empty;
            [DataType(DataType.Currency)]
            public decimal CurrentBalance { get; set; }
            [DataType(DataType.Currency)]
            public decimal CreditBalance { get; set; } = 0;
            public bool HasOutstandingBalance => CurrentBalance > 0;
        }

        public async Task OnGetAsync(string? sortOrder, bool? showOnlyOutstanding) // Made parameters nullable to match typical OnGetAsync patterns if they can be optional
        {
            _logger.LogInformation("OnGetAsync called for CurrentBalancesModel. SortOrder: {SortOrder}, ShowOnlyOutstanding: {ShowFilter}, ReturnedFromUserId: {ReturnedUserId}",
                sortOrder, showOnlyOutstanding, ReturnedFromUserId);

            if (!string.IsNullOrEmpty(ReturnedFromUserId))
            {
                _logger.LogInformation("Returned to CurrentBalances from MyBilling, last viewed user ID: {ReturnedUserId}", ReturnedFromUserId);
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

            if (usersInMemberRole == null || usersInMemberRole.Count == 0)
            {
                _logger.LogWarning("No users found in 'Member' role.");
                MemberBalances = []; // Ensure MemberBalances is initialized even if empty
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
                        fullName = userProfile.LastName;
                    }
                    else if (!string.IsNullOrWhiteSpace(userProfile.FirstName))
                    {
                        fullName = userProfile.FirstName;
                    }
                    else
                    {
                        fullName = user.UserName ?? "N/A";
                    }

                    _logger.LogInformation("Calculating balance for: {userUserName} (ID: {userId})", user.UserName, user.Id);

                    var userInvoices = await _context.Invoices
                        .Where(i => i.UserID == user.Id)
                        .Select(i => new { i.InvoiceID, i.AmountDue, i.AmountPaid, i.Status })
                        .ToListAsync();

                    foreach (var inv in userInvoices)
                    {
                        _logger.LogInformation("User {UserName} - Invoice Detail: ID={InvoiceID}, AmountDue={AmountDue}, AmountPaid={AmountPaid}, Status={Status}", user.UserName, inv.InvoiceID, inv.AmountDue, inv.AmountPaid, inv.Status);
                    }

                    decimal totalChargesFromInvoices = userInvoices
                        .Where(i => i.Status != InvoiceStatus.Cancelled && i.Status != InvoiceStatus.Draft)
                        .Sum(i => i.AmountDue);
                    _logger.LogInformation("User {UserName} - Total Charges from Invoices (excluding Draft): {TotalCharges}", user.UserName, totalChargesFromInvoices);

                    decimal totalAmountPaidOnInvoices = userInvoices
                        .Where(i => i.Status != InvoiceStatus.Cancelled && i.Status != InvoiceStatus.Draft)
                        .Sum(i => i.AmountPaid);
                    _logger.LogInformation("User {UserName} - Total Amount Paid (from Invoices, excluding Draft): {TotalAmountPaid}", user.UserName, totalAmountPaidOnInvoices);

                    decimal currentBalance = totalChargesFromInvoices - totalAmountPaidOnInvoices;
                    _logger.LogInformation("User {UserName} - Calculated Current Balance (Charges - Invoice.AmountPaid): {CurrentBalance}", user.UserName, currentBalance);

                    decimal unappliedCredits = await _context.UserCredits
                        .Where(uc => uc.UserID == user.Id && !uc.IsApplied && !uc.IsVoided)
                        .SumAsync(uc => uc.Amount);
                    _logger.LogInformation("User {userUserName} - Fetched Unapplied Credit Balance: {unappliedCredits}", user.UserName, unappliedCredits);

                    var memberBalance = new MemberBalanceViewModel
                    {
                        UserId = user.Id,
                        FullName = fullName,
                        Email = user.Email ?? "N/A",
                        FirstName = userProfile.FirstName ?? string.Empty,
                        LastName = userProfile.LastName ?? string.Empty,
                        CurrentBalance = currentBalance,
                        CreditBalance = unappliedCredits
                    };

                    if (ShowOnlyOutstanding && memberBalance.CurrentBalance <= 0 && memberBalance.CreditBalance <= 0) // Also consider credit balance for this filter
                    {
                        continue;
                    }
                    memberBalancesTemp.Add(memberBalance);
                }
            }

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
                _ => [.. memberBalancesTemp.OrderBy(s => s.FullName)],
            };
            _logger.LogInformation("Populated MemberBalances. Count: {MemberBalancesCount}", MemberBalances.Count);

            TotalCurrentBalance = MemberBalances.Sum(mb => mb.CurrentBalance);
            TotalCreditBalance = MemberBalances.Sum(mb => mb.CreditBalance);
            _logger.LogInformation("Calculated totals: TotalCurrentBalance = {TotalCurrentBalance}, TotalCreditBalance = {TotalCreditBalance}", TotalCurrentBalance, TotalCreditBalance);
        }

        public async Task<IActionResult> OnGetExportCsvAsync(string? sortOrder, bool? showOnlyOutstanding)
        {
            _logger.LogInformation("[CurrentBalances Export CSV] Handler started. Received sortOrder: '{SortOrder}', showOnlyOutstanding: '{ShowOnlyOutstanding}'", sortOrder, showOnlyOutstanding);

            try
            {
                var memberRoleName = "Member";
                var usersInMemberRole = await _userManager.GetUsersInRoleAsync(memberRoleName);
                _logger.LogInformation("[CurrentBalances Export CSV] Found {UserCount} users in role '{MemberRoleName}'.", usersInMemberRole?.Count ?? 0, memberRoleName);

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
                                fullName = userProfile.LastName;
                            }
                            else if (!string.IsNullOrWhiteSpace(userProfile.FirstName))
                            {
                                fullName = userProfile.FirstName;
                            }
                            else
                            {
                                fullName = user.UserName ?? "N/A";
                            }

                            decimal totalChargesFromInvoices = await _context.Invoices
                                .Where(i => i.UserID == user.Id &&
                                            i.Status != InvoiceStatus.Cancelled &&
                                            i.Status != InvoiceStatus.Draft)
                                .SumAsync(i => i.AmountDue);
                            decimal totalAmountPaidOnInvoices = await _context.Invoices
                                .Where(i => i.UserID == user.Id &&
                                            i.Status != InvoiceStatus.Cancelled &&
                                            i.Status != InvoiceStatus.Draft)
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
                                FirstName = userProfile.FirstName ?? string.Empty,
                                LastName = userProfile.LastName ?? string.Empty,
                                CurrentBalance = currentBalance,
                                CreditBalance = userCreditBalance
                            };

                            bool effectiveShowOnlyOutstanding = showOnlyOutstanding ?? ShowOnlyOutstanding; // Use page's ShowOnlyOutstanding if parameter is null
                            _logger.LogTrace("[CurrentBalances Export CSV] Processing user {UserName}. Balance: {CurrentBalance}, Credit: {UserCreditBalance}. EffectiveShowOutstanding: {EffectiveShowOutstanding}", user.UserName, currentBalance, userCreditBalance, effectiveShowOnlyOutstanding);

                            if (effectiveShowOnlyOutstanding && memberVm.CurrentBalance <= 0 && memberVm.CreditBalance <= 0)
                            {
                                _logger.LogTrace("[CurrentBalances Export CSV] Skipping user {UserName} due to ShowOnlyOutstanding filter.", user.UserName);
                                continue;
                            }
                            dataToExport.Add(memberVm);
                        }
                        else
                        {
                            _logger.LogTrace("[CurrentBalances Export CSV] Skipping user {UserName} as they are not a billing contact or profile is missing.", user.UserName);
                        }
                    }
                }
                _logger.LogInformation("[CurrentBalances Export CSV] Total users processed for potential export: {ProcessedCount}. Users matching criteria for export: {DataToExportCount}", usersInMemberRole?.Count ?? 0, dataToExport.Count);


                if (dataToExport.Count == 0)
                {
                    _logger.LogWarning("[CurrentBalances Export CSV] No data to export after filtering. Returning empty file or message.");
                }

                string currentSortOrder = sortOrder ?? CurrentSort ?? "name_asc";
                _logger.LogInformation("[CurrentBalances Export CSV] Applying sort order: '{CurrentSortOrder}'.", currentSortOrder);

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
                _logger.LogInformation("[CurrentBalances Export CSV] Data sorted. Final count for CSV: {Count}", dataToExport.Count);

                var sb = new StringBuilder();
                sb.AppendLine("\"Full Name\",\"Email\",\"Current Balance\",\"Credit Balance\"");
                foreach (var memberVm in dataToExport)
                {
                    sb.AppendLine($"\"{EscapeCsvField(memberVm.FullName)}\",\"{EscapeCsvField(memberVm.Email)}\",{memberVm.CurrentBalance:F2},{memberVm.CreditBalance:F2}");
                }

                byte[] csvBytes = Encoding.UTF8.GetBytes(sb.ToString());
                string fileName = $"member_balances_export_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
                _logger.LogInformation("[CurrentBalances Export CSV] CSV string generated. Byte length: {Length}. Filename: {FileName}", csvBytes.Length, fileName);

                return File(csvBytes, "text/csv", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CurrentBalances Export CSV] CRITICAL ERROR during CSV export generation. SortOrder: {SortOrder}, ShowOutstanding: {ShowOutstanding}", sortOrder, showOnlyOutstanding);
                TempData["ErrorMessage"] = "A critical error occurred while generating the CSV export for Admin Balances. Please check the logs.";
                return RedirectToPage(new { sortOrder = CurrentSort, showOnlyOutstanding = ShowOnlyOutstanding });
            }
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
            int skippedNoOverdueDues = 0;
            int errorCount = 0;
            var detailedErrorMessages = new List<string>();
            var successMessages = new List<string>();

            // Fetch all users in the 'Member' role
            var memberRoleName = "Member";
            var allUsersInMemberRole = await _userManager.GetUsersInRoleAsync(memberRoleName);

            // If no users found in the 'Member' role, log and return early
            if (allUsersInMemberRole == null || allUsersInMemberRole.Count == 0)
            {
                _logger.LogWarning("OnPostBulkApplyLateFeesAsync: No users found in '{MemberRoleName}' role to begin processing.", memberRoleName);
                TempData["WarningMessage"] = "No users found in the member role to process.";
                return RedirectToPage(new { sortOrder = CurrentSort, showOnlyOutstanding = ShowOnlyOutstanding });
            }

            // Filter for billing contacts
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
            _logger.LogInformation("Found {BillingContactsCount} billing contacts. Starting late fee application process.", billingContactsToProcess.Count);

            // If no billing contacts found, log and return early
            if (billingContactsToProcess.Count == 0)
            {
                TempData["WarningMessage"] = "No billing contacts found among users in the member role to process for late fees.";
                return RedirectToPage(new { sortOrder = CurrentSort, showOnlyOutstanding = ShowOnlyOutstanding });
            }

            // Initialize counts
            processedCount = billingContactsToProcess.Count;

            foreach (var user in billingContactsToProcess)
            {
                // Apply late fee to each billing contact
                LateFeeApplicationResult result;
                try
                {
                    result = await ApplyLateFeeToUserAsync(user.Id, user.UserName);
                }
                catch (Exception ex)
                {
                    errorCount++;
                    string errorMsg = $"Critical error during bulk processing for user {user.UserName ?? user.Id}: {ex.Message}";
                    detailedErrorMessages.Add(errorMsg);
                    _logger.LogError(ex, "Critical error in OnPostBulkApplyLateFeesAsync loop for UserID {UserId}", user.Id);
                    continue;
                }

                if (result.Success)
                {
                    successCount++;
                    successMessages.Add(result.Message);
                }
                else
                {
                    if (result.Message.Contains("not found") || result.Message.Contains("not a billing contact")) // Should ideally not happen due to pre-filter
                    {
                        errorCount++;
                        detailedErrorMessages.Add($"UserID: {result.UserId}, Name: {result.UserName} - Unexpectedly failed pre-filter checks: {result.Message}");
                        _logger.LogWarning("Unexpected issue for UserID {UserId} ({UserName}) after pre-filtering: {ResultMessage}", result.UserId, result.UserName, result.Message);
                    }
                    else if (result.Message.Contains("no outstanding balance")) { skippedNoOutstandingBalance++; }
                    else if (result.Message.Contains("recent late fee")) { skippedRecentFeeExists++; }
                    else if (result.Message.Contains("no overdue invoices")) { skippedNoOverdueDues++; } // Count new skip reason
                    else
                    {
                        errorCount++;
                        detailedErrorMessages.Add($"UserID: {result.UserId}, Name: {result.UserName} - {result.Message}");
                    }
                }
            }

            _logger.LogInformation("OnPostBulkApplyLateFeesAsync COMPLETE. Billing Contacts Targeted: {ProcessedCount}, Successful Applications: {SuccessCount}, Skipped (No Balance): {SkippedNoBalance}, Skipped (Recent Fee): {SkippedRecentFee}, Skipped (No Overdue Dues): {SkippedNoOverdueDues}, Errors: {ErrorCount}",
                processedCount, successCount, skippedNoOutstandingBalance, skippedRecentFeeExists, skippedNoOverdueDues, errorCount);

            var summaryMessage = new StringBuilder();
            summaryMessage.AppendLine($"Bulk late fee process summary:");
            summaryMessage.AppendLine($"- Billing contacts targeted for processing: {processedCount}");
            summaryMessage.AppendLine($"- Late fees successfully applied: {successCount}");
            summaryMessage.AppendLine($"- Skipped (No outstanding balance): {skippedNoOutstandingBalance}");
            summaryMessage.AppendLine($"- Skipped (Recent fee already exists): {skippedRecentFeeExists}");
            summaryMessage.AppendLine($"- Skipped (No overdue invoice): {skippedNoOverdueDues}"); // Add to summary
            summaryMessage.AppendLine($"- Errors encountered: {errorCount}");

            if (successMessages.Count > 0)
            {
                summaryMessage.AppendLine("\nSuccessful applications (first 5):");
                successMessages.Take(5).ToList().ForEach(m => summaryMessage.AppendLine($"- {m}"));
                if (successMessages.Count > 5) summaryMessage.AppendLine($"...and {successMessages.Count - 5} more.");
            }
            if (detailedErrorMessages.Count > 0)
            {
                summaryMessage.AppendLine("\nError details (first 5):");
                detailedErrorMessages.Take(5).ToList().ForEach(e => summaryMessage.AppendLine($"- {e}"));
                if (detailedErrorMessages.Count > 5) summaryMessage.AppendLine($"...and {detailedErrorMessages.Count - 5} more errors.");
            }

            if (errorCount > 0)
            {
                TempData["ErrorMessage"] = summaryMessage.ToString();
            }
            else if (successCount > 0 || skippedNoOutstandingBalance > 0 || skippedRecentFeeExists > 0 || skippedNoOverdueDues > 0) // Include new skip reason
            {
                TempData["StatusMessage"] = summaryMessage.ToString();
            }
            else
            {
                TempData["WarningMessage"] = "Bulk late fee process ran, but no fees were applied or applicable to the targeted billing contacts.";
            }

            // Mark task as completed if any late fees were successfully applied
            if (successCount > 0)
            {
                try
                {

                    // In OnPostBulkApplyLateFeesAsync method, change this line:
                    await _taskService.MarkTaskCompletedAutomaticallyAsync("ApplyLateFees",
                        $"Applied {successCount} late fees automatically");                    
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to mark BulkApplyLateFees task as completed");
                }
            }

            return RedirectToPage(new { sortOrder = CurrentSort, showOnlyOutstanding = ShowOnlyOutstanding });
        }

        public async Task<IActionResult> OnPostEmailBalanceNotificationsAsync()
        {
            _logger.LogInformation("OnPostEmailBalanceNotificationsAsync START - Attempting to send balance notifications.");

            var memberBalancesTemp = new List<MemberBalanceViewModel>();
            var memberRoleName = "Member";
            var usersInMemberRole = await _userManager.GetUsersInRoleAsync(memberRoleName);

            if (usersInMemberRole != null && usersInMemberRole.Any())
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
                            fullName = userProfile.LastName;
                        }
                        else if (!string.IsNullOrWhiteSpace(userProfile.FirstName))
                        {
                            fullName = userProfile.FirstName;
                        }
                        else
                        {
                            fullName = user.UserName ?? "N/A";
                        }

                        decimal totalChargesFromInvoices = await _context.Invoices
                            .Where(i => i.UserID == user.Id &&
                                        i.Status != InvoiceStatus.Cancelled &&
                                        i.Status != InvoiceStatus.Draft)
                            .SumAsync(i => i.AmountDue);
                        decimal totalAmountPaidOnInvoices = await _context.Invoices
                            .Where(i => i.UserID == user.Id &&
                                        i.Status != InvoiceStatus.Cancelled &&
                                        i.Status != InvoiceStatus.Draft)
                            .SumAsync(i => i.AmountPaid);
                        decimal currentBalance = totalChargesFromInvoices - totalAmountPaidOnInvoices;
                        decimal unappliedCredits = await _context.UserCredits
                            .Where(uc => uc.UserID == user.Id && !uc.IsApplied && !uc.IsVoided)
                            .SumAsync(uc => uc.Amount);

                        memberBalancesTemp.Add(new MemberBalanceViewModel
                        {
                            UserId = user.Id,
                            FullName = fullName,
                            Email = user.Email ?? "N/A",
                            FirstName = userProfile.FirstName ?? string.Empty,
                            LastName = userProfile.LastName ?? string.Empty,
                            CurrentBalance = currentBalance,
                            CreditBalance = unappliedCredits
                        });
                    }
                }
            }

            var usersToEmail = memberBalancesTemp.Where(mb => mb.CurrentBalance > 0 || mb.CreditBalance > 0).ToList();

            if (usersToEmail.Count == 0)
            {
                _logger.LogWarning("OnPostEmailBalanceNotificationsAsync: No members found with a balance to notify after re-fetching data.");
                TempData["WarningMessage"] = "No members found with a balance to notify.";
                return RedirectToPage(new { sortOrder = CurrentSort, showOnlyOutstanding = ShowOnlyOutstanding });
            }

            int emailsSentCount = 0;
            var emailErrors = new List<string>();
            var siteName = Environment.GetEnvironmentVariable("SITE_NAME_OAKS_VILLAGE") ?? "Our Community";
            var today = DateTime.Today;
            var dueDate = new DateTime(today.Year, today.Month, 1).AddMonths(1);

            foreach (var member in usersToEmail)
            {
                if (string.IsNullOrEmpty(member.Email) || member.Email == "N/A")
                {
                    _logger.LogWarning("Skipping email for {memberFullName} (User ID: {memberUserId}) due to missing or invalid email address.", member.FullName, member.UserId);
                    emailErrors.Add($"Skipped {member.FullName}: Missing email.");
                    continue;
                }

                string subject;
                string body;

                if (member.CurrentBalance > 0)
                {
                    subject = $"Important: Your {siteName} Account Balance Due";
                    body = $@"
                        <!DOCTYPE html>
                        <html lang=""en"">
                        <head>
                            <meta charset=""UTF-8"">
                            <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
                            <title>{subject}</title>
                        </head>
                        <body style=""font-family: sans-serif; line-height: 1.6; margin: 20px;"">
                            <p style=""margin-bottom: 1em;"">Dear {member.FirstName} {member.LastName},</p>
                            <p style=""margin-bottom: 1em;"">This is a notification regarding your account balance with {siteName}.</p>
                            <p style=""margin-bottom: 1em;"">Your current outstanding balance is: <strong>{member.CurrentBalance:C}</strong>.</p>
                            <p style=""margin-bottom: 1em;"">This amount is due by <strong>{dueDate:MMMM dd, yyyy}</strong>.</p>
                            <p style=""margin-bottom: 1em;"">You can view your detailed billing history and make payments by logging into your account at <a href=""https://{Request.Host}/Member/MyBilling"" style=""color: #007bff; text-decoration: none;"">https://{Request.Host}/Member/MyBilling</a>.</p>
                            {(member.CreditBalance > 0 ? $"<p style=\"margin-bottom: 1em;\">You also have an available credit balance of <strong>{member.CreditBalance:C}</strong>. This credit will be automatically applied to new charges.</p>" : "")}
                            <p style=""margin-bottom: 0;"">Sincerely,</p>
                            <p style=""margin-top: 0;"">The {siteName} Team</p>
                        </body>
                        </html>";
                }
                else if (member.CreditBalance > 0)
                {
                    subject = $"Your {siteName} Account Credit Balance";
                    body = $@"
                        <!DOCTYPE html>
                        <html lang=""en"">
                        <head>
                            <meta charset=""UTF-8"">
                            <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
                            <title>{subject}</title>
                        </head>
                        <body style=""font-family: sans-serif; line-height: 1.6; margin: 20px;"">
                            <p style=""margin-bottom: 1em;"">Dear {member.FirstName} {member.LastName},</p>
                            <p style=""margin-bottom: 1em;"">This is a notification regarding your account balance with {siteName}.</p>
                            <p style=""margin-bottom: 1em;"">You currently have a credit balance of: <strong>{member.CreditBalance:C}</strong>.</p>
                            <p style=""margin-bottom: 1em;"">This credit will be automatically applied to any future charges on your account.</p>
                            <p style=""margin-bottom: 1em;"">You can view your detailed billing history by logging into your account at <a href=""https://{Request.Host}/Member/MyBilling"" style=""color: #007bff; text-decoration: none;"">https://{Request.Host}/Member/MyBilling</a>.</p>
                            <p style=""margin-bottom: 0;"">Sincerely,</p>
                            <p style=""margin-top: 0;"">The {siteName} Team</p>
                        </body>
                        </html>";
                }
                else
                {
                    _logger.LogInformation("Skipping email for {memberFullName} (User ID: {memberUserId}) as they have a zero balance and no credit.", member.FullName, member.UserId);
                    continue;
                }

                try
                {
                    await _emailSender.SendEmailAsync(member.Email, subject, body);
                    emailsSentCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send balance notification email to {memberEmail} for user {memberFullName} (ID: {memberUserId}).", member.Email, member.FullName, member.UserId);
                    emailErrors.Add($"Failed for {member.FullName} ({member.Email}): {ex.Message}");
                }
            }

            var statusMessage = new StringBuilder();
            statusMessage.AppendLine($"Balance notification process completed.");
            statusMessage.AppendLine($"- Emails successfully sent: {emailsSentCount}");
            if (emailErrors.Count != 0)
            {
                statusMessage.AppendLine($"- Errors encountered: {emailErrors.Count}");
                emailErrors.Take(5).ToList().ForEach(err => statusMessage.AppendLine($"  - {err}"));
                if (emailErrors.Count > 5)
                {
                    statusMessage.AppendLine($"  - ...and {emailErrors.Count - 5} more errors (check logs).");
                }
                TempData["ErrorMessage"] = statusMessage.ToString();
            }
            else
            {
                TempData["StatusMessage"] = statusMessage.ToString();
            }

            // Mark task as completed if any emails were sent
            if (emailsSentCount > 0)
            {
                try
                {                    
                    await _taskService.MarkTaskCompletedAutomaticallyAsync("EmailLateFeeWarnings",
                        $"Sent {emailsSentCount} late fee warning emails automatically");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to mark EmailBalanceNotifications task as completed");
                }
            }

            _logger.LogInformation("OnPostEmailBalanceNotificationsAsync COMPLETE. Emails Sent: {EmailsSentCount}, Errors: {EmailErrorsCount}", emailsSentCount, emailErrors.Count);
            return RedirectToPage(new { sortOrder = CurrentSort, showOnlyOutstanding = ShowOnlyOutstanding });
        }

        public async Task<IActionResult> OnPostEmailLateFeeWarningsAsync()
        {
            _logger.LogInformation("OnPostEmailLateFeeWarningsAsync START - Attempting to send late fee warning emails.");

            var today = DateTime.Today;
            if (today.Day > 5)
            {
                _logger.LogWarning("OnPostEmailLateFeeWarningsAsync: Attempted to send warnings after the 5th of the month (Day: {TodayDay}). Action aborted.", today.Day);
                TempData["WarningMessage"] = "Late fee warnings should typically be sent between the 1st and 5th of the month. No emails were sent.";
                return RedirectToPage(new { sortOrder = CurrentSort, showOnlyOutstanding = ShowOnlyOutstanding });
            }
            // No specific check for "on or after the 1st" as the button would typically be used 1st-5th.
            // If clicked before the 1st, it might not be harmful, just early. The main guard is for *after* the 5th.

            var memberBalancesTemp = new List<MemberBalanceViewModel>();
            var memberRoleName = "Member";
            var usersInMemberRole = await _userManager.GetUsersInRoleAsync(memberRoleName);

            if (usersInMemberRole != null && usersInMemberRole.Any())
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
                            fullName = userProfile.LastName;
                        }
                        else if (!string.IsNullOrWhiteSpace(userProfile.FirstName))
                        {
                            fullName = userProfile.FirstName;
                        }
                        else
                        {
                            fullName = user.UserName ?? "N/A";
                        }

                        decimal totalChargesFromInvoices = await _context.Invoices
                            .Where(i => i.UserID == user.Id &&
                                        i.Status != InvoiceStatus.Cancelled &&
                                        i.Status != InvoiceStatus.Draft)
                            .SumAsync(i => i.AmountDue);
                        decimal totalAmountPaidOnInvoices = await _context.Invoices
                            .Where(i => i.UserID == user.Id &&
                                        i.Status != InvoiceStatus.Cancelled &&
                                        i.Status != InvoiceStatus.Draft)
                            .SumAsync(i => i.AmountPaid);
                        decimal currentBalance = totalChargesFromInvoices - totalAmountPaidOnInvoices;

                        // Credit balance is not strictly needed for this warning email logic, but keeping it for consistency with MemberBalanceViewModel
                        decimal unappliedCredits = await _context.UserCredits
                            .Where(uc => uc.UserID == user.Id && !uc.IsApplied && !uc.IsVoided)
                            .SumAsync(uc => uc.Amount);

                        memberBalancesTemp.Add(new MemberBalanceViewModel
                        {
                            UserId = user.Id,
                            FullName = fullName,
                            Email = user.Email ?? "N/A",
                            FirstName = userProfile.FirstName ?? string.Empty,
                            LastName = userProfile.LastName ?? string.Empty,
                            CurrentBalance = currentBalance,
                            CreditBalance = unappliedCredits
                        });
                    }
                }
            }

            var usersToEmail = memberBalancesTemp.Where(mb => mb.CurrentBalance > 0).ToList();

            if (usersToEmail.Count == 0)
            {
                _logger.LogInformation("OnPostEmailLateFeeWarningsAsync: No members found with an outstanding balance to notify.");
                TempData["StatusMessage"] = "No members found with an outstanding balance to send late fee warnings to.";
                return RedirectToPage(new { sortOrder = CurrentSort, showOnlyOutstanding = ShowOnlyOutstanding });
            }

            int emailsSentCount = 0;
            var emailErrors = new List<string>();
            var siteName = Environment.GetEnvironmentVariable("SITE_NAME_OAKS_VILLAGE") ?? "Our Community";
            var fifthOfMonth = new DateTime(today.Year, today.Month, 5);

            foreach (var member in usersToEmail)
            {
                if (string.IsNullOrEmpty(member.Email) || member.Email == "N/A")
                {
                    _logger.LogWarning("Skipping late fee warning email for {memberFullName} (User ID: {memberUserId}) due to missing or invalid email address.", member.FullName, member.UserId);
                    emailErrors.Add($"Skipped {member.FullName}: Missing email.");
                    continue;
                }

                string subject = $"Action Required: Upcoming Late Fee for Your {siteName} Account Balance";
                string body = $@"
                    <!DOCTYPE html>
                    <html lang=""en"">
                    <head>
                        <meta charset=""UTF-8"">
                        <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
                        <title>{subject}</title>
                    </head>
                    <body style=""font-family: sans-serif; line-height: 1.6; margin: 20px;"">
                        <p style=""margin-bottom: 1em;"">Dear {member.FirstName} {member.LastName},</p>
                        <p style=""margin-bottom: 1em;"">This is an important reminder regarding your account balance with {siteName}.</p>
                        <p style=""margin-bottom: 1em;"">Your current outstanding balance is: <strong>{member.CurrentBalance:C}</strong>.</p>
                        <p style=""margin-bottom: 1em; color: #dc3545; font-weight: bold;"">
                            Please be advised that if this balance is not paid in full by the end of day on <strong>{fifthOfMonth:MMMM dd, yyyy}</strong>, 
                            a late fee of $25.00 will be applied to your account.
                        </p>
                        <p style=""margin-bottom: 1em;"">
                            To avoid this fee, please make a payment as soon as possible. You can view your detailed billing history and make payments by logging into your account at:
                            <a href=""https://{Request.Host}/Member/MyBilling"" style=""color: #007bff; text-decoration: none;"">https://{Request.Host}/Member/MyBilling</a>.
                        </p>
                        {(member.CreditBalance > 0 ? $"<p style=\"margin-bottom: 1em;\">You also have an available credit balance of <strong>{member.CreditBalance:C}</strong>. This credit will be automatically applied to new charges, which may reduce or cover your outstanding balance.</p>" : "")}
                        <p style=""margin-bottom: 0;"">Sincerely,</p>
                        <p style=""margin-top: 0;"">The {siteName} Team</p>
                    </body>
                    </html>";

                try
                {
                    await _emailSender.SendEmailAsync(member.Email, subject, body);
                    emailsSentCount++;
                    _logger.LogInformation("Late fee warning email sent to {MemberEmail} for user {MemberFullName} (ID: {MemberUserId}). Balance: {CurrentBalance:C}", member.Email, member.FullName, member.UserId, member.CurrentBalance);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send late fee warning email to {memberEmail} for user {memberFullName} (ID: {memberUserId}).", member.Email, member.FullName, member.UserId);
                    emailErrors.Add($"Failed for {member.FullName} ({member.Email}): {ex.Message}");
                }
            }

            var statusMessage = new StringBuilder();
            statusMessage.AppendLine($"Late fee warning email process completed.");
            statusMessage.AppendLine($"- Emails successfully sent to members with outstanding balances: {emailsSentCount}");
            if (emailErrors.Count != 0)
            {
                statusMessage.AppendLine($"- Errors encountered: {emailErrors.Count}");
                emailErrors.Take(5).ToList().ForEach(err => statusMessage.AppendLine($"  - {err}"));
                if (emailErrors.Count > 5)
                {
                    statusMessage.AppendLine($"  - ...and {emailErrors.Count - 5} more errors (check logs).");
                }
                TempData["ErrorMessage"] = statusMessage.ToString();
            }
            else
            {
                TempData["StatusMessage"] = statusMessage.ToString();
            }

            // Mark task as completed if any emails were sent
            if (emailsSentCount > 0)
            {
                try
                {

                    await _taskService.MarkTaskCompletedAutomaticallyAsync("EmailLateFeeWarnings",
                        $"Sent {emailsSentCount} late fee warning emails automatically");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to mark EmailLateFeeWarnings task as completed");
                }
            }

            _logger.LogInformation("OnPostEmailLateFeeWarningsAsync COMPLETE. Emails Sent: {EmailsSentCount}, Errors: {EmailErrorsCount}", emailsSentCount, emailErrors.Count);
            return RedirectToPage(new { sortOrder = CurrentSort, showOnlyOutstanding = ShowOnlyOutstanding });
        }
    }
}