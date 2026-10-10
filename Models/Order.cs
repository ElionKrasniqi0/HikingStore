using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HikingStore.Data;
using HikingStore.Enum;

namespace HikingStore.Models
{
    public class Order
    {
        [Key]
        public int OrderId { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public virtual ApplicationUser? User { get; set; }

        [Required]
        public DateTime OrderDate { get; set; }

        [StringLength(100)]
        public string? FullName { get; set; }

        [StringLength(256)]
        public string? Email { get; set; }

        [Required]
        public string ShippingAddress { get; set; } = string.Empty;

        [Required]
        public string City { get; set; } = string.Empty;

        [StringLength(20)]
        public string? PostalCode { get; set; }

        [StringLength(60)]
        public string? Country { get; set; }

        [Required]
        public string PhoneNumber { get; set; } = string.Empty;

        public string? Comment { get; set; }

        /// <summary>Sum of line totals before discount and shipping.</summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal Subtotal { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal ShippingCost { get; set; }

        /// <summary>Amount the customer pays: subtotal - discount + shipping.</summary>
        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; }

        [StringLength(40)]
        public string? CouponCode { get; set; }

        [StringLength(30)]
        public string PaymentMethod { get; set; } = PaymentMethods.CashOnDelivery;

        [StringLength(30)]
        public string PaymentStatus { get; set; } = PaymentStatuses.Pending;

        /// <summary>Card payment provider that handles this order (e.g. "Stripe"); null for cash on delivery.</summary>
        [StringLength(30)]
        public string? PaymentProvider { get; set; }

        /// <summary>Provider checkout session of the latest payment attempt. Never contains card data.</summary>
        [StringLength(255)]
        public string? PaymentSessionId { get; set; }

        /// <summary>Provider reference of the confirmed payment (e.g. Stripe PaymentIntent id).</summary>
        [StringLength(255)]
        public string? PaymentTransactionId { get; set; }

        public int PaymentAttempts { get; set; }

        /// <summary>When an unpaid card order is released and its stock returned.</summary>
        public DateTime? PaymentExpiresAt { get; set; }

        public DateTime? PaidAt { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? PaidAmount { get; set; }

        [StringLength(300)]
        public string? PaymentFailureReason { get; set; }

        /// <summary>One-time token from the checkout form; prevents duplicate orders on double submit.</summary>
        public Guid? CheckoutToken { get; set; }

        [Required]
        public OrderStatus Status { get; set; } = OrderStatus.Received;

        public DateTime? VerifiedDate { get; set; }
        public DateTime? ProcessingDate { get; set; }
        public DateTime? ShippedDate { get; set; }
        public DateTime? CompletedDate { get; set; }
        public DateTime? CancelledDate { get; set; }

        public string? TrackingNumber { get; set; }
        public string? AdminNotes { get; set; }
        public string? Carrier { get; set; } = "Standard Shipping";
        public DateTime? EstimatedDelivery { get; set; }
        public string? ShippingService { get; set; } = "Standard";

        public virtual ICollection<TrackingEvent> TrackingEvents { get; set; } = new List<TrackingEvent>();
        public virtual ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();

        [NotMapped]
        public string OrderNumber => FormatNumber(OrderId);

        public static string FormatNumber(int orderId) => $"ORD{orderId:D6}";

        [NotMapped]
        public bool IsCardPayment => PaymentMethod == PaymentMethods.Card;

        /// <summary>Card order created at checkout whose payment the provider has not confirmed yet.</summary>
        [NotMapped]
        public bool IsAwaitingCardPayment => IsCardPayment && PaymentStatus == PaymentStatuses.Pending && Status != OrderStatus.Cancelled;

        /// <summary>Paid card orders need a refund, so customers cancel those through support instead.</summary>
        [NotMapped]
        public bool CanBeCancelledByCustomer => Status < OrderStatus.Shipped && Status != OrderStatus.Cancelled
            && (!IsCardPayment || PaymentStatus == PaymentStatuses.Pending);

        [NotMapped]
        public string PaymentStatusLabel => PaymentStatuses.Label(PaymentStatus, PaymentMethod);
    }

    public static class PaymentMethods
    {
        public const string CashOnDelivery = "CashOnDelivery";
        public const string Card = "Card";

        public static string DisplayName(string? method) => method switch
        {
            CashOnDelivery => "Cash on delivery",
            Card => "Card payment",
            _ => method ?? "Unknown"
        };
    }

    public static class PaymentStatuses
    {
        public const string Pending = "Pending";
        public const string Paid = "Paid";
        public const string Refunded = "Refunded";
        public const string Cancelled = "Cancelled";
        /// <summary>The provider reported the payment as declined or failed.</summary>
        public const string Failed = "Failed";
        /// <summary>The customer did not complete payment before the checkout session expired.</summary>
        public const string Expired = "Expired";
        /// <summary>Money was captured for an order that is cancelled; staff must refund it at the provider.</summary>
        public const string RefundDue = "RefundDue";
        /// <summary>The confirmed payment did not match the order (amount/currency); staff must check it.</summary>
        public const string RequiresReview = "RequiresReview";

        public static string Label(string? status, string? method) => status switch
        {
            Pending when method == PaymentMethods.Card => "Awaiting card payment",
            Pending => "Due on delivery",
            Paid => "Paid",
            Refunded => "Refunded",
            Cancelled => "Cancelled",
            Failed => "Payment failed",
            Expired => "Payment not completed",
            RefundDue => "Refund due",
            RequiresReview => "Under review",
            _ => status ?? "Unknown"
        };
    }
}
