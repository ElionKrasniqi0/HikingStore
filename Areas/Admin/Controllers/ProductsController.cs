using HikingStore.Data;
using HikingStore.Models;
using HikingStore.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HikingStore.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = DbInitializer.AdminRole)]
    public class ProductsController : Controller
    {
        private const int PageSize = 25;
        private readonly ApplicationDbContext _context;
        private readonly ImageUploadService _images;
        private readonly AdminAuditService _audit;

        public ProductsController(ApplicationDbContext context, ImageUploadService images, AdminAuditService audit)
        {
            _context = context;
            _images = images;
            _audit = audit;
        }

        public async Task<IActionResult> Index(string? search, int? category, string? stock, string? status, int page = 1)
        {
            var query = _context.Products.AsNoTracking().Include(p => p.Category).AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(p => p.ProName!.Contains(term) || (p.Brand != null && p.Brand.Contains(term)) || p.ProId.ToString() == term);
            }
            if (category.HasValue)
            {
                query = query.Where(p => p.CatId == category.Value);
            }
            query = stock switch
            {
                "out" => query.Where(p => p.StockQuantity <= 0),
                "low" => query.Where(p => p.StockQuantity > 0 && p.StockQuantity <= 5),
                _ => query
            };
            query = status switch
            {
                "active" => query.Where(p => p.IsActive),
                "hidden" => query.Where(p => !p.IsActive),
                "sale" => query.Where(p => p.CompareAtPrice != null && p.CompareAtPrice > p.Price),
                _ => query
            };

            var total = await query.CountAsync();
            var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
            page = Math.Clamp(page, 1, pageCount);

            ViewBag.Search = search;
            ViewBag.Category = category;
            ViewBag.Stock = stock;
            ViewBag.Status = status;
            ViewBag.Page = page;
            ViewBag.PageCount = pageCount;
            ViewBag.Total = total;
            ViewBag.Categories = new SelectList(await _context.Categories.AsNoTracking().OrderBy(c => c.CatName).ToListAsync(), "CatId", "CatName", category);

            var products = await query.OrderByDescending(p => p.ProId).Skip((page - 1) * PageSize).Take(PageSize).ToListAsync();
            return View(products);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await PopulateCategoriesAsync(null);
            return View("Form", new Product { IsActive = true });
        }

        [HttpPost]
        public async Task<IActionResult> Create(Product model)
        {
            Validate(model);
            if (model.File == null)
            {
                ModelState.AddModelError(nameof(Product.File), "Please upload a main product image.");
            }
            if (!ModelState.IsValid)
            {
                await PopulateCategoriesAsync(model.CatId);
                return View("Form", model);
            }

            var main = await _images.SaveAsync(model.File, ImageUploadService.ProductFolder);
            if (!main.Success)
            {
                ModelState.AddModelError(nameof(Product.File), main.Error!);
                await PopulateCategoriesAsync(model.CatId);
                return View("Form", model);
            }

            var product = new Product();
            CopyEditableFields(model, product);
            product.ProImage = main.FileName;
            product.CreatedAt = DateTime.UtcNow;

            var galleryErrors = await AddGalleryImagesAsync(product, model.GalleryFiles);
            _context.Products.Add(product);
            await _context.SaveChangesAsync();
            await _audit.LogAdminModificationAsync("Product", product.ProId.ToString(), "CREATE");

            TempData[galleryErrors.Count > 0 ? "Error" : "Success"] = galleryErrors.Count > 0
                ? $"Product created, but some gallery images were skipped: {string.Join(" ", galleryErrors)}"
                : $"“{product.ProName}” was created.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var product = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.ProId == id);
            if (product == null) return NotFound();

            await PopulateCategoriesAsync(product.CatId);
            return View("Form", product);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(int id, Product model, List<string>? removeImages)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.ProId == id);
            if (product == null) return NotFound();

            Validate(model);
            if (!ModelState.IsValid)
            {
                model.ProId = id;
                model.ProImage = product.ProImage;
                model.AdditionalImages = product.AdditionalImages;
                await PopulateCategoriesAsync(model.CatId);
                return View("Form", model);
            }

            if (model.File != null)
            {
                var main = await _images.SaveAsync(model.File, ImageUploadService.ProductFolder);
                if (!main.Success)
                {
                    ModelState.AddModelError(nameof(Product.File), main.Error!);
                    model.ProId = id;
                    model.ProImage = product.ProImage;
                    model.AdditionalImages = product.AdditionalImages;
                    await PopulateCategoriesAsync(model.CatId);
                    return View("Form", model);
                }
                _images.Delete(product.ProImage, ImageUploadService.ProductFolder);
                product.ProImage = main.FileName;
            }

            if (removeImages?.Count > 0)
            {
                var kept = product.AdditionalImageList.Where(i => !removeImages.Contains(i)).ToList();
                foreach (var removed in product.AdditionalImageList.Except(kept))
                {
                    _images.Delete(removed, ImageUploadService.ProductFolder);
                }
                product.AdditionalImages = kept.Count > 0 ? string.Join(",", kept) : null;
            }

            var oldPrice = product.Price;
            CopyEditableFields(model, product);
            var galleryErrors = await AddGalleryImagesAsync(product, model.GalleryFiles);
            await _context.SaveChangesAsync();
            await _audit.LogAdminModificationAsync("Product", product.ProId.ToString(), "UPDATE",
                oldPrice != product.Price ? oldPrice : null, oldPrice != product.Price ? product.Price : null);

            TempData[galleryErrors.Count > 0 ? "Error" : "Success"] = galleryErrors.Count > 0
                ? $"Product saved, but some gallery images were skipped: {string.Join(" ", galleryErrors)}"
                : $"“{product.ProName}” was saved.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStock(int id, int stockQuantity)
        {
            if (stockQuantity < 0 || stockQuantity > 1_000_000)
            {
                return Json(new { success = false, message = "Stock must be between 0 and 1,000,000." });
            }

            var updated = await _context.Products.Where(p => p.ProId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, stockQuantity));
            if (updated == 0)
            {
                return Json(new { success = false, message = "Product not found." });
            }

            await _audit.LogAdminModificationAsync("Product", id.ToString(), "STOCK", null, stockQuantity);
            return Json(new { success = true, message = "Stock updated." });
        }

        [HttpPost]
        public async Task<IActionResult> ToggleActive(int id)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.ProId == id);
            if (product == null) return NotFound();

            product.IsActive = !product.IsActive;
            await _context.SaveChangesAsync();
            TempData["Success"] = product.IsActive ? $"“{product.ProName}” is visible in the store." : $"“{product.ProName}” is hidden from the store.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.ProId == id);
            if (product == null) return NotFound();

            if (await _context.OrderDetails.AnyAsync(d => d.ProId == id))
            {
                product.IsActive = false;
                await _context.SaveChangesAsync();
                await _context.ShoppingCarts.Where(c => c.ProId == id).ExecuteDeleteAsync();
                TempData["Success"] = $"“{product.ProName}” appears in past orders, so it was hidden from the store instead of deleted.";
                return RedirectToAction(nameof(Index));
            }

            _context.Products.Remove(product);
            await _context.SaveChangesAsync();
            foreach (var image in product.AllImagePaths)
            {
                _images.Delete(image, ImageUploadService.ProductFolder);
            }
            await _audit.LogAdminDeletionAsync("Product", id.ToString(), product.ProName);
            TempData["Success"] = $"“{product.ProName}” was deleted.";
            return RedirectToAction(nameof(Index));
        }

        private void Validate(Product model)
        {
            ModelState.Remove(nameof(Product.Category));
            ModelState.Remove(nameof(Product.Reviews));
            if (model.CompareAtPrice.HasValue && model.CompareAtPrice.Value > 0 && model.CompareAtPrice.Value <= model.Price)
            {
                ModelState.AddModelError(nameof(Product.CompareAtPrice), "The original price must be higher than the selling price, or left empty.");
            }
            if (model.CatId > 0 && !_context.Categories.Any(c => c.CatId == model.CatId))
            {
                ModelState.AddModelError(nameof(Product.CatId), "Please choose a category.");
            }
        }

        private static void CopyEditableFields(Product source, Product target)
        {
            target.ProName = source.ProName?.Trim();
            target.Descreption = source.Descreption?.Trim();
            target.Price = source.Price;
            target.CompareAtPrice = source.CompareAtPrice is > 0 ? source.CompareAtPrice : null;
            target.Brand = Clean(source.Brand);
            target.Material = Clean(source.Material);
            target.Specifications = Clean(source.Specifications?.Replace("\r", string.Empty));
            target.Sizes = NormalizeList(source.Sizes);
            target.Colors = NormalizeList(source.Colors);
            target.StockQuantity = Math.Max(0, source.StockQuantity);
            target.CatId = source.CatId;
            target.IsActive = source.IsActive;
            target.IsFeatured = source.IsFeatured;
        }

        private async Task<List<string>> AddGalleryImagesAsync(Product product, List<IFormFile>? files)
        {
            var errors = new List<string>();
            if (files == null) return errors;

            var images = product.AdditionalImageList.ToList();
            foreach (var file in files.Where(f => f.Length > 0).Take(8))
            {
                var result = await _images.SaveAsync(file, ImageUploadService.ProductFolder);
                if (result.Success) images.Add(result.FileName!);
                else errors.Add($"{Path.GetFileName(file.FileName)}: {result.Error}");
            }
            product.AdditionalImages = images.Count > 0 ? string.Join(",", images) : null;
            return errors;
        }

        private async Task PopulateCategoriesAsync(int? selected)
        {
            var categories = await _context.Categories.AsNoTracking().Include(c => c.Parent).ToListAsync();
            ViewData["CategoryId"] = new SelectList(
                categories.Select(c => new { c.CatId, Name = c.Parent != null ? $"{c.Parent.CatName} › {c.CatName}" : c.CatName })
                    .OrderBy(c => c.Name),
                "CatId", "Name", selected);
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string? NormalizeList(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var items = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(v => v.Length <= 50)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return items.Count > 0 ? string.Join(",", items) : null;
        }
    }
}
