using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HikingStore.Models
    {
        public class TrackingEvent
        {
            [Key]
            public int TrackingEventId { get; set; }

            [Required]
            public int OrderId { get; set; }

            [ForeignKey("OrderId")]
            public virtual Order Order { get; set; }

            [Required]
            public DateTime EventDate { get; set; }

            [Required]
            public string Location { get; set; }

            [Required]
            public string Status { get; set; }

            public string Description { get; set; }

            public bool IsMilestone { get; set; }
        }
    }
