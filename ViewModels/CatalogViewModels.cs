using HikingStore.Models;

namespace HikingStore.ViewModels
{
    public class CatalogFilter
    {
        public const int DefaultPageSize = 24;

        public string? Q { get; set; }
        public int? Category { get; set; }
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public List<string> Brand { get; set; } = new();
        public string? Size { get; set; }
        public string? Color { get; set; }
        public bool InStock { get; set; }
        public bool OnSale { get; set; }
        public int? Rating { get; set; }
        public string Sort { get; set; } = "newest";
        public int Page { get; set; } = 1;

        public bool HasActiveFilters =>
            !string.IsNullOrWhiteSpace(Q) || Category.HasValue || MinPrice.HasValue || MaxPrice.HasValue ||
            Brand.Count > 0 || !string.IsNullOrEmpty(Size) || !string.IsNullOrEmpty(Color) ||
            InStock || OnSale || Rating.HasValue;

        /// <summary>Route values for building links that keep the current filters.</summary>
        public Dictionary<string, string> ToRouteValues(int? page = null, string? sort = null)
        {
            var values = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(Q)) values["q"] = Q!;
            if (Category.HasValue) values["category"] = Category.Value.ToString();
            if (MinPrice.HasValue) values["minPrice"] = MinPrice.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (MaxPrice.HasValue) values["maxPrice"] = MaxPrice.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            for (var i = 0; i < Brand.Count; i++) values[$"brand[{i}]"] = Brand[i];
            if (!string.IsNullOrEmpty(Size)) values["size"] = Size!;
            if (!string.IsNullOrEmpty(Color)) values["color"] = Color!;
            if (InStock) values["inStock"] = "true";
            if (OnSale) values["onSale"] = "true";
            if (Rating.HasValue) values["rating"] = Rating.Value.ToString();
            var effectiveSort = sort ?? Sort;
            if (effectiveSort != "newest") values["sort"] = effectiveSort;
            var effectivePage = page ?? Page;
            if (effectivePage > 1) values["page"] = effectivePage.ToString();
            return values;
        }
    }

    public class ProductCardModel
    {
        public Product Product { get; set; } = null!;
        public double? AverageRating { get; set; }
        public int ReviewCount { get; set; }
        public bool InWishlist { get; set; }
    }

    public record PaginationModel(int Page, int PageCount, Func<int, string> UrlFor);

    public record FacetOption(string Value, string Label, int Count);

    public class CategoryFacet
    {
        public int CatId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
        public List<CategoryFacet> Children { get; set; } = new();
    }

    public class CatalogViewModel
    {
        public CatalogFilter Filter { get; set; } = new();
        public List<ProductCardModel> Products { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageCount { get; set; }
        public string Title { get; set; } = "Shop all gear";
        public string? Description { get; set; }
        public Category? CurrentCategory { get; set; }
        public List<CategoryFacet> Categories { get; set; } = new();
        public List<FacetOption> Brands { get; set; } = new();
        public List<string> Sizes { get; set; } = new();
        public List<string> Colors { get; set; } = new();
        public decimal PriceFloor { get; set; }
        public decimal PriceCeiling { get; set; }

        public static readonly IReadOnlyList<(string Value, string Label)> SortOptions = new[]
        {
            ("newest", "Newest"),
            ("popular", "Best selling"),
            ("rating", "Top rated"),
            ("price-asc", "Price: low to high"),
            ("price-desc", "Price: high to low"),
            ("discount", "Biggest discount")
        };
    }

    public class HomeViewModel
    {
        public List<Category> Categories { get; set; } = new();
        public List<ProductCardModel> NewArrivals { get; set; } = new();
        public List<ProductCardModel> BestSellers { get; set; } = new();
        public List<ProductCardModel> OnSale { get; set; } = new();
        public List<ProductCardModel> Featured { get; set; } = new();
        public List<Review> LatestReviews { get; set; } = new();
    }

    public class ProductDetailsViewModel
    {
        public Product Product { get; set; } = null!;
        public double? AverageRating { get; set; }
        public int ReviewCount { get; set; }
        public int[] RatingBreakdown { get; set; } = new int[5];
        public List<Review> Reviews { get; set; } = new();
        public List<ProductCardModel> Related { get; set; } = new();
        public List<ProductCardModel> RecentlyViewed { get; set; } = new();
        public List<Category> Breadcrumb { get; set; } = new();
        public bool InWishlist { get; set; }
        public bool CanReview { get; set; }
        public bool HasReviewed { get; set; }
        public string? ReviewBlockedReason { get; set; }
        public ReviewInput ReviewInput { get; set; } = new();
    }

    public class ReviewInput
    {
        public int ProId { get; set; }

        [System.ComponentModel.DataAnnotations.Range(1, 5, ErrorMessage = "Please choose a rating from 1 to 5 stars")]
        public int Rating { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Please add a title")]
        [System.ComponentModel.DataAnnotations.StringLength(100, MinimumLength = 3)]
        public string Title { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Please write a short review")]
        [System.ComponentModel.DataAnnotations.StringLength(2000, MinimumLength = 10)]
        public string Comment { get; set; } = string.Empty;
    }
}
