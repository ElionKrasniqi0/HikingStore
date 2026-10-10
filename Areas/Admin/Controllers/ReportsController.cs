using System.Text;
using HikingStore.Areas.Admin.ViewModel;
using HikingStore.Data;
using HikingStore.Enum;
using HikingStore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HikingStore.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = DbInitializer.AdminRole)]
    public class ReportsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ReportsController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(DateTime? from, DateTime? to)
        {
            var end = (to ?? DateTime.UtcNow).Date;
            var start = (from ?? end.AddDays(-29)).Date;
            if (start > end) (start, end) = (end, start);
            if ((end - start).TotalDays > 366) start = end.AddDays(-366);
            var endExclusive = end.AddDays(1);

            var orders = _context.Orders.AsNoTracking()
                .Where(o => o.OrderDate >= start && o.OrderDate < endExclusive);
            var counted = orders.Where(o => o.Status != OrderStatus.Cancelled
                && (o.PaymentMethod != PaymentMethods.Card || o.PaymentStatus == PaymentStatuses.Paid));

            var model = new ReportViewModel
            {
                From = start,
                To = end,
                OrderCount = await counted.CountAsync(),
                CancelledCount = await orders.CountAsync(o => o.Status == OrderStatus.Cancelled),
                Revenue = await counted.SumAsync(o => (decimal?)o.TotalAmount) ?? 0,
                Discounts = await counted.SumAsync(o => (decimal?)o.DiscountAmount) ?? 0,
                ShippingCollected = await counted.SumAsync(o => (decimal?)o.ShippingCost) ?? 0,
                NewCustomers = await _context.Orders.GroupBy(o => o.UserId)
                    .Select(g => g.Min(o => o.OrderDate))
                    .CountAsync(first => first >= start && first < endExclusive)
            };

            var daily = await counted.GroupBy(o => o.OrderDate.Date)
                .Select(g => new { Date = g.Key, Orders = g.Count(), Revenue = g.Sum(o => o.TotalAmount) })
                .ToListAsync();
            model.Daily = Enumerable.Range(0, (int)(end - start).TotalDays + 1)
                .Select(i => start.AddDays(i))
                .Select(d => daily.FirstOrDefault(x => x.Date == d) is { } x ? new DailySales(d, x.Orders, x.Revenue) : new DailySales(d, 0, 0))
                .ToList();

            var lines = _context.OrderDetails.AsNoTracking()
                .Where(d => d.Order!.Status != OrderStatus.Cancelled && d.Order.OrderDate >= start && d.Order.OrderDate < endExclusive
                    && (d.Order.PaymentMethod != PaymentMethods.Card || d.Order.PaymentStatus == PaymentStatuses.Paid));

            model.TopProducts = (await lines
                    .GroupBy(d => new { d.ProId, Name = d.ProductName ?? d.Product!.ProName })
                    .Select(g => new { g.Key.ProId, g.Key.Name, Units = g.Sum(d => d.Quantity), Revenue = g.Sum(d => d.Price * d.Quantity) })
                    .OrderByDescending(g => g.Revenue).Take(10).ToListAsync())
                .Select(g => new TopProduct(g.ProId, g.Name ?? "Product", g.Units, g.Revenue)).ToList();

            model.ByCategory = (await lines
                    .GroupBy(d => d.Product!.Category!.CatName)
                    .Select(g => new { Name = g.Key, Units = g.Sum(d => d.Quantity), Revenue = g.Sum(d => d.Price * d.Quantity) })
                    .OrderByDescending(g => g.Revenue).ToListAsync())
                .Select(g => new CategorySales(g.Name ?? "Uncategorised", g.Units, g.Revenue)).ToList();

            model.Coupons = (await counted.Where(o => o.CouponCode != null)
                    .GroupBy(o => o.CouponCode)
                    .Select(g => new { Code = g.Key, Uses = g.Count(), Discount = g.Sum(o => o.DiscountAmount) })
                    .OrderByDescending(g => g.Uses).ToListAsync())
                .Select(g => new CouponUsage(g.Code!, g.Uses, g.Discount)).ToList();

            return View(model);
        }

        public async Task<IActionResult> Newsletter()
        {
            var subscribers = await _context.NewsletterSubscribers.AsNoTracking().OrderByDescending(s => s.CreatedAt).ToListAsync();
            return View(subscribers);
        }

        public async Task<IActionResult> NewsletterCsv()
        {
            var subscribers = await _context.NewsletterSubscribers.AsNoTracking().OrderBy(s => s.CreatedAt).ToListAsync();
            var csv = new StringBuilder("Email,SubscribedUtc\n");
            foreach (var s in subscribers)
            {
                var email = s.Email.Replace("\"", "\"\"");
                if (email.Length > 0 && "=+-@\t\r".Contains(email[0])) email = "'" + email;
                csv.Append('"').Append(email).Append("\",").Append(s.CreatedAt.ToString("yyyy-MM-dd HH:mm")).Append('\n');
            }
            return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"newsletter-{DateTime.UtcNow:yyyyMMdd}.csv");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteSubscriber(int id)
        {
            await _context.NewsletterSubscribers.Where(s => s.Id == id).ExecuteDeleteAsync();
            TempData["Success"] = "Subscriber removed.";
            return RedirectToAction(nameof(Newsletter));
        }
    }
}
