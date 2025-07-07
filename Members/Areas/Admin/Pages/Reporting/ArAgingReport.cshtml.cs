using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Members.Data; // Assuming ApplicationDbContext is here
using Members.Models; // Assuming Invoice and UserProfile models are here
using Microsoft.EntityFrameworkCore; // For EF Core operations like Include, ThenInclude
using System.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging; // Corrected placement

namespace Members.Areas.Admin.Pages.Reporting
{
    [Authorize(Roles = "Admin,Manager")]
    public class ArAgingReportModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<ArAgingReportModel> _logger;

        public ArAgingReportModel(ApplicationDbContext context,
                                  UserManager<IdentityUser> userManager,
                                  ILogger<ArAgingReportModel> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        [BindProperty(SupportsGet = true)]
        [DataType(DataType.Date)]
        [Display(Name = "As of Date")]
        public DateTime AsOfDate { get; set; } = DateTime.Today;

        public IList<AgingReportItem> ReportData { get; set; } = new List<AgingReportItem>();
        public AgingReportSummary Totals { get; set; } = new AgingReportSummary();

        private async Task GenerateReportDataAsync()
        {
            ReportData = new List<AgingReportItem>();
            Totals = new AgingReportSummary(); // Reset totals

            // Ensure AsOfDate has the Date part only for consistent comparisons
            DateTime effectiveAsOfDate = AsOfDate.Date;

            var users = await _context.UserProfile.ToDictionaryAsync(up => up.UserId, up => $"{up.FirstName} {up.LastName}".Trim());

            var invoices = await _context.Invoices
                .Where(i => i.Status != InvoiceStatus.Paid &&
                              i.Status != InvoiceStatus.Cancelled &&
                              i.AmountDue > i.AmountPaid)
                .ToListAsync();

            foreach (var invoice in invoices)
            {
                var amountRemaining = invoice.AmountDue - invoice.AmountPaid;
                if (amountRemaining <= 0) continue;

                var reportItem = new AgingReportItem
                {
                    InvoiceId = invoice.InvoiceID,
                    CustomerName = users.TryGetValue(invoice.UserID, out var name) ? (string.IsNullOrEmpty(name) ? "N/A" : name) : "N/A",
                    InvoiceDate = invoice.InvoiceDate,
                    DueDate = invoice.DueDate,
                    TotalAmountDue = invoice.AmountDue,
                    AmountRemaining = amountRemaining
                };

                reportItem.Current = 0; reportItem.Overdue1_30 = 0; reportItem.Overdue31_60 = 0; reportItem.Overdue61_90 = 0; reportItem.Overdue90Plus = 0;

                if (invoice.DueDate.Date >= effectiveAsOfDate) { // Due on or after AsOfDate
                    reportItem.Current = amountRemaining;
                } else { // Due before AsOfDate (i.e. Overdue)
                    int daysStrictlyOverdue = (effectiveAsOfDate - invoice.DueDate.Date).Days;
                    if (daysStrictlyOverdue >= 1 && daysStrictlyOverdue <= 30) reportItem.Overdue1_30 = amountRemaining;
                    else if (daysStrictlyOverdue >= 31 && daysStrictlyOverdue <= 60) reportItem.Overdue31_60 = amountRemaining;
                    else if (daysStrictlyOverdue >= 61 && daysStrictlyOverdue <= 90) reportItem.Overdue61_90 = amountRemaining;
                    else if (daysStrictlyOverdue >= 91) reportItem.Overdue90Plus = amountRemaining;
                }
                ReportData.Add(reportItem);

                Totals.TotalAmountRemaining += reportItem.AmountRemaining;
                Totals.TotalCurrent += reportItem.Current;
                Totals.TotalOverdue1_30 += reportItem.Overdue1_30;
                Totals.TotalOverdue31_60 += reportItem.Overdue31_60;
                Totals.TotalOverdue61_90 += reportItem.Overdue61_90;
                Totals.TotalOverdue90Plus += reportItem.Overdue90Plus;
            }
        }

        public async Task OnGetAsync()
        {
            await GenerateReportDataAsync();
        }

        public async Task<IActionResult> OnGetExportToCsvAsync()
        {
            _logger.LogInformation("Attempting CSV export for A/R Aging Report. AsOfDate: {AsOfDate}", AsOfDate);
            await GenerateReportDataAsync(); // Ensure data is populated using the current AsOfDate

            var builder = new System.Text.StringBuilder();
            // Headers - simple, no spaces, no need to quote them here.
            builder.AppendLine("CustomerName,InvoiceId,InvoiceDate,DueDate,AmountRemaining,Current,Overdue1_30Days,Overdue31_60Days,Overdue61_90Days,Overdue90PlusDays");

            foreach (var item in ReportData)
            {
                // Apply escaping and quoting to string fields
                string customerNameCsv = $"\"{EscapeCsvField(item.CustomerName)}\"";
                string invoiceIdCsv = $"INV-{item.InvoiceId:D5}"; // This is already a formatted string, could be quoted if desired but likely fine.

                builder.AppendLine($"{customerNameCsv},{invoiceIdCsv},{item.InvoiceDate:yyyy-MM-dd},{item.DueDate:yyyy-MM-dd},{item.AmountRemaining:F2},{item.Current:F2},{item.Overdue1_30:F2},{item.Overdue31_60:F2},{item.Overdue61_90:F2},{item.Overdue90Plus:F2}");
            }
            builder.AppendLine(); // Blank line for separation before totals

            // Totals row
            builder.AppendLine($"Totals,,,,{Totals.TotalAmountRemaining:F2},{Totals.TotalCurrent:F2},{Totals.TotalOverdue1_30:F2},{Totals.TotalOverdue31_60:F2},{Totals.TotalOverdue61_90:F2},{Totals.TotalOverdue90Plus:F2}");

            string fileName = $"ArAgingReport_{AsOfDate:yyyy-MM-dd}.csv";
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(builder.ToString());
            _logger.LogInformation("A/R Aging Report CSV generated. Filename: {FileName}, Size: {Size} bytes.", fileName, buffer.Length);
            return File(buffer, "text/csv", fileName);
        }

        private static string EscapeCsvField(string? field)
        {
            if (string.IsNullOrEmpty(field))
                return string.Empty;
            // Replace " with ""
            string escapedField = field.Replace("\"", "\"\"");
            // If the field contains a comma, newline, or double quote, it should be enclosed in double quotes.
            // The calling code will handle the outer quotes. This just escapes internal quotes.
            return escapedField;
        }

        public class AgingReportItem
        {
            public string CustomerName { get; set; } = string.Empty;
            public int InvoiceId { get; set; }
            public DateTime InvoiceDate { get; set; }
            public DateTime DueDate { get; set; }
            public decimal TotalAmountDue { get; set; } // Original amount of the invoice
            public decimal AmountRemaining { get; set; }
            public decimal Current { get; set; }
            public decimal Overdue1_30 { get; set; }
            public decimal Overdue31_60 { get; set; }
            public decimal Overdue61_90 { get; set; }
            public decimal Overdue90Plus { get; set; }
        }

        public class AgingReportSummary
        {
            public decimal TotalAmountRemaining { get; set; }
            public decimal TotalCurrent { get; set; }
            public decimal TotalOverdue1_30 { get; set; }
            public decimal TotalOverdue31_60 { get; set; }
            public decimal TotalOverdue61_90 { get; set; }
            public decimal TotalOverdue90Plus { get; set; }
        }
    }
}
