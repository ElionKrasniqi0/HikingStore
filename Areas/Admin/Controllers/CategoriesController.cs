using HikingStore.Data;
using HikingStore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HikingStore.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class CategoriesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CategoriesController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var categories = await _context.Categories
                .Include(c => c.Product)
                .AsNoTracking()
                .ToListAsync();
            return View(categories);
        }
        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(Category model)
        {
            if (model.File != null && model.File.Length > 0)
            {
                string imageName = Guid.NewGuid().ToString() + ".jpg";
                string filePathImage = Path.Combine(Directory.GetCurrentDirectory(), @"wwwroot\category", imageName);
                using (var stream = System.IO.File.Create(filePathImage))
                {
                    await model.File.CopyToAsync(stream);
                }
                model.CatPhoto = imageName;
            }
            _context.Add(model);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int? id)
        {
            var categories = await _context.Categories.FindAsync(id);
            return View(categories);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(int id, Category model)
        {
            if (model.File != null && model.File.Length > 0)
            {
                string imageName = Guid.NewGuid().ToString() + ".jpg";
                string filePathImage = Path.Combine(Directory.GetCurrentDirectory(), @"wwwroot\category", imageName);
                using (var stream = System.IO.File.Create(filePathImage))
                {
                    await model.File.CopyToAsync(stream);
                }
                model.CatPhoto = imageName;
            }
            else
            {
                // Preserve existing image
                var existingCategory = await _context.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.CatId == id);
                if (existingCategory != null)
                {
                    model.CatPhoto = existingCategory.CatPhoto;
                }
            }
            _context.Update(model);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                TempData["Error"] = "Category ID is required.";
                return RedirectToAction(nameof(Index));
            }

            var cat = await _context.Categories
                .Include(c => c.Product)
                .FirstOrDefaultAsync(c => c.CatId == id);

            if (cat == null)
            {
                TempData["Error"] = "Category not found.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                // Delete the category image file if it exists
                if (!string.IsNullOrEmpty(cat.CatPhoto))
                {
                    string imagePath = Path.Combine(Directory.GetCurrentDirectory(), @"wwwroot\category", cat.CatPhoto);
                    if (System.IO.File.Exists(imagePath))
                    {
                        System.IO.File.Delete(imagePath);
                    }
                }

                // Delete associated product images (products will be cascade deleted by database)
                if (cat.Product != null && cat.Product.Any())
                {
                    foreach (var product in cat.Product)
                    {
                        if (!string.IsNullOrEmpty(product.ProImage))
                        {
                            string productImagePath = Path.Combine(Directory.GetCurrentDirectory(), @"wwwroot\Product", product.ProImage);
                            if (System.IO.File.Exists(productImagePath))
                            {
                                System.IO.File.Delete(productImagePath);
                            }
                        }
                    }
                }

                // Remove the category (products will be cascade deleted)
                string categoryName = cat.CatName;
                _context.Categories.Remove(cat);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"Category '{categoryName}' has been deleted successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["Error"] = "An error occurred while deleting the category. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }
    }
}
