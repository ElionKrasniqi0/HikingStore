using HikingStore.Data;
using HikingStore.Enum;
using HikingStore.Hubs;
using HikingStore.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HikingStore.Controllers
{
    [Authorize]
    public class OrdersController : Controller
    {
        private const int PageSize = 10;

        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly OrderService _orderService;
        private readonly IHubContext<RealtimeHub> _hubContext;
        private readonly OrderEmailService _orderEmails;
        private readonly ILogger<OrdersController> _logger;

        public OrdersController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            OrderService orderService,
            IHubContext<RealtimeHub> hubContext,
            OrderEmailService orderEmails,
            ILogger<OrdersController> logger)
        {
            _context = context;
            _userManager = userManager;
            _orderService = orderService;
            _hubContext = hubContext;
            _orderEmails = orderEmails;
            _logger = logger;
        }

        public async Task<IActionResult> Index(string? status = null, int page = 1)
        {
            var userId = _userManager.GetUserId(User);

            var counts = await _context.Orders.AsNoTracking()
                .Where(o => o.UserId == userId)
                .GroupBy(o => o.Status)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Count);

            var query = _context.Orders.AsNoTracking()
                .Include(o => o.OrderDetails).ThenInclude(d => d.Product)
                .Where(o => o.UserId == userId);

            OrderStatus? filter = System.Enum.TryParse<OrderStatus>(status, out var parsed) ? parsed : null;
            if (filter.HasValue)
            {
                query = query.Where(o => o.Status == filter.Value);
            }

            var total = await query.CountAsync();
            var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
            page = Math.Clamp(page, 1, pageCount);

            ViewBag.StatusCounts = counts;
            ViewBag.TotalCount = counts.Values.Sum();
            ViewBag.CurrentStatus = filter;
            ViewBag.Page = page;
            ViewBag.PageCount = pageCount;

            var orders = await query.OrderByDescending(o => o.OrderDate)
                .Skip((page - 1) * PageSize).Take(PageSize)
                .ToListAsync();
            return View(orders);
        }

        public async Task<IActionResult> Details(int id)
        {
            var userId = _userManager.GetUserId(User);
            var order = await _context.Orders.AsNoTracking()
                .Include(o => o.OrderDetails).ThenInclude(d => d.Product)
                .Include(o => o.TrackingEvents)
                .FirstOrDefaultAsync(o => o.OrderId == id && o.UserId == userId);

            return order == null ? NotFound() : View(order);
        }

        public async Task<IActionResult> Invoice(int id, bool download = false)
        {
            var userId = _userManager.GetUserId(User);
            var order = await _context.Orders.AsNoTracking()
                .Include(o => o.User)
                .Include(o => o.OrderDetails).ThenInclude(d => d.Product)
                .FirstOrDefaultAsync(o => o.OrderId == id && o.UserId == userId);
            if (order == null)
            {
                return NotFound();
            }

            if (download)
            {
                Response.Headers.ContentDisposition = $"attachment; filename=Invoice_{order.OrderNumber}.html";
            }
            return View(order);
        }

        [HttpPost]
        public async Task<IActionResult> CancelOrder(int id)
        {
            var userId = _userManager.GetUserId(User)!;
            var (success, message) = await _orderService.CancelByCustomerAsync(id, userId);
            if (success)
            {
                _logger.LogInformation("Order {OrderId} cancelled by customer {UserId}", id, userId);
                await _hubContext.Clients.Group("Admins").SendAsync("OrderStatusUpdated", new { orderId = id, status = OrderStatus.Cancelled.ToString(), userId, message = $"Order #{HikingStore.Models.Order.FormatNumber(id)} was cancelled by the customer.", href = Url.Action("Details", "Orders", new { area = "Admin", id }) });
                _orderEmails.QueueStatusUpdate(id, $"{Request.Scheme}://{Request.Host}", notifyAdmin: true);
            }

            if (Request.Headers.XRequestedWith == "XMLHttpRequest")
            {
                return Json(new { success, message });
            }

            TempData[success ? "Success" : "Error"] = message;
            return RedirectToAction(nameof(Details), new { id });
        }
    }
}
