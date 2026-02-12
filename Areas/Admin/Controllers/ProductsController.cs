using HikingStore.Data;
using HikingStore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;


namespace HikingStore.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class ProductsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ProductsController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var products = await _context.Products
                .Include(c => c.Category)
                .AsNoTracking()
                .ToListAsync();
            return View(products);
        }

        [HttpGet]
        public IActionResult Create()
        {
            ViewData["CategoryId"] = new SelectList(_context.Categories, "CatId", "CatName");
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(Product model, IFormFileCollection additionalFiles)
        {
            if (model.File != null && model.File.Length > 0)
            {
                var ext = Path.GetExtension(model.File.FileName);
                if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                string imageName = Guid.NewGuid().ToString() + ext;
                string filePathImage = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Product", imageName);
                using (var stream = System.IO.File.Create(filePathImage))
                {
                    await model.File.CopyToAsync(stream);
                }
                model.ProImage = imageName;
            }

            var additionalNames = new List<string>();
            if (additionalFiles != null)
            {
                foreach (var file in additionalFiles.Where(f => f != null && f.Length > 0))
                {
                    var ext = Path.GetExtension(file.FileName);
                    if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                    string name = Guid.NewGuid().ToString() + ext;
                    string path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Product", name);
                    using (var stream = System.IO.File.Create(path))
                        await file.CopyToAsync(stream);
                    additionalNames.Add(name);
                }
            }
            if (additionalNames.Count > 0)
                model.AdditionalImages = string.Join(",", additionalNames);

            if (model.StockQuantity < 0)
                model.StockQuantity = 0;

            _context.Add(model);
            await _context.SaveChangesAsync();
            ViewData["CategoryId"] = new SelectList(_context.Categories, "CatId", "CatName");
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int? id)
        {
            var product = await _context.Products.FindAsync(id);
            ViewData["CategoryId"] = new SelectList(_context.Categories, "CatId", "CatName");
            return View(product);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(int id, Product model, IFormFileCollection additionalFiles)
        {
            var existingProduct = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.ProId == id);
            if (existingProduct == null)
                return NotFound();

            if (model.File != null && model.File.Length > 0)
            {
                var ext = Path.GetExtension(model.File.FileName);
                if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                string imageName = Guid.NewGuid().ToString() + ext;
                string filePathImage = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Product", imageName);
                using (var stream = System.IO.File.Create(filePathImage))
                    await model.File.CopyToAsync(stream);
                model.ProImage = imageName;
            }
            else
                model.ProImage = existingProduct.ProImage;

            var existingAdditional = string.IsNullOrWhiteSpace(model.AdditionalImages)
                ? new List<string>()
                : model.AdditionalImages.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

            if (additionalFiles != null)
            {
                foreach (var file in additionalFiles.Where(f => f != null && f.Length > 0))
                {
                    var ext = Path.GetExtension(file.FileName);
                    if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                    string name = Guid.NewGuid().ToString() + ext;
                    string path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Product", name);
                    using (var stream = System.IO.File.Create(path))
                        await file.CopyToAsync(stream);
                    existingAdditional.Add(name);
                }
            }
            model.AdditionalImages = existingAdditional.Count > 0 ? string.Join(",", existingAdditional) : null;

            if (model.StockQuantity < 0)
                model.StockQuantity = 0;

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