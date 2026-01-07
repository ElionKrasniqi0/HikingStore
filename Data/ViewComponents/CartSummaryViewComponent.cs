using HikingStore.Data;
using HikingStore.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HikingStore.Data.ViewComponents
{
    public class CartSummaryViewComponent : ViewComponent
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<CartSummaryViewComponent> _logger;
        private readonly SessionCartService _sessionCartService;

        public CartSummaryViewComponent(ApplicationDbContext context, ILogger<CartSummaryViewComponent> logger, SessionCartService sessionCartService)
        {
            _context = context;
            _logger = logger;
            _sessionCartService = sessionCartService;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            try
            {
                var userId = UserClaimsPrincipal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    // Anonymous user - get count from session cart
                    var sessionCartCount = _sessionCartService.GetSessionCartCount();
                    return View(sessionCartCount);
                }

                var totalItems = await _context.ShoppingCarts
                    .Where(c => c.UserId == userId)
                    .AsNoTracking()
                    .SumAsync(c => (int?)c.Qty) ?? 0;
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
