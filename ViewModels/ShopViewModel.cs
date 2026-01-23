using HikingStore.Models;

namespace HikingStore.ViewModels
{
    public class ShopViewModel
    {
        public List<Product> Products { get; set; } = new List<Product>();
        public List<Category> Categories { get; set; } = new List<Category>();
    }
}
