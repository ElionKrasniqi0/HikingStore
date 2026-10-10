using System.Net;
using HikingStore.Areas.Admin.ViewModel;
using HikingStore.Data;
using HikingStore.Enum;
using HikingStore.Models;
using HikingStore.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HikingStore.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = DbInitializer.AdminRole)]
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly AdminAuditService _auditService;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<HomeController> _logger;

        public HomeController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            AdminAuditService auditService,
            IEmailSender emailSender,
            ILogger<HomeController> logger)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _auditService = auditService;
            _emailSender = emailSender;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var since30 = DateTime.UtcNow.Date.AddDays(-29);
            var since14 = DateTime.UtcNow.Date.AddDays(-13);
            var revenueOrders = _context.Orders.AsNoTracking().Where(o => o.Status != OrderStatus.Cancelled
                && (o.PaymentMethod != PaymentMethods.Card || o.PaymentStatus == PaymentStatuses.Paid));

            var model = new DashboardViewModel
            {
                TotalRevenue = await revenueOrders.SumAsync(o => (decimal?)o.TotalAmount) ?? 0,
                Revenue30Days = await revenueOrders.Where(o => o.OrderDate >= since30).SumAsync(o => (decimal?)o.TotalAmount) ?? 0,
                OrderCount = await _context.Orders.CountAsync(),
                Orders30Days = await _context.Orders.CountAsync(o => o.OrderDate >= since30),
                OpenOrders = await _context.Orders.CountAsync(o => (o.Status == OrderStatus.Received || o.Status == OrderStatus.Verified || o.Status == OrderStatus.Processing)
                    && (o.PaymentMethod != PaymentMethods.Card || o.PaymentStatus == PaymentStatuses.Paid)),
                CustomerCount = await _context.Users.CountAsync(),
                ActiveProducts = await _context.Products.CountAsync(p => p.IsActive),
                PendingReviews = await _context.Reviews.CountAsync(r => !r.IsApproved),
                MessageCount = await _context.Contacts.CountAsync(),
                NewsletterSubscribers = await _context.NewsletterSubscribers.CountAsync(),
                LowStock = await _context.Products.AsNoTracking()
                    .Where(p => p.IsActive && p.StockQuantity <= 5)
                    .OrderBy(p => p.StockQuantity).Take(8).ToListAsync(),
                RecentOrders = await _context.Orders.AsNoTracking()
                    .Include(o => o.User)
                    .OrderByDescending(o => o.OrderDate).Take(8).ToListAsync(),
                OrdersByStatus = await _context.Orders.GroupBy(o => o.Status)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.Key, g => g.Count)
            };

            var daily = await revenueOrders.Where(o => o.OrderDate >= since14)
                .GroupBy(o => o.OrderDate.Date)
                .Select(g => new { Date = g.Key, Orders = g.Count(), Revenue = g.Sum(o => o.TotalAmount) })
                .ToListAsync();
            model.SalesByDay = Enumerable.Range(0, 14)
                .Select(i => since14.AddDays(i))
                .Select(d => daily.FirstOrDefault(x => x.Date == d) is { } x
                    ? new DailySales(d, x.Orders, x.Revenue)
                    : new DailySales(d, 0, 0))
                .ToList();

            model.TopProducts = (await _context.OrderDetails.AsNoTracking()
                    .Where(d => d.Order!.Status != OrderStatus.Cancelled
                        && (d.Order.PaymentMethod != PaymentMethods.Card || d.Order.PaymentStatus == PaymentStatuses.Paid))
                    .GroupBy(d => new { d.ProId, Name = d.ProductName ?? d.Product!.ProName })
                    .Select(g => new { g.Key.ProId, g.Key.Name, Units = g.Sum(d => d.Quantity), Revenue = g.Sum(d => d.Price * d.Quantity) })
                    .OrderByDescending(g => g.Units).Take(5).ToListAsync())
                .Select(g => new TopProduct(g.ProId, g.Name ?? "Product", g.Units, g.Revenue))
                .ToList();

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Users(string? search)
        {
            var query = _context.Users.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(u => u.Email!.Contains(term) || u.UserName!.Contains(term)
                    || (u.FirstName != null && u.FirstName.Contains(term)) || (u.LastName != null && u.LastName.Contains(term)));
            }

            var users = await query.OrderBy(u => u.Email).ToListAsync();
            var userRoles = await (from ur in _context.UserRoles
                                   join r in _context.Roles on ur.RoleId equals r.Id
                                   select new { ur.UserId, r.Name }).ToListAsync();
            var rolesByUser = userRoles.GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.Select(x => x.Name!).ToList());
            var orderCounts = await _context.Orders.GroupBy(o => o.UserId).Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Count);

            var now = DateTimeOffset.UtcNow;
            var model = users.Select(u => new Users_In_Role_ViewModel
            {
                UserId = u.Id,
                Email = u.Email ?? string.Empty,
                UserName = u.DisplayName,
                IsLocked = u.LockoutEnd.HasValue && u.LockoutEnd.Value > now,
                EmailConfirmed = u.EmailConfirmed,
                OrderCount = orderCounts.GetValueOrDefault(u.Id),
                Roles = rolesByUser.GetValueOrDefault(u.Id) ?? new List<string>()
            }).ToList();

            ViewBag.Search = search;
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> EditRole(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound();
            }

            ViewBag.roles = await _context.Roles.AsNoTracking()
                .Select(r => new SelectListItem { Value = r.Id, Text = r.Name }).ToListAsync();
            ViewBag.UserEmail = user.Email;
            return View(new EditRoleViewModel
            {
                UserId = user.Id,
                UserRoles = (await _userManager.GetRolesAsync(user)).ToList()
            });
        }

        [HttpPost]
        public async Task<IActionResult> EditRole(EditRoleViewModel model)
        {
            var user = await _userManager.FindByIdAsync(model.UserId);
            var role = string.IsNullOrEmpty(model.RoleId) ? null : await _roleManager.FindByIdAsync(model.RoleId);
            if (user == null || role?.Name == null)
            {
                TempData["Error"] = "Please choose a valid role.";
                return RedirectToAction(nameof(EditRole), new { userId = model.UserId });
            }

            if (!await _userManager.IsInRoleAsync(user, role.Name))
            {
                var result = await _userManager.AddToRoleAsync(user, role.Name);
                if (result.Succeeded)
                {
                    await _userManager.UpdateSecurityStampAsync(user);
                    await _auditService.LogAdminModificationAsync("User", user.Id, "ADD_ROLE", null, role.Name);
                    TempData["Success"] = $"Added role {role.Name}.";
                }
                else
                {
                    TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
                }
            }
            return RedirectToAction(nameof(EditRole), new { userId = model.UserId });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveRole(string roleName, string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null || string.IsNullOrEmpty(roleName))
            {
                return RedirectToAction(nameof(Users));
            }

            if (roleName == DbInitializer.AdminRole && user.Id == _userManager.GetUserId(User))
            {
                TempData["Error"] = "You cannot remove your own admin role.";
                return RedirectToAction(nameof(EditRole), new { userId });
            }

            await _userManager.RemoveFromRoleAsync(user, roleName);
            await _userManager.UpdateSecurityStampAsync(user);
            await _auditService.LogAdminModificationAsync("User", user.Id, "REMOVE_ROLE", roleName, null);
            TempData["Success"] = $"Removed role {roleName}.";
            return RedirectToAction(nameof(EditRole), new { userId });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound();
            }
            if (user.Id == _userManager.GetUserId(User))
            {
                TempData["Error"] = "You cannot delete your own account here.";
                return RedirectToAction(nameof(Users));
            }
            if (await _context.Orders.AnyAsync(o => o.UserId == user.Id))
            {
                TempData["Error"] = "This customer has orders, so the account can't be deleted without losing order history. Lock the account instead.";
                return RedirectToAction(nameof(Users));
            }

            await _context.RemoveNonCascadingUserDataAsync(user.Id);
            var result = await _userManager.DeleteAsync(user);
            if (result.Succeeded)
            {
                await _auditService.LogAdminDeletionAsync("User", user.Id, user.Email);
                TempData["Success"] = $"Deleted {user.Email}.";
            }
            else
            {
                TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            }
            return RedirectToAction(nameof(Users));
        }

        [HttpPost]
        public async Task<IActionResult> LockUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();
            if (user.Id == _userManager.GetUserId(User))
            {
                TempData["Error"] = "You cannot lock your own account.";
                return RedirectToAction(nameof(Users));
            }

            await _userManager.SetLockoutEnabledAsync(user, true);
            await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
            await _userManager.UpdateSecurityStampAsync(user);
            await _auditService.LogAdminModificationAsync("User", user.Id, "LOCK");
            TempData["Success"] = $"{user.Email} is locked and signed out.";
            return RedirectToAction(nameof(Users));
        }

        [HttpPost]
        public async Task<IActionResult> UnlockUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            await _userManager.SetLockoutEndDateAsync(user, null);
            await _userManager.ResetAccessFailedCountAsync(user);
            await _auditService.LogAdminModificationAsync("User", user.Id, "UNLOCK");
            TempData["Success"] = $"{user.Email} is unlocked.";
            return RedirectToAction(nameof(Users));
        }

        public async Task<IActionResult> ShowMessage()
        {
            var messages = await _context.Contacts.AsNoTracking().OrderByDescending(c => c.CreatedDate).ToListAsync();
            return View(messages);
        }

        [HttpPost]
        public async Task<IActionResult> DeleteMessage(int id)
        {
            var deleted = await _context.Contacts.Where(c => c.CoId == id).ExecuteDeleteAsync();
            return Json(new { success = deleted > 0, message = deleted > 0 ? "Message deleted." : "Message not found." });
        }

        [HttpPost]
        public async Task<IActionResult> SendReply(int id, string replySubject, string replyMessage)
        {
            var contact = await _context.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.CoId == id);
            if (contact?.Email == null)
            {
                return Json(new { success = false, message = "Message not found." });
            }
            if (string.IsNullOrWhiteSpace(replySubject) || string.IsNullOrWhiteSpace(replyMessage))
            {
                return Json(new { success = false, message = "Subject and message are required." });
            }

            var body = $"<p>Hi {WebUtility.HtmlEncode(contact.Name)},</p>" +
                       $"<p>{WebUtility.HtmlEncode(replyMessage).Replace("\n", "<br>")}</p>" +
                       "<p>— The HikingStore team</p>";
            try
            {
                await _emailSender.SendEmailAsync(contact.Email, replySubject.Trim(), body);
                await _auditService.LogAdminActionAsync("CONTACT_REPLY", $"Replied to contact message {id}", $"Contact:{id}");
                return Json(new { success = true, message = $"Reply sent to {contact.Email}." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send reply to contact message {Id}", id);
                return Json(new { success = false, message = "The email could not be sent. Check the email settings." });
            }
        }
    }
}
