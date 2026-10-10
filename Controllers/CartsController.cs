using HikingStore.Data;
using HikingStore.Hubs;
using HikingStore.Models;
using HikingStore.Services;
using HikingStore.Services.Payments;
using HikingStore.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HikingStore.Controllers
{
    public class CartsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly CartService _cartService;
        private readonly CouponService _couponService;
        private readonly OrderService _orderService;
        private readonly OrderEmailService _orderEmails;
        private readonly StoreSettings _settings;
        private readonly IHubContext<RealtimeHub> _hubContext;
        private readonly PaymentService _paymentService;
        private readonly PaymentProviderRegistry _paymentRegistry;
        private readonly ILogger<CartsController> _logger;

        public CartsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            CartService cartService,
            CouponService couponService,
            OrderService orderService,
            OrderEmailService orderEmails,
            IOptions<StoreSettings> settings,
            IHubContext<RealtimeHub> hubContext,
            PaymentService paymentService,
            PaymentProviderRegistry paymentRegistry,
            ILogger<CartsController> logger)
        {
            _paymentService = paymentService;
            _paymentRegistry = paymentRegistry;
            _context = context;
            _userManager = userManager;
            _cartService = cartService;
            _couponService = couponService;
            _orderService = orderService;
            _orderEmails = orderEmails;
            _settings = settings.Value;
            _hubContext = hubContext;
            _logger = logger;
        }

        private string? CurrentUserId => _userManager.GetUserId(User);

        private bool IsAjax => Request.Headers.XRequestedWith == "XMLHttpRequest"
            || Request.Headers.Accept.ToString().Contains("application/json");

        public async Task<IActionResult> Index()
        {
            return View(await _cartService.GetSummaryAsync(CurrentUserId));
        }

        [HttpGet]
        public async Task<IActionResult> MiniCart()
        {
            return PartialView("_MiniCart", await _cartService.GetSummaryAsync(CurrentUserId));
        }

        [HttpGet]
        public async Task<IActionResult> GetCartCount()
        {
            return Json(new { count = await _cartService.GetItemCountAsync(CurrentUserId) });
        }

        [HttpPost]
        public async Task<IActionResult> AddToCart(int proId, int qty = 1, string? size = null, string? color = null, string? returnUrl = null)
        {
            var userId = CurrentUserId;
            var result = await _cartService.AddAsync(userId, proId, qty, size, color);
            var count = await _cartService.GetItemCountAsync(userId);

            if (result.Success && userId != null)
            {
                await NotifyCartChangedAsync(userId, count);
            }

            if (IsAjax)
            {
                return Json(new { success = result.Success, message = result.Message, cartItemCount = count });
            }

            TempData[result.Success ? "Success" : "Error"] = result.Message;
            if (!result.Success && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl!);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> UpdateCartItem(string lineKey, int quantity)
        {
            var userId = CurrentUserId;
            var result = await _cartService.UpdateQuantityAsync(userId, lineKey, quantity);
            return await CartResponseAsync(userId, result);
        }

        [HttpPost]
        public async Task<IActionResult> RemoveFromCart(string lineKey)
        {
            var userId = CurrentUserId;
            var result = await _cartService.RemoveAsync(userId, lineKey);
            return await CartResponseAsync(userId, result);
        }

        [HttpPost]
        public async Task<IActionResult> RemoveAll()
        {
            var userId = CurrentUserId;
            await _cartService.ClearAsync(userId);
            return await CartResponseAsync(userId, new CartOperationResult(true, "Your cart is now empty."));
        }

        [HttpPost]
        public async Task<IActionResult> ApplyCoupon(string? code)
        {
            var userId = CurrentUserId;
            if (userId == null)
            {
                TempData["Error"] = "Please sign in to use a coupon code.";
                return RedirectToAction(nameof(Index));
            }

            var summary = await _cartService.GetSummaryAsync(userId);
            var result = await _couponService.ValidateAsync(code, userId, summary.Subtotal);
            if (result.IsValid)
            {
                _cartService.SetCoupon(result.Coupon!.Code);
                TempData["Success"] = $"Coupon {result.Coupon.Code} applied: {_settings.FormatPrice(result.Discount)} off.";
            }
            else
            {
                TempData["Error"] = result.Error;
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public IActionResult RemoveCoupon()
        {
            _cartService.RemoveCoupon();
            TempData["Success"] = "Coupon removed.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Checkout()
        {
            var userId = CurrentUserId!;
            var model = await BuildCheckoutModelAsync(userId, null);
            if (model.Cart.IsEmpty)
            {
                TempData["Error"] = "Your cart is empty.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _userManager.GetUserAsync(User);
            var address = model.SavedAddresses.FirstOrDefault(a => a.IsDefault) ?? model.SavedAddresses.FirstOrDefault();
            model.Input = new CheckoutInput
            {
                CheckoutToken = Guid.NewGuid(),
                Email = user?.Email ?? string.Empty,
                FullName = address?.FullName ?? user?.DisplayName ?? string.Empty,
                PhoneNumber = address?.PhoneNumber ?? user?.PhoneNumber ?? string.Empty,
                AddressLine = address?.AddressLine ?? string.Empty,
                City = address?.City ?? string.Empty,
                PostalCode = address?.PostalCode,
                Country = address?.Country ?? "Kosovo",
                ShippingMethod = _settings.DefaultShippingMethod.Code,
                PaymentMethod = model.CashOnDeliveryAvailable || model.CardProviderName == null ? PaymentMethods.CashOnDelivery : PaymentMethods.Card,
                SaveAddress = model.SavedAddresses.Count == 0
            };
            return View(model);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Checkout([Bind(Prefix = "Input")] CheckoutInput input)
        {
            var userId = CurrentUserId!;

            if (ModelState.IsValid)
            {
                PlaceOrderResult result;
                try
                {
                    result = await _orderService.PlaceOrderAsync(userId, input);
                }
                catch (DbUpdateException ex)
                {
                    // A concurrent duplicate submit with the same token loses the unique-index race.
                    var existing = await _context.Orders.Where(o => o.CheckoutToken == input.CheckoutToken && o.UserId == userId)
                        .Select(o => (int?)o.OrderId).FirstOrDefaultAsync();
                    if (existing.HasValue)
                    {
                        return RedirectToAction(nameof(OrderConfirmation), new { id = existing.Value });
                    }
                    _logger.LogError(ex, "Checkout failed for user {UserId}", userId);
                    result = PlaceOrderResult.Fail("We couldn't place your order. Please try again.");
                }

                if (result.Success && input.PaymentMethod == PaymentMethods.Card)
                {
                    if (result.AlreadyPlaced)
                    {
                        return RedirectToAction("Status", "Payments", new { id = result.OrderId });
                    }
                    await NotifyCartChangedAsync(userId, 0);
                    var baseUrl = PaymentsController.ResolveBaseUrl(_paymentRegistry, Request);
                    var start = await _paymentService.StartPaymentAsync(result.OrderId, userId,
                        PaymentsController.BuildUrls(Url, baseUrl, result.OrderId), HttpContext.RequestAborted);
                    if (!start.Success)
                    {
                        TempData["PaymentError"] = start.Message;
                        return RedirectToAction("Status", "Payments", new { id = result.OrderId });
                    }
                    return PaymentsController.RedirectToProvider(this, start.Redirect!);
                }

                if (result.Success)
                {
                    if (!result.AlreadyPlaced)
                    {
                        await NotifyCartChangedAsync(userId, 0);
                        _orderEmails.QueueOrderConfirmation(result.OrderId, $"{Request.Scheme}://{Request.Host}");
                    }
                    return RedirectToAction(nameof(OrderConfirmation), new { id = result.OrderId });
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error);
                }
            }

            var model = await BuildCheckoutModelAsync(userId, input.ShippingMethod);
            if (model.Cart.IsEmpty)
            {
                return RedirectToAction(nameof(Index));
            }
            model.Input = input;
            return View(model);
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> CheckoutSummary(string? shippingMethod)
        {
            var summary = await _cartService.GetSummaryAsync(CurrentUserId, shippingMethod);
            return PartialView("_OrderSummary", summary);
        }

        [Authorize]
        public async Task<IActionResult> OrderConfirmation(int id)
        {
            var userId = CurrentUserId;
            var order = await _context.Orders.AsNoTracking()
                .Include(o => o.OrderDetails).ThenInclude(od => od.Product)
                .FirstOrDefaultAsync(o => o.OrderId == id && o.UserId == userId);

            if (order == null) return NotFound();
            if (order.IsCardPayment && order.PaymentStatus != PaymentStatuses.Paid)
            {
                return RedirectToAction("Status", "Payments", new { id });
            }
            return View(order);
        }

        [AllowAnonymous]
        public IActionResult RequireLogin(string? returnUrl = null) =>
            RedirectToPage("/Account/Login", new { area = "Identity", returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : "/Carts" });

        private async Task<CheckoutViewModel> BuildCheckoutModelAsync(string userId, string? shippingMethod)
        {
            return new CheckoutViewModel
            {
                Cart = await _cartService.GetSummaryAsync(userId, shippingMethod),
                ShippingMethods = _settings.ShippingMethods,
                SavedAddresses = await _context.Addresses.AsNoTracking()
                    .Where(a => a.UserId == userId)
                    .OrderByDescending(a => a.IsDefault).ThenBy(a => a.AddressId)
                    .ToListAsync(),
                CashOnDeliveryAvailable = _paymentRegistry.CashOnDeliveryEnabled,
                CardProviderName = _paymentRegistry.ActiveProvider?.DisplayName,
                CardTestMode = _paymentRegistry.ActiveProvider?.IsTestMode == true,
                CardPaymentTimeoutMinutes = _paymentRegistry.Options.EffectiveTimeoutMinutes
            };
        }

        private async Task<IActionResult> CartResponseAsync(string? userId, CartOperationResult result)
        {
            var summary = await _cartService.GetSummaryAsync(userId);
            if (userId != null)
            {
                await NotifyCartChangedAsync(userId, summary.ItemCount);
            }

            if (IsAjax)
            {
                return Json(new
                {
                    success = result.Success,
                    message = result.Message,
                    cartItemCount = summary.ItemCount,
                    subtotal = summary.Subtotal,
                    total = summary.Total
                });
            }

            TempData[result.Success ? "Success" : "Error"] = result.Message;
            return RedirectToAction(nameof(Index));
        }

        private Task NotifyCartChangedAsync(string userId, int count) =>
            _hubContext.Clients.Group($"User_{userId}").SendAsync("CartUpdated", new { cartItemCount = count });
    }
}
