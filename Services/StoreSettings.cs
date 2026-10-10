namespace HikingStore.Services
{
    public class StoreSettings
    {
        public string Name { get; set; } = "HikingStore";
        public string Currency { get; set; } = "EUR";
        public string CurrencySymbol { get; set; } = "€";
        public string SupportEmail { get; set; } = string.Empty;
        public string SupportPhone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public List<string> OpeningHours { get; set; } = new();
        public decimal FreeShippingThreshold { get; set; } = 100;
        public int ReturnDays { get; set; } = 30;
        public decimal VatRate { get; set; } = 0.18m;
        public string? AnnouncementText { get; set; }
        public string? HeroImageUrl { get; set; }
        public string? HeroImageCredit { get; set; }
        public List<ShippingMethodOption> ShippingMethods { get; set; } = new();
        public Dictionary<string, string> SocialLinks { get; set; } = new();

        public ShippingMethodOption DefaultShippingMethod =>
            ShippingMethods.FirstOrDefault() ?? new ShippingMethodOption { Code = "standard", Name = "Standard Shipping", Cost = 0 };

        public ShippingMethodOption? FindShippingMethod(string? code) =>
            ShippingMethods.FirstOrDefault(m => string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase));

        /// <summary>Shipping cost for a method given the discounted merchandise subtotal.</summary>
        public decimal ShippingCostFor(ShippingMethodOption method, decimal subtotalAfterDiscount)
        {
            if (method.FreeOverThreshold && FreeShippingThreshold > 0 && subtotalAfterDiscount >= FreeShippingThreshold)
            {
                return 0;
            }
            return method.Cost;
        }

        public string FormatPrice(decimal amount) =>
            CurrencySymbol + amount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
    }

    public class ShippingMethodOption
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal Cost { get; set; }
        public string? EstimatedDays { get; set; }
        public bool FreeOverThreshold { get; set; }
    }
}
