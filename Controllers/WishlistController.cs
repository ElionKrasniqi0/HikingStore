using HikingStore.Data;
using HikingStore.Models;
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
        private readonly UserManager<IdentityUser> _userManager;

        public WishlistController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);
            var wishlistItems = await _context.Wishlists
                .Include(w => w.Product)
                    .ThenInclude(p => p.Category)
                .Where(w => w.UserId == userId)
                .OrderByDescending(w => w.AddedDate)
                .ToListAsync();

            return View(wishlistItems);
        }

        [HttpPost]
        public async Task<IActionResult> AddToWishlist(int productId)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "Please login to add items to wishlist." });
            }

            var product = await _context.Products.FindAsync(productId);
            if (product == null)
            {
                return Json(new { success = false, message = "Product not found." });
            }

            // Check if already in wishlist
            var existingWishlist = await _context.Wishlists
                .FirstOrDefaultAsync(w => w.UserId == userId && w.ProId == productId);

            if (existingWishlist != null)
            {
                return Json(new { success = false, message = "Product already in wishlist." });
            }

            var wishlist = new Wishlist
            {
                UserId = userId,
                ProId = productId,
                AddedDate = DateTime.Now
            };

            _context.Wishlists.Add(wishlist);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Added to wishlist successfully." });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveFromWishlist(int productId)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "Please login to remove items from wishlist." });
            }

            var wishlistItem = await _context.Wishlists
                .FirstOrDefaultAsync(w => w.UserId == userId && w.ProId == productId);

            if (wishlistItem == null)
            {
                return Json(new { success = false, message = "Item not found in wishlist." });
            }

            _context.Wishlists.Remove(wishlistItem);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Removed from wishlist successfully." });
        }

        [HttpGet]
        public async Task<IActionResult> IsInWishlist(int productId)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { inWishlist = false });
            }

            var isInWishlist = await _context.Wishlists
                .AnyAsync(w => w.UserId == userId && w.ProId == productId);

            return Json(new { inWishlist = isInWishlist });
        }

        [HttpGet]
        public async Task<IActionResult> GetWishlistCount()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { count = 0 });
            }

            var count = await _context.Wishlists
                .Where(w => w.UserId == userId)
                .CountAsync();

            return Json(new { count = count });
        }
    }
}
