using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace FurniCraft.Models
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
        [NotMapped]
        public IFormFile File { get; set; }
        public int CatId { get; set; }
        [ForeignKey("CatId")]
        public virtual Category Category { get; set; }
    }
}