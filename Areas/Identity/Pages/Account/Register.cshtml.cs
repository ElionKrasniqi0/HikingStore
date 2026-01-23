using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using HikingStore.Data;

namespace HikingStore.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class RegisterModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ILogger<RegisterModel> _logger;
        private readonly IEmailSender _emailSender;

        public RegisterModel(
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager,
            RoleManager<IdentityRole> roleManager,
            ILogger<RegisterModel> logger,
            IEmailSender emailSender)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _logger = logger;
            _emailSender = emailSender;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ReturnUrl { get; set; }

        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        public class InputModel
        {
            [Required]
            [StringLength(50, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 2)]
            [Display(Name = "First Name")]
            public string FirstName { get; set; }

            [Required]
            [StringLength(50, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 2)]
            [Display(Name = "Last Name")]
            public string LastName { get; set; }

            [Required]
            [EmailAddress]
            [Display(Name = "Email")]
            public string Email { get; set; }

            [Required]
            [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 8)]
            [DataType(DataType.Password)]
            [Display(Name = "Password")]
            [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$", 
                ErrorMessage = "Password must be at least 8 characters and contain uppercase, lowercase, number, and special character.")]
            public string Password { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "Confirm password")]
            [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
            public string ConfirmPassword { get; set; }
        }

        public async Task OnGetAsync(string returnUrl = null)
        {
            ReturnUrl = returnUrl;
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync().ConfigureAwait(false)).ToList();
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            
            // ⚡ OPTIMIZATION: Skip ExternalLogins fetch on POST - not needed for registration
            // ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
            
            if (ModelState.IsValid)
            {
                var user = new ApplicationUser 
                { 
                    UserName = Input.Email, 
                    Email = Input.Email,
                    FirstName = Input.FirstName,
                    LastName = Input.LastName
                };
                
                // ⚡ OPTIMIZATION: CreateAsync is already async, but we use ConfigureAwait(false)
                var result = await _userManager.CreateAsync(user, Input.Password).ConfigureAwait(false);
                
                if (result.Succeeded)
                {
                    // SECURITY: Log user registration with IP address for audit trail
                    var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                    _logger.LogInformation("SECURITY: New user registered - Email: {Email}, IP: {IPAddress}", 
                        Input.Email, ipAddress);

                    // ⚡ OPTIMIZATION: Check and assign role asynchronously without blocking
                    // Move role assignment to background task to improve perceived performance
                    const string userRoleName = "User";
                    var userId = user.Id; // Capture before background task
                    
                    // Fire and forget role assignment - don't block registration
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // Re-fetch user in background task to avoid disposed context issues
                            var backgroundUser = await _userManager.FindByIdAsync(userId).ConfigureAwait(false);
                            if (backgroundUser != null)
                            {
                                var roleExists = await _roleManager.RoleExistsAsync(userRoleName).ConfigureAwait(false);
                                if (roleExists)
                                {
                                    await _userManager.AddToRoleAsync(backgroundUser, userRoleName).ConfigureAwait(false);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to assign role to user {UserId}", userId);
                        }
                    });

                    // ⚡ OPTIMIZATION: Generate email confirmation token and send email in background
                    // This prevents blocking the registration response
                    if (_userManager.Options.SignIn.RequireConfirmedAccount)
                    {
                        // Capture values before background task
                        var userEmail = Input.Email;
                        var emailUserId = user.Id;
                        var scheme = Request.Scheme;
                        var host = Request.Host.Value;
                        
                        // Fire and forget email sending - don't block registration
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                // Re-fetch user in background task to avoid disposed context issues
                                var backgroundUser = await _userManager.FindByIdAsync(emailUserId).ConfigureAwait(false);
                                if (backgroundUser == null) return;

                                var code = await _userManager.GenerateEmailConfirmationTokenAsync(backgroundUser).ConfigureAwait(false);
                                code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                                
                                // Build URL manually to avoid HttpContext issues in background task
                                var callbackUrl = $"{scheme}://{host}/Identity/Account/ConfirmEmail?userId={emailUserId}&code={code}&returnUrl={Uri.EscapeDataString(returnUrl)}";

                                await _emailSender.SendEmailAsync(userEmail, "Confirm your email",
                                    $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.").ConfigureAwait(false);
                                
                                _logger.LogInformation("Confirmation email sent successfully to {Email}", userEmail);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to send confirmation email to {Email}", userEmail);
                                // Don't block registration if email fails - user can request resend
                            }
                        });
                        
                        // ⚡ OPTIMIZATION: Redirect immediately, email sends in background
                        return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl = returnUrl });
                    }
                    else
                    {
                        // ⚡ OPTIMIZATION: Sign in immediately with ConfigureAwait(false)
                        await _signInManager.SignInAsync(user, isPersistent: false).ConfigureAwait(false);
                        return LocalRedirect(returnUrl);
                    }
                }
                
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            // If we got this far, something failed, redisplay form
            return Page();
        }
    }
}
