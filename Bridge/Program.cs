using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bridge
{
    /// <summary>
    /// Trade order data model
    /// </summary>
    public class TradeOrder
    {
        public string Id { get; set; }
        public string EventType { get; set; }
        public DateTime Timestamp { get; set; }
        public long? PositionId { get; set; }
        public long? OrderId { get; set; }
        public string Symbol { get; set; }
        public string Direction { get; set; }
        public string OrderType { get; set; }
        public double? Volume { get; set; }
        public double? EntryPrice { get; set; }
        public double? TargetPrice { get; set; }
        public double? StopLoss { get; set; }
        public double? TakeProfit { get; set; }
        public double? ClosingPrice { get; set; }
        public double? NetProfit { get; set; }
        public string Comment { get; set; }
        public bool Processed { get; set; }
        public DateTime? ProcessedAt { get; set; }
    }

    /// <summary>
    /// Thread-safe order queue manager
    /// </summary>
    public class OrderQueueManager
    {
        private readonly ConcurrentDictionary<string, TradeOrder> _orders = new ConcurrentDictionary<string, TradeOrder>();
        private readonly ConcurrentQueue<string> _orderQueue = new ConcurrentQueue<string>();
        private long _orderCounter = 0;

        public string AddOrder(TradeOrder order)
        {
            order.Id = Interlocked.Increment(ref _orderCounter).ToString();
            order.Processed = false;
            order.Timestamp = DateTime.UtcNow;

            _orders[order.Id] = order;
            _orderQueue.Enqueue(order.Id);

            return order.Id;
        }

        public List<TradeOrder> GetPendingOrders(int maxCount = 10)
        {
            var orders = new List<TradeOrder>();
            var processedIds = new List<string>();

            while (orders.Count < maxCount && _orderQueue.TryPeek(out var orderId))
            {
                if (_orders.TryGetValue(orderId, out var order) && !order.Processed)
                {
                    orders.Add(order);
                    _orderQueue.TryDequeue(out _);
                    processedIds.Add(orderId);
                }
                else
                {
                    _orderQueue.TryDequeue(out _);
                }
            }

            return orders;
        }

        public bool MarkAsProcessed(string orderId)
        {
            if (_orders.TryGetValue(orderId, out var order))
            {
                order.Processed = true;
                order.ProcessedAt = DateTime.UtcNow;
                return true;
            }
            return false;
        }

        public TradeOrder GetOrder(string orderId)
        {
            _orders.TryGetValue(orderId, out var order);
            return order;
        }

        public Dictionary<string, object> GetStatistics()
        {
            var now = DateTime.UtcNow;
            var last5Minutes = now.AddMinutes(-5);

            return new Dictionary<string, object>
            {
                { "TotalOrders", _orders.Count },
                { "PendingOrders", _orders.Values.Count(o => !o.Processed) },
                { "ProcessedOrders", _orders.Values.Count(o => o.Processed) },
                { "OrdersLast5Min", _orders.Values.Count(o => o.Timestamp >= last5Minutes) }
            };
        }

        public void CleanupOldOrders(TimeSpan maxAge)
        {
            var cutoffTime = DateTime.UtcNow - maxAge;
            var oldOrders = _orders.Where(kvp => kvp.Value.Processed && kvp.Value.ProcessedAt < cutoffTime)
                                   .Select(kvp => kvp.Key)
                                   .ToList();

            foreach (var orderId in oldOrders)
            {
                _orders.TryRemove(orderId, out _);
            }
        }
    }

    /// <summary>
    /// API Controller for order management
    /// </summary>
    [ApiController]
    [Route("api")]
    public class OrdersController : ControllerBase
    {
        private readonly OrderQueueManager _queueManager;
        private readonly ILogger<OrdersController> _logger;

        public OrdersController(OrderQueueManager queueManager, ILogger<OrdersController> logger)
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
                var orderId = _queueManager.AddOrder(order);
                _logger.LogInformation($"Order received: {order.EventType} for {order.Symbol}");
                return Ok(new { OrderId = orderId, Status = "Queued" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving order");
                return StatusCode(500, new { Error = ex.Message });
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
                var orders = _queueManager.GetPendingOrders(maxCount);
                return Ok(orders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pending orders");
                return StatusCode(500, new { Error = ex.Message });
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
                var success = _queueManager.MarkAsProcessed(orderId);
                if (success)
                {
                    _logger.LogInformation($"Order {orderId} marked as processed");
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
                return StatusCode(500, new { Error = ex.Message });
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
                return StatusCode(500, new { Error = ex.Message });
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
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureServices(services =>
                    {
                        services.AddControllers();
                        services.AddSingleton<OrderQueueManager>();
                        services.AddCors(options =>
                        {
                            options.AddDefaultPolicy(builder =>
                            {
                                builder.AllowAnyOrigin()
                                       .AllowAnyMethod()
                                       .AllowAnyHeader();
                            });
                        });

                        // Add background service for cleanup
                        services.AddHostedService<CleanupService>();
                    });

                    webBuilder.Configure((context, app) =>
                    {
                        app.UseCors();
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapControllers();
                        });
                    });

                    webBuilder.UseUrls("http://0.0.0.0:5000");
                });
    }

    /// <summary>
    /// Background service to cleanup old processed orders
    /// </summary>
    public class CleanupService : BackgroundService
    {
        private readonly OrderQueueManager _queueManager;
        private readonly ILogger<CleanupService> _logger;

        public CleanupService(OrderQueueManager queueManager, ILogger<CleanupService> logger)
        {
            _queueManager = queueManager;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _queueManager.CleanupOldOrders(TimeSpan.FromHours(1));
                    await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in cleanup service");
                }
            }
        }
    }
}
