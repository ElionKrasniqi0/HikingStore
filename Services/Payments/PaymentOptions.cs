namespace HikingStore.Services.Payments
{
    /// <summary>
    /// "Payments" configuration section. Secrets (API keys, webhook/signing secrets) must come from user-secrets,
    /// environment variables or a vault — appsettings.json only contains empty placeholders.
    /// </summary>
    public class PaymentOptions
    {
        public const string SectionName = "Payments";

        public bool CashOnDeliveryEnabled { get; set; } = true;

        /// <summary>Active card provider for new orders: "Stripe", "HostedGateway", or empty to disable card payments.</summary>
        public string? CardProvider { get; set; }

        /// <summary>How long a customer has to complete card payment before the order is released (30–1440).</summary>
        public int PendingPaymentTimeoutMinutes { get; set; } = 60;

        /// <summary>Extra time after expiry before the sweeper releases an order, so late webhooks arrive first.</summary>
        public int ExpiryGraceMinutes { get; set; } = 15;

        /// <summary>Allows test-mode keys while ASPNETCORE_ENVIRONMENT=Production (for a staging site). Off by default.</summary>
        public bool AllowTestModeInProduction { get; set; }

        /// <summary>Public site URL used in links sent from webhooks/background jobs (e.g. https://shop.example.com).</summary>
        public string? PublicBaseUrl { get; set; }

        public StripeOptions Stripe { get; set; } = new();
        public HostedGatewayOptions HostedGateway { get; set; } = new();

        public int EffectiveTimeoutMinutes => Math.Clamp(PendingPaymentTimeoutMinutes, 30, 1440);
    }

    public class StripeOptions
    {
        /// <summary>sk_test_… / sk_live_… or a restricted key rk_…. Secret: never commit.</summary>
        public string? SecretKey { get; set; }

        /// <summary>whsec_… signing secret of the webhook endpoint (or of `stripe listen`). Secret: never commit.</summary>
        public string? WebhookSecret { get; set; }

        /// <summary>Optional label shown on the checkout payment option.</summary>
        public string DisplayName { get; set; } = "Credit or debit card";
    }

    public class HostedGatewayOptions
    {
        /// <summary>Name of the bank/processor shown to customers, e.g. "ProCredit Bank".</summary>
        public string DisplayName { get; set; } = "Credit or debit card";

        public string? MerchantId { get; set; }

        /// <summary>HTTPS URL of the processor's hosted payment page that receives the signed form POST.</summary>
        public string? PaymentPageUrl { get; set; }

        /// <summary>Shared HMAC-SHA256 secret issued by the processor. Secret: never commit.</summary>
        public string? SigningSecret { get; set; }

        public bool TestMode { get; set; } = true;

        /// <summary>Maximum age of a signed notification, protecting against replays.</summary>
        public int NotificationToleranceSeconds { get; set; } = 600;
    }
}
