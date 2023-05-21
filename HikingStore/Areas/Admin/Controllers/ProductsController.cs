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
        public async Task<IActionResult> Create(Product model, IFormFile File)
        {
            if (File != null)
            {
                string imageName = Guid.NewGuid().ToString() + ".jpg";
                string filePathImage = Path.Combine(Directory.GetCurrentDirectory(), @"wwwroot\Product", imageName);
                using (var stream = System.IO.File.Create(filePathImage))
                {
                    await File.CopyToAsync(stream);
                }
                model.ProImage = imageName;
            }
            //var product = new Product
            //{
            //    ProId= model.ProId,
            //    CatId= model.CatId,
            //    ProName= model.ProName,
            //    Descreption=model.Descreption,
            //    Price=model.Price,
            //    ProImage=model.ProImage,
            //};
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
        public async Task<IActionResult> Edit(int id, Product model, IFormFile File)
        {
            if (File != null)
            {
                string imageName = Guid.NewGuid().ToString() + ".jpg";
                string filePathImage = Path.Combine(Directory.GetCurrentDirectory(), @"wwwroot\Product", imageName);
                using (var stream = System.IO.File.Create(filePathImage))
                {
                    await File.CopyToAsync(stream);
                }
                model.ProImage = imageName;
            }
            else
            {
                model.ProImage = model.ProImage;
            }
            _context.Update(model);
            await _context.SaveChangesAsync();
            ViewData["CategoryId"] = new SelectList(_context.Categories, "CatId", "CatName");
            return RedirectToAction(nameof(Index));
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
