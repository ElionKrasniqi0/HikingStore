using HikingStore.Data;
using HikingStore.Enum;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HikingStore.Controllers
{
    [Authorize]
    public class OrdersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public OrdersController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index(string status = "")
        {
            var userId = _userManager.GetUserId(User);

            // Optimize: Get status counts in a single query using GroupBy
            var statusCountsQuery = await _context.Orders
                .Where(o => o.UserId == userId)
                .AsNoTracking()
                .GroupBy(o => o.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            var statusCounts = new Dictionary<OrderStatus, int>();
            foreach (OrderStatus orderStatus in System.Enum.GetValues(typeof(OrderStatus)))
            {
                statusCounts[orderStatus] = 0;
            }
            foreach (var item in statusCountsQuery)
            {
                statusCounts[item.Status] = item.Count;
            }

            var totalCount = statusCounts.Values.Sum();

            // Now filter the query if status is provided
            var query = _context.Orders
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .AsNoTracking()
                .Where(o => o.UserId == userId);

            // Filter by status - FIXED: Use System.Enum explicitly
            if (!string.IsNullOrEmpty(status))
            {
                if (System.Enum.TryParse<OrderStatus>(status, out var orderStatus))
                {
                    query = query.Where(o => o.Status == orderStatus);
                }
            }

            var orders = await query
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            ViewBag.CurrentStatus = status;
            ViewBag.StatusCounts = statusCounts;
            ViewBag.TotalCount = totalCount;
            ViewBag.StatusList = System.Enum.GetValues(typeof(OrderStatus)).Cast<OrderStatus>();

            return View(orders);
        }

        public async Task<IActionResult> Details(int id)
        {
            var userId = _userManager.GetUserId(User);

            var order = await _context.Orders
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .ThenInclude(p => p.Category)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrderId == id && o.UserId == userId);

            if (order == null)
            {
                return NotFound();
            }

            return View(order);
        }

        [HttpPost]
        public async Task<IActionResult> CancelOrder(int id)
        {
            var userId = _userManager.GetUserId(User);
            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.OrderId == id && o.UserId == userId);

            if (order == null)
            {
                return Json(new { success = false, message = "Order not found." });
            }

            // Only allow cancellation for orders that haven't been shipped
            if (order.Status >= OrderStatus.Shipped)
            {
                return Json(new { success = false, message = "Cannot cancel order that has already been shipped." });
            }

            order.Status = OrderStatus.Cancelled;
            order.CancelledDate = DateTime.Now;

            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Order cancelled successfully." });
        }
    }
}

