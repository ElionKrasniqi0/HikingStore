using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace HikingStore.Services.Payments
{
    /// <summary>
    /// Generic hosted-payment-page integration for a bank or card processor (e-commerce acquiring). The customer is
    /// sent to the processor's page with an HMAC-SHA256 signed form POST, and the processor reports the result with
    /// a signed server-to-server notification. The field names and signing rules are documented in docs/payments.md;
    /// adjust <see cref="BuildCanonicalString"/> and the field names here to match the processor's specification.
    /// </summary>
    public class HostedGatewayPaymentProvider : ICardPaymentProvider
    {
        public const string ProviderName = "HostedGateway";

        private readonly HostedGatewayOptions _options;
        private readonly ILogger<HostedGatewayPaymentProvider> _logger;

        public HostedGatewayPaymentProvider(HostedGatewayOptions options, ILogger<HostedGatewayPaymentProvider> logger)
        {
            _options = options;
            _logger = logger;
            ConfigurationProblem = Validate(options);
        }

        public string Name => ProviderName;
        public string DisplayName => _options.DisplayName;
        public bool IsTestMode => _options.TestMode;
        public string? ConfigurationProblem { get; }

        private static string? Validate(HostedGatewayOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.MerchantId))
                return "Payments:HostedGateway:MerchantId is not set.";
            if (!Uri.TryCreate(options.PaymentPageUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
                return "Payments:HostedGateway:PaymentPageUrl must be the processor's https:// payment page URL.";
            if (string.IsNullOrWhiteSpace(options.SigningSecret) || options.SigningSecret.Length < 32)
                return "Payments:HostedGateway:SigningSecret must be set (at least 32 characters).";
            return null;
        }

        public Task<PaymentRedirect> CreateSessionAsync(PaymentRequest request, CancellationToken cancellationToken)
        {
            if (ConfigurationProblem != null) throw new InvalidOperationException(ConfigurationProblem);

            var sessionId = $"HS-{request.OrderId}-{request.Attempt}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(6))}";
            var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["merchant_id"] = _options.MerchantId!,
                ["session_id"] = sessionId,
                ["order_id"] = request.OrderId.ToString(CultureInfo.InvariantCulture),
                ["amount"] = request.AmountMinor.ToString(CultureInfo.InvariantCulture),
                ["currency"] = request.Currency.ToUpperInvariant(),
                ["description"] = request.Description,
                ["email"] = request.CustomerEmail ?? string.Empty,
                ["success_url"] = request.SuccessUrl,
                ["cancel_url"] = request.CancelUrl,
                ["notify_url"] = request.NotificationUrl,
                ["expires_at"] = new DateTimeOffset(request.ExpiresAtUtc, TimeSpan.Zero).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ["test"] = _options.TestMode ? "1" : "0"
            };
            fields["signature"] = Sign(fields);

            _logger.LogInformation("Hosted gateway session {SessionId} prepared for order {OrderId} ({Amount} {Currency})",
                sessionId, request.OrderId, request.AmountMinor, request.Currency);
            return Task.FromResult(new PaymentRedirect(sessionId, _options.PaymentPageUrl!, fields));
        }

        /// <summary>
        /// The generic protocol has no API for closing a payment page, so this cannot guarantee the customer won't
        /// still pay. A payment that arrives for a cancelled order is flagged RefundDue for staff.
        /// </summary>
        public Task<bool> TryCloseSessionAsync(string sessionId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<ProviderSessionState> GetSessionStateAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(ProviderSessionState.Unknown);

        public PaymentNotification? ParseNotification(string body, IHeaderDictionary headers)
        {
            if (ConfigurationProblem != null) throw new PaymentSignatureException(ConfigurationProblem);

            var parsed = QueryHelpers.ParseQuery(body);
            var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in parsed)
            {
                if (value.Count != 1) throw new PaymentSignatureException($"Duplicate notification field '{key}'.");
                fields[key] = value[0] ?? string.Empty;
            }

            if (!fields.Remove("signature", out var signature) || string.IsNullOrEmpty(signature))
                throw new PaymentSignatureException("Missing signature.");

            var expected = Convert.FromHexString(Sign(fields));
            byte[] actual;
            try { actual = Convert.FromHexString(signature); }
            catch (FormatException) { throw new PaymentSignatureException("Malformed signature."); }
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                throw new PaymentSignatureException("Invalid signature.");

            if (!fields.TryGetValue("merchant_id", out var merchant) || merchant != _options.MerchantId)
                throw new PaymentSignatureException("Notification is for another merchant.");
            if (!long.TryParse(Get(fields, "timestamp"), NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp)
                || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp) > _options.NotificationToleranceSeconds)
                throw new PaymentSignatureException("Notification timestamp is outside the allowed window.");

            var eventId = Get(fields, "event_id");
            var status = Get(fields, "status")?.ToLowerInvariant();
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(status)) return null;

            PaymentOutcome? outcome = status switch
            {
                "paid" => PaymentOutcome.Succeeded,
                "pending" => PaymentOutcome.Processing,
                "declined" => PaymentOutcome.AttemptFailed,
                "failed" => PaymentOutcome.Failed,
                "cancelled" or "expired" => PaymentOutcome.Expired,
                "refunded" => PaymentOutcome.Refunded,
                _ => null
            };
            if (outcome == null) return null;

            long? amount = long.TryParse(Get(fields, "amount"), NumberStyles.None, CultureInfo.InvariantCulture, out var a) ? a : null;
            int? orderId = int.TryParse(Get(fields, "order_id"), NumberStyles.None, CultureInfo.InvariantCulture, out var o) ? o : null;
            var reason = Get(fields, "reason");
            return new PaymentNotification(ProviderName, eventId, $"payment.{status}", outcome.Value, orderId,
                Get(fields, "session_id"), Get(fields, "transaction_id"), amount, Get(fields, "currency"),
                string.IsNullOrEmpty(reason) ? null : reason.Length > 300 ? reason[..300] : reason,
                Get(fields, "test") != "1");
        }

        private static string? Get(IDictionary<string, string> fields, string key) =>
            fields.TryGetValue(key, out var value) ? value : null;

        /// <summary>Fields sorted by name (ordinal), joined as name=value with '\n'; signature field excluded.</summary>
        public static string BuildCanonicalString(IEnumerable<KeyValuePair<string, string>> fields) =>
            string.Join('\n', fields.Where(f => f.Key != "signature")
                .OrderBy(f => f.Key, StringComparer.Ordinal)
                .Select(f => $"{f.Key}={f.Value}"));

        private string Sign(IEnumerable<KeyValuePair<string, string>> fields) =>
            ComputeSignature(_options.SigningSecret!, fields);

        public static string ComputeSignature(string secret, IEnumerable<KeyValuePair<string, string>> fields)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(BuildCanonicalString(fields)))).ToLowerInvariant();
        }
    }
}
