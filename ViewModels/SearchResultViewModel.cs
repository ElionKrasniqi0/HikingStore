using HikingStore.Models;

namespace HikingStore.ViewModels
{
    public class SearchResultViewModel
    {
        public string Query { get; set; } = string.Empty;
        public List<Product> Products { get; set; } = new List<Product>();
        public List<Category> Categories { get; set; } = new List<Category>();
        public int ProductCount { get; set; }
        public int CategoryCount { get; set; }
    }
}