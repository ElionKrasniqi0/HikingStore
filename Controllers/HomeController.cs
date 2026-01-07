using HikingStore.Data;
using HikingStore.Models;
using HikingStore.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace HikingStore.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _context;

        public HomeController(ILogger<HomeController> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<List<Product>> GetPage(IQueryable<Product> result, int pagenumber)
        {
            const int Pagesize = 100;
            decimal rowCount = await result.CountAsync();
            var pagecount = Math.Ceiling(rowCount / Pagesize);
            if (pagenumber > pagecount)
            {
                pagenumber = 1;
            }
            pagenumber = pagenumber <= 0 ? 1 : pagenumber;
            int skipCount = (pagenumber - 1) * Pagesize;
            var pageData = await result
                .AsNoTracking()
                .Skip(skipCount)
                .Take(Pagesize)
                .ToListAsync();

            ViewBag.CurrentPage = pagenumber;
            ViewBag.PageCount = pagecount;


            return pageData;
        }

        public async Task<IActionResult> Index()
        {
            var model = new IndexVM
            {
                Categories = await _context.Categories.AsNoTracking().ToListAsync(),
                Products = await _context.Products
                    .Include(p => p.Category)
                    .AsNoTracking()
                    .OrderByDescending(p => p.ProId)
                    .Take(8)
                    .ToListAsync()
            };
            return View(model);
        }
        public async Task<IActionResult> Shop()
        {
            var products = await _context.Products
                .AsNoTracking()
                .ToListAsync();
            var categories = await _context.Categories
                .AsNoTracking()
                .ToListAsync();

            var model = new ShopViewModel
            {
                Products = products,
                Categories = categories
            };

            return View(model);
        }


        public async Task<IActionResult> Product(int page = 1)
        {
            var products = _context.Products;
            var model = await GetPage(products, page);
            return View(model);
        }
        public async Task<IActionResult> ProductCategory(int id)
        {
            var products = await _context.Products
                .Where(c => c.CatId == id)
                .AsNoTracking()
                .ToListAsync();
            return View(products);
        }
        [HttpGet]
        public async Task<IActionResult> QuickSearch(string term)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Length < 2)
            {
                return Json(new { results = new List<object>() });
            }

            var products = await _context.Products
                .Where(p => p.ProName.Contains(term))
                .AsNoTracking()
                .Take(5)
                .Select(p => new
                {
                    id = p.ProId,
                    text = p.ProName,
                    type = "Product",
                    url = $"/Home/ProductDetailes/{p.ProId}"
                })
                .ToListAsync();

            var categories = await _context.Categories
                .Where(c => c.CatName.Contains(term))
                .AsNoTracking()
                .Take(5)
                .Select(c => new
                {
                    id = c.CatId,
                    text = c.CatName,
                    type = "Category",
                    url = $"/Home/ProductCategory/{c.CatId}"
                })
                .ToListAsync();

            var results = products.Concat(categories).ToList();

            return Json(new { results });
        }
        // Add this method to your HomeController class
        [HttpGet]
        public async Task<IActionResult> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return RedirectToAction("Product");
            }

            // Search in both products and categories
            var products = await _context.Products
                .Include(p => p.Category)
                .AsNoTracking()
                .Where(p => p.ProName.Contains(query) ||
                           p.Descreption.Contains(query) ||
                           p.Category.CatName.Contains(query))
                .ToListAsync();

            var categories = await _context.Categories
                .AsNoTracking()
                .Where(c => c.CatName.Contains(query))
                .ToListAsync();

            var model = new SearchResultViewModel
            {
                Query = query,
                Products = products,
                Categories = categories,
                ProductCount = products.Count,
                CategoryCount = categories.Count
            };

            return View("SearchResults", model);
        }

        public async Task<IActionResult> ProductDetailes(int? id)
        {
            var product = await _context.Products
                .Include(x => x.Category)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProId == id);
            return View(product);
        }

        public IActionResult Contact()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> Contact(Contact model)

        {
            if (ModelState.IsValid)
            {
                _context.Add(model);
                await _context.SaveChangesAsync();
                return RedirectToAction("Index");
            }
            return View(model);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        public IActionResult Terms()
        {
            return View();
        }

        public IActionResult About()
        {
            return View();
        }

        public IActionResult Services()
        {
            return View();
        }
    }
}
