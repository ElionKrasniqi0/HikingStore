using System.ComponentModel.DataAnnotations;

namespace HikingStore.Models
{
    /// <summary>
    /// A verified notification received from a payment provider. The unique (Provider, EventId) pair makes
    /// webhook processing idempotent: providers retry deliveries, and each event must only be applied once.
    /// </summary>
    public class PaymentEvent
    {
        public int Id { get; set; }

        [Required, StringLength(30)]
        public string Provider { get; set; } = string.Empty;

        [Required, StringLength(255)]
        public string EventId { get; set; } = string.Empty;

        [Required, StringLength(100)]
        public string EventType { get; set; } = string.Empty;

        public int? OrderId { get; set; }

        public DateTime ReceivedAt { get; set; }

        [StringLength(300)]
        public string? Result { get; set; }
    }
}
