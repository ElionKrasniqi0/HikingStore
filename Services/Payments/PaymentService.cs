using HikingStore.Data;
using HikingStore.Enum;
using HikingStore.Hubs;
using HikingStore.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HikingStore.Services.Payments
{
    public sealed record StartPaymentResult(bool Success, PaymentRedirect? Redirect, string? Message)
    {
        public static StartPaymentResult Fail(string message) => new(false, null, message);
    }

    public enum NotificationResult
    {
        Applied,
        Duplicate,
        Ignored
    }

    /// <summary>
    /// Card-payment lifecycle for orders. The order's payment state only changes from signature-verified provider
    /// notifications (or from definitive provider state when releasing an expired order) — never from the
    /// customer's browser returning to the store.
    /// </summary>
    public class PaymentService
    {
        private readonly ApplicationDbContext _context;
        private readonly PaymentProviderRegistry _registry;
        private readonly OrderService _orderService;
        private readonly OrderEmailService _emails;
        private readonly IHubContext<RealtimeHub>? _hub;
        private readonly StoreSettings _settings;
        private readonly ILogger<PaymentService> _logger;

        public PaymentService(
            ApplicationDbContext context,
            PaymentProviderRegistry registry,
            OrderService orderService,
            OrderEmailService emails,
            IOptions<StoreSettings> settings,
            ILogger<PaymentService> logger,
            IHubContext<RealtimeHub>? hub = null)
        {
            _context = context;
            _registry = registry;
            _orderService = orderService;
            _emails = emails;
            _settings = settings.Value;
            _logger = logger;
            _hub = hub;
        }

        private PaymentOptions Options => _registry.Options;

        /// <summary>
        /// Opens a hosted payment session for an unpaid card order. Any earlier session for the order is closed
        /// first, so at most one session can take money for an order at a time.
        /// </summary>
        public async Task<StartPaymentResult> StartPaymentAsync(int orderId, string userId, PaymentUrls urls, CancellationToken cancellationToken = default)
        {
            var order = await _context.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == orderId && o.UserId == userId, cancellationToken);
            if (order == null) return StartPaymentResult.Fail("Order not found.");
            if (!order.IsAwaitingCardPayment) return StartPaymentResult.Fail("This order isn't waiting for a card payment.");

            var provider = _registry.Get(order.PaymentProvider);
            if (provider == null)
            {
                _logger.LogError("Order {OrderId} uses payment provider {Provider}, which is not configured", orderId, order.PaymentProvider);
                return StartPaymentResult.Fail("Card payment is temporarily unavailable. Please try again later or cancel the order.");
            }

            try
            {
                if (!string.IsNullOrEmpty(order.PaymentSessionId)
                    && !await provider.TryCloseSessionAsync(order.PaymentSessionId, cancellationToken))
                {
                    return StartPaymentResult.Fail("Your payment has already been submitted and is being confirmed. Please wait a moment.");
                }

                var attempt = order.PaymentAttempts + 1;
                var expiresAt = DateTime.UtcNow.AddMinutes(Options.EffectiveTimeoutMinutes);
                var currency = _settings.Currency.ToUpperInvariant();
                var request = new PaymentRequest(
                    order.OrderId,
                    order.OrderNumber,
                    PaymentAmounts.ToMinorUnits(order.TotalAmount, currency),
                    currency,
                    order.Email,
                    $"{_settings.Name} order #{order.OrderNumber}",
                    urls.Success,
                    urls.Cancel,
                    urls.Notification(provider.Name),
                    expiresAt,
                    attempt);
                var redirect = await provider.CreateSessionAsync(request, cancellationToken);

                // Only one concurrent start may win; the loser closes the session it just opened.
                var updated = await _context.Orders
                    .Where(o => o.OrderId == orderId && o.PaymentAttempts == order.PaymentAttempts
                        && o.PaymentStatus == PaymentStatuses.Pending && o.Status != OrderStatus.Cancelled)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(o => o.PaymentSessionId, redirect.SessionId)
                        .SetProperty(o => o.PaymentAttempts, attempt)
                        .SetProperty(o => o.PaymentExpiresAt, expiresAt)
                        .SetProperty(o => o.PaymentFailureReason, (string?)null), cancellationToken);
                if (updated == 0)
                {
                    await provider.TryCloseSessionAsync(redirect.SessionId, cancellationToken);
                    return StartPaymentResult.Fail("This payment was already started in another window. Please refresh the page.");
                }
                return new StartPaymentResult(true, redirect, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not start {Provider} payment for order {OrderId}", provider.Name, orderId);
                return StartPaymentResult.Fail("We couldn't connect to the payment provider. Your order is saved — please try again in a moment.");
            }
        }

        /// <summary>Applies a verified provider notification exactly once.</summary>
        public async Task<NotificationResult> HandleNotificationAsync(PaymentNotification notification, string baseUrl, CancellationToken cancellationToken = default)
        {
            var followUps = new List<Action>();
            NotificationResult result;
            try
            {
                var strategy = _context.Database.CreateExecutionStrategy();
                result = await strategy.ExecuteAsync(async () =>
                {
                    followUps.Clear();
                    _context.ChangeTracker.Clear();
                    await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                    if (await _context.PaymentEvents.AnyAsync(e => e.Provider == notification.Provider && e.EventId == notification.EventId, cancellationToken))
                    {
                        return NotificationResult.Duplicate;
                    }

                    var order = await FindOrderAsync(notification, cancellationToken);
                    string outcomeText;
                    if (order == null)
                    {
                        outcomeText = "No matching order.";
                        _logger.LogWarning("{Provider} event {EventId} ({Type}) matched no order", notification.Provider, notification.EventId, notification.EventType);
                    }
                    else
                    {
                        outcomeText = await ApplyAsync(order, notification, baseUrl, followUps, cancellationToken);
                    }

                    _context.PaymentEvents.Add(new PaymentEvent
                    {
                        Provider = notification.Provider,
                        EventId = notification.EventId,
                        EventType = notification.EventType.Length > 100 ? notification.EventType[..100] : notification.EventType,
                        OrderId = order?.OrderId,
                        ReceivedAt = DateTime.UtcNow,
                        Result = outcomeText.Length > 300 ? outcomeText[..300] : outcomeText
                    });
                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    _logger.LogInformation("{Provider} event {EventId} ({Type}) for order {OrderId}: {Result}",
                        notification.Provider, notification.EventId, notification.EventType, order?.OrderId, outcomeText);
                    return order == null ? NotificationResult.Ignored : NotificationResult.Applied;
                });
            }
            catch (DbUpdateException)
            {
                // Two deliveries of the same event raced; the unique index let only one of them apply it.
                if (await EventRecordedAsync(notification)) return NotificationResult.Duplicate;
                throw;
            }

            foreach (var followUp in followUps)
            {
                followUp();
            }
            return result;
        }

        private async Task<bool> EventRecordedAsync(PaymentNotification notification)
        {
            _context.ChangeTracker.Clear();
            return await _context.PaymentEvents.AnyAsync(e => e.Provider == notification.Provider && e.EventId == notification.EventId);
        }

        private async Task<Order?> FindOrderAsync(PaymentNotification n, CancellationToken cancellationToken)
        {
            Order? order = null;
            if (n.OrderId.HasValue)
                order = await _context.Orders.FirstOrDefaultAsync(o => o.OrderId == n.OrderId.Value, cancellationToken);
            if (order == null && !string.IsNullOrEmpty(n.SessionId))
                order = await _context.Orders.FirstOrDefaultAsync(o => o.PaymentSessionId == n.SessionId, cancellationToken);
            if (order == null && !string.IsNullOrEmpty(n.TransactionId))
                order = await _context.Orders.FirstOrDefaultAsync(o => o.PaymentTransactionId == n.TransactionId, cancellationToken);

            if (order != null && order.PaymentProvider != n.Provider)
            {
                _logger.LogWarning("{Provider} event {EventId} refers to order {OrderId}, which uses provider {OrderProvider}",
                    n.Provider, n.EventId, order.OrderId, order.PaymentProvider);
                return null;
            }
            return order;
        }

        private async Task<string> ApplyAsync(Order order, PaymentNotification n, string baseUrl, List<Action> followUps, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            switch (n.Outcome)
            {
                case PaymentOutcome.Succeeded:
                {
                    var currency = _settings.Currency;
                    var expected = PaymentAmounts.ToMinorUnits(order.TotalAmount, currency);
                    var paidAmount = n.AmountMinor.HasValue && !string.IsNullOrEmpty(n.Currency)
                        ? PaymentAmounts.FromMinorUnits(n.AmountMinor.Value, n.Currency)
                        : (decimal?)null;

                    if (order.PaymentStatus == PaymentStatuses.Paid)
                    {
                        if (string.IsNullOrEmpty(n.TransactionId) || n.TransactionId == order.PaymentTransactionId)
                            return "Already paid.";
                        AppendNote(order, $"Second payment {n.TransactionId} received for an already paid order — refund it in the {n.Provider} dashboard.");
                        followUps.Add(() => _emails.QueuePaymentAttention(order.OrderId, baseUrl,
                            $"A second payment ({n.TransactionId}) was received for order #{order.OrderNumber}, which was already paid with {order.PaymentTransactionId}. Refund the duplicate in the {n.Provider} dashboard."));
                        return "Duplicate payment flagged for refund.";
                    }

                    order.PaymentTransactionId = n.TransactionId ?? order.PaymentTransactionId;
                    order.PaymentSessionId = n.SessionId ?? order.PaymentSessionId;
                    order.PaidAmount = paidAmount;
                    order.PaidAt = now;

                    if (n.AmountMinor != expected || !string.Equals(n.Currency, currency, StringComparison.OrdinalIgnoreCase))
                    {
                        order.PaymentStatus = PaymentStatuses.RequiresReview;
                        var note = $"Payment {n.TransactionId} of {n.AmountMinor} {n.Currency?.ToUpperInvariant()} (minor units) does not match the order total of {expected} {currency}.";
                        AppendNote(order, note);
                        _logger.LogError("Amount mismatch on order {OrderId}: {Note}", order.OrderId, note);
                        followUps.Add(() => _emails.QueuePaymentAttention(order.OrderId, baseUrl, note));
                        return "Amount mismatch — requires review.";
                    }

                    if (order.Status == OrderStatus.Cancelled)
                    {
                        order.PaymentStatus = PaymentStatuses.RefundDue;
                        var note = $"Payment {n.TransactionId} arrived after the order was cancelled and its stock released. Refund it in the {n.Provider} dashboard.";
                        AppendNote(order, note);
                        followUps.Add(() => _emails.QueuePaymentAttention(order.OrderId, baseUrl, note));
                        return "Paid after cancellation — refund due.";
                    }

                    order.PaymentStatus = PaymentStatuses.Paid;
                    order.PaymentFailureReason = null;
                    order.PaymentExpiresAt = null;
                    AddTracking(order, "Payment received", "Card payment confirmed by " + n.Provider);
                    followUps.Add(() => _emails.QueuePaymentReceived(order.OrderId, baseUrl));
                    followUps.Add(() => Notify(order, "Payment received", "paid"));
                    return "Marked paid.";
                }

                case PaymentOutcome.Processing:
                    if (!order.IsAwaitingCardPayment) return $"Ignored (payment status {order.PaymentStatus}).";
                    order.PaymentTransactionId = n.TransactionId ?? order.PaymentTransactionId;
                    order.PaymentSessionId = n.SessionId ?? order.PaymentSessionId;
                    // Funds are on their way; keep the stock reserved until the provider's final answer.
                    order.PaymentExpiresAt = now.AddDays(7);
                    return "Payment processing.";

                case PaymentOutcome.AttemptFailed:
                    if (!order.IsAwaitingCardPayment) return $"Ignored (payment status {order.PaymentStatus}).";
                    order.PaymentFailureReason = n.FailureReason ?? "The card was declined.";
                    return "Attempt declined; customer may retry.";

                case PaymentOutcome.Failed:
                case PaymentOutcome.Expired:
                {
                    if (!order.IsAwaitingCardPayment) return $"Ignored (payment status {order.PaymentStatus}).";
                    if (n.SessionId != null && order.PaymentSessionId != null && n.SessionId != order.PaymentSessionId)
                        return "Ignored: superseded session.";

                    var failed = n.Outcome == PaymentOutcome.Failed;
                    await CancelUnpaidOrderAsync(order,
                        failed ? PaymentStatuses.Failed : PaymentStatuses.Expired,
                        failed ? "Order cancelled — card payment failed" : "Order cancelled — payment not completed in time",
                        n.FailureReason, cancellationToken);
                    followUps.Add(() => _emails.QueuePaymentNotCompleted(order.OrderId, baseUrl));
                    followUps.Add(() => Notify(order, "Payment not completed", "cancelled"));
                    return failed ? "Payment failed; order cancelled and restocked." : "Session expired; order cancelled and restocked.";
                }

                case PaymentOutcome.Refunded:
                    if (order.PaymentStatus is not (PaymentStatuses.Paid or PaymentStatuses.RefundDue or PaymentStatuses.RequiresReview))
                        return $"Ignored (payment status {order.PaymentStatus}).";
                    order.PaymentStatus = PaymentStatuses.Refunded;
                    AddTracking(order, "Payment refunded", $"Payment refunded via {n.Provider}");
                    return "Marked refunded.";

                default:
                    return "Ignored.";
            }
        }

        /// <summary>Customer or staff cancels a card order that hasn't been paid; the provider session is closed first.</summary>
        public async Task<(bool Success, string Message)> CancelUnpaidAsync(int orderId, string? userId, string description, CancellationToken cancellationToken = default)
        {
            var order = await _context.Orders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrderId == orderId && (userId == null || o.UserId == userId), cancellationToken);
            if (order == null) return (false, "Order not found.");
            if (!order.IsAwaitingCardPayment) return (false, "This order is not awaiting payment.");

            if (!string.IsNullOrEmpty(order.PaymentSessionId))
            {
                var provider = _registry.Get(order.PaymentProvider);
                if (provider == null) return (false, "The payment provider is not reachable right now. Please try again later.");
                try
                {
                    if (!await provider.TryCloseSessionAsync(order.PaymentSessionId, cancellationToken))
                        return (false, "This payment has already been completed and is being confirmed, so the order can't be cancelled here. Please contact us if you need to cancel it.");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Could not close payment session for order {OrderId}", orderId);
                    return (false, "We couldn't reach the payment provider to stop the payment. Please try again in a moment.");
                }
            }

            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                var tracked = await _context.Orders.FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);
                if (tracked == null || !tracked.IsAwaitingCardPayment)
                    return (false, "This order's payment status just changed. Please refresh the page.");

                await CancelUnpaidOrderAsync(tracked, PaymentStatuses.Cancelled, description, null, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return (true, "Your order has been cancelled. No payment was taken.");
            });
        }

        /// <summary>Closes the session of an unpaid card order before staff cancel it. False if the customer already paid.</summary>
        public async Task<bool> TryCloseSessionForOrderAsync(Order order, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(order.PaymentSessionId)) return true;
            var provider = _registry.Get(order.PaymentProvider);
            return provider != null && await provider.TryCloseSessionAsync(order.PaymentSessionId, cancellationToken);
        }

        /// <summary>
        /// Releases unpaid card orders whose payment window has passed. The provider is asked first, and an order is
        /// never released while its session shows a completed payment.
        /// </summary>
        public async Task<int> ReleaseExpiredOrdersAsync(CancellationToken cancellationToken = default)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-Math.Max(0, Options.ExpiryGraceMinutes));
            var candidates = await _context.Orders.AsNoTracking()
                .Where(o => o.PaymentMethod == PaymentMethods.Card && o.PaymentStatus == PaymentStatuses.Pending
                    && o.Status != OrderStatus.Cancelled && o.PaymentExpiresAt != null && o.PaymentExpiresAt < cutoff)
                .OrderBy(o => o.PaymentExpiresAt)
                .Select(o => new { o.OrderId, o.PaymentProvider, o.PaymentSessionId })
                .Take(50)
                .ToListAsync(cancellationToken);

            var released = 0;
            var baseUrl = Options.PublicBaseUrl?.TrimEnd('/') ?? string.Empty;
            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrEmpty(candidate.PaymentSessionId))
                {
                    var provider = _registry.Get(candidate.PaymentProvider);
                    if (provider == null) continue;
                    try
                    {
                        var state = await provider.GetSessionStateAsync(candidate.PaymentSessionId, cancellationToken);
                        if (state == ProviderSessionState.Paid) continue;
                        if (!await provider.TryCloseSessionAsync(candidate.PaymentSessionId, cancellationToken)) continue;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Could not check payment session of order {OrderId}; will retry", candidate.OrderId);
                        continue;
                    }
                }

                var cancelled = await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    _context.ChangeTracker.Clear();
                    await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                    var order = await _context.Orders.FirstOrDefaultAsync(o => o.OrderId == candidate.OrderId, cancellationToken);
                    if (order == null || !order.IsAwaitingCardPayment || order.PaymentSessionId != candidate.PaymentSessionId) return false;
                    await CancelUnpaidOrderAsync(order, PaymentStatuses.Expired, "Order cancelled — payment not completed in time", null, cancellationToken);
                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return true;
                });
                if (cancelled)
                {
                    released++;
                    _logger.LogInformation("Released unpaid card order {OrderId} after its payment window expired", candidate.OrderId);
                    if (baseUrl.Length > 0) _emails.QueuePaymentNotCompleted(candidate.OrderId, baseUrl);
                }
            }
            return released;
        }

        private async Task CancelUnpaidOrderAsync(Order order, string paymentStatus, string description, string? reason, CancellationToken cancellationToken)
        {
            order.Status = OrderStatus.Cancelled;
            order.CancelledDate = DateTime.UtcNow;
            order.PaymentStatus = paymentStatus;
            order.PaymentExpiresAt = null;
            if (reason != null) order.PaymentFailureReason = reason;
            AddTracking(order, OrderStatus.Cancelled.ToString(), description);
            await _context.SaveChangesAsync(cancellationToken);
            await _orderService.RestockCancelledOrderAsync(order.OrderId);
        }

        private void AddTracking(Order order, string status, string description) =>
            _context.TrackingEvents.Add(new TrackingEvent
            {
                OrderId = order.OrderId,
                EventDate = DateTime.UtcNow,
                Location = _settings.Name,
                Status = status,
                Description = description,
                IsMilestone = true
            });

        private static void AppendNote(Order order, string note)
        {
            var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC] {note}";
            var notes = string.IsNullOrEmpty(order.AdminNotes) ? line : order.AdminNotes + "\n" + line;
            order.AdminNotes = notes.Length > 2000 ? notes[^2000..] : notes;
        }

        private void Notify(Order order, string message, string paymentState)
        {
            if (_hub == null) return;
            var payload = new
            {
                orderId = order.OrderId,
                status = order.Status.ToString(),
                paymentStatus = order.PaymentStatus,
                paymentState,
                message = $"Order #{order.OrderNumber}: {message}."
            };
            _ = _hub.Clients.Group($"User_{order.UserId}").SendAsync("OrderStatusUpdated", payload);
            _ = _hub.Clients.Group($"Order_{order.OrderId}").SendAsync("OrderStatusUpdated", payload);
            _ = _hub.Clients.Group("Admins").SendAsync("OrderStatusUpdated", payload);
        }
    }

    /// <summary>Absolute URLs the provider sends the customer/notifications to.</summary>
    public sealed record PaymentUrls(string Success, string Cancel, Func<string, string> Notification);
}
