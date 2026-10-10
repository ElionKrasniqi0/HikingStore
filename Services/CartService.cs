using HikingStore.Data;
using HikingStore.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HikingStore.Services
{
    public class CartLine
    {
        /// <summary>"c{CartId}" for saved carts, "s{index}" for guest session carts.</summary>
        public string LineKey { get; set; } = string.Empty;
        public int ProId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Image { get; set; }
        public string? Brand { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal? CompareAtPrice { get; set; }
        public int Quantity { get; set; }
        public string? Size { get; set; }
        public string? Color { get; set; }
        public int AvailableStock { get; set; }
        public bool IsAvailable { get; set; }
        public string? Issue { get; set; }
        public decimal LineTotal => UnitPrice * Quantity;
    }

    public class CartSummary
    {
        public List<CartLine> Lines { get; set; } = new();
        public int ItemCount => Lines.Sum(l => l.Quantity);
        public bool IsEmpty => Lines.Count == 0;
        public bool HasIssues => Lines.Any(l => l.Issue != null);
        public decimal Subtotal => Lines.Where(l => l.IsAvailable).Sum(l => l.LineTotal);
        public string? CouponCode { get; set; }
        public string? CouponDescription { get; set; }
        public string? CouponError { get; set; }
        public decimal Discount { get; set; }
        public ShippingMethodOption ShippingMethod { get; set; } = new();
        public decimal ShippingCost { get; set; }
        public decimal Total => Math.Max(0, Subtotal - Discount) + ShippingCost;
        public decimal AmountUntilFreeShipping { get; set; }
    }

    public record CartOperationResult(bool Success, string Message);

    /// <summary>
    /// Single source of truth for cart contents and pricing. Prices, discounts and shipping are
    /// always recalculated from the database; nothing submitted by the browser is trusted.
    /// </summary>
    public class CartService
    {
        public const int MaxQuantityPerLine = 99;
        private const string CouponSessionKey = "Cart.CouponCode";

        private readonly ApplicationDbContext _context;
        private readonly SessionCartService _sessionCart;
        private readonly CouponService _couponService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly StoreSettings _settings;

        public CartService(
            ApplicationDbContext context,
            SessionCartService sessionCart,
            CouponService couponService,
            IHttpContextAccessor httpContextAccessor,
            IOptions<StoreSettings> settings)
        {
            _context = context;
            _sessionCart = sessionCart;
            _couponService = couponService;
            _httpContextAccessor = httpContextAccessor;
            _settings = settings.Value;
        }

        public async Task<int> GetItemCountAsync(string? userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return _sessionCart.GetSessionCartCount();
            }
            return await _context.ShoppingCarts.Where(c => c.UserId == userId).SumAsync(c => (int?)c.Qty) ?? 0;
        }

        public async Task<CartOperationResult> AddAsync(string? userId, int proId, int quantity, string? size, string? color)
        {
            if (quantity < 1 || quantity > MaxQuantityPerLine)
            {
                return new(false, $"Quantity must be between 1 and {MaxQuantityPerLine}.");
            }

            var product = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.ProId == proId && p.IsActive);
            if (product == null)
            {
                return new(false, "This product is no longer available.");
            }

            var variantError = ValidateVariant(product, ref size, ref color);
            if (variantError != null)
            {
                return new(false, variantError);
            }

            if (string.IsNullOrEmpty(userId))
            {
                var cart = _sessionCart.GetSessionCart();
                var inCart = cart.Where(c => c.ProId == proId).Sum(c => c.Qty);
                if (inCart + quantity > product.StockQuantity)
                {
                    return new(false, StockMessage(product.StockQuantity, inCart));
                }

                var existing = cart.FirstOrDefault(c => c.ProId == proId && c.Size == size && c.Color == color);
                if (existing != null)
                {
                    existing.Qty = Math.Min(existing.Qty + quantity, MaxQuantityPerLine);
                }
                else
                {
                    cart.Add(new SessionCartItem { ProId = proId, Qty = quantity, Size = size, Color = color });
                }
                _sessionCart.SaveSessionCart(cart);
                return new(true, "Added to your cart.");
            }

            var userLines = await _context.ShoppingCarts.Where(c => c.UserId == userId && c.ProId == proId).ToListAsync();
            var alreadyInCart = userLines.Sum(c => c.Qty);
            if (alreadyInCart + quantity > product.StockQuantity)
            {
                return new(false, StockMessage(product.StockQuantity, alreadyInCart));
            }

            var line = userLines.FirstOrDefault(c => c.SelectedSize == size && c.SelectedColor == color);
            if (line != null)
            {
                line.Qty = Math.Min(line.Qty + quantity, MaxQuantityPerLine);
            }
            else
            {
                _context.ShoppingCarts.Add(new ShoppingCart
                {
                    UserId = userId,
                    ProId = proId,
                    Qty = quantity,
                    SelectedSize = size,
                    SelectedColor = color
                });
            }

            await _context.SaveChangesAsync();
            return new(true, "Added to your cart.");
        }

        public async Task<CartOperationResult> UpdateQuantityAsync(string? userId, string lineKey, int quantity)
        {
            if (quantity < 0 || quantity > MaxQuantityPerLine)
            {
                return new(false, $"Quantity must be between 1 and {MaxQuantityPerLine}.");
            }
            if (quantity == 0)
            {
                return await RemoveAsync(userId, lineKey);
            }

            if (string.IsNullOrEmpty(userId))
            {
                var cart = _sessionCart.GetSessionCart();
                if (!TryParseSessionKey(lineKey, cart.Count, out var index))
                {
                    return new(false, "Cart item not found.");
                }

                var item = cart[index];
                var stock = await _context.Products.Where(p => p.ProId == item.ProId).Select(p => (int?)p.StockQuantity).FirstOrDefaultAsync() ?? 0;
                var otherQty = cart.Where((c, i) => i != index && c.ProId == item.ProId).Sum(c => c.Qty);
                if (quantity + otherQty > stock)
                {
                    return new(false, StockMessage(stock, otherQty));
                }
                item.Qty = quantity;
                _sessionCart.SaveSessionCart(cart);
                return new(true, "Cart updated.");
            }

            if (!TryParseCartKey(lineKey, out var cartId))
            {
                return new(false, "Cart item not found.");
            }

            var line = await _context.ShoppingCarts.Include(c => c.Product)
                .FirstOrDefaultAsync(c => c.CartId == cartId && c.UserId == userId);
            if (line?.Product == null)
            {
                return new(false, "Cart item not found.");
            }

            var otherLinesQty = await _context.ShoppingCarts
                .Where(c => c.UserId == userId && c.ProId == line.ProId && c.CartId != cartId)
                .SumAsync(c => (int?)c.Qty) ?? 0;
            if (quantity + otherLinesQty > line.Product.StockQuantity)
            {
                return new(false, StockMessage(line.Product.StockQuantity, otherLinesQty));
            }

            line.Qty = quantity;
            await _context.SaveChangesAsync();
            return new(true, "Cart updated.");
        }

        public async Task<CartOperationResult> RemoveAsync(string? userId, string lineKey)
        {
            if (string.IsNullOrEmpty(userId))
            {
                var cart = _sessionCart.GetSessionCart();
                if (!TryParseSessionKey(lineKey, cart.Count, out var index))
                {
                    return new(false, "Cart item not found.");
                }
                cart.RemoveAt(index);
                _sessionCart.SaveSessionCart(cart);
                return new(true, "Item removed.");
            }

            if (!TryParseCartKey(lineKey, out var cartId))
            {
                return new(false, "Cart item not found.");
            }

            var deleted = await _context.ShoppingCarts.Where(c => c.CartId == cartId && c.UserId == userId).ExecuteDeleteAsync();
            return deleted > 0 ? new(true, "Item removed.") : new(false, "Cart item not found.");
        }

        public async Task ClearAsync(string? userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                _sessionCart.ClearSessionCart();
            }
            else
            {
                await _context.ShoppingCarts.Where(c => c.UserId == userId).ExecuteDeleteAsync();
            }
            RemoveCoupon();
        }

        /// <summary>Moves a guest's session cart into their account after sign-in, respecting stock.</summary>
        public async Task MergeGuestCartAsync(string userId)
        {
            var guestItems = _sessionCart.GetSessionCart();
            if (guestItems.Count == 0) return;

            foreach (var item in guestItems)
            {
                await AddAsync(userId, item.ProId, item.Qty, item.Size, item.Color);
            }
            _sessionCart.ClearSessionCart();
        }

        public string? GetAppliedCouponCode() =>
            _httpContextAccessor.HttpContext?.Session.GetString(CouponSessionKey);

        public void SetCoupon(string code) =>
            _httpContextAccessor.HttpContext?.Session.SetString(CouponSessionKey, CouponService.Normalize(code));

        public void RemoveCoupon() =>
            _httpContextAccessor.HttpContext?.Session.Remove(CouponSessionKey);

        public async Task<CartSummary> GetSummaryAsync(string? userId, string? shippingMethodCode = null)
        {
            var summary = new CartSummary { Lines = await LoadLinesAsync(userId) };

            var couponCode = GetAppliedCouponCode();
            if (!string.IsNullOrEmpty(couponCode))
            {
                if (string.IsNullOrEmpty(userId))
                {
                    summary.CouponCode = couponCode;
                    summary.CouponError = "Sign in to use this coupon.";
                }
                else
                {
                    var result = await _couponService.ValidateAsync(couponCode, userId, summary.Subtotal);
                    summary.CouponCode = couponCode;
                    if (result.IsValid)
                    {
                        summary.Discount = result.Discount;
                        summary.CouponDescription = result.Coupon!.Description ?? result.Coupon.Summary;
                    }
                    else
                    {
                        summary.CouponError = result.Error;
                    }
                }
            }

            var method = _settings.FindShippingMethod(shippingMethodCode) ?? _settings.DefaultShippingMethod;
            var afterDiscount = Math.Max(0, summary.Subtotal - summary.Discount);
            summary.ShippingMethod = method;
            summary.ShippingCost = summary.IsEmpty ? 0 : _settings.ShippingCostFor(method, afterDiscount);
            summary.AmountUntilFreeShipping = _settings.FreeShippingThreshold > afterDiscount
                ? _settings.FreeShippingThreshold - afterDiscount
                : 0;
            return summary;
        }

        private async Task<List<CartLine>> LoadLinesAsync(string? userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                var items = _sessionCart.GetSessionCart();
                var ids = items.Select(i => i.ProId).Distinct().ToList();
                var products = await _context.Products.AsNoTracking().Where(p => ids.Contains(p.ProId)).ToDictionaryAsync(p => p.ProId);
                return items.Select((item, index) =>
                {
                    products.TryGetValue(item.ProId, out var product);
                    return BuildLine($"s{index}", product, item.ProId, item.Qty, item.Size, item.Color);
                }).ToList();
            }

            var rows = await _context.ShoppingCarts.AsNoTracking()
                .Include(c => c.Product)
                .Where(c => c.UserId == userId)
                .OrderBy(c => c.CartId)
                .ToListAsync();
            return rows.Select(r => BuildLine($"c{r.CartId}", r.Product, r.ProId, r.Qty, r.SelectedSize, r.SelectedColor)).ToList();
        }

        private static CartLine BuildLine(string key, Product? product, int proId, int qty, string? size, string? color)
        {
            var line = new CartLine
            {
                LineKey = key,
                ProId = proId,
                Quantity = qty,
                Size = size,
                Color = color,
                Name = product?.ProName ?? "Unavailable product",
                Image = product?.ProImage,
                Brand = product?.Brand,
                UnitPrice = product?.Price ?? 0,
                CompareAtPrice = product?.IsOnSale == true ? product.CompareAtPrice : null,
                AvailableStock = product?.StockQuantity ?? 0,
                IsAvailable = product != null && product.IsActive && product.StockQuantity > 0
            };

            if (product == null || !product.IsActive)
            {
                line.Issue = "This product is no longer available. Please remove it.";
            }
            else if (product.StockQuantity <= 0)
            {
                line.Issue = "Out of stock. Please remove it to continue.";
            }
            else if (qty > product.StockQuantity)
            {
                line.Issue = $"Only {product.StockQuantity} left in stock. Please reduce the quantity.";
            }
            return line;
        }

        /// <summary>Normalizes the chosen size/color against the product's options. Returns an error message when invalid.</summary>
        public static string? ValidateVariant(Product product, ref string? size, ref string? color)
        {
            var sizes = product.SizeOptions;
            var colors = product.ColorOptions;

            if (sizes.Count > 0)
            {
                var requested = size?.Trim();
                var match = sizes.FirstOrDefault(s => string.Equals(s, requested, StringComparison.OrdinalIgnoreCase));
                if (match == null) return "Please choose an available size.";
                size = match;
            }
            else
            {
                size = null;
            }

            if (colors.Count > 0)
            {
                var requested = color?.Trim();
                var match = colors.FirstOrDefault(c => string.Equals(c, requested, StringComparison.OrdinalIgnoreCase));
                if (match == null) return "Please choose an available color.";
                color = match;
            }
            else
            {
                color = null;
            }

            return null;
        }

        private static string StockMessage(int stock, int alreadyInCart) =>
            stock <= 0
                ? "Sorry, this product is out of stock."
                : alreadyInCart > 0
                    ? $"Only {stock} in stock and you already have {alreadyInCart} in your cart."
                    : $"Only {stock} in stock.";

        private static bool TryParseCartKey(string key, out int cartId)
        {
            cartId = 0;
            return key.StartsWith('c') && int.TryParse(key.AsSpan(1), out cartId);
        }

        private static bool TryParseSessionKey(string key, int count, out int index)
        {
            index = -1;
            return key.StartsWith('s') && int.TryParse(key.AsSpan(1), out index) && index >= 0 && index < count;
        }
    }
}
