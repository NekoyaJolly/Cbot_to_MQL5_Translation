using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Prometheus;

namespace Bridge
{
    /// <summary>
    /// Trade order data model
    /// Note: Numeric fields are stored as strings to:
    /// 1. Preserve exact formatting from cBot (invariant culture)
    /// 2. Avoid floating-point precision issues
    /// 3. Pass through to MT5 EA without re-formatting
    /// The Bridge acts as a message broker and doesn't perform numeric operations.
    /// </summary>
    public class TradeOrder
    {
        public string Id { get; set; }
        public string SourceId { get; set; } // Unique identifier from master (PositionId/OrderId)
        public string EventType { get; set; }
        public DateTime Timestamp { get; set; }
        public long? PositionId { get; set; }
        public long? OrderId { get; set; }
        public string Symbol { get; set; }
        public string Direction { get; set; }
        public string OrderType { get; set; }
        public string Volume { get; set; } // String to preserve exact formatting from cBot
        public string EntryPrice { get; set; }
        public string TargetPrice { get; set; }
        public string StopLoss { get; set; }
        public string TakeProfit { get; set; }
        public string ClosingPrice { get; set; }
        public string NetProfit { get; set; }
        public string Comment { get; set; }
        public bool Processed { get; set; }
        public DateTime? ProcessedAt { get; set; }
    }

    /// <summary>
    /// Ticket mapping request model
    /// </summary>
    public class TicketMappingRequest
    {
        public string SourceTicket { get; set; }
        public string SlaveTicket { get; set; }
        public string Symbol { get; set; }
        public string Lots { get; set; }
    }

    /// <summary>
    /// API Controller for order management
    /// </summary>
    [ApiController]
    [Route("api")]
    public class OrdersController : ControllerBase
    {
        private readonly PersistentOrderQueueManager _queueManager;
        private readonly ILogger<OrdersController> _logger;
        private static readonly DateTime ProcessStartTime = DateTime.UtcNow;

        public OrdersController(PersistentOrderQueueManager queueManager, ILogger<OrdersController> logger)
        {
            _queueManager = queueManager;
            _logger = logger;
        }

        /// <summary>
        /// Receive order from cTrader
        /// </summary>
        [HttpPost("orders")]
        public IActionResult ReceiveOrder([FromBody] TradeOrder order)
        {
            try
            {
                // Validate and sanitize input
                if (order == null)
                    return BadRequest(new { Error = "Order data is required" });
                
                // Validate required fields
                if (string.IsNullOrWhiteSpace(order.EventType))
                    return BadRequest(new { Error = "EventType is required" });
                
                if (string.IsNullOrWhiteSpace(order.Symbol))
                    return BadRequest(new { Error = "Symbol is required" });
                
                // Validate SourceId is required (for idempotency)
                if (string.IsNullOrWhiteSpace(order.SourceId))
                    return BadRequest(new { Error = "SourceId is required" });
                
                // Validate string lengths to prevent excessive data
                if (order.EventType?.Length > 50)
                    return BadRequest(new { Error = "EventType is too long" });
                
                if (order.Symbol?.Length > 20)
                    return BadRequest(new { Error = "Symbol is too long" });
                
                if (order.Comment?.Length > 500)
                    order.Comment = order.Comment.Substring(0, 500); // Truncate instead of rejecting
                
                // Sanitize string properties to prevent injection attacks
                order.EventType = SanitizeInput(order.EventType);
                order.Symbol = SanitizeInput(order.Symbol);
                if (!string.IsNullOrEmpty(order.Direction))
                    order.Direction = SanitizeInput(order.Direction);
                if (!string.IsNullOrEmpty(order.OrderType))
                    order.OrderType = SanitizeInput(order.OrderType);
                if (!string.IsNullOrEmpty(order.Comment))
                    order.Comment = SanitizeInput(order.Comment);
                
                // Validate EventType is one of expected values (case-sensitive)
                var validEventTypes = new HashSet<string>(StringComparer.Ordinal)
                {
                    "POSITION_OPENED", "POSITION_CLOSED", "POSITION_MODIFIED",
                    "PENDING_ORDER_CREATED", "PENDING_ORDER_CANCELLED", "PENDING_ORDER_FILLED"
                };
                
                if (!validEventTypes.Contains(order.EventType))
                {
                    // Don't log the actual invalid value to prevent log forging
                    _logger.LogWarning("Invalid EventType received from request");
                    return BadRequest(new { Error = "Invalid EventType" });
                }
                
                var orderId = _queueManager.AddOrder(order);
                // Log without user-provided value to prevent log forging
                // EventType is validated above to be one of the safe values
                _logger.LogInformation("Order received and queued: {OrderId}", orderId);
                return Ok(new { OrderId = orderId, Status = "Queued" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving order");
                return StatusCode(500, new { Error = "Internal server error" });
            }
        }

        /// <summary>
        /// Get pending orders for MT5
        /// </summary>
        [HttpGet("orders/pending")]
        public IActionResult GetPendingOrders([FromQuery] int maxCount = 10)
        {
            try
            {
                // Validate maxCount to prevent abuse
                if (maxCount < 1)
                    maxCount = 1;
                if (maxCount > 100)
                    maxCount = 100;
                
                var orders = _queueManager.GetPendingOrders(maxCount);
                return Ok(orders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pending orders");
                return StatusCode(500, new { Error = "Internal server error" });
            }
        }

        /// <summary>
        /// Mark order as processed by MT5
        /// </summary>
        [HttpPost("orders/{orderId}/processed")]
        public IActionResult MarkProcessed(string orderId)
        {
            try
            {
                // Validate and sanitize orderId
                if (string.IsNullOrEmpty(orderId))
                    return BadRequest(new { Error = "Order ID is required" });
                
                orderId = SanitizeInput(orderId);
                
                var success = _queueManager.MarkAsProcessed(orderId);
                if (success)
                {
                    // Use structured logging with sanitized value
                    _logger.LogInformation("Order {OrderId} marked as processed", orderId);
                    return Ok(new { Status = "Processed" });
                }
                return NotFound(new { Error = "Order not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking order as processed");
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Get order by ID
        /// </summary>
        [HttpGet("orders/{orderId}")]
        public IActionResult GetOrder(string orderId)
        {
            try
            {
                // Validate and sanitize orderId
                if (string.IsNullOrWhiteSpace(orderId))
                    return BadRequest(new { Error = "Order ID is required" });
                
                if (orderId.Length > 20)
                    return BadRequest(new { Error = "Order ID is invalid" });
                
                orderId = SanitizeInput(orderId);
                
                var order = _queueManager.GetOrder(orderId);
                if (order != null)
                {
                    return Ok(order);
                }
                return NotFound(new { Error = "Order not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting order");
                return StatusCode(500, new { Error = "Internal server error" });
            }
        }

        /// <summary>
        /// Get queue statistics
        /// </summary>
        [HttpGet("statistics")]
        public IActionResult GetStatistics()
        {
            try
            {
                var stats = _queueManager.GetStatistics();
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting statistics");
                return StatusCode(500, new { Error = "Internal server error" });
            }
        }

        /// <summary>
        /// Health check endpoint
        /// </summary>
        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow });
        }

        /// <summary>
        /// Get system status and metrics
        /// </summary>
        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            try
            {
                var stats = _queueManager.GetStatistics();
                var status = new
                {
                    Status = "Running",
                    Timestamp = DateTime.UtcNow,
                    Version = "1.0.0",
                    QueueStatistics = stats,
                    Uptime = DateTime.UtcNow - ProcessStartTime
                };
                return Ok(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting status");
                return StatusCode(500, new { Error = "Internal server error" });
            }
        }

        /// <summary>
        /// Get queue details
        /// </summary>
        [HttpGet("queue")]
        public IActionResult GetQueue([FromQuery] int maxCount = 100)
        {
            try
            {
                // Validate maxCount
                if (maxCount < 1)
                    maxCount = 1;
                if (maxCount > 100)
                    maxCount = 100;

                var pendingOrders = _queueManager.GetPendingOrders(maxCount);
                return Ok(new { Count = pendingOrders.Count, Orders = pendingOrders });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting queue");
                return StatusCode(500, new { Error = "Internal server error" });
            }
        }

        /// <summary>
        /// Retry a specific order
        /// </summary>
        [HttpPost("retry/{orderId}")]
        public IActionResult RetryOrder(string orderId)
        {
            try
            {
                // Validate and sanitize orderId
                if (string.IsNullOrWhiteSpace(orderId))
                    return BadRequest(new { Error = "Order ID is required" });

                if (orderId.Length > 50)
                    return BadRequest(new { Error = "Order ID is invalid" });

                orderId = SanitizeInput(orderId);

                var order = _queueManager.GetOrder(orderId);
                if (order == null)
                    return NotFound(new { Error = "Order not found" });

                if (order.Processed)
                    return BadRequest(new { Error = "Order already processed" });

                // Schedule for immediate retry
                var success = _queueManager.IncrementRetryCount(orderId, TimeSpan.Zero);
                if (success)
                {
                    // orderId is already sanitized above, but use a safe log format
                    _logger.LogInformation("Order scheduled for retry");
                    return Ok(new { Status = "Scheduled for retry" });
                }
                return StatusCode(500, new { Error = "Failed to schedule retry" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrying order");
                return StatusCode(500, new { Error = "Internal server error" });
            }
        }

        /// <summary>
        /// Add ticket mapping from MT5
        /// </summary>
        [HttpPost("ticket-map")]
        public IActionResult AddTicketMapping([FromBody] TicketMappingRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.SourceTicket) || 
                    string.IsNullOrWhiteSpace(request.SlaveTicket))
                    return BadRequest(new { Error = "SourceTicket and SlaveTicket are required" });

                _queueManager.AddTicketMapping(request.SourceTicket, request.SlaveTicket, 
                    request.Symbol ?? "", request.Lots ?? "");
                return Ok(new { Status = "Mapping added" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding ticket mapping");
                return StatusCode(500, new { Error = "Internal server error" });
            }
        }

        /// <summary>
        /// Get slave ticket by source ticket
        /// </summary>
        [HttpGet("ticket-map/{sourceTicket}")]
        public IActionResult GetTicketMapping(string sourceTicket)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourceTicket))
                    return BadRequest(new { Error = "Source ticket is required" });

                var slaveTicket = _queueManager.GetSlaveTicket(sourceTicket);
                if (slaveTicket != null)
                    return Ok(new { SourceTicket = sourceTicket, SlaveTicket = slaveTicket });
                
                return NotFound(new { Error = "Mapping not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ticket mapping");
                return StatusCode(500, new { Error = "Internal server error" });
            }
        }

        /// <summary>
        /// Sanitize user input to prevent injection attacks
        /// Removes control characters including newlines, carriage returns, and tabs
        /// </summary>
        private static string SanitizeInput(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input ?? string.Empty;
            
            // Remove all control characters (including newlines, tabs, etc.)
            var sanitized = new StringBuilder(input.Length);
            foreach (char c in input)
            {
                // Allow only printable characters (ASCII 32-126) and common symbols
                if (c >= 32 && c <= 126)
                    sanitized.Append(c);
            }
            
            return sanitized.ToString();
        }
    }

    /// <summary>
    /// Bridge Server Program
    /// </summary>
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog((context, services, configuration) => configuration
                    .ReadFrom.Configuration(context.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext()
                    .WriteTo.Console()
                    .WriteTo.File("logs/bridge-.log", rollingInterval: Serilog.RollingInterval.Day))
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureServices((context, services) =>
                    {
                        services.AddControllers()
                            .AddJsonOptions(options =>
                            {
                                // Limit JSON depth to prevent stack overflow from deeply nested JSON
                                options.JsonSerializerOptions.MaxDepth = 32;
                            });
                        
                        // Register alert service as singleton
                        services.AddSingleton<AlertService>();
                        
                        // Register persistent queue manager as singleton
                        var databasePath = context.Configuration["Bridge:DatabasePath"] ?? "bridge.db";
                        services.AddSingleton<PersistentOrderQueueManager>(sp => 
                        {
                            var logger = sp.GetRequiredService<ILogger<PersistentOrderQueueManager>>();
                            return new PersistentOrderQueueManager(databasePath, logger);
                        });
                        
                        services.AddCors(options =>
                        {
                            options.AddDefaultPolicy(builder =>
                            {
                                // More restrictive CORS - adjust based on your needs
                                builder.WithOrigins("http://localhost", "http://127.0.0.1")
                                       .AllowAnyMethod()
                                       .AllowAnyHeader();
                            });
                        });

                        // Add background service for cleanup
                        services.AddHostedService<CleanupService>();
                        
                        // Get listen URL from configuration
                        var listenUrl = context.Configuration["Bridge:ListenUrl"] ?? "http://0.0.0.0:5000";
                        webBuilder.UseUrls(listenUrl);
                    });

                    webBuilder.Configure((context, app) =>
                    {
                        app.UseCors();
                        
                        // Add rate limiting middleware
                        app.UseMiddleware<RateLimitMiddleware>();
                        
                        // Add API key authentication middleware
                        app.UseMiddleware<ApiKeyAuthMiddleware>();
                        
                        // Add Prometheus metrics endpoint
                        app.UseMetricServer();
                        app.UseHttpMetrics();
                        
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapControllers();
                        });
                    });
                });
    }

    /// <summary>
    /// Background service to cleanup old processed orders
    /// </summary>
    public class CleanupService : BackgroundService
    {
        private readonly PersistentOrderQueueManager _queueManager;
        private readonly ILogger<CleanupService> _logger;
        private readonly IConfiguration _configuration;

        public CleanupService(PersistentOrderQueueManager queueManager, ILogger<CleanupService> logger, IConfiguration configuration)
        {
            _queueManager = queueManager;
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var maxAge = _configuration.GetValue("Bridge:MaxOrderAge", TimeSpan.FromHours(1));
            var interval = _configuration.GetValue("Bridge:CleanupInterval", TimeSpan.FromMinutes(10));
            
            _logger.LogInformation("CleanupService started: MaxAge={MaxAge}, Interval={Interval}", maxAge, interval);
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _queueManager.CleanupOldOrders(maxAge);
                    await Task.Delay(interval, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in cleanup service");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
        }
    }
}
