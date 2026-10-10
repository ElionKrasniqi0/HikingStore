using HikingStore.Data;
using HikingStore.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using System;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace HikingStore.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ResendEmailConfirmationModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly IEmailTemplateService _emailTemplateService;
        private readonly ILogger<ResendEmailConfirmationModel> _logger;

        public ResendEmailConfirmationModel(
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender,
            IEmailTemplateService emailTemplateService,
            ILogger<ResendEmailConfirmationModel> logger)
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _emailTemplateService = emailTemplateService;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            [Display(Name = "Email Address")]
            public string Email { get; set; }
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            const string uniformMessage = "If an unconfirmed account exists for that email, we've sent a new confirmation link. Check your inbox and spam folder.";
            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user == null || user.EmailConfirmed)
            {
                TempData["Success"] = uniformMessage;
                return RedirectToPage("./Login");
            }

            var userId = await _userManager.GetUserIdAsync(user);
            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

            var callbackUrl = Url.Page(
                "/Account/ConfirmEmail",
                pageHandler: null,
                values: new { area = "Identity", userId, code },
                protocol: Request.Scheme)!;

            var emailBody = _emailTemplateService.GenerateEmailConfirmationTemplate(
                HtmlEncoder.Default.Encode(callbackUrl), HtmlEncoder.Default.Encode(user.FirstName ?? user.Email!));

            try
            {
                await _emailSender.SendEmailAsync(Input.Email, "Confirm your HikingStore account", emailBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resend confirmation email for user {UserId}", userId);
            }

            TempData["Success"] = uniformMessage;
            return RedirectToPage("./Login");
        }
    }
}