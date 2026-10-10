using HikingStore.Data;
using HikingStore.Enum;
using HikingStore.Models;
using HikingStore.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HikingStore.Services
{
    /// <summary>Server-side product queries for the storefront: filtering, sorting, paging and product card data.</summary>
    public class CatalogService
    {
        public const string NavigationCacheKey = "Catalog.NavigationTree";
        private readonly ApplicationDbContext _context;
        private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;

        public CatalogService(ApplicationDbContext context, Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        public IQueryable<Product> ActiveProducts() =>
            _context.Products.AsNoTracking().Where(p => p.IsActive);

        /// <summary>Category tree for the header and footer; cached briefly because it renders on every page.</summary>
        public async Task<List<Category>> GetNavigationTreeAsync() =>
            await Microsoft.Extensions.Caching.Memory.CacheExtensions.GetOrCreateAsync(_cache, NavigationCacheKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                return GetCategoryTreeAsync();
            }) ?? new List<Category>();

        public async Task<List<Category>> GetCategoryTreeAsync()
        {
            var categories = await _context.Categories.AsNoTracking()
                .OrderBy(c => c.DisplayOrder).ThenBy(c => c.CatName)
                .ToListAsync();
            var lookup = categories.ToLookup(c => c.ParentCatId);
            foreach (var category in categories)
            {
                category.Children = lookup[category.CatId].ToList();
            }
            return lookup[null].ToList();
        }

        public async Task<List<int>> GetCategoryAndDescendantIdsAsync(int categoryId)
        {
            var all = await _context.Categories.AsNoTracking().Select(c => new { c.CatId, c.ParentCatId }).ToListAsync();
            var result = new List<int> { categoryId };
            var queue = new Queue<int>(result);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var child in all.Where(c => c.ParentCatId == current && !result.Contains(c.CatId)))
                {
                    result.Add(child.CatId);
                    queue.Enqueue(child.CatId);
                }
            }
            return result;
        }

        public async Task<CatalogViewModel> SearchAsync(CatalogFilter filter, string? userId)
        {
            Normalize(filter);

            var model = new CatalogViewModel { Filter = filter };
            var baseQuery = ActiveProducts();

            if (!string.IsNullOrWhiteSpace(filter.Q))
            {
                var term = filter.Q.Trim();
                baseQuery = baseQuery.Where(p =>
                    p.ProName!.Contains(term) ||
                    p.Descreption!.Contains(term) ||
                    (p.Brand != null && p.Brand.Contains(term)) ||
                    (p.Material != null && p.Material.Contains(term)) ||
                    p.Category!.CatName!.Contains(term));
            }

            if (filter.Category.HasValue)
            {
                model.CurrentCategory = await _context.Categories.AsNoTracking().Include(c => c.Parent).FirstOrDefaultAsync(c => c.CatId == filter.Category.Value);
                if (model.CurrentCategory == null)
                {
                    filter.Category = null;
                }
                else
                {
                    var ids = await GetCategoryAndDescendantIdsAsync(filter.Category.Value);
                    baseQuery = baseQuery.Where(p => ids.Contains(p.CatId));
                }
            }

            // Brand facet counts ignore the brand filter itself so shoppers can widen their selection.
            var beforeBrand = ApplyNonBrandFilters(baseQuery, filter);
            model.Brands = (await beforeBrand
                    .Where(p => p.Brand != null && p.Brand != "")
                    .GroupBy(p => p.Brand!)
                    .Select(g => new { Brand = g.Key, Count = g.Count() })
                    .OrderBy(g => g.Brand)
                    .ToListAsync())
                .Select(g => new FacetOption(g.Brand, g.Brand, g.Count))
                .ToList();

            var query = beforeBrand;
            if (filter.Brand.Count > 0)
            {
                var brands = filter.Brand;
                query = query.Where(p => p.Brand != null && brands.Contains(p.Brand));
            }

            model.TotalCount = await query.CountAsync();
            model.PageCount = Math.Max(1, (int)Math.Ceiling(model.TotalCount / (double)CatalogFilter.DefaultPageSize));
            if (filter.Page > model.PageCount) filter.Page = model.PageCount;

            var products = await ApplySort(query, filter.Sort)
                .Skip((filter.Page - 1) * CatalogFilter.DefaultPageSize)
                .Take(CatalogFilter.DefaultPageSize)
                .ToListAsync();
            model.Products = await BuildCardsAsync(products, userId);

            var variantLists = await ActiveProducts().Select(p => new { p.Sizes, p.Colors }).ToListAsync();
            model.Sizes = SplitDistinct(variantLists.Select(v => v.Sizes));
            model.Colors = SplitDistinct(variantLists.Select(v => v.Colors));

            var priceStats = await ActiveProducts()
                .GroupBy(_ => 1)
                .Select(g => new { Min = g.Min(p => p.Price), Max = g.Max(p => p.Price) })
                .FirstOrDefaultAsync();
            model.PriceFloor = Math.Floor(priceStats?.Min ?? 0);
            model.PriceCeiling = Math.Ceiling(priceStats?.Max ?? 0);

            var counts = await ActiveProducts().GroupBy(p => p.CatId).Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Count);
            model.Categories = (await GetCategoryTreeAsync()).Select(c => ToFacet(c, counts)).ToList();

            return model;
        }

        private static CategoryFacet ToFacet(Category category, Dictionary<int, int> counts)
        {
            var children = category.Children.Select(c => ToFacet(c, counts)).ToList();
            return new CategoryFacet
            {
                CatId = category.CatId,
                Name = category.CatName ?? string.Empty,
                Count = counts.GetValueOrDefault(category.CatId) + children.Sum(c => c.Count),
                Children = children
            };
        }

        private static IQueryable<Product> ApplyNonBrandFilters(IQueryable<Product> query, CatalogFilter filter)
        {
            if (filter.MinPrice.HasValue)
            {
                var min = filter.MinPrice.Value;
                query = query.Where(p => p.Price >= min);
            }
            if (filter.MaxPrice.HasValue)
            {
                var max = filter.MaxPrice.Value;
                query = query.Where(p => p.Price <= max);
            }
            if (!string.IsNullOrEmpty(filter.Size))
            {
                var token = "," + filter.Size + ",";
                query = query.Where(p => p.Sizes != null && ("," + p.Sizes + ",").Contains(token));
            }
            if (!string.IsNullOrEmpty(filter.Color))
            {
                var token = "," + filter.Color + ",";
                query = query.Where(p => p.Colors != null && ("," + p.Colors + ",").Contains(token));
            }
            if (filter.InStock)
            {
                query = query.Where(p => p.StockQuantity > 0);
            }
            if (filter.OnSale)
            {
                query = query.Where(p => p.CompareAtPrice != null && p.CompareAtPrice > p.Price);
            }
            if (filter.Rating.HasValue)
            {
                double minRating = filter.Rating.Value;
                query = query.Where(p => p.Reviews.Where(r => r.IsApproved).Average(r => (double?)r.Rating) >= minRating);
            }
            return query;
        }

        private IQueryable<Product> ApplySort(IQueryable<Product> query, string sort) => sort switch
        {
            "price-asc" => query.OrderBy(p => p.Price).ThenBy(p => p.ProId),
            "price-desc" => query.OrderByDescending(p => p.Price).ThenBy(p => p.ProId),
            "rating" => query
                .OrderByDescending(p => p.Reviews.Where(r => r.IsApproved).Average(r => (double?)r.Rating) ?? 0)
                .ThenByDescending(p => p.Reviews.Count(r => r.IsApproved))
                .ThenByDescending(p => p.ProId),
            "popular" => query
                .OrderByDescending(p => _context.OrderDetails
                    .Where(d => d.ProId == p.ProId && d.Order!.Status != OrderStatus.Cancelled)
                    .Sum(d => (int?)d.Quantity) ?? 0)
                .ThenByDescending(p => p.ProId),
            "discount" => query
                .OrderByDescending(p => p.CompareAtPrice != null && p.CompareAtPrice > p.Price
                    ? (p.CompareAtPrice.Value - p.Price) / p.CompareAtPrice.Value
                    : 0)
                .ThenByDescending(p => p.ProId),
            _ => query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.ProId)
        };

        public async Task<List<ProductCardModel>> GetNewArrivalsAsync(int count, string? userId) =>
            await BuildCardsAsync(await ApplySort(ActiveProducts(), "newest").Take(count).ToListAsync(), userId);

        /// <summary>Best sellers by units sold in non-cancelled orders. Products with no sales are excluded.</summary>
        public async Task<List<ProductCardModel>> GetBestSellersAsync(int count, string? userId)
        {
            var topIds = await _context.OrderDetails.AsNoTracking()
                .Where(d => d.Order!.Status != OrderStatus.Cancelled && d.Product!.IsActive)
                .GroupBy(d => d.ProId)
                .Select(g => new { ProId = g.Key, Units = g.Sum(d => d.Quantity) })
                .OrderByDescending(g => g.Units)
                .Take(count)
                .Select(g => g.ProId)
                .ToListAsync();

            var products = await ActiveProducts().Where(p => topIds.Contains(p.ProId)).ToListAsync();
            return await BuildCardsAsync(products.OrderBy(p => topIds.IndexOf(p.ProId)).ToList(), userId);
        }

        public async Task<List<ProductCardModel>> GetOnSaleAsync(int count, string? userId) =>
            await BuildCardsAsync(await ApplySort(ActiveProducts().Where(p => p.CompareAtPrice != null && p.CompareAtPrice > p.Price), "discount")
                .Take(count).ToListAsync(), userId);

        public async Task<List<ProductCardModel>> GetFeaturedAsync(int count, string? userId) =>
            await BuildCardsAsync(await ActiveProducts().Where(p => p.IsFeatured && p.StockQuantity > 0)
                .OrderByDescending(p => p.CreatedAt).Take(count).ToListAsync(), userId);

        public async Task<List<ProductCardModel>> GetRelatedAsync(Product product, int count, string? userId)
        {
            var related = await ActiveProducts()
                .Where(p => p.ProId != product.ProId && (p.CatId == product.CatId || (product.Brand != null && p.Brand == product.Brand)))
                .OrderByDescending(p => p.CatId == product.CatId)
                .ThenByDescending(p => p.StockQuantity > 0)
                .ThenByDescending(p => p.CreatedAt)
                .Take(count)
                .ToListAsync();
            return await BuildCardsAsync(related, userId);
        }

        public async Task<List<ProductCardModel>> GetByIdsAsync(IReadOnlyList<int> ids, string? userId)
        {
            if (ids.Count == 0) return new List<ProductCardModel>();
            var products = await ActiveProducts().Where(p => ids.Contains(p.ProId)).ToListAsync();
            return await BuildCardsAsync(products.OrderBy(p => ids.ToList().IndexOf(p.ProId)).ToList(), userId);
        }

        public async Task<Dictionary<int, (double Average, int Count)>> GetRatingsAsync(IReadOnlyCollection<int> productIds)
        {
            if (productIds.Count == 0) return new Dictionary<int, (double, int)>();
            return await _context.Reviews.AsNoTracking()
                .Where(r => r.IsApproved && productIds.Contains(r.ProId))
                .GroupBy(r => r.ProId)
                .Select(g => new { ProId = g.Key, Average = g.Average(r => (double)r.Rating), Count = g.Count() })
                .ToDictionaryAsync(g => g.ProId, g => (g.Average, g.Count));
        }

        public async Task<List<ProductCardModel>> BuildCardsAsync(List<Product> products, string? userId)
        {
            var ids = products.Select(p => p.ProId).ToList();
            var ratings = await GetRatingsAsync(ids);
            var wishlisted = string.IsNullOrEmpty(userId)
                ? new HashSet<int>()
                : (await _context.Wishlists.Where(w => w.UserId == userId && ids.Contains(w.ProId)).Select(w => w.ProId).ToListAsync()).ToHashSet();

            return products.Select(p => new ProductCardModel
            {
                Product = p,
                AverageRating = ratings.TryGetValue(p.ProId, out var r) ? r.Average : null,
                ReviewCount = ratings.TryGetValue(p.ProId, out var rc) ? rc.Count : 0,
                InWishlist = wishlisted.Contains(p.ProId)
            }).ToList();
        }

        private static void Normalize(CatalogFilter filter)
        {
            filter.Q = string.IsNullOrWhiteSpace(filter.Q) ? null : filter.Q.Trim()[..Math.Min(filter.Q.Trim().Length, 100)];
            if (filter.Page < 1) filter.Page = 1;
            if (filter.MinPrice < 0) filter.MinPrice = null;
            if (filter.MaxPrice < 0) filter.MaxPrice = null;
            if (filter.MinPrice.HasValue && filter.MaxPrice.HasValue && filter.MinPrice > filter.MaxPrice)
            {
                (filter.MinPrice, filter.MaxPrice) = (filter.MaxPrice, filter.MinPrice);
            }
            if (filter.Rating is < 1 or > 5) filter.Rating = null;
            if (!CatalogViewModel.SortOptions.Any(o => o.Value == filter.Sort)) filter.Sort = "newest";
            filter.Brand = filter.Brand.Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b.Trim()).Distinct().Take(20).ToList();
            filter.Size = string.IsNullOrWhiteSpace(filter.Size) ? null : filter.Size.Trim();
            filter.Color = string.IsNullOrWhiteSpace(filter.Color) ? null : filter.Color.Trim();
        }

        private static List<string> SplitDistinct(IEnumerable<string?> values) =>
            values.Where(v => !string.IsNullOrWhiteSpace(v))
                .SelectMany(v => v!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v)
                .ToList();
    }
}
