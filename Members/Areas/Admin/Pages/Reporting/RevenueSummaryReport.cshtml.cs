using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Members.Data;
using Members.Models; // For InvoiceType enum etc.
using Microsoft.EntityFrameworkCore;
using System.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Members.Areas.Admin.Pages.Reporting
{
    [Authorize(Roles = "Admin,Manager")]
    public class RevenueSummaryReportModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<RevenueSummaryReportModel> _logger;

        public RevenueSummaryReportModel(ApplicationDbContext context, ILogger<RevenueSummaryReportModel> logger)
        {
            _context = context;
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

        public RevenueSummaryViewModel? SummaryData { get; set; }


        private async Task GenerateReportDataAsync()
        {
            _logger.LogInformation("GenerateReportDataAsync called for Revenue Summary. StartDate: {StartDate}, EndDate: {EndDate}", StartDate, EndDate);
            SummaryData = new RevenueSummaryViewModel
            {
                ReportStartDate = StartDate.Date,
                ReportEndDate = EndDate.Date
            };

            DateTime effectiveStartDate = StartDate.Date;
            DateTime effectiveEndDate = EndDate.Date.AddDays(1).AddTicks(-1); // Inclusive end date

            // Invoices
            var invoicesInPeriod = await _context.Invoices
                .Where(i => i.InvoiceDate >= effectiveStartDate &&
                              i.InvoiceDate <= effectiveEndDate &&
                              i.Status != InvoiceStatus.Cancelled) // Exclude Cancelled invoices from revenue summary
                .ToListAsync();

            SummaryData.TotalAmountInvoiced_Dues = invoicesInPeriod
                .Where(i => i.Type == InvoiceType.Dues)
                .Sum(i => i.AmountDue);
            SummaryData.TotalAmountInvoiced_Fine = invoicesInPeriod
                .Where(i => i.Type == InvoiceType.Fine)
                .Sum(i => i.AmountDue);
            SummaryData.TotalAmountInvoiced_LateFee = invoicesInPeriod
                .Where(i => i.Type == InvoiceType.LateFee)
                .Sum(i => i.AmountDue);
            SummaryData.TotalAmountInvoiced_MiscCharge = invoicesInPeriod
                .Where(i => i.Type == InvoiceType.MiscCharge)
                .Sum(i => i.AmountDue);
            SummaryData.GrandTotalAmountInvoiced = invoicesInPeriod.Sum(i => i.AmountDue);

            // Payments
            SummaryData.TotalPaymentsReceived = await _context.Payments
                .Where(p => p.PaymentDate >= effectiveStartDate && p.PaymentDate <= effectiveEndDate)
                .SumAsync(p => p.Amount);

            // Credits Issued (Remaining Value)
            SummaryData.TotalCreditsIssued_RemainingValue = await _context.UserCredits
                .Where(uc => uc.CreditDate >= effectiveStartDate &&
                               uc.CreditDate <= effectiveEndDate &&
                               !uc.IsVoided)
                .SumAsync(uc => uc.Amount); // Sum of current remaining amounts of credits created in period

            // Credits Applied
            SummaryData.TotalCreditsApplied = await _context.CreditApplications
                .Where(ca => ca.ApplicationDate >= effectiveStartDate &&
                               ca.ApplicationDate <= effectiveEndDate &&
                               !ca.IsReversed)
                .SumAsync(ca => ca.AmountApplied);

            // Net Change
            SummaryData.NetChange = SummaryData.TotalPaymentsReceived - SummaryData.TotalCreditsApplied;

            _logger.LogInformation("Revenue Summary Data Generated: Invoiced={GrandTotalInvoiced}, Payments={TotalPayments}, CreditsIssued={TotalCreditsIssued}, CreditsApplied={TotalCreditsApplied}, NetChange={NetChange}",
                SummaryData.GrandTotalAmountInvoiced, SummaryData.TotalPaymentsReceived, SummaryData.TotalCreditsIssued_RemainingValue, SummaryData.TotalCreditsApplied, SummaryData.NetChange);
        }

        public async Task OnGetAsync()
        {
            // Always generate data based on StartDate/EndDate which have defaults.
            // GenerateReportDataAsync initializes SummaryData.
            await GenerateReportDataAsync();

            // Ensure SummaryData is not null for the view, even if no underlying data was found
            // (GenerateReportDataAsync already creates the SummaryData object, so this is redundant if it always runs)
            // However, if there was a condition to skip GenerateReportDataAsync, this would be essential:
            if (SummaryData == null)
            {
                _logger.LogWarning("SummaryData was null after OnGetAsync, initializing to default. StartDate: {StartDate}, EndDate: {EndDate}", StartDate, EndDate);
                SummaryData = new RevenueSummaryViewModel
                {
                    ReportStartDate = StartDate.Date,
                    ReportEndDate = EndDate.Date
                    // Other properties will default to 0 for decimals
                };
            }
        }

        public async Task<IActionResult> OnGetExportToCsvAsync()
        {
            await GenerateReportDataAsync();

            var builder = new System.Text.StringBuilder();
            builder.AppendLine("Metric,Value");

            if (SummaryData != null)
            {
                builder.AppendLine($"\"Report Period Start\",{SummaryData.ReportStartDate:yyyy-MM-dd}");
                builder.AppendLine($"\"Report Period End\",{SummaryData.ReportEndDate:yyyy-MM-dd}");
                builder.AppendLine(); // Blank line for spacing
                builder.AppendLine($"\"Total Dues Invoiced\",{SummaryData.TotalAmountInvoiced_Dues:F2}");
                builder.AppendLine($"\"Total Fines Invoiced\",{SummaryData.TotalAmountInvoiced_Fine:F2}");
                builder.AppendLine($"\"Total Late Fees Invoiced\",{SummaryData.TotalAmountInvoiced_LateFee:F2}");
                builder.AppendLine($"\"Total Misc Charges Invoiced\",{SummaryData.TotalAmountInvoiced_MiscCharge:F2}");
                builder.AppendLine($"\"Grand Total Invoiced\",{SummaryData.GrandTotalAmountInvoiced:F2}");
                builder.AppendLine(); // Blank line for spacing
                builder.AppendLine($"\"Total Payments Received\",{SummaryData.TotalPaymentsReceived:F2}");
                builder.AppendLine($"\"Total Credits Issued (Remaining Value)\",{SummaryData.TotalCreditsIssued_RemainingValue:F2}");
                builder.AppendLine($"\"Total Credits Applied\",{SummaryData.TotalCreditsApplied:F2}");
                builder.AppendLine(); // Blank line for spacing
                builder.AppendLine($"\"Net Change (Payments - Credits Applied)\",{SummaryData.NetChange:F2}");
            }

            string fileName = $"RevenueSummary_{StartDate:yyyy-MM-dd}_to_{EndDate:yyyy-MM-dd}.csv";
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(builder.ToString());
            return File(buffer, "text/csv", fileName);
        }

        private static string EscapeCsvField(string? field) // Though not strictly needed for this CSV's labels
        {
            if (string.IsNullOrEmpty(field))
                return string.Empty;
            return field.Replace("\"", "\"\"");
        }


        public class RevenueSummaryViewModel
        {
            [Display(Name = "Total Dues Invoiced")]
            [DataType(DataType.Currency)]
            public decimal TotalAmountInvoiced_Dues { get; set; }

            [Display(Name = "Total Fines Invoiced")]
            [DataType(DataType.Currency)]
            public decimal TotalAmountInvoiced_Fine { get; set; }

            [Display(Name = "Total Late Fees Invoiced")]
            [DataType(DataType.Currency)]
            public decimal TotalAmountInvoiced_LateFee { get; set; }

            [Display(Name = "Total Misc Charges Invoiced")]
            [DataType(DataType.Currency)]
            public decimal TotalAmountInvoiced_MiscCharge { get; set; }

            [Display(Name = "Grand Total Invoiced")]
            [DataType(DataType.Currency)]
            public decimal GrandTotalAmountInvoiced { get; set; }

            [Display(Name = "Total Payments Received")]
            [DataType(DataType.Currency)]
            public decimal TotalPaymentsReceived { get; set; }

            [Display(Name = "Total Credits Issued (Remaining Value)")]
            [DataType(DataType.Currency)]
            public decimal TotalCreditsIssued_RemainingValue { get; set; }

            [Display(Name = "Total Credits Applied")]
            [DataType(DataType.Currency)]
            public decimal TotalCreditsApplied { get; set; }

            [Display(Name = "Net Change (Payments - Credits Applied)")]
            [DataType(DataType.Currency)]
            public decimal NetChange { get; set; }

            public DateTime ReportStartDate { get; set; }
            public DateTime ReportEndDate { get; set; }
        }
    }
}
