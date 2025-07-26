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
using Microsoft.Extensions.Logging; // Added for ILogger

namespace Members.Areas.Admin.Pages.Reporting
{
    [Authorize(Roles = "Admin,Manager")]
    public class PaymentRegisterReportModel(ApplicationDbContext context,
                                      UserManager<IdentityUser> userManager,
                                      ILogger<PaymentRegisterReportModel> logger) : PageModel
    {
        private readonly ApplicationDbContext _context = context;
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly ILogger<PaymentRegisterReportModel> _logger = logger;

        [BindProperty(SupportsGet = true)]
        [DataType(DataType.Date)]
        [Display(Name = "Start Date:")]
        public DateTime StartDate { get; set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        [BindProperty(SupportsGet = true)]
        [DataType(DataType.Date)]
        [Display(Name = "End Date:")]
        public DateTime EndDate { get; set; } = DateTime.Today;

        public IList<PaymentRegisterItem> ReportData { get; set; } = [];
        public PaymentRegisterSummary Totals { get; set; } = new PaymentRegisterSummary();


        private async Task GenerateReportDataAsync()
        {
            ReportData = [];
            Totals = new PaymentRegisterSummary(); // Reset totals

            DateTime effectiveStartDate = StartDate.Date;
            DateTime effectiveEndDate = EndDate.Date.AddDays(1).AddTicks(-1); // Include whole end day to cover all times on EndDate

            var users = await _context.UserProfile.ToDictionaryAsync(up => up.UserId, up => $"{up.FirstName} {up.LastName}".Trim());

            // Fetch relevant payments along with their linked invoice details
            var paymentsQuery = _context.Payments
                .Where(p => p.PaymentDate >= effectiveStartDate && p.PaymentDate <= effectiveEndDate)
                .Include(p => p.Invoice) // Eagerly load the related Invoice
                .OrderBy(p => p.PaymentDate)
                .ThenBy(p => p.PaymentID);

            var payments = await paymentsQuery.ToListAsync();

            foreach (var payment in payments)
            {
                string customerName = users.TryGetValue(payment.UserID, out var name) ? (string.IsNullOrEmpty(name) ? "N/A" : name) : "N/A";
                string primaryInvoiceInfo = "N/A";
                if (payment.Invoice != null) // Check if Invoice was loaded/exists
                {
                    primaryInvoiceInfo = $"INV-{payment.Invoice.InvoiceID:D5}: {payment.Invoice.Description}";
                } else if (payment.InvoiceID.HasValue) {
                    // InvoiceID has a value but Invoice object is null (could happen if Include wasn't effective or data integrity issue)
                    // Optionally, you could do a separate lookup here, but for performance, Include is preferred.
                    // For now, we'll just show the ID if the object isn't loaded.
                    primaryInvoiceInfo = $"INV-{payment.InvoiceID.Value:D5} (Details N/A)";
                }


                var reportItem = new PaymentRegisterItem
                {
                    PaymentId = payment.PaymentID,
                    CustomerName = customerName,
                    PaymentDate = payment.PaymentDate,
                    PaymentAmount = payment.Amount,
                    PaymentMethod = payment.Method.ToString(), // Enum to string
                    ReferenceNumber = payment.ReferenceNumber,
                    PrimaryInvoiceInfo = primaryInvoiceInfo,
                    PaymentNotes = payment.Notes
                };
                ReportData.Add(reportItem);

                Totals.TotalPaymentsAmount += payment.Amount;
            }
            _logger.LogInformation("Generated Payment Register data for {StartDate} to {EndDate}. Count: {Count}, Total: {TotalAmount}", StartDate, EndDate, ReportData.Count, Totals.TotalPaymentsAmount);
        }
        public async Task OnGetAsync()
        {
            await GenerateReportDataAsync();
        }

        // CSV export will use GenerateReportDataAsync - to be fully implemented in CSV step
        public async Task<IActionResult> OnGetExportToCsvAsync()
        {
            await GenerateReportDataAsync();
            // CSV generation logic will be added here
            var builder = new System.Text.StringBuilder();
            builder.AppendLine("PaymentID,CustomerName,PaymentDate,PaymentAmount,PaymentMethod,ReferenceNumber,AppliedToInvoice,PaymentNotes");
            foreach (var item in ReportData)
            {
                builder.AppendLine($"{item.PaymentId},\"{EscapeCsvField(item.CustomerName)}\",{item.PaymentDate:yyyy-MM-dd},{item.PaymentAmount:F2},\"{EscapeCsvField(item.PaymentMethod)}\",\"{EscapeCsvField(item.ReferenceNumber)}\",\"{EscapeCsvField(item.PrimaryInvoiceInfo)}\",\"{EscapeCsvField(item.PaymentNotes)}\"");
            }
            builder.AppendLine();
            builder.AppendLine($",,,Total Payments:,{Totals.TotalPaymentsAmount:F2}");

            string fileName = $"PaymentRegister_{StartDate:yyyy-MM-dd}_to_{EndDate:yyyy-MM-dd}.csv";
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(builder.ToString());
            return File(buffer, "text/csv", fileName);
        }

        private static string EscapeCsvField(string? field)
        {
            if (string.IsNullOrEmpty(field))
                return string.Empty;
            return field.Replace("\"", "\"\"");
        }

        public class PaymentRegisterItem
        {
            public int PaymentId { get; set; }
            public string CustomerName { get; set; } = string.Empty;
            public DateTime PaymentDate { get; set; }
            public decimal PaymentAmount { get; set; }
            public string PaymentMethod { get; set; } = string.Empty;
            public string? ReferenceNumber { get; set; }
            public string? PrimaryInvoiceInfo { get; set; } // e.g., "INV-00001: Annual Dues"
            public string? PaymentNotes { get; set; }
        }

        public class PaymentRegisterSummary
        {
            public decimal TotalPaymentsAmount { get; set; }
        }
    }
}
