using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace HikingStore.Models
{
    public class ShoppingCart
    {

        [Key]
        public int CartId { get; set; }
        public int ProId { get; set; }
        [ForeignKey("ProId")]
        public virtual Product Product { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "From 1")]
        public int Qty { get; set; }

        public string? UserId { get; set; }
        [ForeignKey("UserId")]

        public virtual IdentityUser IdentityUser { get; set; }
    }
}
