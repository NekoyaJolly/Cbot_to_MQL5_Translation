using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Bridge
{
    /// <summary>
    /// Middleware for API Key authentication
    /// </summary>
    public class ApiKeyAuthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ApiKeyAuthMiddleware> _logger;
        private const string API_KEY_HEADER = "X-API-KEY";

        public ApiKeyAuthMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<ApiKeyAuthMiddleware> logger)
        {
            _next = next;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var configuredApiKey = _configuration["Bridge:ApiKey"];
            
            // If no API key is configured, skip authentication
            if (string.IsNullOrEmpty(configuredApiKey))
            {
                await _next(context);
                return;
            }

            // Allow health check endpoint without authentication
            if (context.Request.Path.StartsWithSegments("/api/health"))
            {
                await _next(context);
                return;
            }

            // Allow metrics endpoint without authentication
            if (context.Request.Path.StartsWithSegments("/metrics"))
            {
                await _next(context);
                return;
            }

            // Check for API key in header
            if (!context.Request.Headers.TryGetValue(API_KEY_HEADER, out var apiKey))
            {
                _logger.LogWarning("API request without API key from {RemoteIp}", context.Connection.RemoteIpAddress);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { Error = "API Key is required" });
                return;
            }

            // Validate API key
            if (apiKey != configuredApiKey)
            {
                _logger.LogWarning("Invalid API key attempt from {RemoteIp}", context.Connection.RemoteIpAddress);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { Error = "Invalid API Key" });
                return;
            }

            await _next(context);
        }
    }
}
