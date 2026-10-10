using HikingStore.Areas.Admin.ViewModel;
using HikingStore.Data;
using HikingStore.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HikingStore.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = DbInitializer.AdminRole)]
    public class RoleManagerController : Controller
    {
        private static readonly string[] ProtectedRoles = { DbInitializer.AdminRole, DbInitializer.UserRole };
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AdminAuditService _audit;

        public RoleManagerController(RoleManager<IdentityRole> roleManager, UserManager<ApplicationUser> userManager, AdminAuditService audit)
        {
            _roleManager = roleManager;
            _userManager = userManager;
            _audit = audit;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            ViewBag.roles = await _roleManager.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync();
            ViewBag.ProtectedRoles = ProtectedRoles;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> CreateRole(string roleName)
        {
            roleName = roleName?.Trim() ?? string.Empty;
            if (roleName.Length is < 2 or > 50)
            {
                TempData["Error"] = "Role names must be 2–50 characters.";
            }
            else if (await _roleManager.RoleExistsAsync(roleName))
            {
                TempData["Error"] = $"The role “{roleName}” already exists.";
            }
            else
            {
                await _roleManager.CreateAsync(new IdentityRole(roleName));
                await _audit.LogAdminModificationAsync("Role", roleName, "CREATE");
                TempData["Success"] = $"Role “{roleName}” created.";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> EditRole(string id)
        {
            var role = await _roleManager.FindByIdAsync(id);
            if (role == null) return NotFound();

            var model = new EditRoleNameViewModel { Id = role.Id, RoleName = role.Name };
            foreach (var user in await _userManager.GetUsersInRoleAsync(role.Name!))
            {
                model.Users.Add(user.Email ?? user.UserName ?? user.Id);
            }
            ViewBag.IsProtected = ProtectedRoles.Contains(role.Name);
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> EditRoleConfirmed(string id, string roleName)
        {
            var role = await _roleManager.FindByIdAsync(id);
            if (role == null) return NotFound();

            roleName = roleName?.Trim() ?? string.Empty;
            if (ProtectedRoles.Contains(role.Name))
            {
                TempData["Error"] = "Built-in roles can't be renamed.";
            }
            else if (roleName.Length is < 2 or > 50)
            {
                TempData["Error"] = "Role names must be 2–50 characters.";
            }
            else
            {
                var old = role.Name;
                role.Name = roleName;
                var result = await _roleManager.UpdateAsync(role);
                if (result.Succeeded)
                {
                    await _audit.LogAdminModificationAsync("Role", id, "RENAME", old, roleName);
                    TempData["Success"] = "Role renamed.";
                    return RedirectToAction(nameof(Index));
                }
                TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            }
            return RedirectToAction(nameof(EditRole), new { id });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveRole(string id)
        {
            var role = await _roleManager.FindByIdAsync(id);
            if (role == null) return NotFound();

            if (ProtectedRoles.Contains(role.Name))
            {
                TempData["Error"] = "Built-in roles can't be deleted.";
                return RedirectToAction(nameof(Index));
            }

            var result = await _roleManager.DeleteAsync(role);
            if (result.Succeeded)
            {
                await _audit.LogAdminDeletionAsync("Role", id, role.Name);
                TempData["Success"] = $"Role “{role.Name}” deleted.";
            }
            else
            {
                TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
