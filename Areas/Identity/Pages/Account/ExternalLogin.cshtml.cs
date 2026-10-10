using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using HikingStore.Data;

namespace HikingStore.Areas.Identity.Pages.Account
{
    public class ExternalLoginModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IUserStore<ApplicationUser> _userStore;
        private readonly IEmailSender _emailSender;
        private readonly HikingStore.Services.CartService _cartService;
        private readonly ILogger<ExternalLoginModel> _logger;
        private readonly HikingStore.Services.IEmailTemplateService _templates;

        public ExternalLoginModel(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IUserStore<ApplicationUser> userStore,
            IEmailSender emailSender,
            HikingStore.Services.CartService cartService,
            ILogger<ExternalLoginModel> logger,
            HikingStore.Services.IEmailTemplateService templates)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _roleManager = roleManager;
            _userStore = userStore;
            _emailSender = emailSender;
            _cartService = cartService;
            _logger = logger;
            _templates = templates;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ProviderDisplayName { get; set; }

        public string ReturnUrl { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; }
        }

        public IActionResult OnGet() => RedirectToPage("./Login");

        public IActionResult OnPost(string provider, string returnUrl = null)
        {
            // Request a redirect to the external login provider.
            var redirectUrl = Url.Page("./ExternalLogin", pageHandler: "Callback", values: new { returnUrl });
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            return new ChallengeResult(provider, properties);
        }

        public async Task<IActionResult> OnGetCallbackAsync(string returnUrl = null, string remoteError = null)
        {
            returnUrl ??= Url.Content("~/");
            if (remoteError != null)
            {
                ErrorMessage = $"Error from external provider: {remoteError}";
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }
            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                ErrorMessage = "Error loading external login information.";
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }

            // Sign in the user with this external login provider if the user already has a login.
            var result = await _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
            if (result.Succeeded)
            {
                var signedInUser = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
                if (signedInUser != null)
                {
                    await _cartService.MergeGuestCartAsync(signedInUser.Id);
                }
                return LocalRedirect(returnUrl);
            }
            if (result.IsLockedOut)
            {
                return RedirectToPage("./Lockout");
            }
            else
            {
                // If the user does not have an account, then ask the user to create an account.
                ReturnUrl = returnUrl;
                ProviderDisplayName = info.ProviderDisplayName;
                if (info.Principal.HasClaim(c => c.Type == System.Security.Claims.ClaimTypes.Email))
                {
                    Input = new InputModel
                    {
                        Email = info.Principal.FindFirstValue(System.Security.Claims.ClaimTypes.Email)
                    };
                }
                return Page();
            }
        }

        public async Task<IActionResult> OnPostConfirmationAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            // Get the information about the user from the external login provider
            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                ErrorMessage = "Error loading external login information during confirmation.";
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }

            if (ModelState.IsValid)
            {
                // Check if a user with this email already exists
                var existingUser = await _userManager.FindByEmailAsync(Input.Email);
                
                if (existingUser != null)
                {
                    // Only link when the provider itself vouches for this email; the form field alone proves nothing.
                    var providerEmail = info.Principal.FindFirstValue(ClaimTypes.Email);
                    if (!string.Equals(providerEmail, Input.Email, StringComparison.OrdinalIgnoreCase))
                    {
                        ModelState.AddModelError(string.Empty,
                            $"An account with this email already exists. Sign in with your password, then connect {info.ProviderDisplayName} under Account settings › External logins.");
                        ProviderDisplayName = info.ProviderDisplayName;
                        ReturnUrl = returnUrl;
                        return Page();
                    }

                    var result = await _userManager.AddLoginAsync(existingUser, info);
                    if (result.Succeeded)
                    {
                        // Ensure user has User role if they don't have any role
                        const string userRoleName = "User";
                        if (await _roleManager.RoleExistsAsync(userRoleName))
                        {
                            var userRoles = await _userManager.GetRolesAsync(existingUser);
                            if (userRoles.Count == 0)
                            {
                                await _userManager.AddToRoleAsync(existingUser, userRoleName);
                            }
                        }

                        await _signInManager.SignInAsync(existingUser, isPersistent: false, info.LoginProvider);
                        await _cartService.MergeGuestCartAsync(existingUser.Id);
                        return LocalRedirect(returnUrl);
                    }
                    else
                    {
                        // Failed to link external login
                        foreach (var error in result.Errors)
                        {
                            ModelState.AddModelError(string.Empty, error.Description);
                        }
                    }
                }
                else
                {
                    // User doesn't exist, create a new user
                    var user = CreateUser();

                    await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
                    var emailStore = GetEmailStore();
                    await emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);

                    // Extract first and last name from external claims if available
                    if (info.Principal.HasClaim(c => c.Type == ClaimTypes.GivenName))
                    {
                        if (user is ApplicationUser appUser)
                        {
                            appUser.FirstName = info.Principal.FindFirstValue(ClaimTypes.GivenName);
                        }
                    }
                    if (info.Principal.HasClaim(c => c.Type == ClaimTypes.Surname))
                    {
                        if (user is ApplicationUser appUser)
                        {
                            appUser.LastName = info.Principal.FindFirstValue(ClaimTypes.Surname);
                        }
                    }

                    var result = await _userManager.CreateAsync(user);
                    if (result.Succeeded)
                    {
                        // Assign User role to new external login users
                        const string userRoleName = "User";
                        if (await _roleManager.RoleExistsAsync(userRoleName))
                        {
                            await _userManager.AddToRoleAsync(user, userRoleName);
                        }

                        result = await _userManager.AddLoginAsync(user, info);
                        if (result.Succeeded)
                        {
                            // Generate email confirmation token and send email
                            var userId = await _userManager.GetUserIdAsync(user);
                            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

                            var callbackUrl = Url.Page(
                                "/Account/ConfirmEmail",
                                pageHandler: null,
                                values: new { area = "Identity", userId = userId, code = code },
                                protocol: Request.Scheme);

                            try
                            {
                                await _emailSender.SendEmailAsync(Input.Email, "Confirm your HikingStore account",
                                    _templates.GenerateEmailConfirmationTemplate(
                                        HtmlEncoder.Default.Encode(callbackUrl), HtmlEncoder.Default.Encode(user.FirstName ?? Input.Email)));
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to send confirmation email for external-login user {UserId}", userId);
                            }

                            // If account confirmation is required, we need to show the link if we don't have a real email sender
                            if (_userManager.Options.SignIn.RequireConfirmedAccount)
                            {
                                return RedirectToPage("./RegisterConfirmation", new { email = Input.Email });
                            }

                            await _signInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);
                            return LocalRedirect(returnUrl);
                        }
                    }
                    foreach (var error in result.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }
                }
            }

            ProviderDisplayName = info.ProviderDisplayName;
            ReturnUrl = returnUrl;
            return Page();
        }

        private ApplicationUser CreateUser()
        {
            try
            {
                return Activator.CreateInstance<ApplicationUser>();
            }
            catch
            {
                throw new InvalidOperationException($"Can't create an instance of '{nameof(ApplicationUser)}'. " +
                    $"Ensure that '{nameof(ApplicationUser)}' is not an abstract class and has a parameterless constructor, or alternatively " +
                    $"override the external login page in /Areas/Identity/Pages/Account/ExternalLogin.cshtml");
            }
        }

        private IUserEmailStore<ApplicationUser> GetEmailStore()
        {
            if (!_userManager.SupportsUserEmail)
            {
                throw new NotSupportedException("The default UI requires a user store with email support.");
            }
            return (IUserEmailStore<ApplicationUser>)_userStore;
        }
    }
}
