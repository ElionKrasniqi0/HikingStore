using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HikingStore.Data;
using HikingStore.Models;

namespace HikingStore.Controllers
{
    [Authorize]
    public class ChatController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public ChatController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);
            if (userId == null)
            {
                return RedirectToAction("Login", "Account", new { area = "Identity" });
            }

            var chatRooms = await _context.ChatRooms
                .Include(cr => cr.Agent)
                .Include(cr => cr.Messages)
                .Where(cr => cr.UserId == userId)
                .OrderByDescending(cr => cr.LastMessageAt)
                .ToListAsync();

            ViewData["AccountSection"] = "chat";
            return View(chatRooms);
        }

        [HttpGet]
        public async Task<IActionResult> Room(int id)
        {
            var userId = _userManager.GetUserId(User);
            if (userId == null)
            {
                return RedirectToAction("Login", "Account", new { area = "Identity" });
            }

            var chatRoom = await _context.ChatRooms
                .Include(cr => cr.User)
                .Include(cr => cr.Agent)
                .Include(cr => cr.Messages)
                .FirstOrDefaultAsync(cr => cr.Id == id);

            if (chatRoom == null)
            {
                return NotFound();
            }

            if (chatRoom.UserId != userId)
            {
                return Forbid();
            }

            // Mark messages as read
            var unreadMessages = chatRoom.Messages.Where(m => m.SenderId != userId && !m.IsRead).ToList();
            foreach (var msg in unreadMessages)
            {
                msg.IsRead = true;
                msg.ReadAt = DateTime.UtcNow;
            }
            if (unreadMessages.Count > 0)
            {
                await _context.SaveChangesAsync();
            }

            ViewData["AccountSection"] = "chat";
            return View(chatRoom);
        }

        [HttpPost]
        public async Task<IActionResult> CreateRoom(string subject)
        {
            var userId = _userManager.GetUserId(User);
            if (userId == null)
            {
                return Unauthorized();
            }

            subject = subject?.Trim() ?? string.Empty;
            if (subject.Length > 150) subject = subject[..150];

            var chatRoom = new ChatRoom
            {
                UserId = userId,
                Subject = subject.Length == 0 ? "General question" : subject,
                Status = ChatRoomStatus.Open,
                CreatedAt = DateTime.UtcNow,
                LastMessageAt = DateTime.UtcNow
            };

            _context.ChatRooms.Add(chatRoom);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Room), new { id = chatRoom.Id });
        }

        [HttpPost]
        public async Task<IActionResult> CloseRoom(int id)
        {
            var userId = _userManager.GetUserId(User);
            if (userId == null)
            {
                return Unauthorized();
            }

            var chatRoom = await _context.ChatRooms.FindAsync(id);
            if (chatRoom == null || chatRoom.UserId != userId)
            {
                return NotFound();
            }

            chatRoom.Status = ChatRoomStatus.Closed;
            chatRoom.ResolvedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}
