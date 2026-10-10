using System.Globalization;
using Stripe;
using Stripe.Checkout;

namespace HikingStore.Services.Payments
{
    /// <summary>
    /// Card payments through Stripe Checkout: the customer is redirected to a Stripe-hosted page, and the order is
    /// only marked paid when a signature-verified webhook confirms the payment.
    /// </summary>
    public class StripePaymentProvider : ICardPaymentProvider
    {
        public const string ProviderName = "Stripe";

        private readonly StripeOptions _options;
        private readonly StripeClient? _client;
        private readonly ILogger<StripePaymentProvider> _logger;

        public StripePaymentProvider(StripeOptions options, ILogger<StripePaymentProvider> logger)
        {
            _options = options;
            _logger = logger;
            ConfigurationProblem = Validate(options);
            if (!string.IsNullOrWhiteSpace(options.SecretKey) && ConfigurationProblem == null)
            {
                _client = new StripeClient(options.SecretKey);
            }
        }

        public string Name => ProviderName;
        public string DisplayName => _options.DisplayName;
        public string? ConfigurationProblem { get; }

        public bool IsTestMode => _options.SecretKey?.StartsWith("sk_test_", StringComparison.Ordinal) == true
            || _options.SecretKey?.StartsWith("rk_test_", StringComparison.Ordinal) == true;

        private static string? Validate(StripeOptions options)
        {
            var key = options.SecretKey?.Trim();
            if (string.IsNullOrEmpty(key))
                return "Payments:Stripe:SecretKey is not set.";
            if (key.StartsWith("pk_", StringComparison.Ordinal))
                return "Payments:Stripe:SecretKey contains a publishable key (pk_…); a secret key (sk_… or rk_…) is required.";
            if (!key.StartsWith("sk_", StringComparison.Ordinal) && !key.StartsWith("rk_", StringComparison.Ordinal))
                return "Payments:Stripe:SecretKey is not a Stripe secret key.";
            if (string.IsNullOrWhiteSpace(options.WebhookSecret))
                return "Payments:Stripe:WebhookSecret is not set, so payments could never be confirmed.";
            if (!options.WebhookSecret.Trim().StartsWith("whsec_", StringComparison.Ordinal))
                return "Payments:Stripe:WebhookSecret must be the endpoint signing secret (whsec_…).";
            return null;
        }

        private StripeClient Client => _client ?? throw new InvalidOperationException(ConfigurationProblem ?? "Stripe is not configured.");

        public async Task<PaymentRedirect> CreateSessionAsync(PaymentRequest request, CancellationToken cancellationToken)
        {
            var orderId = request.OrderId.ToString(CultureInfo.InvariantCulture);
            var metadata = new Dictionary<string, string>
            {
                ["order_id"] = orderId,
                ["order_number"] = request.OrderNumber,
                ["attempt"] = request.Attempt.ToString(CultureInfo.InvariantCulture)
            };

            var options = new SessionCreateOptions
            {
                Mode = "payment",
                // Restricts Checkout to cards (credit and debit) even if other methods are enabled in the Dashboard.
                AllowedPaymentMethodTypes = new List<string> { "card" },
                ClientReferenceId = orderId,
                CustomerEmail = string.IsNullOrWhiteSpace(request.CustomerEmail) ? null : request.CustomerEmail,
                SuccessUrl = request.SuccessUrl,
                CancelUrl = request.CancelUrl,
                ExpiresAt = ClampExpiry(request.ExpiresAtUtc),
                Metadata = metadata,
                PaymentIntentData = new SessionPaymentIntentDataOptions
                {
                    Description = request.Description,
                    Metadata = metadata
                },
                LineItems = new List<SessionLineItemOptions>
                {
                    new()
                    {
                        Quantity = 1,
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = request.Currency.ToLowerInvariant(),
                            UnitAmount = request.AmountMinor,
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = request.Description
                            }
                        }
                    }
                }
            };

            // Same order + attempt ⇒ same key, so a retried request can never open a second session.
            var requestOptions = new RequestOptions { IdempotencyKey = $"hikingstore-order-{orderId}-attempt-{request.Attempt}" };
            var session = await new SessionService(Client).CreateAsync(options, requestOptions, cancellationToken);
            _logger.LogInformation("Stripe Checkout session {SessionId} created for order {OrderId} ({Amount} {Currency}, test mode: {TestMode})",
                session.Id, request.OrderId, request.AmountMinor, request.Currency, IsTestMode);
            return new PaymentRedirect(session.Id, session.Url);
        }

        public async Task<bool> TryCloseSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            var service = new SessionService(Client);
            Session session;
            try
            {
                session = await service.GetAsync(sessionId, null, null, cancellationToken);
            }
            catch (StripeException ex) when (ex.StripeError?.Code == "resource_missing")
            {
                return true;
            }

            switch (session.Status)
            {
                case "expired":
                    return true;
                case "complete":
                    return false;
                default:
                    try
                    {
                        await service.ExpireAsync(sessionId, null, null, cancellationToken);
                        return true;
                    }
                    catch (StripeException ex)
                    {
                        // Expiring fails if the customer completed payment a moment ago — re-read to be sure.
                        _logger.LogWarning(ex, "Could not expire Stripe session {SessionId}", sessionId);
                        var latest = await service.GetAsync(sessionId, null, null, cancellationToken);
                        return latest.Status == "expired";
                    }
            }
        }

        public async Task<ProviderSessionState> GetSessionStateAsync(string sessionId, CancellationToken cancellationToken)
        {
            try
            {
                var session = await new SessionService(Client).GetAsync(sessionId, null, null, cancellationToken);
                return session.Status switch
                {
                    "open" => ProviderSessionState.Open,
                    "expired" => ProviderSessionState.Closed,
                    "complete" when session.PaymentStatus is "paid" or "no_payment_required" => ProviderSessionState.Paid,
                    // Complete but unpaid: funds still processing; the async_payment_* webhook decides.
                    "complete" => ProviderSessionState.Paid,
                    _ => ProviderSessionState.Unknown
                };
            }
            catch (StripeException ex) when (ex.StripeError?.Code == "resource_missing")
            {
                return ProviderSessionState.Closed;
            }
        }

        public PaymentNotification? ParseNotification(string body, IHeaderDictionary headers)
        {
            if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
                throw new PaymentSignatureException("Stripe webhook secret is not configured.");

            var signature = headers["Stripe-Signature"].ToString();
            if (string.IsNullOrEmpty(signature))
                throw new PaymentSignatureException("Missing Stripe-Signature header.");

            Event stripeEvent;
            try
            {
                // Verifies the HMAC signature and rejects events older than 5 minutes (replay protection).
                stripeEvent = EventUtility.ConstructEvent(body, signature, _options.WebhookSecret.Trim(), 300, throwOnApiVersionMismatch: false);
            }
            catch (StripeException ex)
            {
                throw new PaymentSignatureException("Stripe signature verification failed.", ex);
            }

            switch (stripeEvent.Type)
            {
                case EventTypes.CheckoutSessionCompleted:
                case EventTypes.CheckoutSessionAsyncPaymentSucceeded:
                case EventTypes.CheckoutSessionAsyncPaymentFailed:
                case EventTypes.CheckoutSessionExpired:
                {
                    if (stripeEvent.Data.Object is not Session session) return null;
                    var outcome = stripeEvent.Type switch
                    {
                        EventTypes.CheckoutSessionAsyncPaymentSucceeded => PaymentOutcome.Succeeded,
                        EventTypes.CheckoutSessionAsyncPaymentFailed => PaymentOutcome.Failed,
                        EventTypes.CheckoutSessionExpired => PaymentOutcome.Expired,
                        _ => session.PaymentStatus is "paid" or "no_payment_required" ? PaymentOutcome.Succeeded : PaymentOutcome.Processing
                    };
                    return new PaymentNotification(ProviderName, stripeEvent.Id, stripeEvent.Type, outcome,
                        ParseOrderId(session.ClientReferenceId) ?? ParseOrderId(session.Metadata),
                        session.Id, session.PaymentIntentId, session.AmountTotal, session.Currency,
                        outcome == PaymentOutcome.Failed ? "The card payment failed." : null,
                        stripeEvent.Livemode);
                }
                case EventTypes.PaymentIntentPaymentFailed:
                {
                    if (stripeEvent.Data.Object is not PaymentIntent intent) return null;
                    return new PaymentNotification(ProviderName, stripeEvent.Id, stripeEvent.Type, PaymentOutcome.AttemptFailed,
                        ParseOrderId(intent.Metadata), null, intent.Id, intent.Amount, intent.Currency,
                        Truncate(intent.LastPaymentError?.Message ?? "The card was declined."), stripeEvent.Livemode);
                }
                case EventTypes.ChargeRefunded:
                {
                    if (stripeEvent.Data.Object is not Charge charge) return null;
                    if (!charge.Refunded)
                    {
                        _logger.LogInformation("Partial refund {Refunded}/{Amount} on {PaymentIntent}; order status unchanged",
                            charge.AmountRefunded, charge.Amount, charge.PaymentIntentId);
                        return null;
                    }
                    return new PaymentNotification(ProviderName, stripeEvent.Id, stripeEvent.Type, PaymentOutcome.Refunded,
                        ParseOrderId(charge.Metadata), null, charge.PaymentIntentId, charge.AmountRefunded, charge.Currency,
                        null, stripeEvent.Livemode);
                }
                default:
                    return null;
            }
        }

        /// <summary>Stripe only accepts expiries between 30 minutes and 24 hours after the session is created.</summary>
        private static DateTime ClampExpiry(DateTime requested)
        {
            var now = DateTime.UtcNow;
            var min = now.AddMinutes(31);
            var max = now.AddHours(23).AddMinutes(55);
            return requested < min ? min : requested > max ? max : requested;
        }

        private static int? ParseOrderId(string? value) =>
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;

        private static int? ParseOrderId(IDictionary<string, string>? metadata) =>
            metadata != null && metadata.TryGetValue("order_id", out var value) ? ParseOrderId(value) : null;

        private static string Truncate(string value) => value.Length <= 300 ? value : value[..300];
    }
}
