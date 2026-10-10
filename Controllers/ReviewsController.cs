using HikingStore.Data;
using HikingStore.Enum;
using HikingStore.Models;
using HikingStore.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HikingStore.Controllers
{
    [Authorize]
    public class ReviewsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public ReviewsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        [HttpPost]
        public async Task<IActionResult> Create(ReviewInput input)
        {
            var userId = _userManager.GetUserId(User)!;
            var product = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.ProId == input.ProId && p.IsActive);
            if (product == null)
            {
                return NotFound();
            }

            var redirect = Url.Action("ProductDetailes", "Home", new { id = product.ProId }) + "#reviews";

            if (!ModelState.IsValid)
            {
                TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return LocalRedirect(redirect);
            }

            var orders = _context.OrderDetails.Where(d => d.ProId == product.ProId && d.Order!.UserId == userId);
            if (!await orders.AnyAsync(d => d.Order!.Status != OrderStatus.Cancelled))
            {
                TempData["Error"] = "Only customers who bought this product can review it.";
                return LocalRedirect(redirect);
            }

            if (await _context.Reviews.AnyAsync(r => r.ProId == product.ProId && r.UserId == userId))
            {
                TempData["Error"] = "You have already reviewed this product.";
                return LocalRedirect(redirect);
            }

            _context.Reviews.Add(new Review
            {
                ProId = product.ProId,
                UserId = userId,
                Rating = input.Rating,
                Title = input.Title.Trim(),
                Comment = input.Comment.Trim(),
                IsVerifiedPurchase = await orders.AnyAsync(d => d.Order!.Status == OrderStatus.Completed),
                IsApproved = false,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            TempData["Success"] = "Thanks for your review! It will appear once our team has approved it.";
            return LocalRedirect(redirect);
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = _userManager.GetUserId(User);
            var review = await _context.Reviews.FirstOrDefaultAsync(r => r.ReviewId == id && r.UserId == userId);
            if (review == null)
            {
                return NotFound();
            }

            _context.Reviews.Remove(review);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Your review was deleted.";
            return RedirectToAction("Reviews", "Account");
        }
    }
}
