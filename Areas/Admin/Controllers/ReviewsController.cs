using HikingStore.Data;
using HikingStore.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HikingStore.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = DbInitializer.AdminRole)]
    public class ReviewsController : Controller
    {
        private const int PageSize = 30;
        private readonly ApplicationDbContext _context;
        private readonly AdminAuditService _audit;

        public ReviewsController(ApplicationDbContext context, AdminAuditService audit)
        {
            _context = context;
            _audit = audit;
        }

        public async Task<IActionResult> Index(string status = "pending", int page = 1)
        {
            var query = _context.Reviews.AsNoTracking().Include(r => r.Product).Include(r => r.User).AsQueryable();
            query = status switch
            {
                "approved" => query.Where(r => r.IsApproved),
                "all" => query,
                _ => query.Where(r => !r.IsApproved)
            };

            var total = await query.CountAsync();
            var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
            page = Math.Clamp(page, 1, pageCount);

            ViewBag.Status = status;
            ViewBag.Page = page;
            ViewBag.PageCount = pageCount;
            ViewBag.PendingCount = await _context.Reviews.CountAsync(r => !r.IsApproved);
            var reviews = await query.OrderByDescending(r => r.CreatedAt).Skip((page - 1) * PageSize).Take(PageSize).ToListAsync();
            return View(reviews);
        }

        [HttpPost]
        public async Task<IActionResult> Approve(int id, string? status)
        {
            var updated = await _context.Reviews.Where(r => r.ReviewId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsApproved, true));
            if (updated > 0)
            {
                await _audit.LogAdminModificationAsync("Review", id.ToString(), "APPROVE");
                TempData["Success"] = "Review approved and published.";
            }
            return RedirectToAction(nameof(Index), new { status });
        }

        [HttpPost]
        public async Task<IActionResult> Unpublish(int id, string? status)
        {
            var updated = await _context.Reviews.Where(r => r.ReviewId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsApproved, false));
            if (updated > 0)
            {
                await _audit.LogAdminModificationAsync("Review", id.ToString(), "UNPUBLISH");
                TempData["Success"] = "Review hidden from the store.";
            }
            return RedirectToAction(nameof(Index), new { status });
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id, string? status)
        {
            var deleted = await _context.Reviews.Where(r => r.ReviewId == id).ExecuteDeleteAsync();
            if (deleted > 0)
            {
                await _audit.LogAdminDeletionAsync("Review", id.ToString());
                TempData["Success"] = "Review deleted.";
            }
            return RedirectToAction(nameof(Index), new { status });
        }
    }
}
