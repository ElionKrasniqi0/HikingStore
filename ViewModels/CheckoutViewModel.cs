using System.ComponentModel.DataAnnotations;
using HikingStore.Models;
using HikingStore.Services;

namespace HikingStore.ViewModels
{
    public class CheckoutInput
    {
        [Required(ErrorMessage = "Please enter your full name")]
        [StringLength(100)]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your email")]
        [EmailAddress]
        [StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter a phone number")]
        [Phone]
        [StringLength(30)]
        [Display(Name = "Phone number")]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your street address")]
        [StringLength(200)]
        [Display(Name = "Street address")]
        public string AddressLine { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your city")]
        [StringLength(100)]
        public string City { get; set; } = string.Empty;

        [StringLength(20)]
        [Display(Name = "Postal code")]
        public string? PostalCode { get; set; }

        [Required]
        [StringLength(60)]
        public string Country { get; set; } = "Kosovo";

        [StringLength(500)]
        [Display(Name = "Order notes (optional)")]
        public string? Comment { get; set; }

        [Required(ErrorMessage = "Please choose a shipping method")]
        [Display(Name = "Shipping method")]
        public string ShippingMethod { get; set; } = "standard";

        [Required(ErrorMessage = "Please choose a payment method")]
        [Display(Name = "Payment method")]
        public string PaymentMethod { get; set; } = PaymentMethods.CashOnDelivery;

        [Display(Name = "Save this address to my account")]
        public bool SaveAddress { get; set; }

        public Guid CheckoutToken { get; set; }
    }

    public class CheckoutViewModel
    {
        public CheckoutInput Input { get; set; } = new();
        public CartSummary Cart { get; set; } = new();
        public List<ShippingMethodOption> ShippingMethods { get; set; } = new();
        public List<Address> SavedAddresses { get; set; } = new();

        public bool CashOnDeliveryAvailable { get; set; }

        /// <summary>Customer-facing name of the active card provider; null when card payment is unavailable.</summary>
        public string? CardProviderName { get; set; }

        public bool CardTestMode { get; set; }
        public int CardPaymentTimeoutMinutes { get; set; }

        public bool HasPaymentMethod => CashOnDeliveryAvailable || CardProviderName != null;
    }
}
