using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace HikingStore.Models
{
    public class Product
    {
        [Key]
        public int ProId { get; set; }

        [Required(ErrorMessage = "This field must be completed")]
        public string? ProName { get; set; }

        [Required(ErrorMessage = "This field must be completed")]
        public string? Descreption { get; set; }

        [Required(ErrorMessage = "This field must be completed")]
        public decimal Price { get; set; }

        public string ProImage { get; set; }

        /// <summary>Comma-separated additional image file names (e.g. "img2.jpg,img3.jpg"). Stored in ~/Product/ like ProImage.</summary>
        public string? AdditionalImages { get; set; }

        /// <summary>Main image first, then any additional. Use in views for gallery.</summary>
        [NotMapped]
        public IReadOnlyList<string> AllImagePaths
        {
            get
            {
                var list = new List<string>();
                if (!string.IsNullOrWhiteSpace(ProImage))
                    list.Add(ProImage.Trim());
                if (!string.IsNullOrWhiteSpace(AdditionalImages))
                {
                    foreach (var s in AdditionalImages.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var trimmed = s.Trim();
                        if (trimmed.Length > 0 && !list.Contains(trimmed))
                            list.Add(trimmed);
                    }
                }
                return list;
            }
        }

        [NotMapped]
        public IFormFile File { get; set; }

        [Required]
        public int StockQuantity { get; set; } = 0;

        public bool IsInStock => StockQuantity > 0;

        public int CatId { get; set; }

        [ForeignKey("CatId")]
        public virtual Category Category { get; set; }
    }
}