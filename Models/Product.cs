using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HikingStore.Models
{
    public class Product
    {
        [Key]
        public int ProId { get; set; }

        [Required(ErrorMessage = "Product name is required")]
        [StringLength(200)]
        [Display(Name = "Product name")]
        public string? ProName { get; set; }

        [Required(ErrorMessage = "Description is required")]
        [Display(Name = "Description")]
        public string? Descreption { get; set; }

        [Required(ErrorMessage = "Price is required")]
        [Range(0.01, 1_000_000, ErrorMessage = "Price must be greater than 0")]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }

        /// <summary>Original price before discount. The product is on sale when this is greater than <see cref="Price"/>.</summary>
        [Range(0, 1_000_000)]
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Original price (before sale)")]
        public decimal? CompareAtPrice { get; set; }

        [StringLength(100)]
        public string? Brand { get; set; }

        [StringLength(200)]
        public string? Material { get; set; }

        /// <summary>One specification per line, formatted as "Name: Value".</summary>
        [StringLength(4000)]
        public string? Specifications { get; set; }

        /// <summary>Comma-separated list of selectable sizes, e.g. "S,M,L,XL".</summary>
        [StringLength(500)]
        [Display(Name = "Sizes (comma-separated)")]
        public string? Sizes { get; set; }

        /// <summary>Comma-separated list of selectable colors, e.g. "Forest Green,Black".</summary>
        [StringLength(500)]
        [Display(Name = "Colors (comma-separated)")]
        public string? Colors { get; set; }

        public string? ProImage { get; set; }

        /// <summary>Comma-separated additional image file names stored in ~/Product/ like ProImage.</summary>
        public string? AdditionalImages { get; set; }

        [Range(0, 1_000_000, ErrorMessage = "Stock cannot be negative")]
        [Display(Name = "Stock quantity")]
        public int StockQuantity { get; set; }

        /// <summary>Inactive products are hidden from the storefront but kept for order history.</summary>
        [Display(Name = "Visible in store")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Featured on homepage")]
        public bool IsFeatured { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Display(Name = "Category")]
        [Range(1, int.MaxValue, ErrorMessage = "Please choose a category")]
        public int CatId { get; set; }

        [ForeignKey("CatId")]
        public virtual Category? Category { get; set; }

        public virtual ICollection<Review> Reviews { get; set; } = new List<Review>();

        [NotMapped]
        public IFormFile? File { get; set; }

        [NotMapped]
        public List<IFormFile>? GalleryFiles { get; set; }

        [NotMapped]
        public bool IsInStock => StockQuantity > 0;

        [NotMapped]
        public bool IsOnSale => CompareAtPrice.HasValue && CompareAtPrice.Value > Price;

        [NotMapped]
        public int DiscountPercent => IsOnSale ? (int)Math.Round((1 - Price / CompareAtPrice!.Value) * 100) : 0;

        [NotMapped]
        public IReadOnlyList<string> SizeOptions => SplitList(Sizes);

        [NotMapped]
        public IReadOnlyList<string> ColorOptions => SplitList(Colors);

        [NotMapped]
        public IReadOnlyList<string> AdditionalImageList => SplitList(AdditionalImages);

        /// <summary>Main image first, then any additional images.</summary>
        [NotMapped]
        public IReadOnlyList<string> AllImagePaths
        {
            get
            {
                var list = new List<string>();
                if (!string.IsNullOrWhiteSpace(ProImage))
                    list.Add(ProImage.Trim());
                foreach (var image in AdditionalImageList)
                {
                    if (!list.Contains(image))
                        list.Add(image);
                }
                return list;
            }
        }

        [NotMapped]
        public IReadOnlyList<KeyValuePair<string, string>> SpecificationList
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Specifications))
                    return Array.Empty<KeyValuePair<string, string>>();

                return Specifications
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(line =>
                    {
                        var index = line.IndexOf(':');
                        return index > 0
                            ? new KeyValuePair<string, string>(line[..index].Trim(), line[(index + 1)..].Trim())
                            : new KeyValuePair<string, string>(line, string.Empty);
                    })
                    .ToList();
            }
        }

        private static IReadOnlyList<string> SplitList(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? Array.Empty<string>()
                : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
    }
}
