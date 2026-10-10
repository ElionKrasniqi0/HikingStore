using HikingStore.Data;
using HikingStore.Models;
using HikingStore.Services;
using HikingStore.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HikingStore.Controllers
{
    public class PaymentsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly PaymentService _payments;
        private readonly PaymentProviderRegistry _registry;
        private readonly CartService _cartService;
        private readonly ILogger<PaymentsController> _logger;

        public PaymentsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            PaymentService payments,
            PaymentProviderRegistry registry,
            CartService cartService,
            ILogger<PaymentsController> logger)
        {
            _context = context;
            _userManager = userManager;
            _payments = payments;
            _registry = registry;
            _cartService = cartService;
            _logger = logger;
        }

        private string BaseUrl => ResolveBaseUrl(_registry, Request);

        /// <summary>
        /// Where the provider sends the customer back to (both after paying and after cancelling). Returning here
        /// never changes the order; it only shows the state confirmed so far by the provider's webhook.
        /// </summary>
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Status(int id, string? result = null)
        {
            var order = await LoadOwnOrderAsync(id);
            if (order == null) return NotFound();
            if (order.PaymentMethod != PaymentMethods.Card)
                return RedirectToAction("Details", "Orders", new { id });
            if (order.PaymentStatus == PaymentStatuses.Paid)
                return RedirectToAction("OrderConfirmation", "Carts", new { id });

            ViewBag.ReturnedFromProvider = result == "success";
            ViewBag.CancelledAtProvider = result == "cancelled";
            ViewBag.ProviderName = _registry.Get(order.PaymentProvider)?.DisplayName ?? "Card payment";
            ViewBag.IsTestMode = _registry.Get(order.PaymentProvider)?.IsTestMode == true;
            return View(order);
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> StatusJson(int id)
        {
            var userId = _userManager.GetUserId(User);
            var state = await _context.Orders.AsNoTracking()
                .Where(o => o.OrderId == id && o.UserId == userId)
                .Select(o => new { o.PaymentStatus, o.Status, o.PaymentFailureReason })
                .FirstOrDefaultAsync();
            if (state == null) return NotFound();
            Response.Headers.CacheControl = "no-store";
            return Json(new
            {
                paymentStatus = state.PaymentStatus,
                cancelled = state.Status == Enum.OrderStatus.Cancelled,
                failureReason = state.PaymentFailureReason
            });
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Pay(int id, CancellationToken cancellationToken)
        {
            var userId = _userManager.GetUserId(User)!;
            var start = await _payments.StartPaymentAsync(id, userId, BuildUrls(id), cancellationToken);
            if (!start.Success)
            {
                TempData["PaymentError"] = start.Message;
                return RedirectToAction(nameof(Status), new { id });
            }
            return RedirectToProvider(start.Redirect!);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken)
        {
            var userId = _userManager.GetUserId(User)!;
            var (success, message) = await _payments.CancelUnpaidAsync(id, userId, "Order cancelled by customer before payment", cancellationToken);
            if (!success)
            {
                TempData["PaymentError"] = message;
                return RedirectToAction(nameof(Status), new { id });
            }

            var restored = await RestoreCartAsync(id, userId);
            TempData["Success"] = restored
                ? "Your order was cancelled and no payment was taken. Your items are back in your cart."
                : message;
            return restored ? RedirectToAction("Index", "Carts") : RedirectToAction("Details", "Orders", new { id });
        }

        /// <summary>Receives provider notifications. Authenticity comes only from the provider's signature.</summary>
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        [HttpPost("payments/webhooks/{provider}")]
        [RequestSizeLimit(512 * 1024)]
        public async Task<IActionResult> Webhook(string provider, CancellationToken cancellationToken)
        {
            var paymentProvider = _registry.Get(provider);
            if (paymentProvider == null) return NotFound();

            string body;
            using (var reader = new StreamReader(Request.Body))
            {
                body = await reader.ReadToEndAsync(cancellationToken);
            }

            PaymentNotification? notification;
            try
            {
                notification = paymentProvider.ParseNotification(body, Request.Headers);
            }
            catch (PaymentSignatureException ex)
            {
                _logger.LogWarning("Rejected {Provider} webhook from {IP}: {Reason}", paymentProvider.Name, HttpContext.Connection.RemoteIpAddress, ex.Message);
                return BadRequest();
            }

            if (notification == null) return Ok();
            if (notification.LiveMode == paymentProvider.IsTestMode)
            {
                _logger.LogWarning("Ignored {Provider} event {EventId}: live/test mode does not match the configured credentials", paymentProvider.Name, notification.EventId);
                return Ok();
            }

            await _payments.HandleNotificationAsync(notification, BaseUrl, cancellationToken);
            return Ok();
        }

        private PaymentUrls BuildUrls(int orderId) => BuildUrls(Url, BaseUrl, orderId);

        public static PaymentUrls BuildUrls(IUrlHelper url, string baseUrl, int orderId) => new(
            $"{baseUrl}{url.Action(nameof(Status), "Payments", new { id = orderId, result = "success" })}",
            $"{baseUrl}{url.Action(nameof(Status), "Payments", new { id = orderId, result = "cancelled" })}",
            provider => $"{baseUrl}/payments/webhooks/{provider.ToLowerInvariant()}");

        public static string ResolveBaseUrl(PaymentProviderRegistry registry, HttpRequest request) =>
            registry.Options.PublicBaseUrl?.TrimEnd('/') is { Length: > 0 } configured
                ? configured
                : $"{request.Scheme}://{request.Host}";

        /// <summary>Sends the browser to the provider's hosted page (redirect, or auto-submitted signed form).</summary>
        public static IActionResult RedirectToProvider(Controller controller, PaymentRedirect redirect) =>
            redirect.FormFields == null
                ? controller.Redirect(redirect.Url)
                : controller.View("~/Views/Payments/Redirect.cshtml", redirect);

        private IActionResult RedirectToProvider(PaymentRedirect redirect) => RedirectToProvider(this, redirect);

        private async Task<Order?> LoadOwnOrderAsync(int id)
        {
            var userId = _userManager.GetUserId(User);
            return await _context.Orders.AsNoTracking()
                .Include(o => o.OrderDetails).ThenInclude(d => d.Product)
                .FirstOrDefaultAsync(o => o.OrderId == id && o.UserId == userId);
        }

        private async Task<bool> RestoreCartAsync(int orderId, string userId)
        {
            var lines = await _context.OrderDetails.AsNoTracking().Where(d => d.OrderId == orderId).ToListAsync();
            var restoredAny = false;
            foreach (var line in lines)
            {
                var added = await _cartService.AddAsync(userId, line.ProId, line.Quantity, line.SelectedSize, line.SelectedColor);
                restoredAny |= added.Success;
            }
            return restoredAny;
        }
    }
}
