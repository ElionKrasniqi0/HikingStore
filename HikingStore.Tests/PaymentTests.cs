using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HikingStore.Enum;
using HikingStore.Models;
using HikingStore.Services;
using HikingStore.Services.Payments;
using HikingStore.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HikingStore.Tests;

public class PaymentTests : IDisposable
{
    private const string BaseUrl = "https://shop.test";
    private readonly TestDb _db = new();
    private readonly FakeCardProvider _provider = new();

    public void Dispose() => _db.Dispose();

    private PaymentProviderRegistry Registry(ICardPaymentProvider? active, bool cod = true) =>
        new(new PaymentOptions { CardProvider = active?.Name, CashOnDeliveryEnabled = cod, PendingPaymentTimeoutMinutes = 60 },
            isProduction: false, active == null ? Array.Empty<ICardPaymentProvider>() : new[] { active }, NullLogger.Instance);

    private OrderService Orders(PaymentProviderRegistry registry) =>
        new(_db.Context, _db.CreateCartService(), new CouponService(_db.Context), Options.Create(_db.Settings), registry, NullLogger<OrderService>.Instance);

    private PaymentService Payments(PaymentProviderRegistry registry) =>
        new(_db.Context, registry, Orders(registry),
            new OrderEmailService(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), NullLogger<OrderEmailService>.Instance),
            Options.Create(_db.Settings), NullLogger<PaymentService>.Instance);

    private static CheckoutInput Input(string method) => new()
    {
        FullName = "Ana Hiker", Email = "ana@example.com", PhoneNumber = "+38344111222", AddressLine = "Rruga 1",
        City = "Prishtina", Country = "Kosovo", ShippingMethod = "standard", PaymentMethod = method, CheckoutToken = Guid.NewGuid()
    };

    private async Task<(Order Order, Product Product, string UserId)> PlaceCardOrderAsync(PaymentProviderRegistry registry, int quantity = 2)
    {
        var user = _db.AddUser($"hiker-{Guid.NewGuid():N}@example.com");
        var product = _db.AddProduct(price: 25m, stock: 10);
        await _db.CreateCartService().AddAsync(user.Id, product.ProId, quantity, null, null);
        var result = await Orders(registry).PlaceOrderAsync(user.Id, Input(PaymentMethods.Card));
        Assert.True(result.Success, string.Join(" ", result.Errors));
        return (await Reload(result.OrderId), product, user.Id);
    }

    private async Task<Order> Reload(int orderId)
    {
        _db.Context.ChangeTracker.Clear();
        return await _db.Context.Orders.AsNoTracking().SingleAsync(o => o.OrderId == orderId);
    }

    private async Task<int> Stock(int productId)
    {
        _db.Context.ChangeTracker.Clear();
        return await _db.Context.Products.Where(p => p.ProId == productId).Select(p => p.StockQuantity).SingleAsync();
    }

    private PaymentNotification Note(Order order, PaymentOutcome outcome, string eventId = "evt_1", long? amount = null, string currency = "eur", string? session = null, string transaction = "pi_1") =>
        new(_provider.Name, eventId, "test.event", outcome, order.OrderId, session ?? order.PaymentSessionId, transaction,
            amount ?? PaymentAmounts.ToMinorUnits(order.TotalAmount, "EUR"), currency, null, LiveMode: false);

    [Fact]
    public async Task Card_order_is_created_pending_with_reserved_stock_and_empty_cart()
    {
        var registry = Registry(_provider);
        var (order, product, userId) = await PlaceCardOrderAsync(registry);

        Assert.Equal(PaymentMethods.Card, order.PaymentMethod);
        Assert.Equal(PaymentStatuses.Pending, order.PaymentStatus);
        Assert.Equal(OrderStatus.Received, order.Status);
        Assert.Equal(_provider.Name, order.PaymentProvider);
        Assert.NotNull(order.PaymentExpiresAt);
        Assert.Equal(54.90m, order.TotalAmount); // 2 Ã— 25 + 4.90 shipping, computed on the server
        Assert.Equal(8, await Stock(product.ProId));
        Assert.False(await _db.Context.ShoppingCarts.AnyAsync(c => c.UserId == userId));
    }

    [Fact]
    public async Task Card_order_is_rejected_when_no_provider_is_configured()
    {
        var user = _db.AddUser();
        var product = _db.AddProduct();
        await _db.CreateCartService().AddAsync(user.Id, product.ProId, 1, null, null);

        var result = await Orders(Registry(null)).PlaceOrderAsync(user.Id, Input(PaymentMethods.Card));

        Assert.False(result.Success);
        Assert.False(await _db.Context.Orders.AnyAsync());
    }

    [Fact]
    public async Task Cash_on_delivery_is_rejected_when_disabled()
    {
        var user = _db.AddUser();
        var product = _db.AddProduct();
        await _db.CreateCartService().AddAsync(user.Id, product.ProId, 1, null, null);

        var result = await Orders(Registry(_provider, cod: false)).PlaceOrderAsync(user.Id, Input(PaymentMethods.CashOnDelivery));

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Starting_payment_sends_server_total_in_minor_units_and_records_session()
    {
        var registry = Registry(_provider);
        var (order, _, userId) = await PlaceCardOrderAsync(registry);

        var start = await Payments(registry).StartPaymentAsync(order.OrderId, userId, Urls());

        Assert.True(start.Success, start.Message);
        Assert.Equal(5490, _provider.LastRequest!.AmountMinor);
        Assert.Equal("EUR", _provider.LastRequest.Currency);
        var saved = await Reload(order.OrderId);
        Assert.Equal(start.Redirect!.SessionId, saved.PaymentSessionId);
        Assert.Equal(1, saved.PaymentAttempts);
        Assert.Equal(PaymentStatuses.Pending, saved.PaymentStatus);
    }

    [Fact]
    public async Task Retry_closes_previous_session_and_refuses_when_it_was_already_paid()
    {
        var registry = Registry(_provider);
        var (order, _, userId) = await PlaceCardOrderAsync(registry);
        var payments = Payments(registry);
        var first = await payments.StartPaymentAsync(order.OrderId, userId, Urls());

        var second = await payments.StartPaymentAsync(order.OrderId, userId, Urls());
        Assert.True(second.Success);
        Assert.Contains(first.Redirect!.SessionId, _provider.ClosedSessions);

        _provider.CompletedSessions.Add(second.Redirect!.SessionId);
        var third = await payments.StartPaymentAsync(order.OrderId, userId, Urls());
        Assert.False(third.Success);
        Assert.Equal(second.Redirect.SessionId, (await Reload(order.OrderId)).PaymentSessionId);
    }

    [Fact]
    public async Task Verified_success_marks_paid_once_and_duplicate_events_are_ignored()
    {
        var registry = Registry(_provider);
        var (order, _, userId) = await PlaceCardOrderAsync(registry);
        var payments = Payments(registry);
        await payments.StartPaymentAsync(order.OrderId, userId, Urls());
        order = await Reload(order.OrderId);

        Assert.Equal(NotificationResult.Applied, await payments.HandleNotificationAsync(Note(order, PaymentOutcome.Succeeded), BaseUrl));
        Assert.Equal(NotificationResult.Duplicate, await payments.HandleNotificationAsync(Note(order, PaymentOutcome.Succeeded), BaseUrl));

        var paid = await Reload(order.OrderId);
        Assert.Equal(PaymentStatuses.Paid, paid.PaymentStatus);
        Assert.Equal(54.90m, paid.PaidAmount);
        Assert.Equal("pi_1", paid.PaymentTransactionId);
        Assert.NotNull(paid.PaidAt);
        Assert.Equal(1, await _db.Context.PaymentEvents.CountAsync());
        Assert.Equal(1, await _db.Context.TrackingEvents.CountAsync(t => t.OrderId == order.OrderId && t.Status == "Payment received"));
    }

    [Fact]
    public async Task Amount_or_currency_mismatch_is_flagged_for_review_not_paid()
    {
        var registry = Registry(_provider);
        var (order, _, _) = await PlaceCardOrderAsync(registry);
        var payments = Payments(registry);

        await payments.HandleNotificationAsync(Note(order, PaymentOutcome.Succeeded, amount: 100), BaseUrl);
        Assert.Equal(PaymentStatuses.RequiresReview, (await Reload(order.OrderId)).PaymentStatus);

        var (other, _, _) = await PlaceCardOrderAsync(registry);
        await payments.HandleNotificationAsync(Note(other, PaymentOutcome.Succeeded, eventId: "evt_2", currency: "usd"), BaseUrl);
        Assert.Equal(PaymentStatuses.RequiresReview, (await Reload(other.OrderId)).PaymentStatus);
    }

    [Fact]
    public async Task Processing_and_declined_attempts_keep_the_order_pending()
    {
        var registry = Registry(_provider);
        var (order, _, _) = await PlaceCardOrderAsync(registry);
        var payments = Payments(registry);

        await payments.HandleNotificationAsync(Note(order, PaymentOutcome.Processing, eventId: "evt_p"), BaseUrl);
        await payments.HandleNotificationAsync(Note(order, PaymentOutcome.AttemptFailed, eventId: "evt_d") with { FailureReason = "Your card was declined." }, BaseUrl);

        var saved = await Reload(order.OrderId);
        Assert.Equal(PaymentStatuses.Pending, saved.PaymentStatus);
        Assert.Equal(OrderStatus.Received, saved.Status);
        Assert.Equal("Your card was declined.", saved.PaymentFailureReason);
    }

    [Fact]
    public async Task Expired_session_cancels_order_and_restocks_but_superseded_session_is_ignored()
    {
        var registry = Registry(_provider);
        var (order, product, userId) = await PlaceCardOrderAsync(registry);
        var payments = Payments(registry);
        await payments.StartPaymentAsync(order.OrderId, userId, Urls());
        order = await Reload(order.OrderId);

        await payments.HandleNotificationAsync(Note(order, PaymentOutcome.Expired, eventId: "evt_old", session: "cs_superseded"), BaseUrl);
        Assert.Equal(PaymentStatuses.Pending, (await Reload(order.OrderId)).PaymentStatus);

        await payments.HandleNotificationAsync(Note(order, PaymentOutcome.Expired, eventId: "evt_exp"), BaseUrl);
        var expired = await Reload(order.OrderId);
        Assert.Equal(PaymentStatuses.Expired, expired.PaymentStatus);
        Assert.Equal(OrderStatus.Cancelled, expired.Status);
        Assert.Equal(10, await Stock(product.ProId));
    }

    [Fact]
    public async Task Failed_payment_cancels_and_restocks()
    {
        var registry = Registry(_provider);
        var (order, product, _) = await PlaceCardOrderAsync(registry);

        await Payments(registry).HandleNotificationAsync(Note(order, PaymentOutcome.Failed), BaseUrl);

        var failed = await Reload(order.OrderId);
        Assert.Equal(PaymentStatuses.Failed, failed.PaymentStatus);
        Assert.Equal(OrderStatus.Cancelled, failed.Status);
        Assert.Equal(10, await Stock(product.ProId));
    }

    [Fact]
    public async Task Payment_arriving_after_cancellation_is_flagged_refund_due()
    {
        var registry = Registry(_provider);
        var (order, _, _) = await PlaceCardOrderAsync(registry);
        var payments = Payments(registry);
        await payments.HandleNotificationAsync(Note(order, PaymentOutcome.Expired, eventId: "evt_exp"), BaseUrl);

        await payments.HandleNotificationAsync(Note(order, PaymentOutcome.Succeeded, eventId: "evt_late"), BaseUrl);

        var saved = await Reload(order.OrderId);
        Assert.Equal(PaymentStatuses.RefundDue, saved.PaymentStatus);
        Assert.Equal(OrderStatus.Cancelled, saved.Status);
        Assert.Contains("Refund", saved.AdminNotes);
    }

    [Fact]
    public async Task Full_refund_marks_paid_order_refunded()
    {
        var registry = Registry(_provider);
        var (order, _, _) = await PlaceCardOrderAsync(registry);
        var payments = Payments(registry);
        await payments.HandleNotificationAsync(Note(order, PaymentOutcome.Succeeded), BaseUrl);

        await payments.HandleNotificationAsync(Note(order, PaymentOutcome.Refunded, eventId: "evt_ref") with { OrderId = null, SessionId = null }, BaseUrl);

        Assert.Equal(PaymentStatuses.Refunded, (await Reload(order.OrderId)).PaymentStatus);
    }

    [Fact]
    public async Task Customer_cancel_closes_session_restocks_and_is_refused_once_paid_at_provider()
    {
        var registry = Registry(_provider);
        var (order, product, userId) = await PlaceCardOrderAsync(registry);
        var payments = Payments(registry);
        var start = await payments.StartPaymentAsync(order.OrderId, userId, Urls());

        _provider.CompletedSessions.Add(start.Redirect!.SessionId);
        var refused = await payments.CancelUnpaidAsync(order.OrderId, userId, "test");
        Assert.False(refused.Success);
        Assert.Equal(PaymentStatuses.Pending, (await Reload(order.OrderId)).PaymentStatus);

        _provider.CompletedSessions.Clear();
        var cancelled = await payments.CancelUnpaidAsync(order.OrderId, userId, "test");
        Assert.True(cancelled.Success);
        var saved = await Reload(order.OrderId);
        Assert.Equal(OrderStatus.Cancelled, saved.Status);
        Assert.Equal(PaymentStatuses.Cancelled, saved.PaymentStatus);
        Assert.Equal(10, await Stock(product.ProId));
    }

    [Fact]
    public async Task Cancelling_another_customers_order_is_not_possible()
    {
        var registry = Registry(_provider);
        var (order, _, _) = await PlaceCardOrderAsync(registry);

        var result = await Payments(registry).CancelUnpaidAsync(order.OrderId, "someone-else", "test");

        Assert.False(result.Success);
        Assert.Equal(PaymentStatuses.Pending, (await Reload(order.OrderId)).PaymentStatus);
    }

    [Fact]
    public async Task Sweeper_releases_expired_unpaid_orders_but_never_ones_paid_at_the_provider()
    {
        var registry = Registry(_provider);
        var (open, openProduct, userId) = await PlaceCardOrderAsync(registry);
        var payments = Payments(registry);
        await payments.StartPaymentAsync(open.OrderId, userId, Urls());
        var (paidAtProvider, _, userId2) = await PlaceCardOrderAsync(registry, quantity: 1);
        var started = await payments.StartPaymentAsync(paidAtProvider.OrderId, userId2, Urls());
        _provider.CompletedSessions.Add(started.Redirect!.SessionId);

        await _db.Context.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.PaymentExpiresAt, DateTime.UtcNow.AddHours(-2)));

        Assert.Equal(1, await payments.ReleaseExpiredOrdersAsync());
        Assert.Equal(PaymentStatuses.Expired, (await Reload(open.OrderId)).PaymentStatus);
        Assert.Equal(PaymentStatuses.Pending, (await Reload(paidAtProvider.OrderId)).PaymentStatus);
        Assert.Equal(OrderStatus.Received, (await Reload(paidAtProvider.OrderId)).Status);
    }

    [Fact]
    public async Task Customer_cannot_cancel_card_order_through_cash_on_delivery_path()
    {
        var registry = Registry(_provider);
        var (order, _, userId) = await PlaceCardOrderAsync(registry);

        var result = await Orders(registry).CancelByCustomerAsync(order.OrderId, userId);

        Assert.False(result.Success);
        Assert.Equal(PaymentStatuses.Pending, (await Reload(order.OrderId)).PaymentStatus);
    }

    [Theory]
    [InlineData("54.90", "EUR", 5490)]
    [InlineData("0.1", "eur", 10)]
    [InlineData("1999.995", "EUR", 200000)]
    [InlineData("500", "JPY", 500)]
    public void Minor_unit_conversion(string amount, string currency, long expected) =>
        Assert.Equal(expected, PaymentAmounts.ToMinorUnits(decimal.Parse(amount, CultureInfo.InvariantCulture), currency));

    [Fact]
    public void Test_keys_disable_card_payments_in_production_unless_allowed()
    {
        var stripe = new StripePaymentProvider(new StripeOptions { SecretKey = "sk_test_abc", WebhookSecret = "whsec_abc" }, NullLogger<StripePaymentProvider>.Instance);
        var options = new PaymentOptions { CardProvider = "Stripe" };

        Assert.Null(new PaymentProviderRegistry(options, isProduction: true, new[] { stripe }, NullLogger.Instance).ActiveProvider);
        Assert.NotNull(new PaymentProviderRegistry(options, isProduction: false, new[] { stripe }, NullLogger.Instance).ActiveProvider);
        options.AllowTestModeInProduction = true;
        Assert.NotNull(new PaymentProviderRegistry(options, isProduction: true, new[] { stripe }, NullLogger.Instance).ActiveProvider);
    }

    [Theory]
    [InlineData("pk_test_abc", "whsec_abc")]
    [InlineData("sk_test_abc", "")]
    [InlineData("", "whsec_abc")]
    public void Stripe_rejects_incomplete_configuration(string key, string webhookSecret)
    {
        var stripe = new StripePaymentProvider(new StripeOptions { SecretKey = key, WebhookSecret = webhookSecret }, NullLogger<StripePaymentProvider>.Instance);
        Assert.NotNull(stripe.ConfigurationProblem);
    }

    private const string StripeSecret = "whsec_unit_test_secret";

    private static StripePaymentProvider CreateStripe() =>
        new(new StripeOptions { SecretKey = "sk_test_unit", WebhookSecret = StripeSecret }, NullLogger<StripePaymentProvider>.Instance);

    private static string StripeEvent(string type, string objectJson, string id = "evt_test_1") =>
        "{\"id\":\"" + id + "\",\"object\":\"event\",\"api_version\":\"2024-06-20\",\"created\":" + DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        + ",\"livemode\":false,\"type\":\"" + type + "\",\"data\":{\"object\":" + objectJson + "}}";

    private static IHeaderDictionary StripeHeaders(string payload, string secret = StripeSecret, long? timestamp = null)
    {
        var t = (timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString(CultureInfo.InvariantCulture);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{t}.{payload}"))).ToLowerInvariant();
        return new HeaderDictionary { ["Stripe-Signature"] = $"t={t},v1={signature}" };
    }

    private const string PaidSession = """{"id":"cs_test_1","object":"checkout.session","client_reference_id":"42","amount_total":5490,"currency":"eur","payment_status":"paid","status":"complete","payment_intent":"pi_test_1","metadata":{"order_id":"42"}}""";

    [Fact]
    public void Stripe_signed_checkout_completed_event_is_translated()
    {
        var payload = StripeEvent("checkout.session.completed", PaidSession);

        var note = CreateStripe().ParseNotification(payload, StripeHeaders(payload));

        Assert.NotNull(note);
        Assert.Equal(PaymentOutcome.Succeeded, note!.Outcome);
        Assert.Equal(42, note.OrderId);
        Assert.Equal("cs_test_1", note.SessionId);
        Assert.Equal("pi_test_1", note.TransactionId);
        Assert.Equal(5490, note.AmountMinor);
        Assert.Equal("eur", note.Currency);
        Assert.False(note.LiveMode);
    }

    [Fact]
    public void Stripe_completed_but_unpaid_session_is_processing_not_paid()
    {
        var payload = StripeEvent("checkout.session.completed", PaidSession.Replace("\"payment_status\":\"paid\"", "\"payment_status\":\"unpaid\""));
        Assert.Equal(PaymentOutcome.Processing, CreateStripe().ParseNotification(payload, StripeHeaders(payload))!.Outcome);
    }

    [Fact]
    public void Stripe_rejects_tampered_wrong_secret_old_and_unsigned_payloads()
    {
        var payload = StripeEvent("checkout.session.completed", PaidSession);
        var stripe = CreateStripe();

        Assert.Throws<PaymentSignatureException>(() => stripe.ParseNotification(payload.Replace("5490", "1"), StripeHeaders(payload)));
        Assert.Throws<PaymentSignatureException>(() => stripe.ParseNotification(payload, StripeHeaders(payload, "whsec_other")));
        Assert.Throws<PaymentSignatureException>(() => stripe.ParseNotification(payload, StripeHeaders(payload, timestamp: DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds())));
        Assert.Throws<PaymentSignatureException>(() => stripe.ParseNotification(payload, new HeaderDictionary()));
    }

    [Fact]
    public void Stripe_ignores_unrelated_event_types()
    {
        var payload = StripeEvent("customer.created", """{"id":"cus_1","object":"customer"}""");
        Assert.Null(CreateStripe().ParseNotification(payload, StripeHeaders(payload)));
    }

    private const string GatewaySecret = "0123456789abcdef0123456789abcdef-gateway";

    private static HostedGatewayPaymentProvider Gateway() => new(new HostedGatewayOptions
    {
        MerchantId = "M-100", PaymentPageUrl = "https://pay.bank.test/checkout", SigningSecret = GatewaySecret, TestMode = true
    }, NullLogger<HostedGatewayPaymentProvider>.Instance);

    private static string GatewayBody(Dictionary<string, string> fields, string secret = GatewaySecret)
    {
        fields["signature"] = HostedGatewayPaymentProvider.ComputeSignature(secret, fields);
        return string.Join("&", fields.Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"));
    }

    private static Dictionary<string, string> GatewayFields(long? timestamp = null) => new()
    {
        ["merchant_id"] = "M-100", ["event_id"] = "n-1", ["session_id"] = "HS-7-1-AB", ["order_id"] = "7",
        ["transaction_id"] = "T-99", ["status"] = "paid", ["amount"] = "5490", ["currency"] = "EUR",
        ["timestamp"] = (timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString(CultureInfo.InvariantCulture), ["test"] = "1"
    };

    [Fact]
    public async Task Hosted_gateway_signs_redirect_and_verifies_notifications()
    {
        var gateway = Gateway();
        var redirect = await gateway.CreateSessionAsync(new PaymentRequest(7, "ORD000007", 5490, "EUR", "a@b.c", "Order", "https://s/ok", "https://s/cancel", "https://s/hook", DateTime.UtcNow.AddHours(1), 1), default);
        Assert.Equal(HostedGatewayPaymentProvider.ComputeSignature(GatewaySecret, redirect.FormFields!), redirect.FormFields!["signature"]);
        Assert.DoesNotContain(redirect.FormFields!, f => f.Value.Contains(GatewaySecret));

        var note = gateway.ParseNotification(GatewayBody(GatewayFields()), new HeaderDictionary());
        Assert.Equal(PaymentOutcome.Succeeded, note!.Outcome);
        Assert.Equal(7, note.OrderId);
        Assert.Equal(5490, note.AmountMinor);
        Assert.False(note.LiveMode);
    }

    [Fact]
    public void Hosted_gateway_rejects_bad_signatures_replays_and_other_merchants()
    {
        var gateway = Gateway();
        var tampered = GatewayBody(GatewayFields()).Replace("amount=5490", "amount=1");
        Assert.Throws<PaymentSignatureException>(() => gateway.ParseNotification(tampered, new HeaderDictionary()));
        Assert.Throws<PaymentSignatureException>(() => gateway.ParseNotification(GatewayBody(GatewayFields(), "wrong-secret-wrong-secret-wrong-secret"), new HeaderDictionary()));
        Assert.Throws<PaymentSignatureException>(() => gateway.ParseNotification(GatewayBody(GatewayFields(DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds())), new HeaderDictionary()));
        var otherMerchant = GatewayFields();
        otherMerchant["merchant_id"] = "M-999";
        Assert.Throws<PaymentSignatureException>(() => gateway.ParseNotification(GatewayBody(otherMerchant), new HeaderDictionary()));
    }

    private static PaymentUrls Urls() => new($"{BaseUrl}/Payments/Status/1?result=success", $"{BaseUrl}/Payments/Status/1?result=cancelled", p => $"{BaseUrl}/payments/webhooks/{p}");

    private sealed class FakeCardProvider : ICardPaymentProvider
    {
        private int _sessions;
        public string Name => "Fake";
        public string DisplayName => "Test card";
        public bool IsTestMode => true;
        public string? ConfigurationProblem => null;
        public PaymentRequest? LastRequest { get; private set; }
        public HashSet<string> CompletedSessions { get; } = new();
        public List<string> ClosedSessions { get; } = new();

        public Task<PaymentRedirect> CreateSessionAsync(PaymentRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new PaymentRedirect($"cs_fake_{++_sessions}", $"https://pay.test/{_sessions}"));
        }

        public Task<bool> TryCloseSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            if (CompletedSessions.Contains(sessionId)) return Task.FromResult(false);
            ClosedSessions.Add(sessionId);
            return Task.FromResult(true);
        }

        public Task<ProviderSessionState> GetSessionStateAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(CompletedSessions.Contains(sessionId) ? ProviderSessionState.Paid : ProviderSessionState.Open);

        public PaymentNotification? ParseNotification(string body, IHeaderDictionary headers) => throw new NotSupportedException();
    }
}
