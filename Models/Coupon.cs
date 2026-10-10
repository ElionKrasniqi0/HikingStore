using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HikingStore.Models
{
    public enum DiscountType
    {
        [Display(Name = "Percentage (%)")]
        Percentage = 0,

        [Display(Name = "Fixed amount (€)")]
        FixedAmount = 1
    }

    public class Coupon
    {
        [Key]
        public int CouponId { get; set; }

        [Required, StringLength(40, MinimumLength = 3)]
        [RegularExpression("^[A-Za-z0-9_-]+$", ErrorMessage = "Use letters, numbers, dashes or underscores only")]
        public string Code { get; set; } = string.Empty;

        [StringLength(200)]
        public string? Description { get; set; }

        [Display(Name = "Discount type")]
        public DiscountType DiscountType { get; set; }

        [Range(0.01, 100_000)]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Value { get; set; }

        [Display(Name = "Minimum order subtotal")]
        [Range(0, 100_000)]
        [Column(TypeName = "decimal(18,2)")]
        public decimal? MinimumOrderAmount { get; set; }

        [Display(Name = "Maximum discount")]
        [Range(0, 100_000)]
        [Column(TypeName = "decimal(18,2)")]
        public decimal? MaxDiscountAmount { get; set; }

        [Display(Name = "Valid from (UTC)")]
        public DateTime? StartsAt { get; set; }

        [Display(Name = "Expires (UTC)")]
        public DateTime? ExpiresAt { get; set; }

        [Display(Name = "Total usage limit")]
        [Range(1, 1_000_000)]
        public int? UsageLimit { get; set; }

        [Display(Name = "Uses per customer")]
        [Range(1, 1000)]
        public int? UsageLimitPerUser { get; set; } = 1;

        public int TimesUsed { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [NotMapped]
        public string Summary => DiscountType == DiscountType.Percentage
            ? $"{Value:0.##}% off"
            : $"€{Value:0.00} off";
    }
}
