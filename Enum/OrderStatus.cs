using System.ComponentModel.DataAnnotations;

namespace HikingStore.Enum
{
    public enum OrderStatus
    {
        [Display(Name = "Received")]
        Received = 1,

        [Display(Name = "Verified")]
        Verified = 2,

        [Display(Name = "Processing")]
        Processing = 3,

        [Display(Name = "Shipped")]
        Shipped = 4,

        [Display(Name = "Completed")]
        Completed = 5,

        [Display(Name = "Cancelled")]
        Cancelled = 6
    }
}

