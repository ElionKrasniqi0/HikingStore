using HikingStore.Data;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace HikingStore.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ConfirmEmailModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public ConfirmEmailModel(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public bool Succeeded { get; private set; }

        public async Task<IActionResult> OnGetAsync(string? userId, string? code)
        {
            if (userId == null || code == null)
            {
                return LocalRedirect("~/");
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Page();
            }

            try
            {
                code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            }
            catch (FormatException)
            {
                return Page();
            }

            var result = await _userManager.ConfirmEmailAsync(user, code);
            Succeeded = result.Succeeded;
            return Page();
        }
    }
}
