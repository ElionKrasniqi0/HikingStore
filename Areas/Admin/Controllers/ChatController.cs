using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HikingStore.Data;
using HikingStore.Models;

namespace HikingStore.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class ChatController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public ChatController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var chatRooms = await _context.ChatRooms
                .Include(cr => cr.User)
                .Include(cr => cr.Agent)
                .Include(cr => cr.Messages.OrderByDescending(m => m.SentAt).Take(1))
                .OrderByDescending(cr => cr.LastMessageAt)
                .ToListAsync();

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
                .Include(cr => cr.Messages.OrderBy(m => m.SentAt))
                .FirstOrDefaultAsync(cr => cr.Id == id);

            if (chatRoom == null)
            {
                return NotFound();
            }

            // Auto-assign agent if not assigned
            if (chatRoom.AgentId == null)
            {
                chatRoom.AgentId = userId;
                chatRoom.Status = ChatRoomStatus.InProgress;
                await _context.SaveChangesAsync();
            }

            // Mark messages as read
            var unreadMessages = chatRoom.Messages.Where(m => m.SenderId != userId && !m.IsRead).ToList();
            foreach (var msg in unreadMessages)
            {
                msg.IsRead = true;
                msg.ReadAt = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();

            return View(chatRoom);
        }

        [HttpPost]
        public async Task<IActionResult> AssignAgent(int id, string agentId)
        {
            var chatRoom = await _context.ChatRooms.FindAsync(id);
            if (chatRoom == null)
            {
                return NotFound();
            }

            chatRoom.AgentId = agentId;
            chatRoom.Status = ChatRoomStatus.InProgress;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Room), new { id });
        }

        [HttpPost]
        public async Task<IActionResult> ResolveRoom(int id)
        {
            var chatRoom = await _context.ChatRooms.FindAsync(id);
            if (chatRoom == null)
            {
                return NotFound();
            }

            chatRoom.Status = ChatRoomStatus.Resolved;
            chatRoom.ResolvedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> CloseRoom(int id)
        {
            var chatRoom = await _context.ChatRooms.FindAsync(id);
            if (chatRoom == null)
            {
                return NotFound();
            }

            chatRoom.Status = ChatRoomStatus.Closed;
            chatRoom.ResolvedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> GetUnreadCount()
        {
            var unreadCount = await _context.ChatMessages
                .Where(m => !m.IsRead && !m.IsAgentMessage)
                .Select(m => m.ChatRoomId)
                .Distinct()
                .CountAsync();

            return Json(new { count = unreadCount });
        }
    }
}
