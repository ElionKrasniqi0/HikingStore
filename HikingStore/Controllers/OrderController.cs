using Microsoft.AspNetCore.Mvc;

namespace HikingStore.Controllers
{
    public class OrderController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
