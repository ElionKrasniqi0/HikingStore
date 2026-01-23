using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using HikingStore.Data;
using System.Security.Claims;

namespace HikingStore.Hubs
{
    /// <summary>
    /// Real-time hub for cart updates, order status changes, and other live updates
    /// </summary>
    [Authorize]
    public class RealtimeHub : Hub
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<RealtimeHub> _logger;

        public RealtimeHub(UserManager<IdentityUser> userManager, ILogger<RealtimeHub> logger)
        {
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

            if (userId != null)
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
