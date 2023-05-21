using HikingStore.Data;
using Microsoft.AspNetCore.Mvc;

namespace HikingStore.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
        {
            _context = context;
        }
        public IActionResult Index()
        {
            return View();
        }
        public IActionResult ShowMessage()
        {
            var message = _context.Contacts.ToList();
            return View(message);
        }
    }
}
