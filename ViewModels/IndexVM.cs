using HikingStore.Models;

namespace HikingStore.ViewModels
{
    public class IndexVM
    {
        public List<Category> Categories { get; set; } = new List<Category>();
        public List<Product> Products { get; set; } = new List<Product>();
    }
}
