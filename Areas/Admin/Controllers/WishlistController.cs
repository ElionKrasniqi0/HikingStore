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
        private readonly UserManager<IdentityUser> _userManager;

        public WishlistController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index(string userId = "", string search = "")
        {
            var query = _context.Wishlists
                .Include(w => w.Product)
                    .ThenInclude(p => p.Category)
                .Include(w => w.IdentityUser)
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
                    w.Product.ProName.Contains(search) ||
                    w.IdentityUser.UserName.Contains(search) ||
                    w.IdentityUser.Email.Contains(search));
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
            var mostWishedProducts = await _context.Wishlists
                .Include(w => w.Product)
                    .ThenInclude(p => p.Category)
                .GroupBy(w => w.ProId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    Count = g.Count(),
                    Product = g.First().Product
                })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync();

            ViewBag.TotalWishlists = totalWishlists;
            ViewBag.UniqueUsers = uniqueUsers;
            ViewBag.MostWishedProducts = mostWishedProducts;

            return View();
        }
    }
}
