using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HikingStore.Data;

namespace HikingStore.Models
{
    public class Review
    {
        [Key]
        public int ReviewId { get; set; }

        public int ProId { get; set; }

        [ForeignKey("ProId")]
        public virtual Product? Product { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public virtual ApplicationUser? User { get; set; }

        [Range(1, 5)]
        public int Rating { get; set; }

        [Required, StringLength(100, MinimumLength = 3)]
        public string Title { get; set; } = string.Empty;

        [Required, StringLength(2000, MinimumLength = 10)]
        public string Comment { get; set; } = string.Empty;

        /// <summary>Only approved reviews are shown and counted in the average rating.</summary>
        public bool IsApproved { get; set; }

        /// <summary>True when the reviewer has a delivered order containing the product.</summary>
        public bool IsVerifiedPurchase { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
