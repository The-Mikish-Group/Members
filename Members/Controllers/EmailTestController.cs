using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;

namespace Members.Controllers
{
    [Authorize(Roles = "Admin")]
    public class EmailTestController(IEmailSender emailService, ILogger<EmailTestController> logger) : Controller
    {
        private readonly IEmailSender _emailService = emailService;
        private readonly ILogger<EmailTestController> _logger = logger;

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> TestEmail(string testEmail)
        {
            if (string.IsNullOrEmpty(testEmail))
            {
                ViewBag.Message = "Please provide a test email address.";
                ViewBag.Success = false;
                return View("Index");
            }

            try
            {
                // Get environment variables (matching EmailService variable names)
                string siteName = Environment.GetEnvironmentVariable("SITE_NAME_OAKS_VILLAGE") ?? "Oaks-Village";

                // Log environment variables (matching your EmailService)
                _logger.LogInformation("Testing email with the following configuration:");
                _logger.LogInformation("SMTP_SERVER_OAKS_VILLAGE: {Server}", Environment.GetEnvironmentVariable("SMTP_SERVER_OAKS_VILLAGE"));
                _logger.LogInformation("SMTP_PORT: {Port}", Environment.GetEnvironmentVariable("SMTP_PORT"));
                _logger.LogInformation("SMTP_USERNAME_OAKS_VILLAGE: {Username}", Environment.GetEnvironmentVariable("SMTP_USERNAME_OAKS_VILLAGE"));
                _logger.LogInformation("SMTP_SSL: {SSL}", Environment.GetEnvironmentVariable("SMTP_SSL"));
                _logger.LogInformation("SITE_NAME_OAKS_VILLAGE: {SiteName}", siteName);

                await _emailService.SendEmailAsync(
                    testEmail,
                    $"Test Email from {siteName}",
                    $"<h2>Test Email from {siteName}</h2>" +
                    "<p>This is a test email to verify SMTP configuration.</p>" +
                    $"<p>Sent at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>" +
                    $"<p>Site: {siteName}</p>"
                );

                ViewBag.Message = $"Test email sent successfully to {testEmail}";
                ViewBag.Success = true;
                _logger.LogInformation("Test email sent successfully to {Email}", testEmail);
            }
            catch (Exception ex)
            {
                ViewBag.Message = $"Failed to send test email: {ex.Message}";
                ViewBag.Success = false;
                _logger.LogError(ex, "Failed to send test email to {Email}", testEmail);
            }

            return View("Index");
        }
    }
}