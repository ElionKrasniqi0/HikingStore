using HikingStore.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HikingStore.Controllers
{
    [Authorize(Roles = "Admin")]
    public class TestEmailController : Controller
    {
        private readonly IEmailSender _emailSender;
        private readonly ILogger<TestEmailController> _logger;

        public TestEmailController(IEmailSender emailSender, ILogger<TestEmailController> logger)
        {
            _emailSender = emailSender;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> SendTestEmail(string testEmail)
        {
            if (string.IsNullOrEmpty(testEmail))
            {
                ViewBag.Error = "Please enter an email address";
                return View("Index");
            }

            if (_emailSender is DevelopmentEmailSender)
            {
                ViewBag.Error = "Email isn't configured, so nothing can be sent. In development, emails are written to the application log instead. Set the SMTP settings below and restart the app.";
                return View("Index");
            }

            try
            {
                await _emailSender.SendEmailAsync(
                    testEmail,
                    "Test Email from HikingStore",
                    "<h2>Email test</h2><p>If you received this email, the HikingStore email configuration is working.</p>");

                ViewBag.Success = $"The SMTP server accepted the message for {testEmail}. Check that inbox (and its spam folder) to confirm delivery.";
                _logger.LogInformation("Test email sent successfully to {Email}", testEmail);
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Failed to send test email: {ex.Message}";
                _logger.LogError(ex, "Failed to send test email to {Email}", testEmail);
            }

            return View("Index");
        }
    }
}


