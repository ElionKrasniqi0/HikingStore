using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HikingStore.Data;
using HikingStore.Areas.Admin.ViewModel;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using HikingStore.Models;
using HikingStore.Enum;

namespace HikingStore.Areas.Admin.Controllers
{

    [Area("Admin")]
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public HomeController(ApplicationDbContext context, SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _signInManager = signInManager;
        }
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var allusers = await _context.Users.ToListAsync();
            List<Users_In_Role_ViewModel> model = new();

            foreach (var item in allusers)
            {
                // List of roles for each user
                var rolesList = await (from user in _context.Users
                                       join userRoles in _context.UserRoles on user.Id equals userRoles.UserId
                                       join role in _context.Roles on userRoles.RoleId equals role.Id
                                       select new { UserId = user.Id, UserName = user.UserName, RoleId = role.Id, RoleName = role.Name })
                                    .Where(t => t.UserId == item.Id)
                                    .Select(x => x.RoleName)
                                    .ToListAsync();

                Users_In_Role_ViewModel tempUsers = new()
                {
                    UserId = item.Id,
                    Email = item.Email,
                    UserName = item.UserName,
                    IsLocked = item.LockoutEnabled,
                    Roles = rolesList,
                };
                model.Add(tempUsers);
            }
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> EditRole(Guid UserId)
        {
            if (UserExists(UserId))
            {
                List<SelectListItem> roles = _context.Roles.Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.Name }).AsNoTracking().ToList();

                var user = await _userManager.FindByIdAsync(UserId.ToString());
                var userRoles = (List<string>)await _userManager.GetRolesAsync(user);

                ViewBag.roles = roles;

                EditRoleViewModel model = new()
                {
                    UserId = UserId.ToString(),
                    UserRoles = userRoles
                };

                return View(model);
            }
            else
            {
                return NotFound("User not Found");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditRoleAsync(EditRoleViewModel model)
        {
            IdentityUser user = await _userManager
                .FindByIdAsync(model.UserId.ToString());

            var role = await _roleManager
                .FindByIdAsync(model.RoleId);

            var userAlreadyInRole = await _context.UserRoles
                .Where(x => x.RoleId == role.Id && x.UserId == user.Id)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (userAlreadyInRole == null)
            {
                var roleResult = await _userManager.AddToRoleAsync(user, role.Name);

                if (roleResult.Succeeded)
                {
                    await _userManager.UpdateSecurityStampAsync(user);

                    return RedirectToAction("EditRole", new { UserId = model.UserId });
                }

                foreach (var error in roleResult.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }

                return RedirectToAction("EditRole", new { model.UserId });
            }

            return RedirectToAction("EditRole", new { model.UserId });
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveRole(string RoleName, string UserId)
        {
            if (!String.IsNullOrEmpty(RoleName) && !String.IsNullOrEmpty(UserId))
            {
                IdentityUser user = await _userManager.FindByIdAsync(UserId);
                if (user != null)
                {
                    // Removes the role from the said user and the said role.
                    await _userManager.RemoveFromRoleAsync(user, RoleName);

                    Guid guidUserId = Guid.Parse(UserId);

                    await _userManager.UpdateSecurityStampAsync(user);
                    return RedirectToAction("EditRole", new { UserId = guidUserId });
                }
                return RedirectToAction("Index");
            }
            else
            {
                return RedirectToAction("Index");
            }
        }

        [ActionName("DeleteUser")]
        [HttpPost]
        public async Task<IActionResult> DeleteUser(Guid UserId)
        {
            if (UserExists(UserId))
            {
                var user = await _userManager.FindByIdAsync(UserId.ToString());
                var result = await _userManager.DeleteAsync(user);

                if (result.Succeeded)
                {
                    //TempData["Message"] = $"User: {user.UserName} deleted successfully.";
                    //_notyf.Success($"User: {user.UserName} was deleted successfully!");

                    return RedirectToAction("Index");
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }
                return View("Index");
            }

            ViewBag.ErrorMessage = $"User with the Id = {UserId}, cannot be found!";
            return View("NotFound");
        }

        public bool UserExists(Guid UserId)
        {
            if (UserId != Guid.Empty)
            {
                var user = _context.Users.Where(x => x.Id.Equals(UserId.ToString())).FirstOrDefault();
                if (user != null)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            return View();
        }

        [ValidateAntiForgeryToken]
        [System.Web.Http.HttpPost]
        public async Task<IActionResult> LockUser(Guid UserId)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(UserId.ToString());
                if (user.LockoutEnabled != true)
                {
                    DateTime resctritedDate = DateTime.Now.AddYears(1);

                    var lockUserTask = await _userManager.SetLockoutEnabledAsync(user, true);
                    var lockDateTask = await _userManager.SetLockoutEndDateAsync(user, resctritedDate);

                    // Force Logouts the User
                    await _userManager.UpdateSecurityStampAsync(user);

                    return RedirectToAction("Index", "Home", new { area = "Admin" });
                }
                return RedirectToAction("Index", "Home", new { area = "Admin" });
            }
            catch (Exception)
            {
                return RedirectToAction("Index", "Home", new { area = "Admin" });
            }
        }

        public async Task<IActionResult> UnlockUser(Guid UserId)
        {
            try
            {
                DateTime resctritedDate = DateTime.Now - TimeSpan.FromMinutes(1);

                var user = await _userManager.FindByIdAsync(UserId.ToString());

                var setLockoutEndDateTask = await _userManager.SetLockoutEndDateAsync(user, resctritedDate);
                var lockDisabledTask = await _userManager.SetLockoutEnabledAsync(user, false);

                // Force Logout the User
                await _userManager.UpdateSecurityStampAsync(user);

                return RedirectToAction("Index", "Home", new { area = "Admin" });
            }
            catch (Exception)
            {
                return RedirectToAction("Index", "Home", new { area = "Admin" });
            }
        }

        public IActionResult ShowMessage()
        {
            var message = _context.Contacts.ToList();
            return View(message);
        }
    }
}
