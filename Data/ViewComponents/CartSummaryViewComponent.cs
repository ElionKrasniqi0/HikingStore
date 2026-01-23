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
                    return View(new CartSummaryViewModel { ItemCount = sessionCartCount, CartItems = new List<CartItemViewModel>(), TotalAmount = 0 });
                }

                var cartItems = await _context.ShoppingCarts
                    .Include(c => c.Product)
                    .ThenInclude(p => p.Category)
                    .Where(c => c.UserId == userId)
                    .AsNoTracking()
                    .ToListAsync();

                var totalItems = cartItems.Sum(c => c.Qty);
                var totalAmount = cartItems.Sum(c => c.Product.Price * c.Qty);

                var viewModel = new CartSummaryViewModel
                {
                    ItemCount = totalItems,
                    CartItems = cartItems.Select(c => new CartItemViewModel
                    {
                        CartId = c.CartId,
                        ProductId = c.ProId,
                        ProductName = c.Product.ProName ?? "Unknown Product",
                        ProductImage = c.Product.ProImage ?? "",
                        Price = c.Product.Price,
                        Quantity = c.Qty,
                        ItemTotal = c.Product.Price * c.Qty
                    }).ToList(),
                    TotalAmount = totalAmount
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading cart summary for user");
                return View(new CartSummaryViewModel { ItemCount = 0, CartItems = new List<CartItemViewModel>(), TotalAmount = 0 });
            }
        }
    }

    public class CartSummaryViewModel
    {
        public int ItemCount { get; set; }
        public List<CartItemViewModel> CartItems { get; set; } = new List<CartItemViewModel>();
        public decimal TotalAmount { get; set; }
    }

    public class CartItemViewModel
    {
        public int CartId { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public string ProductImage { get; set; } = "";
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public decimal ItemTotal { get; set; }
    }
}
