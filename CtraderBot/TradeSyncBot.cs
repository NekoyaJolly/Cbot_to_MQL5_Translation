using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using cAlgo.API;
using cAlgo.API.Internals;

namespace CtraderBot
{
    /// <summary>
    /// cTrader cBot that hooks all trade events and sends them to the Bridge server
    /// for synchronization with MT5
    /// </summary>
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class TradeSyncBot : Robot
    {
        [Parameter("Bridge Server URL", DefaultValue = "http://localhost:5000")]
        public string BridgeUrl { get; set; }

        [Parameter("Enable Sync", DefaultValue = true)]
        public bool EnableSync { get; set; }

        [Parameter("Bridge API Key", DefaultValue = "")]
        public string BridgeApiKey { get; set; }

        [Parameter("Master Label", DefaultValue = "MASTER")]
        public string MasterLabel { get; set; }

        [Parameter("Max Queue Size", DefaultValue = 10000)]
        public int MaxQueueSize { get; set; }

        [Parameter("Max Persist File Size MB", DefaultValue = 100)]
        public int MaxPersistFileSizeMB { get; set; }

        private HttpClient _httpClient;
        private int _consecutiveFailures = 0;
        private const int MAX_CONSECUTIVE_FAILURES = 10;
        private DateTime _lastFailureTime = DateTime.MinValue;
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _failedMessagesQueue = 
            new System.Collections.Concurrent.ConcurrentQueue<string>();
        private readonly string _persistDir = "persist/failed";
        private readonly string _persistFile = "persist/failed/failed_queue.log";
        private System.Threading.Timer _retryTimer;
        private readonly object _fileLock = new object();

        protected override void OnStart()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
            
            // Set default headers
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "CtraderBot/1.0");
            
            // Add API key if provided
            if (!string.IsNullOrEmpty(BridgeApiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-API-KEY", BridgeApiKey);
            }

            // Create persist directory if it doesn't exist
            try
            {
                if (!System.IO.Directory.Exists(_persistDir))
                {
                    System.IO.Directory.CreateDirectory(_persistDir);
                }
            }
            catch (Exception ex)
            {
                Print("Warning: Could not create persist directory: {0}", ex.Message);
            }

            // Load and retry failed messages from previous runs
            LoadFailedMessages();

            // Start background retry timer (every 60 seconds)
            _retryTimer = new System.Threading.Timer(
                _ => {
                    try
                    {
                        RetryFailedMessages().Wait();
                    }
                    catch (Exception ex)
                    {
                        Print("Error in retry timer: {0}", ex.Message);
                    }
                },
                null,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(60)
            );

            // Subscribe to position events
            Positions.Opened += OnPositionOpened;
            Positions.Closed += OnPositionClosed;
            Positions.Modified += OnPositionModified;

            // Subscribe to pending order events
            PendingOrders.Created += OnPendingOrderCreated;
            PendingOrders.Cancelled += OnPendingOrderCancelled;
            PendingOrders.Filled += OnPendingOrderFilled;

            Print("TradeSyncBot started. Bridge URL: {0}, API Key configured: {1}", 
                  BridgeUrl, !string.IsNullOrEmpty(BridgeApiKey));
        }

        protected override void OnStop()
        {
            // Unsubscribe from events
            Positions.Opened -= OnPositionOpened;
            Positions.Closed -= OnPositionClosed;
            Positions.Modified -= OnPositionModified;

            PendingOrders.Created -= OnPendingOrderCreated;
            PendingOrders.Cancelled -= OnPendingOrderCancelled;
            PendingOrders.Filled -= OnPendingOrderFilled;

            // Stop retry timer
            _retryTimer?.Dispose();

            _httpClient?.Dispose();

            Print("TradeSyncBot stopped.");
        }

        private void OnPositionOpened(PositionOpenedEventArgs args)
        {
            if (!EnableSync) return;
            
            if (args?.Position == null)
            {
                Print("Error: Position is null in OnPositionOpened");
                return;
            }

            var position = args.Position;
            
            // Get the symbol to calculate lots using broker's LotSize
            var symbol = Symbols.GetSymbol(position.SymbolName);
            var lotSize = symbol?.LotSize ?? 100000.0; // Fallback to standard lot size if symbol not found
            
            var orderData = new
            {
                EventType = "POSITION_OPENED",
                Timestamp = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                SourceId = position.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                PositionId = position.Id,
                Symbol = position.SymbolName ?? "",
                Direction = position.TradeType.ToString(),
                Volume = (position.VolumeInUnits / lotSize).ToString("F5", System.Globalization.CultureInfo.InvariantCulture),
                EntryPrice = position.EntryPrice.ToString("F5", System.Globalization.CultureInfo.InvariantCulture),
                StopLoss = position.StopLoss?.ToString("F5", System.Globalization.CultureInfo.InvariantCulture),
                TakeProfit = position.TakeProfit?.ToString("F5", System.Globalization.CultureInfo.InvariantCulture),
                Comment = position.Comment ?? MasterLabel
            };

            SendToBridge(orderData);
        }

        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            if (!EnableSync) return;
            
            if (args?.Position == null)
            {
                Print("Error: Position is null in OnPositionClosed");
                return;
            }

            var position = args.Position;
            var orderData = new
            {
                EventType = "POSITION_CLOSED",
                Timestamp = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                SourceId = position.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                PositionId = position.Id,
                Symbol = position.SymbolName ?? "",
                NetProfit = position.NetProfit.ToString("F5", System.Globalization.CultureInfo.InvariantCulture)
            };

            SendToBridge(orderData);
        }

        private void OnPositionModified(PositionModifiedEventArgs args)
        {
            if (!EnableSync) return;
            
            if (args?.Position == null)
            {
                Print("Error: Position is null in OnPositionModified");
                return;
            }

            var position = args.Position;
            var orderData = new
            {
                EventType = "POSITION_MODIFIED",
                Timestamp = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                SourceId = position.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                PositionId = position.Id,
                Symbol = position.SymbolName ?? "",
                StopLoss = position.StopLoss?.ToString("F5", System.Globalization.CultureInfo.InvariantCulture),
                TakeProfit = position.TakeProfit?.ToString("F5", System.Globalization.CultureInfo.InvariantCulture)
            };

            SendToBridge(orderData);
        }

        private void OnPendingOrderCreated(PendingOrderCreatedEventArgs args)
        {
            if (!EnableSync) return;
            
            if (args?.PendingOrder == null)
            {
                Print("Error: PendingOrder is null in OnPendingOrderCreated");
                return;
            }

            var order = args.PendingOrder;
            
            // Get the symbol to calculate lots using broker's LotSize
            var symbol = Symbols.GetSymbol(order.SymbolName);
            var lotSize = symbol?.LotSize ?? 100000.0; // Fallback to standard lot size if symbol not found
            
            var orderData = new
            {
                EventType = "PENDING_ORDER_CREATED",
                Timestamp = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                SourceId = order.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                OrderId = order.Id,
                Symbol = order.SymbolName ?? "",
                OrderType = order.OrderType.ToString(),
                Direction = order.TradeType.ToString(),
                Volume = (order.VolumeInUnits / lotSize).ToString("F5", System.Globalization.CultureInfo.InvariantCulture),
                TargetPrice = order.TargetPrice.ToString("F5", System.Globalization.CultureInfo.InvariantCulture),
                StopLoss = order.StopLoss?.ToString("F5", System.Globalization.CultureInfo.InvariantCulture),
                TakeProfit = order.TakeProfit?.ToString("F5", System.Globalization.CultureInfo.InvariantCulture),
                Comment = order.Comment ?? MasterLabel
            };

            SendToBridge(orderData);
        }

        private void OnPendingOrderCancelled(PendingOrderCancelledEventArgs args)
        {
            if (!EnableSync) return;
            
            if (args?.PendingOrder == null)
            {
                Print("Error: PendingOrder is null in OnPendingOrderCancelled");
                return;
            }

            var order = args.PendingOrder;
            var orderData = new
            {
                EventType = "PENDING_ORDER_CANCELLED",
                Timestamp = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                SourceId = order.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                OrderId = order.Id,
                Symbol = order.SymbolName ?? ""
            };

            SendToBridge(orderData);
        }

        private void OnPendingOrderFilled(PendingOrderFilledEventArgs args)
        {
            if (!EnableSync) return;
            
            if (args?.PendingOrder == null || args?.Position == null)
            {
                Print("Error: PendingOrder or Position is null in OnPendingOrderFilled");
                return;
            }

            var order = args.PendingOrder;
            var orderData = new
            {
                EventType = "PENDING_ORDER_FILLED",
                Timestamp = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                SourceId = order.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                OrderId = order.Id,
                PositionId = args.Position.Id,
                Symbol = order.SymbolName ?? ""
            };

            SendToBridge(orderData);
        }

        private async void SendToBridge(object orderData)
        {
            if (!EnableSync)
                return;

            // Circuit breaker: skip if too many consecutive failures
            if (_consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
            {
                var timeSinceLastFailure = DateTime.UtcNow - _lastFailureTime;
                if (timeSinceLastFailure < TimeSpan.FromMinutes(5))
                {
                    // Still in cooldown period - persist message for later retry
                    PersistFailedMessage(orderData);
                    return;
                }
                else
                {
                    // Reset after cooldown
                    _consecutiveFailures = 0;
                    Print("Circuit breaker reset - attempting to reconnect to bridge");
                }
            }

            var retryCount = 0;
            var success = await TrySendHttp(orderData, retryCount);
            
            if (!success)
            {
                // Persist for background retry
                PersistFailedMessage(orderData);
            }
        }

        private async Task<bool> TrySendHttp(object orderData, int retryCount)
        {
            try
            {
                // Validate orderData
                if (orderData == null)
                {
                    Print("Error: orderData is null");
                    return false;
                }

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(orderData);
                
                // Validate JSON
                if (string.IsNullOrEmpty(json))
                {
                    Print("Error: Failed to serialize orderData");
                    return false;
                }

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{BridgeUrl}/api/orders", content);

                if (response.IsSuccessStatusCode)
                {
                    _consecutiveFailures = 0; // Reset on success
                    var eventType = orderData.GetType().GetProperty("EventType")?.GetValue(orderData);
                    var sourceId = orderData.GetType().GetProperty("SourceId")?.GetValue(orderData);
                    Print("Order sent successfully: EventType={0}, SourceId={1}, RetryCount={2}", 
                          eventType, sourceId, retryCount);
                    return true;
                }
                else
                {
                    _consecutiveFailures++;
                    _lastFailureTime = DateTime.UtcNow;
                    var eventType = orderData.GetType().GetProperty("EventType")?.GetValue(orderData);
                    var sourceId = orderData.GetType().GetProperty("SourceId")?.GetValue(orderData);
                    Print("Failed to send order: EventType={0}, SourceId={1}, Status={2}, Failures={3}/{4}, RetryCount={5}", 
                          eventType, sourceId, response.StatusCode, _consecutiveFailures, MAX_CONSECUTIVE_FAILURES, retryCount);
                    return false;
                }
            }
            catch (HttpRequestException ex)
            {
                _consecutiveFailures++;
                _lastFailureTime = DateTime.UtcNow;
                Print("Network error: {0}, Failures={1}/{2}, RetryCount={3}", 
                      ex.Message, _consecutiveFailures, MAX_CONSECUTIVE_FAILURES, retryCount);
                return false;
            }
            catch (TaskCanceledException ex)
            {
                _consecutiveFailures++;
                _lastFailureTime = DateTime.UtcNow;
                Print("Timeout error: {0}, Failures={1}/{2}, RetryCount={3}", 
                      ex.Message, _consecutiveFailures, MAX_CONSECUTIVE_FAILURES, retryCount);
                return false;
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                _lastFailureTime = DateTime.UtcNow;
                Print("Error sending order: {0}, Failures={1}/{2}, RetryCount={3}", 
                      ex.Message, _consecutiveFailures, MAX_CONSECUTIVE_FAILURES, retryCount);
                return false;
            }
        }

        private void PersistFailedMessage(object orderData)
        {
            try
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(orderData);
                _failedMessagesQueue.Enqueue(json);
                
                // Check queue size limit
                if (_failedMessagesQueue.Count > MaxQueueSize)
                {
                    Print("Warning: Queue size exceeded {0}, dropping oldest message", MaxQueueSize);
                    _failedMessagesQueue.TryDequeue(out _);
                }
                
                // Write to single append file for durability across restarts
                lock (_fileLock)
                {
                    // Check file size before writing
                    var maxFileSize = MaxPersistFileSizeMB * 1024 * 1024;
                    if (System.IO.File.Exists(_persistFile))
                    {
                        var fileInfo = new System.IO.FileInfo(_persistFile);
                        if (fileInfo.Length >= maxFileSize)
                        {
                            // Rotate file
                            RotatePersistFile();
                        }
                    }
                    
                    // Append to file (one JSON per line)
                    System.IO.File.AppendAllText(_persistFile, json + Environment.NewLine);
                }
                
                var eventType = orderData.GetType().GetProperty("EventType")?.GetValue(orderData);
                var sourceId = orderData.GetType().GetProperty("SourceId")?.GetValue(orderData);
                Print("Message persisted: EventType={0}, SourceId={1}, QueueSize={2}", 
                      eventType, sourceId, _failedMessagesQueue.Count);
            }
            catch (Exception ex)
            {
                Print("Error persisting failed message: {0}", ex.Message);
            }
        }

        private void RotatePersistFile()
        {
            try
            {
                if (!System.IO.File.Exists(_persistFile))
                    return;
                
                // Create backup with timestamp
                var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
                var backupFile = System.IO.Path.Combine(_persistDir, $"failed_queue_{timestamp}.log.bak");
                
                System.IO.File.Move(_persistFile, backupFile);
                Print("Persist file rotated to: {0}", backupFile);
                
                // Clean up old backup files (keep only last 10)
                CleanupOldBackups();
            }
            catch (Exception ex)
            {
                Print("Error rotating persist file: {0}", ex.Message);
            }
        }

        private void CleanupOldBackups()
        {
            try
            {
                var backupFiles = System.IO.Directory.GetFiles(_persistDir, "failed_queue_*.log.bak")
                    .Select(f => new System.IO.FileInfo(f))
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .ToArray();
                
                // Keep only the last 10 backup files
                for (int i = 10; i < backupFiles.Length; i++)
                {
                    try
                    {
                        backupFiles[i].Delete();
                        Print("Deleted old backup: {0}", backupFiles[i].Name);
                    }
                    catch (Exception ex)
                    {
                        Print("Error deleting backup {0}: {1}", backupFiles[i].Name, ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Print("Error cleaning up old backups: {0}", ex.Message);
            }
        }

        private void LoadFailedMessages()
        {
            try
            {
                if (!System.IO.Directory.Exists(_persistDir))
                    return;

                var messageCount = 0;
                const int MAX_MESSAGES_TO_LOAD = 10000; // Prevent memory exhaustion
                const int FILE_SIZE_BUFFER_MULTIPLIER = 2; // Allow 2x buffer to handle transient size increases
                
                // Load from main persist file
                if (System.IO.File.Exists(_persistFile))
                {
                    lock (_fileLock)
                    {
                        try
                        {
                            // Check file size to prevent loading extremely large files
                            var fileInfo = new System.IO.FileInfo(_persistFile);
                            var maxFileSize = MaxPersistFileSizeMB * 1024 * 1024;
                            
                            if (fileInfo.Length > maxFileSize * FILE_SIZE_BUFFER_MULTIPLIER)
                            {
                                Print("Warning: Persist file {0} is too large ({1} bytes). Rotating before loading.", 
                                      _persistFile, fileInfo.Length);
                                RotatePersistFile();
                                return;
                            }
                            
                            var lines = System.IO.File.ReadAllLines(_persistFile);
                            var validLineCount = 0;
                            
                            foreach (var line in lines)
                            {
                                if (messageCount >= MAX_MESSAGES_TO_LOAD)
                                {
                                    Print("Warning: Maximum message load limit ({0}) reached. Remaining messages will be loaded on next restart.", 
                                          MAX_MESSAGES_TO_LOAD);
                                    break;
                                }
                                
                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    // Validate JSON structure before enqueueing
                                    // Use lightweight validation by checking basic JSON structure
                                    var trimmedLine = line.Trim();
                                    if ((trimmedLine.StartsWith("{") && trimmedLine.EndsWith("}")) ||
                                        (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]")))
                                    {
                                        try
                                        {
                                            // Only deserialize for validation, don't store result
                                            Newtonsoft.Json.JsonConvert.DeserializeObject<object>(line);
                                            _failedMessagesQueue.Enqueue(line);
                                            messageCount++;
                                            validLineCount++;
                                        }
                                        catch (Newtonsoft.Json.JsonException ex)
                                        {
                                            Print("Warning: Skipping corrupted JSON line in persist file: {0}", ex.Message);
                                        }
                                    }
                                    else
                                    {
                                        Print("Warning: Skipping line with invalid JSON structure");
                                    }
                                }
                            }
                            
                            // Clear the file after loading valid entries
                            if (validLineCount > 0)
                            {
                                System.IO.File.WriteAllText(_persistFile, string.Empty);
                            }
                            else if (lines.Length > 0)
                            {
                                Print("Warning: Persist file contained no valid entries. File may be corrupted.");
                                // Rotate corrupted file instead of deleting
                                RotatePersistFile();
                            }
                        }
                        catch (System.IO.IOException ex)
                        {
                            Print("Error loading failed messages from {0}: {1}", _persistFile, ex.Message);
                        }
                        catch (System.UnauthorizedAccessException ex)
                        {
                            Print("Error: Access denied loading {0}: {1}", _persistFile, ex.Message);
                        }
                        catch (Exception ex)
                        {
                            Print("Error loading failed messages from {0}: {1}", _persistFile, ex.Message);
                        }
                    }
                }
                
                // Also load from any old-style failed_*.log files for backward compatibility
                var oldFiles = System.IO.Directory.GetFiles(_persistDir, "failed_*.log");
                foreach (var file in oldFiles)
                {
                    if (messageCount >= MAX_MESSAGES_TO_LOAD)
                    {
                        Print("Warning: Maximum message load limit ({0}) reached. Skipping remaining old-style files.", 
                              MAX_MESSAGES_TO_LOAD);
                        break;
                    }
                    
                    try
                    {
                        var lines = System.IO.File.ReadAllLines(file);
                        var validLineCount = 0;
                        
                        foreach (var line in lines)
                        {
                            if (messageCount >= MAX_MESSAGES_TO_LOAD)
                                break;
                            
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                // Validate JSON before enqueueing with lightweight check
                                var trimmedLine = line.Trim();
                                if ((trimmedLine.StartsWith("{") && trimmedLine.EndsWith("}")) ||
                                    (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]")))
                                {
                                    try
                                    {
                                        Newtonsoft.Json.JsonConvert.DeserializeObject<object>(line);
                                        _failedMessagesQueue.Enqueue(line);
                                        messageCount++;
                                        validLineCount++;
                                    }
                                    catch (Newtonsoft.Json.JsonException)
                                    {
                                        // Skip corrupted lines silently for old files
                                    }
                                }
                            }
                        }
                        
                        // Delete the old file after loading
                        System.IO.File.Delete(file);
                        
                        if (validLineCount > 0)
                        {
                            Print("Loaded {0} valid messages from old-style file: {1}", validLineCount, file);
                        }
                    }
                    catch (Exception ex)
                    {
                        Print("Error loading failed messages from {0}: {1}", file, ex.Message);
                    }
                }
                
                if (messageCount > 0)
                {
                    Print("Loaded {0} failed messages for retry", messageCount);
                }
            }
            catch (Exception ex)
            {
                Print("Error loading failed messages: {0}", ex.Message);
            }
        }

        private async Task RetryFailedMessages()
        {
            if (_failedMessagesQueue.IsEmpty)
                return;

            var retryCount = 0;
            var maxRetries = 10; // Process up to 10 messages per retry cycle
            
            while (retryCount < maxRetries && _failedMessagesQueue.TryPeek(out var json))
            {
                try
                {
                    // Deserialize to object - using anonymous types for flexibility
                    // The JSON structure is validated when creating the orderData
                    var orderData = Newtonsoft.Json.JsonConvert.DeserializeObject(json);
                    
                    // Calculate exponential backoff delay
                    var backoffDelay = CalculateBackoffDelay(retryCount);
                    if (backoffDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(backoffDelay);
                    }
                    
                    var success = await TrySendHttp(orderData, retryCount + 1);
                    
                    if (success)
                    {
                        // Only dequeue on success
                        _failedMessagesQueue.TryDequeue(out _);
                        retryCount++;
                    }
                    else
                    {
                        // Stop processing if we hit a failure - will retry in next cycle
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Print("Error retrying failed message: {0}", ex.Message);
                    // Stop processing on error
                    break;
                }
            }
            
            if (retryCount > 0)
            {
                Print("Retry cycle completed: {0} messages sent, {1} remaining in queue", 
                      retryCount, _failedMessagesQueue.Count);
            }
        }

        private TimeSpan CalculateBackoffDelay(int retryCount)
        {
            // Exponential backoff: 0s, 1s, 2s, 4s, 8s, 16s, 32s, 60s (capped at 60s)
            if (retryCount == 0)
                return TimeSpan.Zero;
            
            var delaySeconds = Math.Min(Math.Pow(2, retryCount - 1), 60);
            return TimeSpan.FromSeconds(delaySeconds);
        }
    }
}
