#nullable disable

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HikingStore.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class RegisterConfirmationModel : PageModel
    {
        public string Email { get; set; }

        public IActionResult OnGet(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return LocalRedirect("~/");
            }

            Email = email;
            return Page();
        }
    }
}
