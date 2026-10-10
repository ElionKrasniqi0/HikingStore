using HikingStore.Data;
using HikingStore.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HikingStore.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = DbInitializer.AdminRole)]
    public class CategoriesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ImageUploadService _images;
        private readonly AdminAuditService _audit;
        private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;

        public CategoriesController(ApplicationDbContext context, ImageUploadService images, AdminAuditService audit,
            Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
        {
            _context = context;
            _images = images;
            _audit = audit;
            _cache = cache;
        }

        public override void OnActionExecuted(Microsoft.AspNetCore.Mvc.Filters.ActionExecutedContext context)
        {
            if (HttpMethods.IsPost(Request.Method))
            {
                _cache.Remove(CatalogService.NavigationCacheKey);
            }
            base.OnActionExecuted(context);
        }

        public async Task<IActionResult> Index()
        {
            var categories = await _context.Categories.AsNoTracking()
                .Include(c => c.Parent)
                .OrderBy(c => c.ParentCatId.HasValue).ThenBy(c => c.DisplayOrder).ThenBy(c => c.CatName)
                .ToListAsync();
            ViewBag.ProductCounts = await _context.Products.GroupBy(p => p.CatId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Count);
            return View(categories);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await PopulateParentsAsync(null, null);
            return View("Form", new Category());
        }

        [HttpPost]
        public async Task<IActionResult> Create(Category model)
        {
            await ValidateAsync(model, null);
            if (!ModelState.IsValid)
            {
                await PopulateParentsAsync(model.ParentCatId, null);
                return View("Form", model);
            }

            var category = new Category();
            Copy(model, category);
            if (model.File != null)
            {
                var upload = await _images.SaveAsync(model.File, ImageUploadService.CategoryFolder);
                if (!upload.Success)
                {
                    ModelState.AddModelError(nameof(Category.File), upload.Error!);
                    await PopulateParentsAsync(model.ParentCatId, null);
                    return View("Form", model);
                }
                category.CatPhoto = upload.FileName;
            }

            _context.Categories.Add(category);
            await _context.SaveChangesAsync();
            await _audit.LogAdminModificationAsync("Category", category.CatId.ToString(), "CREATE");
            TempData["Success"] = $"Category “{category.CatName}” created.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var category = await _context.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.CatId == id);
            if (category == null) return NotFound();

            await PopulateParentsAsync(category.ParentCatId, id);
            return View("Form", category);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(int id, Category model)
        {
            var category = await _context.Categories.FirstOrDefaultAsync(c => c.CatId == id);
            if (category == null) return NotFound();

            await ValidateAsync(model, id);
            if (!ModelState.IsValid)
            {
                model.CatId = id;
                model.CatPhoto = category.CatPhoto;
                await PopulateParentsAsync(model.ParentCatId, id);
                return View("Form", model);
            }

            if (model.File != null)
            {
                var upload = await _images.SaveAsync(model.File, ImageUploadService.CategoryFolder);
                if (!upload.Success)
                {
                    ModelState.AddModelError(nameof(Category.File), upload.Error!);
                    model.CatId = id;
                    model.CatPhoto = category.CatPhoto;
                    await PopulateParentsAsync(model.ParentCatId, id);
                    return View("Form", model);
                }
                _images.Delete(category.CatPhoto, ImageUploadService.CategoryFolder);
                category.CatPhoto = upload.FileName;
            }

            Copy(model, category);
            await _context.SaveChangesAsync();
            await _audit.LogAdminModificationAsync("Category", id.ToString(), "UPDATE");
            TempData["Success"] = $"Category “{category.CatName}” saved.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var category = await _context.Categories.FirstOrDefaultAsync(c => c.CatId == id);
            if (category == null)
            {
                TempData["Error"] = "Category not found.";
                return RedirectToAction(nameof(Index));
            }

            if (await _context.Categories.AnyAsync(c => c.ParentCatId == id))
            {
                TempData["Error"] = $"“{category.CatName}” has subcategories. Move or delete them first.";
                return RedirectToAction(nameof(Index));
            }
            var productCount = await _context.Products.CountAsync(p => p.CatId == id);
            if (productCount > 0)
            {
                TempData["Error"] = $"“{category.CatName}” still contains {productCount} product(s). Move them to another category first.";
                return RedirectToAction(nameof(Index));
            }

            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();
            _images.Delete(category.CatPhoto, ImageUploadService.CategoryFolder);
            await _audit.LogAdminDeletionAsync("Category", id.ToString(), category.CatName);
            TempData["Success"] = $"Category “{category.CatName}” deleted.";
            return RedirectToAction(nameof(Index));
        }

        private async Task ValidateAsync(Category model, int? editingId)
        {
            ModelState.Remove(nameof(Category.Product));
            ModelState.Remove(nameof(Category.Children));
            ModelState.Remove(nameof(Category.Parent));

            if (model.ParentCatId.HasValue)
            {
                if (model.ParentCatId == editingId)
                {
                    ModelState.AddModelError(nameof(Category.ParentCatId), "A category cannot be its own parent.");
                }
                else
                {
                    var parent = await _context.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.CatId == model.ParentCatId);
                    if (parent == null)
                        ModelState.AddModelError(nameof(Category.ParentCatId), "Parent category not found.");
                    else if (parent.ParentCatId.HasValue)
                        ModelState.AddModelError(nameof(Category.ParentCatId), "Subcategories can only be one level deep.");
                    else if (editingId.HasValue && await _context.Categories.AnyAsync(c => c.ParentCatId == editingId))
                        ModelState.AddModelError(nameof(Category.ParentCatId), "This category has subcategories, so it must stay top-level.");
                }
            }

            var name = model.CatName?.Trim();
            if (!string.IsNullOrEmpty(name) && await _context.Categories.AnyAsync(c => c.CatName == name && c.CatId != editingId))
            {
                ModelState.AddModelError(nameof(Category.CatName), "A category with this name already exists.");
            }
        }

        private static void Copy(Category source, Category target)
        {
            target.CatName = source.CatName?.Trim();
            target.Description = string.IsNullOrWhiteSpace(source.Description) ? null : source.Description.Trim();
            target.ParentCatId = source.ParentCatId;
            target.DisplayOrder = source.DisplayOrder;
        }

        private async Task PopulateParentsAsync(int? selected, int? excludeId)
        {
            var parents = await _context.Categories.AsNoTracking()
                .Where(c => c.ParentCatId == null && c.CatId != excludeId)
                .OrderBy(c => c.CatName).ToListAsync();
            ViewBag.Parents = new SelectList(parents, "CatId", "CatName", selected);
        }
    }
}
