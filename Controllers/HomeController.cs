using HikingStore.Data;
using HikingStore.Enum;
using HikingStore.Models;
using HikingStore.Services;
using HikingStore.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HikingStore.Controllers
{
    public class HomeController : Controller
    {
        private const string RecentlyViewedKey = "RecentlyViewed";

        private readonly ApplicationDbContext _context;
        private readonly CatalogService _catalog;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<HomeController> _logger;

        public HomeController(
            ApplicationDbContext context,
            CatalogService catalog,
            UserManager<ApplicationUser> userManager,
            ILogger<HomeController> logger)
        {
            _context = context;
            _catalog = catalog;
            _userManager = userManager;
            _logger = logger;
        }

        private string? CurrentUserId => _userManager.GetUserId(User);

        public async Task<IActionResult> Index()
        {
            var userId = CurrentUserId;
            var model = new HomeViewModel
            {
                Categories = (await _catalog.GetCategoryTreeAsync()).ToList(),
                NewArrivals = await _catalog.GetNewArrivalsAsync(8, userId),
                BestSellers = await _catalog.GetBestSellersAsync(8, userId),
                OnSale = await _catalog.GetOnSaleAsync(8, userId),
                Featured = await _catalog.GetFeaturedAsync(4, userId),
                LatestReviews = await _context.Reviews.AsNoTracking()
                    .Include(r => r.Product)
                    .Include(r => r.User)
                    .Where(r => r.IsApproved && r.Rating >= 4 && r.Product!.IsActive)
                    .OrderByDescending(r => r.CreatedAt)
                    .Take(3)
                    .ToListAsync()
            };
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Shop([FromQuery] CatalogFilter filter)
        {
            var model = await _catalog.SearchAsync(filter, CurrentUserId);

            if (model.CurrentCategory != null)
            {
                model.Title = model.CurrentCategory.CatName ?? "Category";
                model.Description = model.CurrentCategory.Description;
            }
            else if (!string.IsNullOrEmpty(filter.Q))
            {
                model.Title = $"Results for “{filter.Q}”";
            }
            else if (filter.OnSale)
            {
                model.Title = "Sale";
                model.Description = "Discounted outdoor gear while stock lasts.";
            }
            else if (filter.Sort == "popular")
            {
                model.Title = "Best sellers";
            }
            else
            {
                model.Title = "Shop all gear";
            }

            return View("Shop", model);
        }

        // Legacy URLs kept so existing links and bookmarks still work.
        public IActionResult Product(int page = 1) => RedirectToActionPermanent(nameof(Shop), page > 1 ? new { page } : null);

        public IActionResult ProductCategory(int id) => RedirectToActionPermanent(nameof(Shop), new { category = id });

        [HttpGet]
        public IActionResult Search(string? query) =>
            string.IsNullOrWhiteSpace(query) ? RedirectToAction(nameof(Shop)) : RedirectToAction(nameof(Shop), new { q = query });

        [HttpGet]
        public async Task<IActionResult> QuickSearch(string? term)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2)
            {
                return Json(new { results = Array.Empty<object>() });
            }

            term = term.Trim();
            var products = await _catalog.ActiveProducts()
                .Where(p => p.ProName!.Contains(term) || (p.Brand != null && p.Brand.Contains(term)))
                .OrderByDescending(p => p.StockQuantity > 0)
                .Take(6)
                .Select(p => new
                {
                    text = p.ProName,
                    type = "Product",
                    price = p.Price,
                    image = p.ProImage != null ? "/Product/" + p.ProImage : null,
                    url = Url.Action(nameof(ProductDetailes), new { id = p.ProId })
                })
                .ToListAsync();

            var categories = await _context.Categories.AsNoTracking()
                .Where(c => c.CatName!.Contains(term))
                .Take(4)
                .Select(c => new
                {
                    text = c.CatName,
                    type = "Category",
                    price = (decimal?)null,
                    image = (string?)null,
                    url = Url.Action(nameof(Shop), new { category = c.CatId })
                })
                .ToListAsync();

            return Json(new { results = products.Cast<object>().Concat(categories) });
        }

        public async Task<IActionResult> ProductDetailes(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var product = await _context.Products.AsNoTracking()
                .Include(p => p.Category)
                .FirstOrDefaultAsync(p => p.ProId == id && p.IsActive);
            if (product == null)
            {
                return NotFound();
            }

            var userId = CurrentUserId;
            var reviews = await _context.Reviews.AsNoTracking()
                .Include(r => r.User)
                .Where(r => r.ProId == product.ProId && r.IsApproved)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            var model = new ProductDetailsViewModel
            {
                Product = product,
                Reviews = reviews,
                ReviewCount = reviews.Count,
                AverageRating = reviews.Count > 0 ? reviews.Average(r => r.Rating) : null,
                Related = await _catalog.GetRelatedAsync(product, 8, userId),
                ReviewInput = new ReviewInput { ProId = product.ProId }
            };
            foreach (var review in reviews)
            {
                model.RatingBreakdown[review.Rating - 1]++;
            }

            var breadcrumb = new List<Category>();
            var category = product.Category;
            var guard = 0;
            while (category != null && guard++ < 5)
            {
                breadcrumb.Insert(0, category);
                category = category.ParentCatId.HasValue
                    ? await _context.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.CatId == category.ParentCatId)
                    : null;
            }
            model.Breadcrumb = breadcrumb;

            if (userId != null)
            {
                model.InWishlist = await _context.Wishlists.AnyAsync(w => w.UserId == userId && w.ProId == product.ProId);
                model.HasReviewed = await _context.Reviews.AnyAsync(r => r.UserId == userId && r.ProId == product.ProId);
                var purchased = await _context.OrderDetails.AnyAsync(d => d.ProId == product.ProId
                    && d.Order!.UserId == userId && d.Order.Status != OrderStatus.Cancelled
                    && (d.Order.PaymentMethod != HikingStore.Models.PaymentMethods.Card || d.Order.PaymentStatus == HikingStore.Models.PaymentStatuses.Paid));
                model.CanReview = purchased && !model.HasReviewed;
                if (!purchased) model.ReviewBlockedReason = "Only customers who bought this product can review it.";
                else if (model.HasReviewed) model.ReviewBlockedReason = "Thanks — you have already reviewed this product.";
            }
            else
            {
                model.ReviewBlockedReason = "Sign in and purchase this product to leave a review.";
            }

            var recent = GetRecentlyViewed();
            model.RecentlyViewed = await _catalog.GetByIdsAsync(recent.Where(i => i != product.ProId).Take(6).ToList(), userId);
            recent.Remove(product.ProId);
            recent.Insert(0, product.ProId);
            HttpContext.Session.SetString(RecentlyViewedKey, JsonSerializer.Serialize(recent.Take(12)));

            return View(model);
        }

        private List<int> GetRecentlyViewed()
        {
            var json = HttpContext.Session.GetString(RecentlyViewedKey);
            if (string.IsNullOrEmpty(json)) return new List<int>();
            try
            {
                return JsonSerializer.Deserialize<List<int>>(json) ?? new List<int>();
            }
            catch (JsonException)
            {
                return new List<int>();
            }
        }

        [HttpGet]
        public IActionResult Contact(string? subject) => View(new Contact
        {
            Subject = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim()[..Math.Min(subject.Trim().Length, 150)]
        });

        [HttpPost]
        public async Task<IActionResult> Contact([Bind("Name,Email,Subject,Message")] Contact model)
        {
            if (string.IsNullOrWhiteSpace(model.Message))
                ModelState.AddModelError(nameof(model.Message), "Please write your message.");
            else if (model.Message.Length > 2000)
                ModelState.AddModelError(nameof(model.Message), "Please keep your message under 2,000 characters.");
            if (model.Name?.Length > 100)
                ModelState.AddModelError(nameof(model.Name), "Please use at most 100 characters.");
            if (model.Email?.Length > 256)
                ModelState.AddModelError(nameof(model.Email), "Please use at most 256 characters.");
            if (model.Subject?.Length > 150)
                ModelState.AddModelError(nameof(model.Subject), "Please use at most 150 characters.");
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            model.Name = model.Name!.Trim();
            model.Email = model.Email!.Trim();
            model.Subject = string.IsNullOrWhiteSpace(model.Subject) ? "General question" : model.Subject.Trim();
            model.Message = model.Message!.Trim();
            model.CreatedDate = DateTime.UtcNow;
            _context.Contacts.Add(model);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Thanks for your message. Our team will reply by email as soon as possible.";
            return RedirectToAction(nameof(Contact));
        }

        [HttpPost]
        public async Task<IActionResult> Subscribe(string? email, string? returnUrl)
        {
            var target = Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Action(nameof(Index))!;
            email = email?.Trim();
            if (string.IsNullOrEmpty(email) || email.Length > 256 || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            {
                TempData["Error"] = "Please enter a valid email address.";
                return LocalRedirect(target);
            }

            var normalized = email.ToLowerInvariant();
            if (!await _context.NewsletterSubscribers.AnyAsync(s => s.Email == normalized))
            {
                _context.NewsletterSubscribers.Add(new NewsletterSubscriber { Email = normalized });
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException ex)
                {
                    _logger.LogInformation(ex, "Newsletter subscription race for an existing address");
                }
            }

            TempData["Success"] = "Thanks — you're on the HikingStore newsletter list.";
            return LocalRedirect(target);
        }

        public IActionResult Privacy() => View();
        public IActionResult Terms() => View();
        public IActionResult About() => View();
        public IActionResult Services() => View();
        public IActionResult Shipping() => View();
        public IActionResult Returns() => View();
        public IActionResult Faq() => View();
    }
}
