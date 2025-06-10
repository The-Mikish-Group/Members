//using Humanizer;
using Microsoft.AspNetCore.Identity.UI.Services;
using System.Net;
using System.Net.Mail;

namespace Members.Services
{
    public class EmailService : IEmailSender
    {
        private readonly string _smtpHost;
        private readonly int _smtpPort;
        private readonly string _smtpUser;
        private readonly string _smtpPassword;
        private readonly bool _enableSsl;
        private readonly ILogger<EmailService> _logger;

        public EmailService(ILogger<EmailService> logger)
        {
            _logger = logger;

            _smtpHost = Environment.GetEnvironmentVariable("SMTP_SERVER")!;
            string portString = Environment.GetEnvironmentVariable("SMTP_PORT")!;
            _smtpUser = Environment.GetEnvironmentVariable("SMTP_USERNAME")!;
            _smtpPassword = Environment.GetEnvironmentVariable("SMTP_PASSWORD")!;
            string sslString = Environment.GetEnvironmentVariable("SMTP_SSL")!;

            if (string.IsNullOrEmpty(_smtpHost) || string.IsNullOrEmpty(portString) || string.IsNullOrEmpty(_smtpUser) || string.IsNullOrEmpty(_smtpPassword) || string.IsNullOrEmpty(sslString))
            {
                _logger.LogError("SMTP environment variables are not set.");
                throw new InvalidOperationException("SMTP environment variables are not set.");
            }

            if (!int.TryParse(portString, out _smtpPort))
            {
                _logger.LogError("Invalid SMTP port number.");
                throw new InvalidOperationException("Invalid SMTP port number.");
            }

            if (!bool.TryParse(sslString, out _enableSsl))
            {
                _logger.LogError("Invalid SMTP SSL value.");
                throw new InvalidOperationException("Invalid SMTP SSL value.");
            }
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            if (string.IsNullOrEmpty(_smtpUser))
            {
                _logger.LogError("SMTP user is not configured.");
                throw new InvalidOperationException("SMTP user is not configured.");
            }

            try
            {
                using var client = new SmtpClient(_smtpHost, _smtpPort);
                client.Credentials = new NetworkCredential(_smtpUser, _smtpPassword);
                client.EnableSsl = _enableSsl;

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(_smtpUser),
                    Subject = subject,
                    Body = htmlMessage,
                    IsBodyHtml = true
                };
                mailMessage.To.Add(email);

                await client.SendMailAsync(mailMessage);
            }
            catch (SmtpException ex)
            {
                _logger.LogError("SMTP Error: {Message}", ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error: {Message}", ex.Message);
                throw;
            }
        }
    }
}