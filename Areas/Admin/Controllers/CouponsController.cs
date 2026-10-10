using HikingStore.Data;
using HikingStore.Models;
using HikingStore.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HikingStore.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = DbInitializer.AdminRole)]
    public class CouponsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly AdminAuditService _audit;

        public CouponsController(ApplicationDbContext context, AdminAuditService audit)
        {
            _context = context;
            _audit = audit;
        }

        public async Task<IActionResult> Index()
        {
            var coupons = await _context.Coupons.AsNoTracking().OrderByDescending(c => c.CreatedAt).ToListAsync();
            return View(coupons);
        }

        [HttpGet]
        public IActionResult Create() => View("Form", new Coupon());

        [HttpPost]
        public async Task<IActionResult> Create(Coupon model)
        {
            await ValidateAsync(model, null);
            if (!ModelState.IsValid) return View("Form", model);

            var coupon = new Coupon { CreatedAt = DateTime.UtcNow };
            Copy(model, coupon);
            _context.Coupons.Add(coupon);
            await _context.SaveChangesAsync();
            await _audit.LogAdminModificationAsync("Coupon", coupon.Code, "CREATE");
            TempData["Success"] = $"Coupon {coupon.Code} created.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var coupon = await _context.Coupons.AsNoTracking().FirstOrDefaultAsync(c => c.CouponId == id);
            return coupon == null ? NotFound() : View("Form", coupon);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(int id, Coupon model)
        {
            var coupon = await _context.Coupons.FirstOrDefaultAsync(c => c.CouponId == id);
            if (coupon == null) return NotFound();

            await ValidateAsync(model, id);
            if (!ModelState.IsValid)
            {
                model.CouponId = id;
                model.TimesUsed = coupon.TimesUsed;
                return View("Form", model);
            }

            Copy(model, coupon);
            await _context.SaveChangesAsync();
            await _audit.LogAdminModificationAsync("Coupon", coupon.Code, "UPDATE");
            TempData["Success"] = $"Coupon {coupon.Code} saved.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> ToggleActive(int id)
        {
            var coupon = await _context.Coupons.FirstOrDefaultAsync(c => c.CouponId == id);
            if (coupon == null) return NotFound();

            coupon.IsActive = !coupon.IsActive;
            await _context.SaveChangesAsync();
            TempData["Success"] = $"Coupon {coupon.Code} {(coupon.IsActive ? "activated" : "deactivated")}.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var coupon = await _context.Coupons.FirstOrDefaultAsync(c => c.CouponId == id);
            if (coupon == null) return NotFound();

            if (coupon.TimesUsed > 0 || await _context.Orders.AnyAsync(o => o.CouponCode == coupon.Code))
            {
                coupon.IsActive = false;
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Coupon {coupon.Code} has been used on orders, so it was deactivated instead of deleted.";
                return RedirectToAction(nameof(Index));
            }

            _context.Coupons.Remove(coupon);
            await _context.SaveChangesAsync();
            await _audit.LogAdminDeletionAsync("Coupon", coupon.Code);
            TempData["Success"] = $"Coupon {coupon.Code} deleted.";
            return RedirectToAction(nameof(Index));
        }

        private async Task ValidateAsync(Coupon model, int? editingId)
        {
            model.Code = CouponService.Normalize(model.Code);
            ModelState.Remove(nameof(Coupon.Code));
            if (string.IsNullOrEmpty(model.Code) || model.Code.Length is < 3 or > 40 || !model.Code.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'))
            {
                ModelState.AddModelError(nameof(Coupon.Code), "Codes must be 3–40 letters, numbers, dashes or underscores.");
            }
            else if (await _context.Coupons.AnyAsync(c => c.Code == model.Code && c.CouponId != editingId))
            {
                ModelState.AddModelError(nameof(Coupon.Code), "Another coupon already uses this code.");
            }

            if (model.DiscountType == DiscountType.Percentage && model.Value > 100)
            {
                ModelState.AddModelError(nameof(Coupon.Value), "A percentage discount can't exceed 100%.");
            }
            if (model.StartsAt.HasValue && model.ExpiresAt.HasValue && model.ExpiresAt <= model.StartsAt)
            {
                ModelState.AddModelError(nameof(Coupon.ExpiresAt), "The expiry date must be after the start date.");
            }
        }

        private static void Copy(Coupon source, Coupon target)
        {
            target.Code = source.Code;
            target.Description = string.IsNullOrWhiteSpace(source.Description) ? null : source.Description.Trim();
            target.DiscountType = source.DiscountType;
            target.Value = source.Value;
            target.MinimumOrderAmount = source.MinimumOrderAmount is > 0 ? source.MinimumOrderAmount : null;
            target.MaxDiscountAmount = source.MaxDiscountAmount is > 0 ? source.MaxDiscountAmount : null;
            target.StartsAt = source.StartsAt.HasValue ? DateTime.SpecifyKind(source.StartsAt.Value, DateTimeKind.Utc) : null;
            target.ExpiresAt = source.ExpiresAt.HasValue ? DateTime.SpecifyKind(source.ExpiresAt.Value, DateTimeKind.Utc) : null;
            target.UsageLimit = source.UsageLimit;
            target.UsageLimitPerUser = source.UsageLimitPerUser;
            target.IsActive = source.IsActive;
        }
    }
}
