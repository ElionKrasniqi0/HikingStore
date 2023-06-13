using HikingStore.Areas.Admin.ViewModel;
using HikingStore.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HikingStore.Areas.Admin.Controllers
{
    [Area("Admin")]
    //[Authorize(Roles = "Admin")]
    [AllowAnonymous]
    public class RoleManagerController : Controller
    {
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ApplicationDbContext _context;
        public RoleManagerController(RoleManager<IdentityRole> roleManager, ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _roleManager = roleManager;
            _userManager = userManager;
            _context = context;
        }
        [HttpGet]
        public IActionResult Index()
        {
            ViewBag.roles = _roleManager.Roles.ToList();
            return View();
        }
        [ValidateAntiForgeryToken]
        [HttpPost]
        public async Task<IActionResult> CreateRole(string roleName)
        {
            if (!await _roleManager.RoleExistsAsync(roleName))
            {
                if (!string.IsNullOrEmpty(roleName))
                {
                    await _roleManager.CreateAsync(new IdentityRole(roleName));
                }
            }
            return RedirectToAction(nameof(Index));
        }
        [HttpGet]
        public async Task<IActionResult> EditRole(string Id)
        {
            var role = await _roleManager.FindByIdAsync(Id);
            if (role == null)
            {
                ViewBag.ErrorMsage = $"Role with Id = {Id} Cannot be found!";
                return View("NotFound");
            }
            var model = new EditRoleNameViewModel
            {
                Id = role.Id,
                RoleName = role.Name,
            };
            foreach (var user in _userManager.Users.ToList())
            {
                if (await _userManager.IsInRoleAsync(user, role.Name))
                {
                    model.Users.Add(user.UserName);
                }
            }
            return View(model);
        }
        public async Task<IActionResult> EditRoleConfirmed(string RoleName, string Id)
        {
            try
            {
                IdentityRole newRole = await _roleManager.FindByIdAsync(Id);
                newRole.Name = RoleName;
                await _roleManager.UpdateAsync(newRole);
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                return NotFound(ex);
            }
        }
        [ActionName("RemoveUserRole")]
        [ValidateAntiForgeryToken]
        [HttpPost]
        public async Task<IActionResult> RemoveUserRole(Guid UserId, string roleName)
        {
            if (!String.IsNullOrEmpty(roleName))
            {
                if (await _roleManager.RoleExistsAsync(roleName))
                {
                    var role = await _roleManager.FindByNameAsync(roleName);
                    var user = await _userManager.FindByIdAsync(UserId.ToString());

                    await _userManager.RemoveFromRoleAsync(user, roleName);
                }
            }
            return RedirectToAction("EditRole", "Home", new { UserId = UserId.ToString() });
        }

        public async Task<IActionResult> RemoveRole(string Id)
        {
            var role = await _roleManager.FindByIdAsync(Id);
            if (role == null)
            {
                ViewBag.ErrorMessage = $"Role With Id = {Id} cannot be found!";
                return NotFound(ViewBag);
            }
            else
            {
                var result = await _roleManager.DeleteAsync(role);
                if (result.Succeeded)
                {
                    return RedirectToAction("Index");
                }
            }
            return View("Index");
        }
    }
}