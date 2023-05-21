using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HikingStore.Models
{
    public class Category
    {
        [Key]
        public int CatId { get; set; }
        [Required(ErrorMessage = "This field must be completed")]
        public string? CatName { get; set; }
        public string? CatPhoto { get; set; }
        [NotMapped]
        public IFormFile File { get; set; }

        public virtual ICollection<Product> Product { get; set; }
    }
}
