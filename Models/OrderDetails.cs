using HikingStore.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class OrderDetail
{
    [Key]
    public int OrderDetailId { get; set; }

    [Required]
    public int OrderId { get; set; }

    [ForeignKey("OrderId")]
    public virtual Order? Order { get; set; }

    [Required]
    public int ProId { get; set; }

    [ForeignKey("ProId")]
    public virtual Product? Product { get; set; }

    /// <summary>Product name at the time of purchase, so history survives product edits.</summary>
    [StringLength(200)]
    public string? ProductName { get; set; }

    [StringLength(50)]
    public string? SelectedSize { get; set; }

    [StringLength(50)]
    public string? SelectedColor { get; set; }

    [Required]
    public int Quantity { get; set; }

    /// <summary>Unit price charged at checkout.</summary>
    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }

    [NotMapped]
    public decimal LineTotal => Price * Quantity;

    [NotMapped]
    public string DisplayName => ProductName ?? Product?.ProName ?? "Product";
}
