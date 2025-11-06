using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Bridge
{
    /// <summary>
    /// Middleware for rate limiting requests
    /// </summary>
    public class RateLimitMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RateLimitMiddleware> _logger;
        private readonly ConcurrentDictionary<string, RateLimitInfo> _rateLimitStore = new();

        public RateLimitMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<RateLimitMiddleware> logger)
        {
            _next = next;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var enabled = _configuration.GetValue("Bridge:RateLimiting:Enabled", false);
            if (!enabled)
            {
                await _next(context);
                return;
            }

            // Get client IP
            var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // Check IP whitelist
            var whitelist = _configuration.GetSection("Bridge:RateLimiting:WhitelistedIPs").Get<string[]>();
            if (whitelist != null && Array.Exists(whitelist, ip => ip == clientIp))
            {
                await _next(context);
                return;
            }

            // Allow health and metrics endpoints without rate limiting
            if (context.Request.Path.StartsWithSegments("/api/health") ||
                context.Request.Path.StartsWithSegments("/metrics"))
            {
                await _next(context);
                return;
            }

            var maxRequests = _configuration.GetValue("Bridge:RateLimiting:MaxRequestsPerMinute", 60);
            var now = DateTime.UtcNow;

            var rateLimitInfo = _rateLimitStore.GetOrAdd(clientIp, _ => new RateLimitInfo());

            bool exceedsLimit = false;
            lock (rateLimitInfo)
            {
                // Clean old entries
                rateLimitInfo.Requests.RemoveAll(time => now - time > TimeSpan.FromMinutes(1));

                // Check rate limit
                if (rateLimitInfo.Requests.Count >= maxRequests)
                {
                    exceedsLimit = true;
                }
                else
                {
                    rateLimitInfo.Requests.Add(now);
                }
            }

            if (exceedsLimit)
            {
                _logger.LogWarning("Rate limit exceeded for {ClientIp}", clientIp);
                context.Response.StatusCode = 429; // Too Many Requests
                await context.Response.WriteAsJsonAsync(new { Error = "Rate limit exceeded" });
                return;
            }

            await _next(context);
        }

        private class RateLimitInfo
        {
            public System.Collections.Generic.List<DateTime> Requests { get; } = new();
        }
    }
}
