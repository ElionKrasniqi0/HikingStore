using HikingStore.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HikingStore.Data.ViewComponents
{
    public class CartSummaryViewComponent : ViewComponent
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<CartSummaryViewComponent> _logger;

        public CartSummaryViewComponent(ApplicationDbContext context, ILogger<CartSummaryViewComponent> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            try
            {
                var userId = UserClaimsPrincipal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return View(0);
                }

                var cartItems = await _context.ShoppingCarts
                    .Where(c => c.UserId == userId)
                    .ToListAsync();

                var totalItems = cartItems.Sum(c => c.Qty);
                return View(totalItems);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading cart summary for user");
                return View(0);
            }
        }
    }
}
