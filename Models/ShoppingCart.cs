using HikingStore.Models;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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

public class ShoppingCartDetails
{
    public List<ShoppingCart> Carts { get; set; }
    public decimal TotalAmount { get; set; }
    public bool IsEmpty { get; set; } // Add this property

}
