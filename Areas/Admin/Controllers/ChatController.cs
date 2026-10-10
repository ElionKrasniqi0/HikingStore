using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HikingStore.Data;
using HikingStore.Models;
using System;
using System.Collections.Generic;

namespace HikingStore.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
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
            try
            {
                var chatRooms = await _context.ChatRooms
                    .Include(cr => cr.User)
                    .Include(cr => cr.Agent)
                    .Include(cr => cr.Messages)
                    .AsNoTracking()
                    .OrderByDescending(cr => cr.LastMessageAt)
                    .ToListAsync();

                return View(chatRooms);
            }
            catch (Exception ex)
            {
                // Log error if needed
                TempData["Error"] = "An error occurred while loading chat rooms. Please try again.";
                return View(new List<ChatRoom>());
            }
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

        [HttpPost]
        public async Task<IActionResult> ReopenRoom(int id)
        {
            var chatRoom = await _context.ChatRooms.FindAsync(id);
            if (chatRoom == null)
            {
                return NotFound();
            }

            // Only reopen if closed or resolved
            if (chatRoom.Status == ChatRoomStatus.Closed || chatRoom.Status == ChatRoomStatus.Resolved)
            {
                chatRoom.Status = ChatRoomStatus.InProgress;
                chatRoom.ResolvedAt = null;
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Room), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteRoom(int id)
        {
            var chatRoom = await _context.ChatRooms
                .Include(cr => cr.Messages)
                .FirstOrDefaultAsync(cr => cr.Id == id);

            if (chatRoom == null)
            {
                return NotFound();
            }

            // Only allow deletion of resolved or closed chats
            if (chatRoom.Status != ChatRoomStatus.Resolved && chatRoom.Status != ChatRoomStatus.Closed)
            {
                TempData["Error"] = "You can only delete resolved or closed chat rooms.";
                return RedirectToAction(nameof(Room), new { id });
            }

            // Delete all messages first (they will be cascade deleted, but being explicit)
            _context.ChatMessages.RemoveRange(chatRoom.Messages);
            
            // Delete the chat room
            _context.ChatRooms.Remove(chatRoom);
            
            await _context.SaveChangesAsync();

            TempData["Success"] = "Chat room has been deleted successfully.";
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
