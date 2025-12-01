using Microsoft.AspNetCore.Identity.UI.Services;
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HikingStore.Services
{
    public class EmailSender : IEmailSender
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailSender> _logger;
        private readonly IEmailTemplateService _emailTemplateService;

        public EmailSender(IConfiguration configuration, ILogger<EmailSender> logger, IEmailTemplateService emailTemplateService)
        {
            _configuration = configuration;
            _logger = logger;
            _emailTemplateService = emailTemplateService;
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            try
            {
                var emailSettings = _configuration.GetSection("EmailSettings");

                var fromEmail = emailSettings["SenderEmail"];
                var fromPassword = emailSettings["SenderPassword"];

                // Gmail SMTP settings
                var smtpHost = "smtp.gmail.com";
                var port = 587;
                var senderName = emailSettings["SenderName"] ?? "HikingStore";

                // Validate configuration
                if (string.IsNullOrEmpty(fromEmail) || string.IsNullOrEmpty(fromPassword))
                {
                    _logger.LogError("❌ Gmail configuration is missing. Please check appsettings.json");
                    throw new Exception("Gmail configuration is missing. Please check your appsettings.json file.");
                }

                _logger.LogInformation("📧 Preparing to send email to: {Email}", email);

                using (var message = new MailMessage())
                {
                    message.From = new MailAddress(fromEmail, senderName);
                    message.To.Add(new MailAddress(email));
                    message.Subject = subject;
                    message.Body = htmlMessage;
                    message.IsBodyHtml = true;

                    using (var smtpClient = new SmtpClient(smtpHost, port))
                    {
                        smtpClient.Credentials = new NetworkCredential(fromEmail, fromPassword);
                        smtpClient.EnableSsl = true;
                        smtpClient.Timeout = 30000;

                        _logger.LogInformation("🚀 Sending email to {Email}...", email);

                        await smtpClient.SendMailAsync(message);

                        _logger.LogInformation("✅ Email sent successfully to {Email}", email);
                    }
                }
            }
            catch (SmtpException smtpEx)
            {
                _logger.LogError(smtpEx, "❌ SMTP Error sending email to {Email}. Check your Gmail settings and App Password.", email);
                throw new Exception($"SMTP Error: {smtpEx.Message}. Please check your Gmail configuration.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to send email to {Email}", email);
                throw new Exception($"Failed to send email: {ex.Message}");
            }
        }
    }
}
