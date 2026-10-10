using HikingStore.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace HikingStore.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<LoginModel> _logger;
        private readonly HikingStore.Services.CartService _cartService;

        public LoginModel(SignInManager<ApplicationUser> signInManager,
            ILogger<LoginModel> logger,
            UserManager<ApplicationUser> userManager,
            HikingStore.Services.CartService cartService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _logger = logger;
            _cartService = cartService;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        public string ReturnUrl { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; }

            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; }

            [Display(Name = "Remember me?")]
            public bool RememberMe { get; set; }
        }

        public async Task OnGetAsync(string returnUrl = null)
        {
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                ModelState.AddModelError(string.Empty, ErrorMessage);
            }

            returnUrl ??= Url.Content("~/");

            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme).ConfigureAwait(false);

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync().ConfigureAwait(false)).ToList();

            ReturnUrl = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            ReturnUrl = returnUrl;
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (ModelState.IsValid)
            {
                // ⚡ OPTIMIZATION: Use FindByNameAsync directly since email is used as username
                // This avoids the extra query that FindByEmailAsync does (which queries both Email and UserName)
                var user = await _userManager.FindByNameAsync(Input.Email).ConfigureAwait(false);
                
                if (user == null)
                {
                    await Task.Delay(1000);
                    ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                    return Page();
                }

                var result = await _signInManager.PasswordSignInAsync(
                    user.UserName,
                    Input.Password,
                    Input.RememberMe,
                    lockoutOnFailure: true);

                if (result.Succeeded)
                {
                    _logger.LogInformation("SECURITY: User {UserId} logged in from IP {IPAddress}",
                        user.Id, HttpContext.Connection.RemoteIpAddress?.ToString());
                    await _cartService.MergeGuestCartAsync(user.Id);
                    return LocalRedirect(returnUrl);
                }

                if (result.IsNotAllowed)
                {
                    // Only reached after a correct password, so this does not reveal whether an account exists.
                    ModelState.AddModelError(string.Empty, "Please confirm your email address before signing in. Check your inbox for the confirmation link, or request a new one below.");
                    return Page();
                }
                
                if (result.RequiresTwoFactor)
                {
                    return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, RememberMe = Input.RememberMe });
                }
                
                if (result.IsLockedOut)
                {
                    // SECURITY: Log lockout event with IP address
                    var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                    _logger.LogWarning("SECURITY: User account {Email} locked out due to multiple failed login attempts from IP {IPAddress}", 
                        Input.Email, ipAddress);
                    return RedirectToPage("./Lockout");
                }
                else
                {
                    // SECURITY: Log failed login attempt with IP address
                    var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                    _logger.LogWarning("SECURITY: Failed login attempt for user {Email} from IP {IPAddress}", 
                        Input.Email, ipAddress);
                    
                    // SECURITY: Add delay to prevent timing attacks and slow down brute force attempts
                    await Task.Delay(1000).ConfigureAwait(false); // SECURITY: Increased delay
                    ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                    return Page();
                }
            }

            // If we got this far, something failed, redisplay form
            return Page();
        }
    }
}
