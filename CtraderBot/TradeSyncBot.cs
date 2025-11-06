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

        protected override void OnStart()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

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

            var position = args.Position;
            var orderData = new
            {
                EventType = "POSITION_OPENED",
                Timestamp = DateTime.UtcNow.ToString("o"),
                PositionId = position.Id,
                Symbol = position.SymbolName,
                Direction = position.TradeType.ToString(),
                Volume = position.VolumeInUnits / 100000.0, // Convert to lots
                EntryPrice = position.EntryPrice,
                StopLoss = position.StopLoss,
                TakeProfit = position.TakeProfit,
                Comment = position.Comment ?? ""
            };

            SendToBlidge(orderData);
        }

        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            if (!EnableSync) return;

            var position = args.Position;
            var orderData = new
            {
                EventType = "POSITION_CLOSED",
                Timestamp = DateTime.UtcNow.ToString("o"),
                PositionId = position.Id,
                Symbol = position.SymbolName,
                ClosingPrice = position.Pips,
                NetProfit = position.NetProfit
            };

            SendToBlidge(orderData);
        }

        private void OnPositionModified(PositionModifiedEventArgs args)
        {
            if (!EnableSync) return;

            var position = args.Position;
            var orderData = new
            {
                EventType = "POSITION_MODIFIED",
                Timestamp = DateTime.UtcNow.ToString("o"),
                PositionId = position.Id,
                Symbol = position.SymbolName,
                StopLoss = position.StopLoss,
                TakeProfit = position.TakeProfit
            };

            SendToBlidge(orderData);
        }

        private void OnPendingOrderCreated(PendingOrderCreatedEventArgs args)
        {
            if (!EnableSync) return;

            var order = args.PendingOrder;
            var orderData = new
            {
                EventType = "PENDING_ORDER_CREATED",
                Timestamp = DateTime.UtcNow.ToString("o"),
                OrderId = order.Id,
                Symbol = order.SymbolName,
                OrderType = order.OrderType.ToString(),
                Direction = order.TradeType.ToString(),
                Volume = order.VolumeInUnits / 100000.0, // Convert to lots
                TargetPrice = order.TargetPrice,
                StopLoss = order.StopLoss,
                TakeProfit = order.TakeProfit,
                Comment = order.Comment ?? ""
            };

            SendToBlidge(orderData);
        }

        private void OnPendingOrderCancelled(PendingOrderCancelledEventArgs args)
        {
            if (!EnableSync) return;

            var order = args.PendingOrder;
            var orderData = new
            {
                EventType = "PENDING_ORDER_CANCELLED",
                Timestamp = DateTime.UtcNow.ToString("o"),
                OrderId = order.Id,
                Symbol = order.SymbolName
            };

            SendToBlidge(orderData);
        }

        private void OnPendingOrderFilled(PendingOrderFilledEventArgs args)
        {
            if (!EnableSync) return;

            var order = args.PendingOrder;
            var orderData = new
            {
                EventType = "PENDING_ORDER_FILLED",
                Timestamp = DateTime.UtcNow.ToString("o"),
                OrderId = order.Id,
                PositionId = args.Position.Id,
                Symbol = order.SymbolName
            };

            SendToBlidge(orderData);
        }

        private async void SendToBlidge(object orderData)
        {
            try
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(orderData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{BridgeUrl}/api/orders", content);

                if (response.IsSuccessStatusCode)
                {
                    Print("Order sent to bridge successfully: {0}", orderData.GetType().GetProperty("EventType").GetValue(orderData));
                }
                else
                {
                    Print("Failed to send order to bridge. Status: {0}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                Print("Error sending order to bridge: {0}", ex.Message);
            }
        }
    }
}
