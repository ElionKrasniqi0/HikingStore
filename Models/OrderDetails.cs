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
    public virtual Order Order { get; set; }

    [Required]
    public int ProId { get; set; }

    [ForeignKey("ProId")]
    public virtual Product Product { get; set; }

    [Required]
    public int Quantity { get; set; }

    [Required]
    public decimal Price { get; set; }
}
