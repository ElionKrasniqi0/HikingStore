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
        public bool CanBeCancelledByCustomer => Status < OrderStatus.Shipped && Status != OrderStatus.Cancelled;
    }

    public static class PaymentMethods
    {
        public const string CashOnDelivery = "CashOnDelivery";

        public static string DisplayName(string? method) => method switch
        {
            CashOnDelivery => "Cash on delivery",
            _ => method ?? "Unknown"
        };
    }

    public static class PaymentStatuses
    {
        public const string Pending = "Pending";
        public const string Paid = "Paid";
        public const string Refunded = "Refunded";
        public const string Cancelled = "Cancelled";
    }
}
