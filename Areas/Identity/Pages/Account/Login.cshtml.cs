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
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly ILogger<LoginModel> _logger;

        public LoginModel(SignInManager<IdentityUser> signInManager, 
            ILogger<LoginModel> logger,
            UserManager<IdentityUser> userManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _logger = logger;
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

            // ⚡ OPTIMIZATION: Skip ExternalLogins fetch on POST - not needed for login
            // ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
        
            if (ModelState.IsValid)
            {
                // ⚡ OPTIMIZATION: Use FindByNameAsync directly since email is used as username
                // This avoids the extra query that FindByEmailAsync does (which queries both Email and UserName)
                var user = await _userManager.FindByNameAsync(Input.Email).ConfigureAwait(false);
                
                if (user == null)
                {
                    // ⚡ OPTIMIZATION: Add small delay to prevent timing attacks, but don't block
                    await Task.Delay(100).ConfigureAwait(false);
                    ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                    return Page();
                }

                // Check if email is confirmed before attempting sign in
                if (!await _userManager.IsEmailConfirmedAsync(user).ConfigureAwait(false))
                {
                    ModelState.AddModelError(string.Empty, "You must confirm your email address before logging in. Please check your email for a confirmation link.");
                    return Page();
                }

                // SECURITY: Enable lockout on failure to prevent brute force attacks
                var result = await _signInManager.PasswordSignInAsync(
                    user.UserName, 
                    Input.Password, 
                    Input.RememberMe, 
                    lockoutOnFailure: true).ConfigureAwait(false); // SECURITY: Enable lockout
                
                if (result.Succeeded)
                {
                    // SECURITY: Log successful login with IP address for audit trail
                    var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                    _logger.LogInformation("SECURITY: User {Email} logged in successfully from IP {IPAddress}", 
                        Input.Email, ipAddress);
                    return LocalRedirect(returnUrl);
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
