using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace HikingStore.Services
{
    public class EmailSender : IEmailSender
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(IConfiguration configuration, ILogger<EmailSender> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            try
            {
                var emailSettings = _configuration.GetSection("EmailSettings");
                var fromEmail = emailSettings["SenderEmail"]?.Trim();
                // Remove spaces from Gmail App Password if present (Gmail App Passwords should not have spaces)
                var fromPassword = emailSettings["SenderPassword"]?.Replace(" ", "")?.Trim() ?? "";
                var smtpServer = emailSettings["SmtpServer"] ?? "smtp.gmail.com";
                var port = int.Parse(emailSettings["SmtpPort"] ?? "587");
                var senderName = emailSettings["SenderName"] ?? "HikingStore";
                var enableSsl = bool.Parse(emailSettings["EnableSsl"] ?? "true");

                if (string.IsNullOrEmpty(fromEmail) || string.IsNullOrEmpty(fromPassword))
                {
                    _logger.LogError("❌ Email configuration is missing. SenderEmail: {SenderEmail}, Password: {HasPassword}", 
                        string.IsNullOrEmpty(fromEmail) ? "MISSING" : "SET", 
                        string.IsNullOrEmpty(fromPassword) ? "MISSING" : "SET");
                    throw new InvalidOperationException("Email is not configured. Set EmailSettings:SenderEmail and EmailSettings:SenderPassword with user secrets or environment variables.");
                }

                _logger.LogInformation("📧 Preparing to send email from {FromEmail} to {ToEmail}", fromEmail, email);
                _logger.LogInformation("📧 SMTP Server: {SmtpServer}:{Port}, SSL: {EnableSsl}", smtpServer, port, enableSsl);

                using (var message = new MailMessage())
                {
                    message.From = new MailAddress(fromEmail, senderName);
                    message.To.Add(new MailAddress(email));
                    message.Subject = subject;
                    message.Body = htmlMessage;
                    message.IsBodyHtml = true;
                    message.Priority = MailPriority.Normal;

                    using (var smtpClient = new SmtpClient(smtpServer, port))
                    {
                        smtpClient.Credentials = new NetworkCredential(fromEmail, fromPassword);
                        smtpClient.EnableSsl = enableSsl;
                        smtpClient.Timeout = 60000; // Increased timeout to 60 seconds
                        smtpClient.DeliveryMethod = SmtpDeliveryMethod.Network;
                        smtpClient.UseDefaultCredentials = false;

                        _logger.LogInformation("🚀 Attempting to send email to {Email} via {SmtpServer}:{Port}...", email, smtpServer, port);
                        
                        await smtpClient.SendMailAsync(message);
                        
                        _logger.LogInformation("✅ Email sent successfully to {Email}", email);
                    }
                }
            }
            catch (SmtpException smtpEx)
            {
                var errorMessage = $"SMTP Error: {smtpEx.Message}";
                
                // Provide helpful error messages for common Gmail issues
                if (smtpEx.Message.Contains("535") || smtpEx.Message.Contains("5.7.8") || smtpEx.Message.Contains("Authentication"))
                {
                    errorMessage += "\n\nGmail Authentication Error: Please verify:\n" +
                                   "1. You are using a Gmail App Password (not your regular password)\n" +
                                   "2. 2-Step Verification is enabled on your Gmail account\n" +
                                   "3. The App Password is correct (16 characters, no spaces)\n" +
                                   "4. Generate a new App Password at: https://myaccount.google.com/apppasswords";
                }
                else if (smtpEx.Message.Contains("5.7.1") || smtpEx.Message.Contains("relay"))
                {
                    errorMessage += "\n\nGmail Relay Error: Your account may need to allow less secure apps or use App Passwords.";
                }
                
                _logger.LogError(smtpEx, "❌ SMTP Error sending email to {Email}. {ErrorMessage}", email, errorMessage);
                throw new Exception(errorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to send email to {Email}. Error: {ErrorMessage}", email, ex.Message);
                throw new Exception($"Failed to send email: {ex.Message}");
            }
        }
    }
}