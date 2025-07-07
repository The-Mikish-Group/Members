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

namespace Members.Areas.Admin.Pages.Reporting
{
    [Authorize(Roles = "Admin,Manager")]
    public class CreditRegisterReportModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<CreditRegisterReportModel> _logger;

        public CreditRegisterReportModel(ApplicationDbContext context,
                                         UserManager<IdentityUser> userManager,
                                         ILogger<CreditRegisterReportModel> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        [BindProperty(SupportsGet = true)]
        [DataType(DataType.Date)]
        [Display(Name = "Start Date")]
        public DateTime StartDate { get; set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        [BindProperty(SupportsGet = true)]
        [DataType(DataType.Date)]
        [Display(Name = "End Date")]
        public DateTime EndDate { get; set; } = DateTime.Today;

        public IList<CreditRegisterItemViewModel> ReportData { get; set; } = new List<CreditRegisterItemViewModel>();
        public CreditRegisterSummaryViewModel Totals { get; set; } = new CreditRegisterSummaryViewModel();


        private async Task GenerateReportDataAsync()
        {
            ReportData = new List<CreditRegisterItemViewModel>();
            Totals = new CreditRegisterSummaryViewModel();

            DateTime effectiveStartDate = StartDate.Date;
            DateTime effectiveEndDate = EndDate.Date.AddDays(1).AddTicks(-1);

            var users = await _context.UserProfile.ToDictionaryAsync(up => up.UserId, up => $"{up.FirstName} {up.LastName}".Trim());

            var creditsInPeriod = await _context.UserCredits
                .Where(uc => uc.CreditDate >= effectiveStartDate && uc.CreditDate <= effectiveEndDate)
                .Include(uc => uc.SourcePayment) // Eagerly load source payment
                .OrderBy(uc => uc.CreditDate)
                .ThenBy(uc => uc.UserCreditID)
                .ToListAsync();

            foreach (var credit in creditsInPeriod)
            {
                // Fetch applications for THIS credit first to calculate OriginalAmount
                var applicationsForThisCredit = await _context.CreditApplications // Corrected DbSet name
                    .Where(ca => ca.UserCreditID == credit.UserCreditID)
                    .Include(ca => ca.Invoice)   // Then include the Invoice related to each CreditApplication
                    .OrderBy(ca => ca.ApplicationDate)
                    .ToListAsync();

                decimal sumOfApplications = applicationsForThisCredit.Sum(app => app.AmountApplied);
                decimal calculatedOriginalAmount = credit.Amount + sumOfApplications;

                string customerName = users.TryGetValue(credit.UserID, out var name) ? (string.IsNullOrEmpty(name) ? "N/A" : name) : "N/A";
                string sourceInfo = credit.SourcePaymentID.HasValue
                                    ? $"From Payment P-{credit.SourcePaymentID.Value}"
                                    : "Manual/Other";
                if (credit.SourcePaymentID.HasValue && credit.SourcePayment != null)
                {
                    sourceInfo = $"From Payment P-{credit.SourcePaymentID.Value} (Date: {credit.SourcePayment.PaymentDate:yyyy-MM-dd}, Amt: {credit.SourcePayment.Amount:C})";
                }

                var itemVM = new CreditRegisterItemViewModel
                {
                    CreditId = credit.UserCreditID,
                    CustomerName = customerName,
                    CreditDate = credit.CreditDate,
                    OriginalAmount = calculatedOriginalAmount, // Use calculated value
                    RemainingAmount = credit.Amount,
                    Reason = credit.Reason,
                    SourceInfo = sourceInfo,
                    IsVoided = credit.IsVoided,
                    AppliedApplications = new List<AppliedApplicationViewModel>()
                };

                // Determine Status using calculatedOriginalAmount
                if (credit.IsVoided) itemVM.Status = "Voided";
                else if (credit.IsApplied && credit.Amount <= 0) itemVM.Status = "Fully Applied"; // This implies original was > 0 if IsApplied is true
                else if (calculatedOriginalAmount > 0 && credit.Amount < calculatedOriginalAmount && credit.Amount > 0) itemVM.Status = "Partially Applied";
                else if (calculatedOriginalAmount > 0 && credit.Amount == calculatedOriginalAmount && !credit.IsApplied) itemVM.Status = "Available";
                else if (calculatedOriginalAmount == 0 && credit.Amount == 0 && (credit.IsApplied || applicationsForThisCredit.Any())) itemVM.Status = "Fully Applied (Zero Value)";
                else if (calculatedOriginalAmount == 0 && credit.Amount == 0 && !credit.IsApplied && !applicationsForThisCredit.Any()) itemVM.Status = "Available (Zero Value)";
                else itemVM.Status = "N/A"; // Fallback

                decimal amountAppliedFromThisCreditInLoop = 0; // Renamed to avoid conflict with sumOfApplications
                if (applicationsForThisCredit.Any())
                {
                    foreach (var app in applicationsForThisCredit)
                    {
                        var appVM = new AppliedApplicationViewModel
                        {
                            AppliedToInvoiceId = $"INV-{app.InvoiceID:D5}", // InvoiceID is non-nullable int
                            AppliedToInvoiceDescription = app.Invoice?.Description ?? "(Details N/A)", // Safe navigation for Description
                            AmountApplied = app.AmountApplied,
                            ApplicationDate = app.ApplicationDate
                        };
                        itemVM.AppliedApplications.Add(appVM);
                        amountAppliedFromThisCreditInLoop += app.AmountApplied; // This is same as sumOfApplications if all apps are processed
                    }
                }

                ReportData.Add(itemVM);

                // Update Totals
                Totals.TotalOriginalCreditAmount += calculatedOriginalAmount; // Use calculated value
                Totals.TotalRemainingCreditAmount += credit.Amount; // Current remaining amount
                Totals.TotalAmountAppliedFromListedCredits += sumOfApplications; // Sum of applications from credits listed in report
            }
            _logger.LogInformation("Generated Credit Register data for {StartDate} to {EndDate}. Credit Count: {Count}", StartDate, EndDate, ReportData.Count);
        }

        public async Task OnGetAsync()
        {
            await GenerateReportDataAsync();
        }

        // CSV export will use GenerateReportDataAsync
        public async Task<IActionResult> OnGetExportToCsvAsync()
        {
            await GenerateReportDataAsync();
            var builder = new System.Text.StringBuilder();
            // CSV Headers - designed for flattened structure
            builder.AppendLine("CreditID,CustomerName,CreditDate,Reason,SourceInfo,OriginalAmount,RemainingAmount,CreditStatus,IsVoided,AppliedToInvoiceID,AppliedToInvoiceDesc,AmountAppliedToInvoice,ApplicationDate");

            foreach (var creditItem in ReportData)
            {
                string creditIdCsv = $"UC-{creditItem.CreditId:D5}";
                if (creditItem.AppliedApplications.Any())
                {
                    foreach (var appItem in creditItem.AppliedApplications)
                    {
                        // Repeat credit info for each application line
                        builder.Append($"\"{EscapeCsvField(creditIdCsv)}\",");
                        builder.Append($"\"{EscapeCsvField(creditItem.CustomerName)}\",");
                        builder.Append($"{creditItem.CreditDate:yyyy-MM-dd},");
                        builder.Append($"\"{EscapeCsvField(creditItem.Reason)}\",");
                        builder.Append($"\"{EscapeCsvField(creditItem.SourceInfo)}\",");
                        builder.Append($"{creditItem.OriginalAmount:F2},");
                        builder.Append($"{creditItem.RemainingAmount:F2},");
                        builder.Append($"\"{EscapeCsvField(creditItem.Status)}\",");
                        builder.Append($"{(creditItem.IsVoided ? "Yes" : "No")},"); // Output Yes/No for boolean
                        // Application specific info
                        builder.Append($"\"{EscapeCsvField(appItem.AppliedToInvoiceId)}\",");
                        builder.Append($"\"{EscapeCsvField(appItem.AppliedToInvoiceDescription)}\",");
                        builder.Append($"{appItem.AmountApplied:F2},");
                        builder.AppendLine($"{appItem.ApplicationDate:yyyy-MM-dd}");
                    }
                }
                else
                {
                    // Credit with no applications
                    builder.Append($"\"{EscapeCsvField(creditIdCsv)}\",");
                    builder.Append($"\"{EscapeCsvField(creditItem.CustomerName)}\",");
                    builder.Append($"{creditItem.CreditDate:yyyy-MM-dd},");
                    builder.Append($"\"{EscapeCsvField(creditItem.Reason)}\",");
                    builder.Append($"\"{EscapeCsvField(creditItem.SourceInfo)}\",");
                    builder.Append($"{creditItem.OriginalAmount:F2},");
                    builder.Append($"{creditItem.RemainingAmount:F2},");
                    builder.Append($"\"{EscapeCsvField(creditItem.Status)}\",");
                    builder.Append($"{(creditItem.IsVoided ? "Yes" : "No")},"); // Output Yes/No for boolean
                    builder.AppendLine(",,,"); // Empty cells for application fields: AppliedToInvoiceID, AppliedToInvoiceDesc, AmountAppliedToInvoice, ApplicationDate
                }
            }
            // Add summary totals to CSV
            // Headers: CreditID,CustomerName,CreditDate,Reason,SourceInfo,OriginalAmount,RemainingAmount,CreditStatus,IsVoided,AppliedToInvoiceID,AppliedToInvoiceDesc,AmountAppliedToInvoice,ApplicationDate (13 headers)
            // Totals align under OriginalAmount (6th item, index 5), RemainingAmount (7th item, index 6), and AmountApplied (12th item, index 11 for the concept)
            builder.AppendLine();
            builder.AppendLine($",,,,Total Original Credit Amount:,{Totals.TotalOriginalCreditAmount:F2}");
            builder.AppendLine($",,,,,Total Remaining Credit Amount:,{Totals.TotalRemainingCreditAmount:F2}");
            builder.AppendLine($",,,,,,,,,,Total Amount Applied (from listed credits):,{Totals.TotalAmountAppliedFromListedCredits:F2}");


            string fileName = $"CreditRegister_{StartDate:yyyy-MM-dd}_to_{EndDate:yyyy-MM-dd}.csv";
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(builder.ToString());
            return File(buffer, "text/csv", fileName);
        }

        private static string EscapeCsvField(string? field)
        {
            if (string.IsNullOrEmpty(field))
                return string.Empty;
            return field.Replace("\"", "\"\"");
        }


        // ViewModel for displaying each UserCredit and its applications
        public class CreditRegisterItemViewModel
        {
            public int CreditId { get; set; }
            public string CustomerName { get; set; } = string.Empty;
            public DateTime CreditDate { get; set; }
            public decimal OriginalAmount { get; set; }
            public decimal RemainingAmount { get; set; }
            public string Reason { get; set; } = string.Empty;
            public string SourceInfo { get; set; } = string.Empty; // e.g., "From Payment P-XXXXX", "Manual Adjustment"
            public bool IsVoided { get; set; }
            public string Status { get; set; } = string.Empty; // e.g. "Fully Applied", "Partially Applied", "Available", "Voided"
            public List<AppliedApplicationViewModel> AppliedApplications { get; set; } = new List<AppliedApplicationViewModel>();
        }

        // ViewModel for displaying details of each credit application
        public class AppliedApplicationViewModel
        {
            public string AppliedToInvoiceId { get; set; } = string.Empty; // e.g. "INV-00001"
            public string AppliedToInvoiceDescription { get; set; } = string.Empty;
            public decimal AmountApplied { get; set; }
            public DateTime ApplicationDate { get; set; }
        }

        // ViewModel for summary totals
        public class CreditRegisterSummaryViewModel
        {
            public decimal TotalOriginalCreditAmount { get; set; }
            public decimal TotalRemainingCreditAmount { get; set; }
            public decimal TotalAmountAppliedFromListedCredits { get; set; } // Sum of amounts from AppliedApplications for credits in the report
        }
    }
}
