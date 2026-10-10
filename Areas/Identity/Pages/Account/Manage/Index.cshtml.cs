using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HikingStore.Areas.Identity.Pages.Account.Manage
{
    public class IndexModel : PageModel
    {
        public IActionResult OnGet() => RedirectToAction("Profile", "Account", new { area = "" });
    }
}
