using HikingStore.Data;
using HikingStore.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;


namespace HikingStore.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class ProductsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ProductsController(ApplicationDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            var products = _context.Products.Include(c => c.Category).ToList();
            return View(products);
        }

        [HttpGet]
        public IActionResult Create()
        {
            ViewData["CategoryId"] = new SelectList(_context.Categories, "CatId", "CatName");
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(Product model)
        {
            if (model.File != null && model.File.Length > 0)
            {
                string imageName = Guid.NewGuid().ToString() + ".jpg";
                string filePathImage = Path.Combine(Directory.GetCurrentDirectory(), @"wwwroot\Product", imageName);
                using (var stream = System.IO.File.Create(filePathImage))
                {
                    await model.File.CopyToAsync(stream);
                }
                model.ProImage = imageName;
            }

            // Set default stock if not provided
            if (model.StockQuantity < 0)
            {
                model.StockQuantity = 0;
            }

            _context.Add(model);
            await _context.SaveChangesAsync();
            ViewData["CategoryId"] = new SelectList(_context.Categories, "CatId", "CatName");
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public IActionResult Edit(int? id)
        {
            var product = _context.Products.Find(id);
            ViewData["CategoryId"] = new SelectList(_context.Categories, "CatId", "CatName");
            return View(product);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(int id, Product model)
        {
            if (model.File != null && model.File.Length > 0)
            {
                string imageName = Guid.NewGuid().ToString() + ".jpg";
                string filePathImage = Path.Combine(Directory.GetCurrentDirectory(), @"wwwroot\Product", imageName);
                using (var stream = System.IO.File.Create(filePathImage))
                {
                    await model.File.CopyToAsync(stream);
                }
                model.ProImage = imageName;
            }
            else
            {
                // Preserve existing image
                var existingProduct = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.ProId == id);
                if (existingProduct != null)
                {
                    model.ProImage = existingProduct.ProImage;
                }
            }

            // Ensure stock is not negative
            if (model.StockQuantity < 0)
            {
                model.StockQuantity = 0;
            }

            _context.Update(model);
            await _context.SaveChangesAsync();
            ViewData["CategoryId"] = new SelectList(_context.Categories, "CatId", "CatName");
            return RedirectToAction(nameof(Index));
        }

        // New action for updating stock
        [HttpPost]
        public async Task<IActionResult> UpdateStock(int id, int stockQuantity)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null)
            {
                return Json(new { success = false, message = "Product not found." });
            }

            product.StockQuantity = stockQuantity;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Stock updated successfully." });
        }

        public IActionResult Delete(int? id)
        {
            if (id != null)
            {
                var product = _context.Products.Find(id);
                _context.Products.Remove(product);
                _context.SaveChanges();
                return RedirectToAction(nameof(Index));
            }
            return RedirectToAction(nameof(Index));
        }
    }
}