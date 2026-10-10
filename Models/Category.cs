using HikingStore.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class Category
{
    [Key]
    public int CatId { get; set; }

    [Required(ErrorMessage = "Category name is required")]
    [StringLength(100)]
    [Display(Name = "Name")]
    public string? CatName { get; set; }

    public string? CatPhoto { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>Parent category for subcategories; null for top-level categories.</summary>
    [Display(Name = "Parent category")]
    public int? ParentCatId { get; set; }

    [ForeignKey("ParentCatId")]
    public virtual Category? Parent { get; set; }

    public virtual ICollection<Category> Children { get; set; } = new List<Category>();

    [Display(Name = "Display order")]
    public int DisplayOrder { get; set; }

    [NotMapped]
    public IFormFile? File { get; set; }

    public virtual ICollection<Product> Product { get; set; } = new List<Product>();
}
