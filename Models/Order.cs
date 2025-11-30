using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using HikingStore.Enum;

namespace FurniCraft.Models
{
    public class Order
    {
        [Key]
        public int OrderId { get; set; }

        [Required]
        public string UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual IdentityUser User { get; set; }

        [Required]
        public DateTime OrderDate { get; set; }

        [Required]
        public decimal TotalAmount { get; set; }

        [Required]
        public string ShippingAddress { get; set; }

        [Required]
        public string City { get; set; }

        [Required]
        public string PhoneNumber { get; set; }

        public string Comment { get; set; }

        // Order Status Properties
        [Required]
        public OrderStatus Status { get; set; } = OrderStatus.Received;

        public DateTime? VerifiedDate { get; set; }
        public DateTime? ProcessingDate { get; set; }
        public DateTime? ShippedDate { get; set; }
        public DateTime? CompletedDate { get; set; }
        public DateTime? CancelledDate { get; set; }

        public string TrackingNumber { get; set; }
        public string AdminNotes { get; set; }
        public string Carrier { get; set; } = "Standard Shipping";
        public DateTime? EstimatedDelivery { get; set; }
        public string ShippingService { get; set; } = "Standard";

        // Tracking Events/History
        public virtual ICollection<TrackingEvent> TrackingEvents { get; set; }
        public virtual ICollection<OrderDetail> OrderDetails { get; set; }
    }
}
