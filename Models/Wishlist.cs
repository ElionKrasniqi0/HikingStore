using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HikingStore.Models
{
    public class Wishlist
    {
        [Key]
        public int WishlistId { get; set; }
        
        public int ProId { get; set; }
        
        [ForeignKey("ProId")]
        public virtual Product Product { get; set; }

        public string? UserId { get; set; }
        
        [ForeignKey("UserId")]
        public virtual IdentityUser IdentityUser { get; set; }

        public DateTime AddedDate { get; set; } = DateTime.Now;
    }
}
