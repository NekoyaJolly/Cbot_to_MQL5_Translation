using System;
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
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.Internet)]
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

        private HttpClient _httpClient;
        private int _consecutiveFailures = 0;
        private const int MAX_CONSECUTIVE_FAILURES = 10;
        private DateTime _lastFailureTime = DateTime.MinValue;
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _failedMessagesQueue = 
            new System.Collections.Concurrent.ConcurrentQueue<string>();
        private readonly string _persistDir = "persist/failed";
        private System.Threading.Timer _retryTimer;

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
                async _ => await RetryFailedMessages(),
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
            var orderData = new
            {
                EventType = "POSITION_OPENED",
                Timestamp = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                SourceId = position.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                PositionId = position.Id,
                Symbol = position.SymbolName ?? "",
                Direction = position.TradeType.ToString(),
                Volume = (position.VolumeInUnits / 100000.0).ToString("F5", System.Globalization.CultureInfo.InvariantCulture),
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
                ClosingPrice = position.Pips.ToString("F5", System.Globalization.CultureInfo.InvariantCulture),
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
            var orderData = new
            {
                EventType = "PENDING_ORDER_CREATED",
                Timestamp = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                SourceId = order.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                OrderId = order.Id,
                Symbol = order.SymbolName ?? "",
                OrderType = order.OrderType.ToString(),
                Direction = order.TradeType.ToString(),
                Volume = (order.VolumeInUnits / 100000.0).ToString("F5", System.Globalization.CultureInfo.InvariantCulture),
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
                
                // Also write to file for durability across restarts
                var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
                var filename = System.IO.Path.Combine(_persistDir, $"failed_{timestamp}.log");
                
                // Append to file (one JSON per line)
                System.IO.File.AppendAllText(filename, json + Environment.NewLine);
                
                var eventType = orderData.GetType().GetProperty("EventType")?.GetValue(orderData);
                var sourceId = orderData.GetType().GetProperty("SourceId")?.GetValue(orderData);
                Print("Message persisted: EventType={0}, SourceId={1}, File={2}", 
                      eventType, sourceId, filename);
            }
            catch (Exception ex)
            {
                Print("Error persisting failed message: {0}", ex.Message);
            }
        }

        private void LoadFailedMessages()
        {
            try
            {
                if (!System.IO.Directory.Exists(_persistDir))
                    return;

                var files = System.IO.Directory.GetFiles(_persistDir, "failed_*.log");
                var messageCount = 0;
                
                foreach (var file in files)
                {
                    try
                    {
                        var lines = System.IO.File.ReadAllLines(file);
                        foreach (var line in lines)
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                _failedMessagesQueue.Enqueue(line);
                                messageCount++;
                            }
                        }
                        
                        // Delete the file after loading
                        System.IO.File.Delete(file);
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
            
            while (retryCount < maxRetries && _failedMessagesQueue.TryDequeue(out var json))
            {
                try
                {
                    var orderData = Newtonsoft.Json.JsonConvert.DeserializeObject(json);
                    var success = await TrySendHttp(orderData, retryCount + 1);
                    
                    if (!success)
                    {
                        // Re-queue for next retry
                        _failedMessagesQueue.Enqueue(json);
                        break; // Stop processing if we hit a failure
                    }
                    
                    retryCount++;
                }
                catch (Exception ex)
                {
                    Print("Error retrying failed message: {0}", ex.Message);
                    // Re-queue the message
                    _failedMessagesQueue.Enqueue(json);
                    break;
                }
            }
            
            if (retryCount > 0)
            {
                Print("Retry cycle completed: {0} messages sent, {1} remaining in queue", 
                      retryCount, _failedMessagesQueue.Count);
            }
        }
    }
}
