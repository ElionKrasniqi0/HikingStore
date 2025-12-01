using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;

namespace HikingStore.Services
{
    public class DevelopmentEmailSender : IEmailSender
    {
        private readonly ILogger<DevelopmentEmailSender> _logger;

        public DevelopmentEmailSender(ILogger<DevelopmentEmailSender> logger)
        {
            _logger = logger;
        }

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            _logger.LogInformation("🎯 EMAIL CONFIRMATION TRIGGERED!");
            _logger.LogInformation("📧 TO: {Email}", email);
            _logger.LogInformation("📝 SUBJECT: {Subject}", subject);
            _logger.LogInformation("📄 MESSAGE CONTENT:");
            _logger.LogInformation("{Message}", htmlMessage);
            _logger.LogInformation("✅ Email would be sent in production mode");
            _logger.LogInformation("🔗 Confirmation link is in the message above");

            // In development, we just log the email instead of sending it
            return Task.CompletedTask;
        }
    }
}
