namespace HikingStore.Services.Payments
{
    /// <summary>
    /// A card processor that hosts the payment page itself, so card numbers and CVV codes are entered on the
    /// provider's PCI-compliant page and never reach this application.
    /// </summary>
    public interface ICardPaymentProvider
    {
        /// <summary>Stable key stored on orders and used in the webhook route, e.g. "Stripe".</summary>
        string Name { get; }

        /// <summary>Label shown to customers at checkout.</summary>
        string DisplayName { get; }

        bool IsTestMode { get; }

        /// <summary>Null when the provider is fully configured; otherwise what is missing.</summary>
        string? ConfigurationProblem { get; }

        Task<PaymentRedirect> CreateSessionAsync(PaymentRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Makes the session unpayable. Returns false if the customer already completed payment on it, in which
        /// case the order must wait for the provider's confirmation instead of being cancelled.
        /// </summary>
        Task<bool> TryCloseSessionAsync(string sessionId, CancellationToken cancellationToken);

        Task<ProviderSessionState> GetSessionStateAsync(string sessionId, CancellationToken cancellationToken);

        /// <summary>
        /// Verifies the signature of a provider notification and translates it. Throws
        /// <see cref="PaymentSignatureException"/> when the request is not authentic; returns null for event
        /// types this store doesn't act on.
        /// </summary>
        PaymentNotification? ParseNotification(string body, IHeaderDictionary headers);
    }

    public sealed record PaymentRequest(
        int OrderId,
        string OrderNumber,
        long AmountMinor,
        string Currency,
        string? CustomerEmail,
        string Description,
        string SuccessUrl,
        string CancelUrl,
        string NotificationUrl,
        DateTime ExpiresAtUtc,
        int Attempt);

    /// <summary>Where to send the customer. Providers that need a POST supply <see cref="FormFields"/>.</summary>
    public sealed record PaymentRedirect(string SessionId, string Url, IReadOnlyDictionary<string, string>? FormFields = null);

    public enum ProviderSessionState
    {
        Unknown,
        Open,
        Paid,
        Closed
    }

    public enum PaymentOutcome
    {
        /// <summary>Funds confirmed by the provider.</summary>
        Succeeded,
        /// <summary>Checkout finished but the funds are still being confirmed.</summary>
        Processing,
        /// <summary>One attempt was declined; the customer may still try again on the same session.</summary>
        AttemptFailed,
        /// <summary>The payment definitively failed.</summary>
        Failed,
        /// <summary>The session expired or was cancelled without payment.</summary>
        Expired,
        /// <summary>The payment was fully refunded at the provider.</summary>
        Refunded
    }

    public sealed record PaymentNotification(
        string Provider,
        string EventId,
        string EventType,
        PaymentOutcome Outcome,
        int? OrderId,
        string? SessionId,
        string? TransactionId,
        long? AmountMinor,
        string? Currency,
        string? FailureReason,
        bool LiveMode);

    public class PaymentSignatureException : Exception
    {
        public PaymentSignatureException(string message, Exception? inner = null) : base(message, inner) { }
    }

    public static class PaymentAmounts
    {
        private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
        {
            "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF"
        };

        /// <summary>Converts a decimal amount to the provider's smallest currency unit (cents for EUR).</summary>
        public static long ToMinorUnits(decimal amount, string currency)
        {
            var factor = ZeroDecimalCurrencies.Contains(currency) ? 1m : 100m;
            return (long)decimal.Round(amount * factor, 0, MidpointRounding.AwayFromZero);
        }

        public static decimal FromMinorUnits(long amount, string currency) =>
            ZeroDecimalCurrencies.Contains(currency) ? amount : amount / 100m;
    }
}
