using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Threading.Tasks;

namespace HikingStore.Middleware
{
    /// <summary>
    /// Rate limiting middleware to prevent brute force attacks and abuse
    /// </summary>
    public class RateLimitingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IMemoryCache _cache;
        private readonly ILogger<RateLimitingMiddleware> _logger;

        public RateLimitingMiddleware(
            RequestDelegate next,
            IMemoryCache cache,
            ILogger<RateLimitingMiddleware> logger)
        {
            _next = next;
            _cache = cache;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // SECURITY: Apply rate limiting only to POST requests on authentication endpoints
            // GET requests (loading pages) should not be rate limited
            if (IsAuthenticationEndpoint(context.Request.Path) && context.Request.Method == "POST")
            {
                var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                
                // SECURITY: Limit login attempts to 5 per 15 minutes per IP
                if (context.Request.Path.StartsWithSegments("/Identity/Account/Login"))
                {
                    var cacheKey = $"rate_limit_login_{ipAddress}";
                    
                    if (!_cache.TryGetValue(cacheKey, out int attemptCount))
                    {
                        attemptCount = 0;
                    }

                    if (attemptCount >= 5)
                    {
                        _logger.LogWarning("SECURITY: Rate limit exceeded for login attempts. IP: {IP}, Path: {Path}", 
                            ipAddress, context.Request.Path);
                        
                        if (!context.Response.HasStarted)
                        {
                            context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                            context.Response.Headers.Append("Retry-After", "900"); // 15 minutes
                            context.Response.ContentType = "text/plain";
                            await context.Response.WriteAsync("Too many login attempts. Please try again later.");
                        }
                        return;
                    }

                    // Increment attempt count only on POST (actual login attempt)
                    _cache.Set(cacheKey, attemptCount + 1, TimeSpan.FromMinutes(15));
                }

                // SECURITY: Limit registration attempts to 3 per hour per IP
                if (context.Request.Path.StartsWithSegments("/Identity/Account/Register"))
                {
                    var registerKey = $"rate_limit_register_{ipAddress}";
                    
                    if (!_cache.TryGetValue(registerKey, out int registerCount))
                    {
                        registerCount = 0;
                    }

                    if (registerCount >= 3)
                    {
                        _logger.LogWarning("SECURITY: Rate limit exceeded for registration attempts. IP: {IP}", ipAddress);
                        
                        if (!context.Response.HasStarted)
                        {
                            context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                            context.Response.Headers.Append("Retry-After", "3600"); // 1 hour
                            context.Response.ContentType = "text/plain";
                            await context.Response.WriteAsync("Too many registration attempts. Please try again later.");
                        }
                        return;
                    }

                    // Increment attempt count only on POST (actual registration attempt)
                    _cache.Set(registerKey, registerCount + 1, TimeSpan.FromHours(1));
                }
            }

            await _next(context);
        }

        private bool IsAuthenticationEndpoint(PathString path)
        {
            return path.StartsWithSegments("/Identity/Account/Login") ||
                   path.StartsWithSegments("/Identity/Account/Register") ||
                   path.StartsWithSegments("/Identity/Account/ForgotPassword") ||
                   path.StartsWithSegments("/Identity/Account/ResendEmailConfirmation");
        }
    }
}
