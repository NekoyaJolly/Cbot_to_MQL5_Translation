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
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class TradeSyncBot : Robot
    {
        [Parameter("Bridge Server URL", DefaultValue = "http://localhost:5000")]
        public string BridgeUrl { get; set; }

        [Parameter("Enable Sync", DefaultValue = true)]
        public bool EnableSync { get; set; }

        private HttpClient _httpClient;
        private int _consecutiveFailures = 0;
        private const int MAX_CONSECUTIVE_FAILURES = 10;
        private DateTime _lastFailureTime = DateTime.MinValue;

        protected override void OnStart()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
            
            // Set default headers
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "CtraderBot/1.0");

            // Subscribe to position events
            Positions.Opened += OnPositionOpened;
            Positions.Closed += OnPositionClosed;
            Positions.Modified += OnPositionModified;

            // Subscribe to pending order events
            PendingOrders.Created += OnPendingOrderCreated;
            PendingOrders.Cancelled += OnPendingOrderCancelled;
            PendingOrders.Filled += OnPendingOrderFilled;

            Print("TradeSyncBot started. Bridge URL: {0}", BridgeUrl);
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
                Timestamp = DateTime.UtcNow.ToString("o"),
                PositionId = position.Id,
                Symbol = position.SymbolName ?? "",
                Direction = position.TradeType.ToString(),
                Volume = position.VolumeInUnits / 100000.0, // Convert to lots
                EntryPrice = position.EntryPrice,
                StopLoss = position.StopLoss,
                TakeProfit = position.TakeProfit,
                Comment = position.Comment ?? ""
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
                Timestamp = DateTime.UtcNow.ToString("o"),
                PositionId = position.Id,
                Symbol = position.SymbolName ?? "",
                ClosingPrice = position.Pips,
                NetProfit = position.NetProfit
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
                Timestamp = DateTime.UtcNow.ToString("o"),
                PositionId = position.Id,
                Symbol = position.SymbolName ?? "",
                StopLoss = position.StopLoss,
                TakeProfit = position.TakeProfit
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
                Timestamp = DateTime.UtcNow.ToString("o"),
                OrderId = order.Id,
                Symbol = order.SymbolName ?? "",
                OrderType = order.OrderType.ToString(),
                Direction = order.TradeType.ToString(),
                Volume = order.VolumeInUnits / 100000.0, // Convert to lots
                TargetPrice = order.TargetPrice,
                StopLoss = order.StopLoss,
                TakeProfit = order.TakeProfit,
                Comment = order.Comment ?? ""
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
                Timestamp = DateTime.UtcNow.ToString("o"),
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
                Timestamp = DateTime.UtcNow.ToString("o"),
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
                    // Still in cooldown period
                    return;
                }
                else
                {
                    // Reset after cooldown
                    _consecutiveFailures = 0;
                    Print("Circuit breaker reset - attempting to reconnect to bridge");
                }
            }

            try
            {
                // Validate orderData
                if (orderData == null)
                {
                    Print("Error: orderData is null");
                    return;
                }

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(orderData);
                
                // Validate JSON
                if (string.IsNullOrEmpty(json))
                {
                    Print("Error: Failed to serialize orderData");
                    return;
                }

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{BridgeUrl}/api/orders", content);

                if (response.IsSuccessStatusCode)
                {
                    _consecutiveFailures = 0; // Reset on success
                    var eventType = orderData.GetType().GetProperty("EventType")?.GetValue(orderData);
                    Print("Order sent to bridge successfully: {0}", eventType);
                }
                else
                {
                    _consecutiveFailures++;
                    _lastFailureTime = DateTime.UtcNow;
                    Print("Failed to send order to bridge. Status: {0}, Failures: {1}/{2}", 
                          response.StatusCode, _consecutiveFailures, MAX_CONSECUTIVE_FAILURES);
                }
            }
            catch (HttpRequestException ex)
            {
                _consecutiveFailures++;
                _lastFailureTime = DateTime.UtcNow;
                Print("Network error sending order to bridge: {0}, Failures: {1}/{2}", 
                      ex.Message, _consecutiveFailures, MAX_CONSECUTIVE_FAILURES);
            }
            catch (TaskCanceledException ex)
            {
                _consecutiveFailures++;
                _lastFailureTime = DateTime.UtcNow;
                Print("Timeout sending order to bridge: {0}, Failures: {1}/{2}", 
                      ex.Message, _consecutiveFailures, MAX_CONSECUTIVE_FAILURES);
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                _lastFailureTime = DateTime.UtcNow;
                Print("Error sending order to bridge: {0}, Failures: {1}/{2}", 
                      ex.Message, _consecutiveFailures, MAX_CONSECUTIVE_FAILURES);
            }
        }
    }
}
