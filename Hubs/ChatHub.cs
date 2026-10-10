using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using HikingStore.Data;
using HikingStore.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HikingStore.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        public const int MaxMessageLength = 2000;

        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<ChatHub> _logger;

        public ChatHub(ApplicationDbContext context, UserManager<ApplicationUser> userManager, ILogger<ChatHub> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier;
            if (userId == null)
            {
                userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            }

            if (userId != null)
            {
                var user = await _userManager.FindByIdAsync(userId);
                if (user != null)
                {
                    var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
                    if (isAdmin)
                    {
                        await Groups.AddToGroupAsync(Context.ConnectionId, "Agents");
                        await Clients.OthersInGroup("Agents").SendAsync("AgentConnected", user.UserName);
                    }
                    else
                    {
                        await Groups.AddToGroupAsync(Context.ConnectionId, userId);
                    }
                }
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.UserIdentifier;
            if (userId == null)
            {
                userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            }

            if (userId != null)
            {
                var user = await _userManager.FindByIdAsync(userId);
                if (user != null)
                {
                    var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
                    if (isAdmin)
                    {
                        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Agents");
                        await Clients.OthersInGroup("Agents").SendAsync("AgentDisconnected", user.UserName);
                    }
                    else
                    {
                        await Groups.RemoveFromGroupAsync(Context.ConnectionId, userId);
                    }
                }
            }
            await base.OnDisconnectedAsync(exception);
        }

        public async Task SendMessage(int chatRoomId, string message)
        {
            try
            {
                var userId = Context.UserIdentifier;
                if (userId == null)
                {
                    userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
                }
                
                if (userId == null)
                {
                    await Clients.Caller.SendAsync("ErrorMessage", "You must be logged in to send messages.");
                    return;
                }

                message = message?.Trim() ?? string.Empty;
                if (message.Length == 0) return;
                if (message.Length > MaxMessageLength)
                {
                    await Clients.Caller.SendAsync("ErrorMessage", $"Messages can be at most {MaxMessageLength} characters.");
                    return;
                }

                var user = await _userManager.FindByIdAsync(userId);
                if (user == null) return;

                var chatRoom = await _context.ChatRooms
                    .Include(cr => cr.User)
                    .Include(cr => cr.Agent)
                    .FirstOrDefaultAsync(cr => cr.Id == chatRoomId);

                if (chatRoom == null) return;

                var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
                var isAgent = isAdmin || chatRoom.AgentId == userId;
                var isOwner = chatRoom.UserId == userId;

                // Only room owner or assigned agent can send messages
                if (!isOwner && !isAgent)
                {
                    await Clients.Caller.SendAsync("ErrorMessage", "You don't have permission to send messages in this chat room.");
                    return;
                }

                // Create message
                var chatMessage = new ChatMessage
                {
                    ChatRoomId = chatRoomId,
                    SenderId = userId,
                    Message = message,
                    IsAgentMessage = isAdmin || isAgent,
                    SentAt = DateTime.UtcNow,
                    IsRead = isOwner // If owner sends, mark as read for them
                };

                _context.ChatMessages.Add(chatMessage);

                // Update room - reopen if closed/resolved
                chatRoom.LastMessageAt = DateTime.UtcNow;
                
                // Reopen closed/resolved chats when a message is sent
                if (chatRoom.Status == ChatRoomStatus.Closed || chatRoom.Status == ChatRoomStatus.Resolved)
                {
                    chatRoom.Status = ChatRoomStatus.InProgress;
                    chatRoom.ResolvedAt = null; // Clear resolved date
                }
                
                if (chatRoom.Status == ChatRoomStatus.Open && isAgent)
                {
                    chatRoom.Status = ChatRoomStatus.InProgress;
                    if (chatRoom.AgentId == null)
                    {
                        chatRoom.AgentId = userId;
                    }
                }
                
                // Auto-assign agent if message is from agent and no agent assigned
                if (isAgent && chatRoom.AgentId == null)
                {
                    chatRoom.AgentId = userId;
                    if (chatRoom.Status == ChatRoomStatus.Open)
                    {
                        chatRoom.Status = ChatRoomStatus.InProgress;
                    }
                }

                await _context.SaveChangesAsync();

                // Send to appropriate users
                var senderName = user.UserName ?? user.Email ?? "Unknown";
                var messageData = new
                {
                    id = chatMessage.Id,
                    chatRoomId = chatRoomId,
                    senderId = userId,
                    senderName = senderName,
                    message = message,
                    isAgentMessage = chatMessage.IsAgentMessage,
                    sentAt = chatMessage.SentAt,
                    isRead = chatMessage.IsRead
                };

                // Participants viewing the room are in the room group; sending to one group avoids duplicate delivery.
                await Clients.Group($"Room_{chatRoomId}").SendAsync("ReceiveMessage", messageData);

                // If message is from agent, notify all agents about new activity (except sender)
                if (isAgent)
                {
                    await Clients.OthersInGroup("Agents").SendAsync("RoomUpdated", new
                    {
                        chatRoomId = chatRoomId,
                        lastMessageAt = chatRoom.LastMessageAt
                    });
                }
                else
                {
                    // If message is from user, notify all agents
                    await Clients.Group("Agents").SendAsync("NewUserMessage", new
                    {
                        chatRoomId = chatRoomId,
                        userId = chatRoom.UserId,
                        userName = chatRoom.User?.UserName ?? chatRoom.User?.Email ?? "Unknown",
                        message = message,
                        timestamp = chatRoom.LastMessageAt
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message");
                await Clients.Caller.SendAsync("ErrorMessage", "An error occurred while sending your message.");
            }
        }

        public async Task JoinRoom(int chatRoomId)
        {
            var userId = Context.UserIdentifier;
            if (userId == null)
            {
                userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            }
            
            if (userId == null) return;

            var chatRoom = await _context.ChatRooms.FindAsync(chatRoomId);
            if (chatRoom == null) return;

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return;

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            var isOwner = chatRoom.UserId == userId;
            var isAgent = chatRoom.AgentId == userId || isAdmin;

            if (isOwner || isAgent)
            {
                // Join the room group
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Room_{chatRoomId}");
                
                // Also ensure user is in their own group for direct messaging
                if (isOwner)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, userId);
                }
                
                // If agent, also ensure they're in agents group
                if (isAgent && !isOwner)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, "Agents");
                }
                
                // Mark messages as read
                var unreadMessages = await _context.ChatMessages
                    .Where(m => m.ChatRoomId == chatRoomId && 
                                m.SenderId != userId && 
                                !m.IsRead)
                    .ToListAsync();

                foreach (var msg in unreadMessages)
                {
                    msg.IsRead = true;
                    msg.ReadAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();

                await Clients.Group($"Room_{chatRoomId}").SendAsync("MessagesRead", chatRoomId);
            }
        }

        public async Task Typing(int chatRoomId, bool isTyping)
        {
            var userId = Context.UserIdentifier;
            if (userId == null)
            {
                userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            }
            
            if (userId == null) return;

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return;

            var chatRoom = await _context.ChatRooms.AsNoTracking().FirstOrDefaultAsync(cr => cr.Id == chatRoomId);
            if (chatRoom == null) return;
            if (chatRoom.UserId != userId && chatRoom.AgentId != userId && !await _userManager.IsInRoleAsync(user, "Admin")) return;

            // Send typing indicator to others in the room (not to self)
            await Clients.OthersInGroup($"Room_{chatRoomId}").SendAsync("UserTyping", new
            {
                userId = userId,
                userName = user.UserName ?? user.Email ?? "Unknown",
                isTyping = isTyping
            });
        }
    }
}
