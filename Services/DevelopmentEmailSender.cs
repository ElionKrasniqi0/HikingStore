using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;

namespace HikingStore.Services
{
    /// <summary>Development-only sender used when SMTP credentials aren't configured: writes emails to the log instead of sending them.</summary>
    public class DevelopmentEmailSender : IEmailSender
    {
        private readonly ILogger<DevelopmentEmailSender> _logger;

        public DevelopmentEmailSender(ILogger<DevelopmentEmailSender> logger)
        {
            _logger = logger;
        }

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            _logger.LogWarning("Email not sent (SMTP not configured). To: {Email}; Subject: {Subject}; Body: {Body}", email, subject, htmlMessage);
            return Task.CompletedTask;
        }
    }
}
