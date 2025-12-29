using HikingStore.Models;

namespace HikingStore.ViewModels
{
    public class SearchResultViewModel
    {
        public string Query { get; set; }
        public List<Product> Products { get; set; }
        public List<Category> Categories { get; set; }
        public int ProductCount { get; set; }
        public int CategoryCount { get; set; }
    }
}