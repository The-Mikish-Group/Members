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
using System.Text.RegularExpressions; // For parsing description

namespace Members.Areas.Admin.Pages.Reporting
{
    [Authorize(Roles = "Admin,Manager")]
    public class LateFeeRegisterReportModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<LateFeeRegisterReportModel> _logger;

        public LateFeeRegisterReportModel(ApplicationDbContext context,
                                          UserManager<IdentityUser> userManager,
                                          ILogger<LateFeeRegisterReportModel> logger)
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

        public IList<LateFeeRegisterItemViewModel> ReportData { get; set; } = new List<LateFeeRegisterItemViewModel>();
        public LateFeeRegisterSummaryViewModel Totals { get; set; } = new LateFeeRegisterSummaryViewModel();


        private static string ParseOriginalInvoiceId(string description)
        {
            if (string.IsNullOrWhiteSpace(description)) return "N/A";

            // Regex to find patterns like "INV-12345", "Invoice #12345", "Original Invoice: 12345" etc.
            // This regex looks for "INV-" followed by digits, or a common word indicating invoice then digits.
            // It's a basic example and might need refinement based on actual description patterns.
            Match match = Regex.Match(description, @"(INV-|Invoice\s*#|Original Invoice:?\s*)(\d+)", RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 2)
            {
                // Return the full matched part or just the number, e.g., "INV-" + digits or just digits
                // For consistency, let's try to return "INV-XXXXX" if possible
                string prefix = match.Groups[1].Value;
                string number = match.Groups[2].Value;
                if (prefix.ToUpper().StartsWith("INV-")) return $"INV-{int.Parse(number):D5}";
                return $"INV-{int.Parse(number):D5}"; // Default to INV- format
            }
            return "N/A";
        }

        private async Task GenerateReportDataAsync()
        {
            ReportData = new List<LateFeeRegisterItemViewModel>();
            Totals = new LateFeeRegisterSummaryViewModel();

            DateTime effectiveStartDate = StartDate.Date;
            DateTime effectiveEndDate = EndDate.Date.AddDays(1).AddTicks(-1);

            var users = await _context.UserProfile.ToDictionaryAsync(up => up.UserId, up => $"{up.FirstName} {up.LastName}".Trim());

            var lateFeeInvoices = await _context.Invoices
                .Where(i => i.Type == InvoiceType.LateFee &&
                              i.InvoiceDate >= effectiveStartDate &&
                              i.InvoiceDate <= effectiveEndDate)
                .OrderBy(i => i.InvoiceDate)
                .ThenBy(i => i.InvoiceID)
                .ToListAsync();

            foreach (var lfInvoice in lateFeeInvoices)
            {
                string customerName = users.TryGetValue(lfInvoice.UserID, out var name) ? (string.IsNullOrEmpty(name) ? "N/A" : name) : "N/A";

                var itemVM = new LateFeeRegisterItemViewModel
                {
                    LateFeeInvoiceId = $"LF-INV-{lfInvoice.InvoiceID:D5}",
                    CustomerName = customerName,
                    LateFeeInvoiceDate = lfInvoice.InvoiceDate,
                    LateFeeDueDate = lfInvoice.DueDate,
                    LateFeeAmount = lfInvoice.AmountDue,
                    AmountPaidOnLateFee = lfInvoice.AmountPaid,
                    LateFeeStatus = lfInvoice.Status.ToString(),
                    OriginalInvoiceIdRef = ParseOriginalInvoiceId(lfInvoice.Description),
                    LateFeeDescription = lfInvoice.Description
                };
                ReportData.Add(itemVM);

                Totals.TotalLateFeesInvoiced += lfInvoice.AmountDue;
                Totals.TotalLateFeesPaid += lfInvoice.AmountPaid;
                if (lfInvoice.Status != InvoiceStatus.Paid && lfInvoice.Status != InvoiceStatus.Cancelled)
                {
                    Totals.TotalLateFeesOutstanding += (lfInvoice.AmountDue - lfInvoice.AmountPaid);
                }
            }
            _logger.LogInformation("Generated Late Fee Register data for {StartDate} to {EndDate}. Count: {Count}", StartDate, EndDate, ReportData.Count);
        }

        public async Task OnGetAsync()
        {
            await GenerateReportDataAsync();
        }

        public async Task<IActionResult> OnGetExportToCsvAsync()
        {
            await GenerateReportDataAsync();
            var builder = new System.Text.StringBuilder();
            builder.AppendLine("LateFeeInvoiceID,CustomerName,LateFeeInvoiceDate,LateFeeDueDate,LateFeeAmount,AmountPaidOnLateFee,LateFeeStatus,OriginalInvoiceIdRef,LateFeeDescription");

            foreach (var item in ReportData)
            {
                builder.AppendLine(
                    $"\"{EscapeCsvField(item.LateFeeInvoiceId)}\"," +
                    $"\"{EscapeCsvField(item.CustomerName)}\"," +
                    $"{item.LateFeeInvoiceDate:yyyy-MM-dd}," +
                    $"{item.LateFeeDueDate:yyyy-MM-dd}," +
                    $"{item.LateFeeAmount:F2}," +
                    $"{item.AmountPaidOnLateFee:F2}," +
                    $"\"{EscapeCsvField(item.LateFeeStatus)}\"," +
                    $"\"{EscapeCsvField(item.OriginalInvoiceIdRef)}\"," +
                    $"\"{EscapeCsvField(item.LateFeeDescription)}\""
                );
            }
            builder.AppendLine();
            builder.AppendLine($",,,,Total Invoiced:,{Totals.TotalLateFeesInvoiced:F2}");
            builder.AppendLine($",,,,Total Paid:,{Totals.TotalLateFeesPaid:F2}");
            builder.AppendLine($",,,,Total Outstanding:,{Totals.TotalLateFeesOutstanding:F2}");

            string fileName = $"LateFeeRegister_{StartDate:yyyy-MM-dd}_to_{EndDate:yyyy-MM-dd}.csv";
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(builder.ToString());
            return File(buffer, "text/csv", fileName);
        }

        private static string EscapeCsvField(string? field)
        {
            if (string.IsNullOrEmpty(field))
                return string.Empty;
            return field.Replace("\"", "\"\"");
        }

        public class LateFeeRegisterItemViewModel
        {
            public string LateFeeInvoiceId { get; set; } = string.Empty; // e.g., LF-INV-XXXXX
            public string CustomerName { get; set; } = string.Empty;
            public DateTime LateFeeInvoiceDate { get; set; }
            public DateTime LateFeeDueDate { get; set; }
            public decimal LateFeeAmount { get; set; }
            public decimal AmountPaidOnLateFee { get; set; }
            public string LateFeeStatus { get; set; } = string.Empty;
            public string OriginalInvoiceIdRef { get; set; } = string.Empty; // Parsed from description
            public string LateFeeDescription { get; set; } = string.Empty;
        }

        public class LateFeeRegisterSummaryViewModel
        {
            public decimal TotalLateFeesInvoiced { get; set; }
            public decimal TotalLateFeesPaid { get; set; }
            public decimal TotalLateFeesOutstanding { get; set; }
        }
    }
}
