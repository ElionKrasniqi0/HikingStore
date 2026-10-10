using HikingStore.Data;
using HikingStore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HikingStore.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class WishlistController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public WishlistController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index(string userId = "", string search = "")
        {
            var query = _context.Wishlists
                .Include(w => w.Product)
                    .ThenInclude(p => p.Category)
                .Include(w => w.ApplicationUser)
                .AsQueryable();

            // Filter by user if specified
            if (!string.IsNullOrEmpty(userId))
            {
                query = query.Where(w => w.UserId == userId);
            }

            // Search functionality
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(w =>
                    w.Product!.ProName!.Contains(search) ||
                    w.ApplicationUser!.UserName!.Contains(search) ||
                    w.ApplicationUser!.Email!.Contains(search));
            }

            var wishlists = await query
                .OrderByDescending(w => w.AddedDate)
                .ToListAsync();

            ViewBag.Search = search;
            ViewBag.UserId = userId;

            // Get all users for filter dropdown
            var users = await _context.Users
                .Select(u => new { u.Id, u.UserName })
                .ToListAsync();
            ViewBag.Users = users;

            return View(wishlists);
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var wishlistItem = await _context.Wishlists.FindAsync(id);
            if (wishlistItem == null)
            {
                return Json(new { success = false, message = "Wishlist item not found." });
            }

            _context.Wishlists.Remove(wishlistItem);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Wishlist item deleted successfully." });
        }

        [HttpGet]
        public async Task<IActionResult> Statistics()
        {
            var totalWishlists = await _context.Wishlists.CountAsync();
            var uniqueUsers = await _context.Wishlists.Select(w => w.UserId).Distinct().CountAsync();
            var counts = await _context.Wishlists
                .GroupBy(w => w.ProId)
                .Select(g => new { ProductId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync();
            var ids = counts.Select(c => c.ProductId).ToList();
            var products = await _context.Products.AsNoTracking().Include(p => p.Category)
                .Where(p => ids.Contains(p.ProId)).ToDictionaryAsync(p => p.ProId);
            var mostWishedProducts = counts
                .Where(c => products.ContainsKey(c.ProductId))
                .Select(c => new MostWishedProduct(c.ProductId, c.Count, products[c.ProductId]))
                .ToList();

            ViewBag.TotalWishlists = totalWishlists;
            ViewBag.UniqueUsers = uniqueUsers;
            ViewBag.MostWishedProducts = mostWishedProducts;

            return View();
        }
    }

    public record MostWishedProduct(int ProductId, int Count, Product Product);
}
