using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using HikingStore.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace HikingStore.Hubs
{
    /// <summary>
    /// Real-time hub for cart updates, order status changes, and other live updates
    /// </summary>
    [Authorize]
    public class RealtimeHub : Hub
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<RealtimeHub> _logger;

        public RealtimeHub(UserManager<ApplicationUser> userManager, ApplicationDbContext context, ILogger<RealtimeHub> logger)
        {
            _userManager = userManager;
            _context = context;
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
                // Add user to their personal group for real-time updates
                await Groups.AddToGroupAsync(Context.ConnectionId, $"User_{userId}");
                
                var user = await _userManager.FindByIdAsync(userId);
                if (user != null)
                {
                    var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
                    if (isAdmin)
                    {
                        // Admins get updates for all orders
                        await Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
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
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"User_{userId}");
                
                var user = await _userManager.FindByIdAsync(userId);
                if (user != null)
                {
                    var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
                    if (isAdmin)
                    {
                        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Admins");
                    }
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Join a specific order group for real-time order updates
        /// </summary>
        public async Task JoinOrderGroup(int orderId)
        {
            var userId = Context.UserIdentifier;
            if (userId == null)
            {
                userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            }

            if (userId == null)
            {
                return;
            }

            var user = await _userManager.FindByIdAsync(userId);
            var allowed = user != null && (await _userManager.IsInRoleAsync(user, "Admin")
                || await _context.Orders.AnyAsync(o => o.OrderId == orderId && o.UserId == userId));
            if (allowed)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Order_{orderId}");
            }
        }

        /// <summary>
        /// Leave a specific order group
        /// </summary>
        public async Task LeaveOrderGroup(int orderId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Order_{orderId}");
        }
    }
}
