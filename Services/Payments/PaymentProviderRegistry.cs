using Microsoft.Extensions.Options;

namespace HikingStore.Services.Payments
{
    /// <summary>
    /// Builds the configured card providers once at startup and decides which one new orders may use.
    /// Providers stay resolvable by name so webhooks for older orders still work after switching provider.
    /// </summary>
    public class PaymentProviderRegistry
    {
        private readonly Dictionary<string, ICardPaymentProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

        public PaymentProviderRegistry(IOptions<PaymentOptions> options, IWebHostEnvironment environment, ILoggerFactory loggerFactory)
            : this(options.Value, environment.IsProduction(), new ICardPaymentProvider[]
            {
                new StripePaymentProvider(options.Value.Stripe, loggerFactory.CreateLogger<StripePaymentProvider>()),
                new HostedGatewayPaymentProvider(options.Value.HostedGateway, loggerFactory.CreateLogger<HostedGatewayPaymentProvider>())
            }, loggerFactory.CreateLogger<PaymentProviderRegistry>())
        {
        }

        public PaymentProviderRegistry(PaymentOptions options, bool isProduction, IEnumerable<ICardPaymentProvider> providers, ILogger logger)
        {
            Options = options;
            foreach (var provider in providers)
            {
                if (provider.ConfigurationProblem == null) _providers[provider.Name] = provider;
            }

            var selected = options.CardProvider?.Trim();
            if (string.IsNullOrEmpty(selected))
            {
                CardUnavailableReason = "Card payments are disabled (Payments:CardProvider is empty).";
            }
            else
            {
                var candidate = providers.FirstOrDefault(p => string.Equals(p.Name, selected, StringComparison.OrdinalIgnoreCase));
                if (candidate == null)
                    CardUnavailableReason = $"Unknown Payments:CardProvider '{selected}'. Use 'Stripe' or 'HostedGateway'.";
                else if (candidate.ConfigurationProblem != null)
                    CardUnavailableReason = candidate.ConfigurationProblem;
                else if (isProduction && candidate.IsTestMode && !options.AllowTestModeInProduction)
                    CardUnavailableReason = $"{candidate.Name} is configured with test credentials in Production; card payments stay off until live credentials are set.";
                else
                    ActiveProvider = candidate;
            }

            if (ActiveProvider != null)
                logger.LogInformation("Card payments enabled via {Provider} (test mode: {TestMode})", ActiveProvider.Name, ActiveProvider.IsTestMode);
            else
                logger.LogWarning("Card payments unavailable: {Reason}", CardUnavailableReason);
        }

        public PaymentOptions Options { get; }

        /// <summary>Provider used for new card orders; null when card payments are unavailable.</summary>
        public ICardPaymentProvider? ActiveProvider { get; }

        public string? CardUnavailableReason { get; }

        public bool CashOnDeliveryEnabled => Options.CashOnDeliveryEnabled;

        /// <summary>A configured provider by name, for existing orders and incoming webhooks.</summary>
        public ICardPaymentProvider? Get(string? name) =>
            name != null && _providers.TryGetValue(name, out var provider) ? provider : null;
    }
}
