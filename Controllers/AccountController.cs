using System.ComponentModel.DataAnnotations;
using HikingStore.Data;
using HikingStore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HikingStore.Controllers
{
    public class ProfileInput
    {
        [StringLength(100)]
        [Display(Name = "First name")]
        public string? FirstName { get; set; }

        [StringLength(100)]
        [Display(Name = "Last name")]
        public string? LastName { get; set; }

        [Phone]
        [StringLength(30)]
        [Display(Name = "Phone number")]
        public string? PhoneNumber { get; set; }

        public string? Email { get; set; }
    }

    public class AccountDashboardViewModel
    {
        public ApplicationUser User { get; set; } = null!;
        public List<Order> RecentOrders { get; set; } = new();
        public int OrderCount { get; set; }
        public int WishlistCount { get; set; }
        public int AddressCount { get; set; }
        public int ReviewCount { get; set; }
    }

    /// <summary>Customer account area. Password, email and 2FA use the built-in Identity pages.</summary>
    [Authorize]
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        public AccountController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
        {
            _context = context;
            _userManager = userManager;
            _signInManager = signInManager;
        }

        private string UserId => _userManager.GetUserId(User)!;

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var model = new AccountDashboardViewModel
            {
                User = user,
                RecentOrders = await _context.Orders.AsNoTracking()
                    .Include(o => o.OrderDetails)
                    .Where(o => o.UserId == UserId)
                    .OrderByDescending(o => o.OrderDate)
                    .Take(3)
                    .ToListAsync(),
                OrderCount = await _context.Orders.CountAsync(o => o.UserId == UserId),
                WishlistCount = await _context.Wishlists.CountAsync(w => w.UserId == UserId && w.Product!.IsActive),
                AddressCount = await _context.Addresses.CountAsync(a => a.UserId == UserId),
                ReviewCount = await _context.Reviews.CountAsync(r => r.UserId == UserId)
            };
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();
            return View(new ProfileInput
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                PhoneNumber = user.PhoneNumber,
                Email = user.Email
            });
        }

        [HttpPost]
        public async Task<IActionResult> Profile(ProfileInput input)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();
            input.Email = user.Email;

            if (!ModelState.IsValid)
            {
                return View(input);
            }

            user.FirstName = input.FirstName?.Trim();
            user.LastName = input.LastName?.Trim();
            user.PhoneNumber = string.IsNullOrWhiteSpace(input.PhoneNumber) ? null : input.PhoneNumber.Trim();
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
                return View(input);
            }

            await _signInManager.RefreshSignInAsync(user);
            TempData["Success"] = "Your profile was updated.";
            return RedirectToAction(nameof(Profile));
        }

        public async Task<IActionResult> Addresses()
        {
            var addresses = await _context.Addresses.AsNoTracking()
                .Where(a => a.UserId == UserId)
                .OrderByDescending(a => a.IsDefault).ThenBy(a => a.AddressId)
                .ToListAsync();
            return View(addresses);
        }

        [HttpGet]
        public async Task<IActionResult> AddressForm(int? id)
        {
            if (id == null)
            {
                return View(new Address { Country = "Kosovo" });
            }

            var address = await _context.Addresses.AsNoTracking().FirstOrDefaultAsync(a => a.AddressId == id && a.UserId == UserId);
            return address == null ? NotFound() : View(address);
        }

        [HttpPost]
        public async Task<IActionResult> AddressForm(int? id, Address input)
        {
            ModelState.Remove(nameof(Address.UserId));
            if (!ModelState.IsValid)
            {
                return View(input);
            }

            Address address;
            if (id.HasValue)
            {
                var existing = await _context.Addresses.FirstOrDefaultAsync(a => a.AddressId == id && a.UserId == UserId);
                if (existing == null) return NotFound();
                address = existing;
            }
            else
            {
                address = new Address { UserId = UserId };
                _context.Addresses.Add(address);
            }

            address.Label = input.Label?.Trim();
            address.FullName = input.FullName.Trim();
            address.PhoneNumber = input.PhoneNumber.Trim();
            address.AddressLine = input.AddressLine.Trim();
            address.City = input.City.Trim();
            address.PostalCode = input.PostalCode?.Trim();
            address.Country = input.Country.Trim();

            var hasOthers = await _context.Addresses.AnyAsync(a => a.UserId == UserId && a.AddressId != address.AddressId);
            address.IsDefault = input.IsDefault || !hasOthers;
            if (address.IsDefault)
            {
                await _context.Addresses.Where(a => a.UserId == UserId && a.AddressId != address.AddressId && a.IsDefault)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDefault, false));
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = id.HasValue ? "Address updated." : "Address added.";
            return RedirectToAction(nameof(Addresses));
        }

        [HttpPost]
        public async Task<IActionResult> DeleteAddress(int id)
        {
            var address = await _context.Addresses.FirstOrDefaultAsync(a => a.AddressId == id && a.UserId == UserId);
            if (address == null) return NotFound();

            _context.Addresses.Remove(address);
            await _context.SaveChangesAsync();

            if (address.IsDefault)
            {
                var next = await _context.Addresses.Where(a => a.UserId == UserId).OrderBy(a => a.AddressId).FirstOrDefaultAsync();
                if (next != null)
                {
                    next.IsDefault = true;
                    await _context.SaveChangesAsync();
                }
            }

            TempData["Success"] = "Address deleted.";
            return RedirectToAction(nameof(Addresses));
        }

        [HttpPost]
        public async Task<IActionResult> SetDefaultAddress(int id)
        {
            if (!await _context.Addresses.AnyAsync(a => a.AddressId == id && a.UserId == UserId)) return NotFound();

            await _context.Addresses.Where(a => a.UserId == UserId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDefault, a => a.AddressId == id));
            TempData["Success"] = "Default address updated.";
            return RedirectToAction(nameof(Addresses));
        }

        public async Task<IActionResult> Reviews()
        {
            var reviews = await _context.Reviews.AsNoTracking()
                .Include(r => r.Product)
                .Where(r => r.UserId == UserId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
            return View(reviews);
        }
    }
}
