using HikingStore.Data;
using HikingStore.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class ShoppingCart
{
    [Key]
    public int CartId { get; set; }

    public int ProId { get; set; }

    [ForeignKey("ProId")]
    public virtual Product? Product { get; set; }

    [Range(1, 999, ErrorMessage = "Quantity must be between 1 and 999")]
    public int Qty { get; set; }

    public string? ShippingOption { get; set; }

    [StringLength(50)]
    public string? SelectedSize { get; set; }

    [StringLength(50)]
    public string? SelectedColor { get; set; }

    public string? UserId { get; set; }

    [ForeignKey("UserId")]
    public virtual ApplicationUser? ApplicationUser { get; set; }
}
