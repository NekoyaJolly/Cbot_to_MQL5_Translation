using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Prometheus;

namespace Bridge
{
    /// <summary>
    /// Persistent order queue manager using SQLite
    /// </summary>
    public class PersistentOrderQueueManager : IDisposable
    {
        private readonly string _connectionString;
        private readonly ILogger<PersistentOrderQueueManager> _logger;
        private readonly object _lock = new object();
        private readonly int _maxRetryCount;

        // Prometheus metrics
        private static readonly Counter OrdersReceivedCounter = Metrics.CreateCounter(
            "bridge_orders_received_total", 
            "Total number of orders received");
        
        private static readonly Counter OrdersProcessedCounter = Metrics.CreateCounter(
            "bridge_orders_processed_total", 
            "Total number of orders marked as processed");
        
        private static readonly Gauge PendingOrdersGauge = Metrics.CreateGauge(
            "bridge_orders_pending", 
            "Current number of pending orders");
        
        private static readonly Counter OrdersFailedCounter = Metrics.CreateCounter(
            "bridge_orders_failed_total", 
            "Total number of orders that failed after max retries");
        
        private static readonly Gauge RetryQueueSizeGauge = Metrics.CreateGauge(
            "bridge_retry_queue_size", 
            "Current number of orders waiting for retry");
        
        private static readonly Counter DuplicateOrdersCounter = Metrics.CreateCounter(
            "bridge_duplicate_orders_total", 
            "Total number of duplicate orders rejected");
        
        private static readonly Histogram OrderProcessingDuration = Metrics.CreateHistogram(
            "bridge_order_processing_duration_seconds",
            "Duration of order processing",
            new HistogramConfiguration
            {
                Buckets = Histogram.LinearBuckets(start: 0.1, width: 0.5, count: 10)
            });

        public PersistentOrderQueueManager(string databasePath, ILogger<PersistentOrderQueueManager> logger, int maxRetryCount = 3)
        {
            _connectionString = $"Data Source={databasePath}";
            _logger = logger;
            _maxRetryCount = maxRetryCount;
            InitializeDatabase();
        }

        /// <summary>
        /// Sanitize string for logging to prevent log forging
        /// </summary>
        private static string SanitizeForLog(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;
            
            // Remove control characters to prevent log forging
            var sanitized = new StringBuilder(input.Length);
            foreach (char c in input)
            {
                // Allow all characters except control characters
                if (!char.IsControl(c))
                    sanitized.Append(c);
            }
            
            // Limit length to prevent log bloat
            var result = sanitized.ToString();
            return result.Length > 100 ? result.Substring(0, 100) + "..." : result;
        }

        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS Orders (
                    Id TEXT PRIMARY KEY,
                    SourceId TEXT NOT NULL,
                    EventType TEXT NOT NULL,
                    Timestamp TEXT NOT NULL,
                    PositionId INTEGER,
                    OrderId INTEGER,
                    Symbol TEXT NOT NULL,
                    Direction TEXT,
                    OrderType TEXT,
                    Volume TEXT,
                    EntryPrice TEXT,
                    TargetPrice TEXT,
                    StopLoss TEXT,
                    TakeProfit TEXT,
                    ClosingPrice TEXT,
                    NetProfit TEXT,
                    Comment TEXT,
                    Processed INTEGER NOT NULL DEFAULT 0,
                    ProcessedAt TEXT,
                    CreatedAt TEXT NOT NULL,
                    RetryCount INTEGER NOT NULL DEFAULT 0,
                    LastRetryAt TEXT,
                    NextRetryAt TEXT,
                    Processing INTEGER NOT NULL DEFAULT 0,
                    ProcessingBy TEXT,
                    ProcessingAt TEXT,
                    UNIQUE(SourceId, EventType)
                );
                
                CREATE INDEX IF NOT EXISTS idx_orders_processed ON Orders(Processed);
                CREATE INDEX IF NOT EXISTS idx_orders_sourceid ON Orders(SourceId);
                CREATE INDEX IF NOT EXISTS idx_orders_timestamp ON Orders(Timestamp);
                CREATE INDEX IF NOT EXISTS idx_orders_retry ON Orders(NextRetryAt) WHERE Processed = 0;
                CREATE INDEX IF NOT EXISTS idx_orders_processing ON Orders(Processing) WHERE Processed = 0;
                
                CREATE TABLE IF NOT EXISTS TicketMap (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SourceTicket TEXT NOT NULL,
                    SlaveTicket TEXT NOT NULL,
                    Symbol TEXT NOT NULL,
                    Lots TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UNIQUE(SourceTicket)
                );
                
                CREATE INDEX IF NOT EXISTS idx_ticketmap_source ON TicketMap(SourceTicket);
                CREATE INDEX IF NOT EXISTS idx_ticketmap_slave ON TicketMap(SlaveTicket);
            ";
            command.ExecuteNonQuery();

            // Perform migration for existing databases
            MigrateDatabase(connection);

            _logger.LogInformation("Database initialized at {DatabasePath}", _connectionString);
        }

        private void MigrateDatabase(SqliteConnection connection)
        {
            try
            {
                // Check if Processing column exists
                var command = connection.CreateCommand();
                command.CommandText = "PRAGMA table_info(Orders)";
                using var reader = command.ExecuteReader();
                
                bool hasProcessing = false;
                bool hasProcessingBy = false;
                bool hasProcessingAt = false;
                
                while (reader.Read())
                {
                    var columnName = reader.GetString(1);
                    if (columnName == "Processing") hasProcessing = true;
                    if (columnName == "ProcessingBy") hasProcessingBy = true;
                    if (columnName == "ProcessingAt") hasProcessingAt = true;
                }
                reader.Close();

                // Add missing columns
                if (!hasProcessing)
                {
                    _logger.LogInformation("Adding Processing column to Orders table");
                    command = connection.CreateCommand();
                    command.CommandText = "ALTER TABLE Orders ADD COLUMN Processing INTEGER NOT NULL DEFAULT 0";
                    command.ExecuteNonQuery();
                }

                if (!hasProcessingBy)
                {
                    _logger.LogInformation("Adding ProcessingBy column to Orders table");
                    command = connection.CreateCommand();
                    command.CommandText = "ALTER TABLE Orders ADD COLUMN ProcessingBy TEXT";
                    command.ExecuteNonQuery();
                }

                if (!hasProcessingAt)
                {
                    _logger.LogInformation("Adding ProcessingAt column to Orders table");
                    command = connection.CreateCommand();
                    command.CommandText = "ALTER TABLE Orders ADD COLUMN ProcessingAt TEXT";
                    command.ExecuteNonQuery();
                }

                // Create index if needed
                if (!hasProcessing)
                {
                    command = connection.CreateCommand();
                    command.CommandText = "CREATE INDEX IF NOT EXISTS idx_orders_processing ON Orders(Processing) WHERE Processed = 0";
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during database migration - may be a new database");
            }
        }

        public string AddOrder(TradeOrder order)
        {
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                // Generate ID if not provided
                if (string.IsNullOrEmpty(order.Id))
                {
                    order.Id = Guid.NewGuid().ToString();
                }

                // Check for idempotency using SourceId + EventType
                if (!string.IsNullOrEmpty(order.SourceId))
                {
                    var checkCommand = connection.CreateCommand();
                    checkCommand.CommandText = "SELECT Id FROM Orders WHERE SourceId = @SourceId AND EventType = @EventType";
                    checkCommand.Parameters.AddWithValue("@SourceId", order.SourceId);
                    checkCommand.Parameters.AddWithValue("@EventType", order.EventType);
                    
                    var existingId = checkCommand.ExecuteScalar() as string;
                    if (existingId != null)
                    {
                        _logger.LogInformation("Order with SourceId {SourceId} and EventType {EventType} already exists, skipping duplicate", 
                            SanitizeForLog(order.SourceId), SanitizeForLog(order.EventType));
                        DuplicateOrdersCounter.Inc();
                        return existingId;
                    }
                }

                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO Orders (
                        Id, SourceId, EventType, Timestamp, PositionId, OrderId, Symbol, 
                        Direction, OrderType, Volume, EntryPrice, TargetPrice, 
                        StopLoss, TakeProfit, ClosingPrice, NetProfit, Comment, 
                        Processed, ProcessedAt, CreatedAt, RetryCount, LastRetryAt, NextRetryAt,
                        Processing, ProcessingBy, ProcessingAt
                    ) VALUES (
                        @Id, @SourceId, @EventType, @Timestamp, @PositionId, @OrderId, @Symbol,
                        @Direction, @OrderType, @Volume, @EntryPrice, @TargetPrice,
                        @StopLoss, @TakeProfit, @ClosingPrice, @NetProfit, @Comment,
                        0, NULL, @CreatedAt, @RetryCount, @LastRetryAt, @NextRetryAt,
                        0, NULL, NULL
                    )
                ";

                command.Parameters.AddWithValue("@Id", order.Id);
                command.Parameters.AddWithValue("@SourceId", order.SourceId ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@EventType", order.EventType);
                command.Parameters.AddWithValue("@Timestamp", order.Timestamp.ToString("o"));
                command.Parameters.AddWithValue("@PositionId", order.PositionId ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@OrderId", order.OrderId ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Symbol", order.Symbol ?? "");
                command.Parameters.AddWithValue("@Direction", order.Direction ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@OrderType", order.OrderType ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Volume", order.Volume ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@EntryPrice", order.EntryPrice ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@TargetPrice", order.TargetPrice ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@StopLoss", order.StopLoss ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@TakeProfit", order.TakeProfit ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@ClosingPrice", order.ClosingPrice ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@NetProfit", order.NetProfit ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Comment", order.Comment ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow.ToString("o"));
                command.Parameters.AddWithValue("@RetryCount", 0);
                command.Parameters.AddWithValue("@LastRetryAt", DBNull.Value);
                command.Parameters.AddWithValue("@NextRetryAt", DBNull.Value);

                command.ExecuteNonQuery();

                _logger.LogInformation("Order added: Id={Id}, SourceId={SourceId}, EventType={EventType}", 
                    SanitizeForLog(order.Id), SanitizeForLog(order.SourceId), SanitizeForLog(order.EventType));

                // Increment metrics
                OrdersReceivedCounter.Inc();
                UpdatePendingOrdersGauge();

                return order.Id;
            }
        }

        /// <summary>
        /// Get pending orders with atomic locking to prevent duplicate processing
        /// Uses consumerId to mark which consumer is processing the orders
        /// </summary>
        public List<TradeOrder> GetPendingOrders(int maxCount = 10, string consumerId = null)
        {
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var transaction = connection.BeginTransaction();
                try
                {
                    // If no consumerId provided, generate one
                    if (string.IsNullOrEmpty(consumerId))
                    {
                        consumerId = $"consumer-{Guid.NewGuid():N}"[..18]; // Use 18 chars of hex
                    }

                    var now = DateTime.UtcNow.ToString("o");

                    // First, select orders that are eligible for processing
                    // Eligible orders are:
                    // 1. Not processed (Processed = 0)
                    // 2. Not currently being processed (Processing = 0)
                    // 3. Either no retry scheduled, or retry time has passed
                    var selectCommand = connection.CreateCommand();
                    selectCommand.Transaction = transaction;
                    selectCommand.CommandText = @"
                        SELECT Id FROM Orders 
                        WHERE Processed = 0 
                        AND Processing = 0
                        AND (NextRetryAt IS NULL OR NextRetryAt <= @Now)
                        ORDER BY Timestamp 
                        LIMIT @MaxCount
                    ";
                    selectCommand.Parameters.AddWithValue("@Now", now);
                    selectCommand.Parameters.AddWithValue("@MaxCount", maxCount);

                    var orderIds = new List<string>();
                    using (var reader = selectCommand.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            orderIds.Add(reader.GetString(0));
                        }
                    }

                    if (orderIds.Count == 0)
                    {
                        transaction.Commit();
                        return new List<TradeOrder>();
                    }

                    // Mark selected orders as being processed
                    var updateSql = BuildParameterizedInClause("UPDATE Orders SET Processing = 1, ProcessingBy = @ProcessingBy, ProcessingAt = @ProcessingAt WHERE Id IN (", orderIds.Count, ")");
                    var updateCommand = connection.CreateCommand();
                    updateCommand.Transaction = transaction;
                    updateCommand.CommandText = updateSql;
                    updateCommand.Parameters.AddWithValue("@ProcessingBy", consumerId);
                    updateCommand.Parameters.AddWithValue("@ProcessingAt", now);
                    AddIdParameters(updateCommand, orderIds);
                    updateCommand.ExecuteNonQuery();

                    // Retrieve the marked orders
                    var retrieveSql = BuildParameterizedInClause("SELECT * FROM Orders WHERE Id IN (", orderIds.Count, ") ORDER BY Timestamp");
                    var retrieveCommand = connection.CreateCommand();
                    retrieveCommand.Transaction = transaction;
                    retrieveCommand.CommandText = retrieveSql;
                    AddIdParameters(retrieveCommand, orderIds);

                    var orders = new List<TradeOrder>();
                    using (var reader = retrieveCommand.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            orders.Add(ReadOrder(reader));
                        }
                    }

                    transaction.Commit();

                    _logger.LogInformation("Retrieved {Count} pending orders for consumer {ConsumerId}", 
                        orders.Count, SanitizeForLog(consumerId));

                    return orders;
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    _logger.LogError(ex, "Error getting pending orders");
                    throw;
                }
            }
        }

        /// <summary>
        /// Build a parameterized IN clause SQL string
        /// </summary>
        private static string BuildParameterizedInClause(string prefix, int count, string suffix)
        {
            var parameters = string.Join(",", Enumerable.Range(0, count).Select(i => $"@Id{i}"));
            return $"{prefix}{parameters}{suffix}";
        }

        /// <summary>
        /// Add ID parameters to a command
        /// </summary>
        private static void AddIdParameters(SqliteCommand command, List<string> orderIds)
        {
            for (int i = 0; i < orderIds.Count; i++)
            {
                command.Parameters.AddWithValue($"@Id{i}", orderIds[i]);
            }
        }

        public bool MarkAsProcessed(string orderId)
        {
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    UPDATE Orders 
                    SET Processed = 1, 
                        ProcessedAt = @ProcessedAt,
                        Processing = 0,
                        ProcessingBy = NULL,
                        NextRetryAt = NULL
                    WHERE Id = @Id AND Processed = 0
                ";
                command.Parameters.AddWithValue("@Id", orderId);
                command.Parameters.AddWithValue("@ProcessedAt", DateTime.UtcNow.ToString("o"));

                var rowsAffected = command.ExecuteNonQuery();
                
                if (rowsAffected > 0)
                {
                    _logger.LogInformation("Order marked as processed: Id={Id}", SanitizeForLog(orderId));
                    OrdersProcessedCounter.Inc();
                    UpdatePendingOrdersGauge();
                    return true;
                }
                
                return false;
            }
        }

        public TradeOrder GetOrder(string orderId)
        {
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT * FROM Orders WHERE Id = @Id";
                command.Parameters.AddWithValue("@Id", orderId);

                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    return ReadOrder(reader);
                }

                return null;
            }
        }

        public Dictionary<string, object> GetStatistics()
        {
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var stats = new Dictionary<string, object>();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM Orders";
                stats["TotalOrders"] = Convert.ToInt32(command.ExecuteScalar());

                command.CommandText = "SELECT COUNT(*) FROM Orders WHERE Processed = 0";
                stats["PendingOrders"] = Convert.ToInt32(command.ExecuteScalar());

                command.CommandText = "SELECT COUNT(*) FROM Orders WHERE Processed = 1";
                stats["ProcessedOrders"] = Convert.ToInt32(command.ExecuteScalar());

                var last5Min = DateTime.UtcNow.AddMinutes(-5).ToString("o");
                command.CommandText = "SELECT COUNT(*) FROM Orders WHERE Timestamp >= @Last5Min";
                command.Parameters.AddWithValue("@Last5Min", last5Min);
                stats["OrdersLast5Min"] = Convert.ToInt32(command.ExecuteScalar());

                return stats;
            }
        }

        public void CleanupOldOrders(TimeSpan maxAge)
        {
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var cutoffTime = DateTime.UtcNow - maxAge;
                var command = connection.CreateCommand();
                command.CommandText = @"
                    DELETE FROM Orders 
                    WHERE Processed = 1 AND ProcessedAt < @CutoffTime
                ";
                command.Parameters.AddWithValue("@CutoffTime", cutoffTime.ToString("o"));

                var deleted = command.ExecuteNonQuery();
                if (deleted > 0)
                {
                    _logger.LogInformation("Cleaned up {Count} old orders", deleted);
                }
            }
        }

        private TradeOrder ReadOrder(SqliteDataReader reader)
        {
            return new TradeOrder
            {
                Id = reader.GetString("Id"),
                SourceId = reader.IsDBNull("SourceId") ? null : reader.GetString("SourceId"),
                EventType = reader.GetString("EventType"),
                Timestamp = DateTime.Parse(reader.GetString("Timestamp")),
                PositionId = reader.IsDBNull("PositionId") ? null : reader.GetInt64("PositionId"),
                OrderId = reader.IsDBNull("OrderId") ? null : reader.GetInt64("OrderId"),
                Symbol = reader.GetString("Symbol"),
                Direction = reader.IsDBNull("Direction") ? null : reader.GetString("Direction"),
                OrderType = reader.IsDBNull("OrderType") ? null : reader.GetString("OrderType"),
                Volume = reader.IsDBNull("Volume") ? null : reader.GetString("Volume"),
                EntryPrice = reader.IsDBNull("EntryPrice") ? null : reader.GetString("EntryPrice"),
                TargetPrice = reader.IsDBNull("TargetPrice") ? null : reader.GetString("TargetPrice"),
                StopLoss = reader.IsDBNull("StopLoss") ? null : reader.GetString("StopLoss"),
                TakeProfit = reader.IsDBNull("TakeProfit") ? null : reader.GetString("TakeProfit"),
                ClosingPrice = reader.IsDBNull("ClosingPrice") ? null : reader.GetString("ClosingPrice"),
                NetProfit = reader.IsDBNull("NetProfit") ? null : reader.GetString("NetProfit"),
                Comment = reader.IsDBNull("Comment") ? null : reader.GetString("Comment"),
                Processed = reader.GetInt32("Processed") == 1,
                ProcessedAt = reader.IsDBNull("ProcessedAt") ? null : DateTime.Parse(reader.GetString("ProcessedAt"))
            };
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
        }

        /// <summary>
        /// Add a ticket mapping between source and slave
        /// </summary>
        public void AddTicketMapping(string sourceTicket, string slaveTicket, string symbol, string lots)
        {
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT OR REPLACE INTO TicketMap (SourceTicket, SlaveTicket, Symbol, Lots, CreatedAt)
                    VALUES (@SourceTicket, @SlaveTicket, @Symbol, @Lots, @CreatedAt)
                ";
                command.Parameters.AddWithValue("@SourceTicket", sourceTicket);
                command.Parameters.AddWithValue("@SlaveTicket", slaveTicket);
                command.Parameters.AddWithValue("@Symbol", symbol);
                command.Parameters.AddWithValue("@Lots", lots);
                command.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow.ToString("o"));
                
                command.ExecuteNonQuery();
                _logger.LogInformation("Ticket mapping added: Source={Source}, Slave={Slave}", 
                    SanitizeForLog(sourceTicket), SanitizeForLog(slaveTicket));
            }
        }

        /// <summary>
        /// Get slave ticket by source ticket
        /// </summary>
        public string GetSlaveTicket(string sourceTicket)
        {
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT SlaveTicket FROM TicketMap WHERE SourceTicket = @SourceTicket";
                command.Parameters.AddWithValue("@SourceTicket", sourceTicket);
                
                return command.ExecuteScalar() as string;
            }
        }

        /// <summary>
        /// Increment retry count for a failed order
        /// </summary>
        public bool IncrementRetryCount(string orderId, TimeSpan retryDelay)
        {
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    UPDATE Orders 
                    SET RetryCount = RetryCount + 1, 
                        LastRetryAt = @LastRetryAt,
                        NextRetryAt = @NextRetryAt
                    WHERE Id = @Id AND Processed = 0
                ";
                command.Parameters.AddWithValue("@Id", orderId);
                command.Parameters.AddWithValue("@LastRetryAt", DateTime.UtcNow.ToString("o"));
                command.Parameters.AddWithValue("@NextRetryAt", DateTime.UtcNow.Add(retryDelay).ToString("o"));

                var rowsAffected = command.ExecuteNonQuery();
                return rowsAffected > 0;
            }
        }

        /// <summary>
        /// Get orders ready for retry
        /// </summary>
        public List<TradeOrder> GetOrdersForRetry(int maxCount = 10)
        {
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT * FROM Orders 
                    WHERE Processed = 0 
                    AND NextRetryAt IS NOT NULL 
                    AND NextRetryAt <= @Now
                    ORDER BY NextRetryAt 
                    LIMIT @MaxCount
                ";
                command.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("o"));
                command.Parameters.AddWithValue("@MaxCount", maxCount);

                var orders = new List<TradeOrder>();
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    orders.Add(ReadOrder(reader));
                }

                return orders;
            }
        }

        /// <summary>
        /// Release orders that have been processing for too long (stale locks)
        /// This handles cases where a consumer crashes without marking orders as processed
        /// </summary>
        public int ReleaseStaleProcessingOrders(TimeSpan timeout)
        {
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var cutoffTime = DateTime.UtcNow.Add(-timeout).ToString("o");
                
                var command = connection.CreateCommand();
                command.CommandText = @"
                    UPDATE Orders 
                    SET Processing = 0,
                        ProcessingBy = NULL,
                        RetryCount = RetryCount + 1,
                        NextRetryAt = @NextRetryAt
                    WHERE Processing = 1 
                    AND Processed = 0
                    AND ProcessingAt < @CutoffTime
                    AND RetryCount < @MaxRetryCount
                ";
                command.Parameters.AddWithValue("@CutoffTime", cutoffTime);
                command.Parameters.AddWithValue("@NextRetryAt", DateTime.UtcNow.AddSeconds(30).ToString("o"));
                command.Parameters.AddWithValue("@MaxRetryCount", _maxRetryCount);

                var released = command.ExecuteNonQuery();
                
                if (released > 0)
                {
                    _logger.LogWarning("Released {Count} stale processing orders", released);
                }
                
                return released;
            }
        }

        /// <summary>
        /// Mark orders that have exceeded max retry count as failed
        /// </summary>
        public int MarkFailedOrders()
        {
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    UPDATE Orders 
                    SET Processing = 0,
                        ProcessingBy = NULL,
                        NextRetryAt = NULL
                    WHERE Processing = 1 
                    AND Processed = 0
                    AND RetryCount >= @MaxRetryCount
                ";
                command.Parameters.AddWithValue("@MaxRetryCount", _maxRetryCount);

                var marked = command.ExecuteNonQuery();
                
                if (marked > 0)
                {
                    _logger.LogError("Marked {Count} orders as failed due to max retry count", marked);
                    OrdersFailedCounter.Inc(marked);
                }
                
                return marked;
            }
        }

        /// <summary>
        /// Update Prometheus gauge with current pending orders count
        /// </summary>
        private void UpdatePendingOrdersGauge()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                
                var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM Orders WHERE Processed = 0";
                var count = Convert.ToInt32(command.ExecuteScalar());
                PendingOrdersGauge.Set(count);
                
                // Also update retry queue size
                command.CommandText = @"
                    SELECT COUNT(*) FROM Orders 
                    WHERE Processed = 0 
                    AND NextRetryAt IS NOT NULL 
                    AND NextRetryAt > @Now
                ";
                command.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("o"));
                var retryCount = Convert.ToInt32(command.ExecuteScalar());
                RetryQueueSizeGauge.Set(retryCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update metrics gauge");
            }
        }

        /// <summary>
        /// Check if database is accessible and healthy
        /// </summary>
        public bool CheckDatabaseHealth()
        {
            try
            {
                lock (_lock)
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();

                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT COUNT(*) FROM Orders LIMIT 1";
                    command.ExecuteScalar();
                    
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database health check failed");
                return false;
            }
        }
    }
}
