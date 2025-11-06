using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

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

        public PersistentOrderQueueManager(string databasePath, ILogger<PersistentOrderQueueManager> logger)
        {
            _connectionString = $"Data Source={databasePath}";
            _logger = logger;
            InitializeDatabase();
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
                    UNIQUE(SourceId, EventType)
                );
                
                CREATE INDEX IF NOT EXISTS idx_orders_processed ON Orders(Processed);
                CREATE INDEX IF NOT EXISTS idx_orders_sourceid ON Orders(SourceId);
                CREATE INDEX IF NOT EXISTS idx_orders_timestamp ON Orders(Timestamp);
            ";
            command.ExecuteNonQuery();

            _logger.LogInformation("Database initialized at {DatabasePath}", _connectionString);
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
                            order.SourceId, order.EventType);
                        return existingId;
                    }
                }

                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO Orders (
                        Id, SourceId, EventType, Timestamp, PositionId, OrderId, Symbol, 
                        Direction, OrderType, Volume, EntryPrice, TargetPrice, 
                        StopLoss, TakeProfit, ClosingPrice, NetProfit, Comment, 
                        Processed, ProcessedAt, CreatedAt
                    ) VALUES (
                        @Id, @SourceId, @EventType, @Timestamp, @PositionId, @OrderId, @Symbol,
                        @Direction, @OrderType, @Volume, @EntryPrice, @TargetPrice,
                        @StopLoss, @TakeProfit, @ClosingPrice, @NetProfit, @Comment,
                        0, NULL, @CreatedAt
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

                command.ExecuteNonQuery();

                _logger.LogInformation("Order added: Id={Id}, SourceId={SourceId}, EventType={EventType}", 
                    order.Id, order.SourceId, order.EventType);

                return order.Id;
            }
        }

        public List<TradeOrder> GetPendingOrders(int maxCount = 10)
        {
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT * FROM Orders 
                    WHERE Processed = 0 
                    ORDER BY Timestamp 
                    LIMIT @MaxCount
                ";
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

        public bool MarkAsProcessed(string orderId)
        {
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    UPDATE Orders 
                    SET Processed = 1, ProcessedAt = @ProcessedAt 
                    WHERE Id = @Id AND Processed = 0
                ";
                command.Parameters.AddWithValue("@Id", orderId);
                command.Parameters.AddWithValue("@ProcessedAt", DateTime.UtcNow.ToString("o"));

                var rowsAffected = command.ExecuteNonQuery();
                
                if (rowsAffected > 0)
                {
                    _logger.LogInformation("Order marked as processed: Id={Id}", orderId);
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
    }
}
