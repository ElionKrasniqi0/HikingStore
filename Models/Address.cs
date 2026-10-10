using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HikingStore.Data;

namespace HikingStore.Models
{
    public class Address
    {
        [Key]
        public int AddressId { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public virtual ApplicationUser? User { get; set; }

        [StringLength(50)]
        [Display(Name = "Label (e.g. Home, Work)")]
        public string? Label { get; set; }

        [Required, StringLength(100)]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Required, StringLength(30)]
        [Phone]
        [Display(Name = "Phone number")]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required, StringLength(200)]
        [Display(Name = "Street address")]
        public string AddressLine { get; set; } = string.Empty;

        [Required, StringLength(100)]
        public string City { get; set; } = string.Empty;

        [StringLength(20)]
        [Display(Name = "Postal code")]
        public string? PostalCode { get; set; }

        [Required, StringLength(60)]
        public string Country { get; set; } = "Kosovo";

        [Display(Name = "Use as default address")]
        public bool IsDefault { get; set; }
    }
}
