using HikingStore.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace HikingStore.Services
{
    /// <summary>
    /// Service for logging admin actions for security audit and tracking
    /// </summary>
    public class AdminAuditService
    {
        private readonly ILogger<AdminAuditService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly UserManager<ApplicationUser> _userManager;

        public AdminAuditService(
            ILogger<AdminAuditService> logger,
            IHttpContextAccessor httpContextAccessor,
            UserManager<ApplicationUser> userManager)
        {
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
            _userManager = userManager;
        }

        /// <summary>
        /// Log an admin action for audit purposes
        /// </summary>
        public async Task LogAdminActionAsync(string action, string details, string? targetResource = null)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null) return;

            var userId = _userManager.GetUserId(httpContext.User);
            var userName = httpContext.User?.Identity?.Name ?? "Unknown";
            var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            var userAgent = httpContext.Request.Headers["User-Agent"].ToString();
            var timestamp = DateTime.UtcNow;

            // SECURITY: Log admin action with full context
            _logger.LogWarning(
                "ADMIN_AUDIT: Action={Action}, Admin={UserName} (ID: {UserId}), Details={Details}, Target={TargetResource}, IP={IPAddress}, UserAgent={UserAgent}, Timestamp={Timestamp}",
                action,
                userName,
                userId ?? "Unknown",
                details,
                targetResource ?? "N/A",
                ipAddress,
                userAgent,
                timestamp
            );

            // In a production system, you might also write to a database table for audit trail
            // await _auditRepository.LogAdminActionAsync(...);
            
            await Task.CompletedTask; // Make async method complete
        }

        /// <summary>
        /// Log admin access to sensitive resources
        /// </summary>
        public async Task LogAdminAccessAsync(string resource, string action)
        {
            await LogAdminActionAsync($"ACCESS_{action}", $"Accessed {resource}", resource);
        }

        /// <summary>
        /// Log admin data modification
        /// </summary>
        public async Task LogAdminModificationAsync(string entityType, string entityId, string modificationType, object? oldValue = null, object? newValue = null)
        {
            var details = $"{modificationType} on {entityType} (ID: {entityId})";
            if (oldValue != null && newValue != null)
            {
                details += $" - Changed from {oldValue} to {newValue}";
            }
            await LogAdminActionAsync($"MODIFY_{modificationType}", details, $"{entityType}:{entityId}");
        }

        /// <summary>
        /// Log admin deletion
        /// </summary>
        public async Task LogAdminDeletionAsync(string entityType, string entityId, string? details = null)
        {
            var deletionDetails = $"Deleted {entityType} (ID: {entityId})";
            if (!string.IsNullOrEmpty(details))
            {
                deletionDetails += $" - {details}";
            }
            await LogAdminActionAsync("DELETE", deletionDetails, $"{entityType}:{entityId}");
        }
    }
}
