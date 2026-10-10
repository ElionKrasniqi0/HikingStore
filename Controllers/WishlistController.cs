using HikingStore.Data;
using HikingStore.Models;
using HikingStore.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HikingStore.Controllers
{
    [Authorize]
    public class WishlistController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly CatalogService _catalog;

        public WishlistController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, CatalogService catalog)
        {
            _context = context;
            _userManager = userManager;
            _catalog = catalog;
        }

        private string UserId => _userManager.GetUserId(User)!;

        public async Task<IActionResult> Index()
        {
            var products = await _context.Wishlists.AsNoTracking()
                .Where(w => w.UserId == UserId && w.Product!.IsActive)
                .OrderByDescending(w => w.AddedDate)
                .Select(w => w.Product!)
                .ToListAsync();
            return View(await _catalog.BuildCardsAsync(products, UserId));
        }

        [HttpPost]
        public async Task<IActionResult> Toggle(int productId)
        {
            var existing = await _context.Wishlists.FirstOrDefaultAsync(w => w.UserId == UserId && w.ProId == productId);
            bool inWishlist;
            string message;
            if (existing != null)
            {
                _context.Wishlists.Remove(existing);
                inWishlist = false;
                message = "Removed from your wishlist.";
            }
            else
            {
                if (!await _context.Products.AnyAsync(p => p.ProId == productId && p.IsActive))
                {
                    return Json(new { success = false, message = "Product not found." });
                }
                _context.Wishlists.Add(new Wishlist { UserId = UserId, ProId = productId, AddedDate = DateTime.UtcNow });
                inWishlist = true;
                message = "Saved to your wishlist.";
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                _context.ChangeTracker.Clear();
                inWishlist = await _context.Wishlists.AnyAsync(w => w.UserId == UserId && w.ProId == productId);
                message = inWishlist ? "Saved to your wishlist." : "Removed from your wishlist.";
            }

            return Json(new { success = true, inWishlist, message, count = await CountAsync() });
        }

        [HttpPost]
        public async Task<IActionResult> AddToWishlist(int productId)
        {
            if (await _context.Wishlists.AnyAsync(w => w.UserId == UserId && w.ProId == productId))
            {
                return Json(new { success = true, message = "Already in your wishlist.", count = await CountAsync() });
            }
            return await Toggle(productId);
        }

        [HttpPost]
        public async Task<IActionResult> RemoveFromWishlist(int productId)
        {
            await _context.Wishlists.Where(w => w.UserId == UserId && w.ProId == productId).ExecuteDeleteAsync();

            if (Request.Headers.XRequestedWith == "XMLHttpRequest")
            {
                return Json(new { success = true, message = "Removed from your wishlist.", count = await CountAsync() });
            }
            TempData["Success"] = "Removed from your wishlist.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> GetWishlistCount() => Json(new { count = await CountAsync() });

        private Task<int> CountAsync() => _context.Wishlists.CountAsync(w => w.UserId == UserId && w.Product!.IsActive);
    }
}
