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
using Microsoft.Extensions.Logging; // Corrected placement

namespace Members.Areas.Admin.Pages.Reporting
{
    [Authorize(Roles = "Admin,Manager")]
    public class InvoiceRegisterReportModel(ApplicationDbContext context,
                                      UserManager<IdentityUser> userManager,
                                      ILogger<InvoiceRegisterReportModel> logger) : PageModel
    {
        private readonly ApplicationDbContext _context = context;
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly ILogger<InvoiceRegisterReportModel> _logger = logger;

        [BindProperty(SupportsGet = true)]
        [DataType(DataType.Date)]
        [Display(Name = "Start Date:")]
        public DateTime StartDate { get; set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        [BindProperty(SupportsGet = true)]
        [DataType(DataType.Date)]
        [Display(Name = "End Date:")]
        public DateTime EndDate { get; set; } = DateTime.Today;

        public IList<InvoiceRegisterItem> ReportData { get; set; } = [];
        public InvoiceRegisterSummary Totals { get; set; } = new InvoiceRegisterSummary();

        private async Task GenerateReportDataAsync()
        {
            ReportData = [];
            Totals = new InvoiceRegisterSummary(); // Reset totals

            DateTime effectiveStartDate = StartDate.Date;
            DateTime effectiveEndDate = EndDate.Date.AddDays(1).AddTicks(-1); // Include whole end day

            var users = await _context.UserProfile.ToDictionaryAsync(up => up.UserId, up => $"{up.FirstName} {up.LastName}".Trim());

            // Removed the OrderBy from the database query since we'll sort after processing
            // Exclude Draft status invoices
            var invoices = await _context.Invoices
                .Where(i => i.InvoiceDate >= effectiveStartDate &&
                           i.InvoiceDate <= effectiveEndDate &&
                           i.Status != InvoiceStatus.Draft)
                .ToListAsync();

            var reportItems = new List<InvoiceRegisterItem>();

            foreach (var invoice in invoices)
            {
                var amountRemaining = invoice.AmountDue - invoice.AmountPaid;

                var reportItem = new InvoiceRegisterItem
                {
                    InvoiceId = invoice.InvoiceID,
                    CustomerName = users.TryGetValue(invoice.UserID, out var name) ? (string.IsNullOrEmpty(name) ? "N/A" : name) : "N/A",
                    InvoiceDate = invoice.InvoiceDate,
                    DueDate = invoice.DueDate,
                    Description = invoice.Description,
                    AmountDue = invoice.AmountDue,
                    AmountPaid = invoice.AmountPaid,
                    AmountRemaining = amountRemaining,
                    Status = invoice.Status.ToString(),
                    Type = invoice.Type.ToString() // Get the string representation of the enum
                };

                reportItems.Add(reportItem);

                Totals.TotalAmountDue += invoice.AmountDue;
                Totals.TotalAmountPaid += invoice.AmountPaid;
                Totals.TotalAmountRemaining += amountRemaining;

                if (invoice.Type == InvoiceType.LateFee)
                {
                    Totals.TotalLateFeeInvoices++;
                    Totals.TotalLateFeeValue += invoice.AmountDue;
                }
            }

            // Sort by Customer Name first, then by Invoice ID
            ReportData = [.. reportItems
                .OrderBy(x => x.CustomerName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.InvoiceId)];
        }

        public async Task OnGetAsync()
        {
            await GenerateReportDataAsync();
        }

        // Placeholder for CSV export - will reuse GenerateReportDataAsync
        public async Task<IActionResult> OnGetExportToCsvAsync()
        {
            _logger.LogInformation("Attempting CSV export for Invoice Register Report. StartDate: {StartDate}, EndDate: {EndDate}", StartDate, EndDate);
            await GenerateReportDataAsync();

            var builder = new System.Text.StringBuilder();
            // Headers - simplified, no spaces
            builder.AppendLine("InvoiceId,CustomerName,InvoiceDate,DueDate,Description,Type,AmountDue,AmountPaid,AmountRemaining,Status");

            foreach (var item in ReportData)
            {
                string customerNameCsv = $"\"{EscapeCsvField(item.CustomerName)}\"";
                string descriptionCsv = $"\"{EscapeCsvField(item.Description)}\"";
                string typeCsv = $"\"{EscapeCsvField(item.Type)}\""; // Type is string representation of enum
                string statusCsv = $"\"{EscapeCsvField(item.Status)}\""; // Status is string representation of enum
                string invoiceIdCsv = $"INV-{item.InvoiceId:D5}";

                builder.AppendLine($"{invoiceIdCsv},{customerNameCsv},{item.InvoiceDate:yyyy-MM-dd},{item.DueDate:yyyy-MM-dd},{descriptionCsv},{typeCsv},{item.AmountDue:F2},{item.AmountPaid:F2},{item.AmountRemaining:F2},{statusCsv}");
            }
            builder.AppendLine(); // Blank line for separation

            // Totals
            builder.AppendLine($",,,,,TotalAmountDue,{Totals.TotalAmountDue:F2}");
            builder.AppendLine($",,,,,TotalAmountPaid,{Totals.TotalAmountPaid:F2}");
            builder.AppendLine($",,,,,TotalAmountRemaining,{Totals.TotalAmountRemaining:F2}");
            if (Totals.TotalLateFeeInvoices > 0)
            {
                builder.AppendLine($",,,,,TotalLateFeeInvoices,{Totals.TotalLateFeeInvoices}");
                builder.AppendLine($",,,,,TotalLateFeeValue,{Totals.TotalLateFeeValue:F2}");
            }

            string fileName = $"InvoiceRegister_{StartDate:yyyy-MM-dd}_to_{EndDate:yyyy-MM-dd}.csv";
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(builder.ToString());
            _logger.LogInformation("Invoice Register Report CSV generated. Filename: {FileName}, Size: {Size} bytes.", fileName, buffer.Length);
            return File(buffer, "text/csv", fileName);
        }

        private static string EscapeCsvField(string? field)
        {
            if (string.IsNullOrEmpty(field))
                return string.Empty;
            return field.Replace("\"", "\"\"");
        }

        public class InvoiceRegisterItem
        {
            public int InvoiceId { get; set; }
            public string CustomerName { get; set; } = string.Empty;
            public DateTime InvoiceDate { get; set; }
            public DateTime DueDate { get; set; }
            public string Description { get; set; } = string.Empty;
            public decimal AmountDue { get; set; }
            public decimal AmountPaid { get; set; }
            public decimal AmountRemaining { get; set; }
            public string Status { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
        }

        public class InvoiceRegisterSummary
        {
            public decimal TotalAmountDue { get; set; }
            public decimal TotalAmountPaid { get; set; }
            public decimal TotalAmountRemaining { get; set; }
            public int TotalLateFeeInvoices { get; set; }
            public decimal TotalLateFeeValue { get; set; }
        }
    }
}
