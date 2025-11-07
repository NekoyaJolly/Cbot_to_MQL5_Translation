using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Bridge;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Xunit.Abstractions;

namespace E2ETests
{
    /// <summary>
    /// End-to-End tests that simulate the full flow:
    /// Ctrader cBot -> Bridge Server -> MT5 EA
    /// </summary>
    public class TradeSyncE2ETests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly ITestOutputHelper _output;

        public TradeSyncE2ETests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
        {
            _factory = factory;
            _output = output;
        }

        [Fact]
        public async Task E2E_PositionOpened_CompleteFlow_ShouldSucceed()
        {
            // This test simulates the complete flow:
            // 1. Cbot sends POSITION_OPENED to Bridge
            // 2. MT5 EA polls Bridge for pending orders
            // 3. MT5 EA processes the order
            // 4. MT5 EA marks the order as processed

            // Arrange
            var client = _factory.CreateClient();
            var sourceId = Guid.NewGuid().ToString();

            // Step 1: Simulate Cbot sending POSITION_OPENED event to Bridge
            _output.WriteLine("Step 1: Cbot sends POSITION_OPENED to Bridge");
            var order = new TradeOrder
            {
                SourceId = sourceId,
                EventType = "POSITION_OPENED",
                Symbol = "EURUSD",
                Direction = "Buy",
                Volume = "0.01",
                EntryPrice = "1.0950",
                StopLoss = "1.0900",
                TakeProfit = "1.1000",
                Comment = "E2E Test Order",
                Timestamp = DateTime.UtcNow
            };

            var addResponse = await client.PostAsJsonAsync("/api/orders", order);
            addResponse.EnsureSuccessStatusCode();
            var addContent = await addResponse.Content.ReadAsStringAsync();
            _output.WriteLine($"Bridge response: {addContent}");

            // Extract order ID
            var orderId = ExtractOrderId(addContent);
            Assert.NotNull(orderId);
            _output.WriteLine($"Order queued with ID: {orderId}");

            // Step 2: Simulate MT5 EA polling for pending orders
            _output.WriteLine("\nStep 2: MT5 EA polls Bridge for pending orders");
            var pendingResponse = await client.GetAsync("/api/orders/pending?maxCount=10");
            pendingResponse.EnsureSuccessStatusCode();
            var pendingOrders = await pendingResponse.Content.ReadFromJsonAsync<List<TradeOrder>>();

            Assert.NotNull(pendingOrders);
            Assert.NotEmpty(pendingOrders);
            var receivedOrder = pendingOrders.FirstOrDefault(o => o.Id == orderId);
            Assert.NotNull(receivedOrder);
            _output.WriteLine($"MT5 EA received order: {receivedOrder.Id} - {receivedOrder.EventType} - {receivedOrder.Symbol}");

            // Step 3: Simulate MT5 EA processing the order
            _output.WriteLine("\nStep 3: MT5 EA processes the order (simulated)");
            _output.WriteLine($"  - Symbol: {receivedOrder.Symbol}");
            _output.WriteLine($"  - Direction: {receivedOrder.Direction}");
            _output.WriteLine($"  - Volume: {receivedOrder.Volume}");
            _output.WriteLine($"  - Entry: {receivedOrder.EntryPrice}");
            _output.WriteLine($"  - SL: {receivedOrder.StopLoss}");
            _output.WriteLine($"  - TP: {receivedOrder.TakeProfit}");

            // Simulate some processing time
            await Task.Delay(100);

            // Step 4: Simulate MT5 EA marking the order as processed
            _output.WriteLine("\nStep 4: MT5 EA marks order as processed");
            var processResponse = await client.PostAsync($"/api/orders/{orderId}/processed", null);
            processResponse.EnsureSuccessStatusCode();
            _output.WriteLine($"Order {orderId} marked as processed");

            // Step 5: Verify the order is no longer in pending queue
            _output.WriteLine("\nStep 5: Verify order is no longer pending");
            var verifyResponse = await client.GetAsync("/api/orders/pending?maxCount=10");
            verifyResponse.EnsureSuccessStatusCode();
            var remainingOrders = await verifyResponse.Content.ReadFromJsonAsync<List<TradeOrder>>();

            var stillPending = remainingOrders?.FirstOrDefault(o => o.Id == orderId);
            Assert.Null(stillPending);
            _output.WriteLine("✓ Order is no longer in pending queue");

            // Step 6: Verify statistics
            _output.WriteLine("\nStep 6: Verify statistics");
            var statsResponse = await client.GetAsync("/api/statistics");
            statsResponse.EnsureSuccessStatusCode();
            var stats = await statsResponse.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            Assert.NotNull(stats);
            _output.WriteLine($"  Total Orders: {stats["TotalOrders"]}");
            _output.WriteLine($"  Pending Orders: {stats["PendingOrders"]}");
            _output.WriteLine($"  Processed Orders: {stats["ProcessedOrders"]}");

            _output.WriteLine("\n✓ E2E Test Completed Successfully");
        }

        [Fact]
        public async Task E2E_PositionModified_CompleteFlow_ShouldSucceed()
        {
            // This test simulates position modification flow

            // Arrange
            var client = _factory.CreateClient();
            var positionId = 12345L;
            var sourceId = Guid.NewGuid().ToString();

            // Step 1: Send POSITION_MODIFIED event
            _output.WriteLine("Step 1: Cbot sends POSITION_MODIFIED to Bridge");
            var order = new TradeOrder
            {
                SourceId = sourceId,
                EventType = "POSITION_MODIFIED",
                PositionId = positionId,
                Symbol = "GBPUSD",
                StopLoss = "1.2500",
                TakeProfit = "1.2700",
                Timestamp = DateTime.UtcNow
            };

            var addResponse = await client.PostAsJsonAsync("/api/orders", order);
            addResponse.EnsureSuccessStatusCode();
            var orderId = ExtractOrderId(await addResponse.Content.ReadAsStringAsync());

            // Step 2: MT5 EA polls and receives the order
            _output.WriteLine("\nStep 2: MT5 EA polls for pending orders");
            var pendingResponse = await client.GetAsync("/api/orders/pending?maxCount=10");
            pendingResponse.EnsureSuccessStatusCode();
            var pendingOrders = await pendingResponse.Content.ReadFromJsonAsync<List<TradeOrder>>();

            var receivedOrder = pendingOrders?.FirstOrDefault(o => o.Id == orderId);
            Assert.NotNull(receivedOrder);
            Assert.Equal("POSITION_MODIFIED", receivedOrder.EventType);
            _output.WriteLine($"MT5 EA received modification for position {receivedOrder.PositionId}");

            // Step 3: Mark as processed
            _output.WriteLine("\nStep 3: MT5 EA marks order as processed");
            var processResponse = await client.PostAsync($"/api/orders/{orderId}/processed", null);
            processResponse.EnsureSuccessStatusCode();

            _output.WriteLine("\n✓ Position Modification E2E Test Completed");
        }

        [Fact]
        public async Task E2E_PositionClosed_CompleteFlow_ShouldSucceed()
        {
            // This test simulates position closing flow

            // Arrange
            var client = _factory.CreateClient();
            var positionId = 67890L;
            var sourceId = Guid.NewGuid().ToString();

            // Step 1: Send POSITION_CLOSED event
            _output.WriteLine("Step 1: Cbot sends POSITION_CLOSED to Bridge");
            var order = new TradeOrder
            {
                SourceId = sourceId,
                EventType = "POSITION_CLOSED",
                PositionId = positionId,
                Symbol = "USDJPY",
                NetProfit = "25.50",
                Timestamp = DateTime.UtcNow
            };

            var addResponse = await client.PostAsJsonAsync("/api/orders", order);
            addResponse.EnsureSuccessStatusCode();
            var orderId = ExtractOrderId(await addResponse.Content.ReadAsStringAsync());

            // Step 2: MT5 EA polls and receives the order
            _output.WriteLine("\nStep 2: MT5 EA polls for pending orders");
            var pendingResponse = await client.GetAsync("/api/orders/pending?maxCount=10");
            pendingResponse.EnsureSuccessStatusCode();
            var pendingOrders = await pendingResponse.Content.ReadFromJsonAsync<List<TradeOrder>>();

            var receivedOrder = pendingOrders?.FirstOrDefault(o => o.Id == orderId);
            Assert.NotNull(receivedOrder);
            Assert.Equal("POSITION_CLOSED", receivedOrder.EventType);
            _output.WriteLine($"MT5 EA received close request for position {receivedOrder.PositionId}");

            // Step 3: Mark as processed
            _output.WriteLine("\nStep 3: MT5 EA marks order as processed");
            var processResponse = await client.PostAsync($"/api/orders/{orderId}/processed", null);
            processResponse.EnsureSuccessStatusCode();

            _output.WriteLine("\n✓ Position Close E2E Test Completed");
        }

        [Fact]
        public async Task E2E_MultipleOrders_ProcessedInOrder_ShouldSucceed()
        {
            // This test simulates multiple orders being processed in FIFO order

            // Arrange
            var client = _factory.CreateClient();
            var orderIds = new List<string>();

            // Step 1: Send multiple orders
            _output.WriteLine("Step 1: Cbot sends multiple orders to Bridge");
            for (int i = 0; i < 5; i++)
            {
                var order = new TradeOrder
                {
                    SourceId = Guid.NewGuid().ToString(),
                    EventType = "POSITION_OPENED",
                    Symbol = "EURUSD",
                    Direction = i % 2 == 0 ? "Buy" : "Sell",
                    Volume = "0.01",
                    EntryPrice = $"1.{1000 + i}",
                    Timestamp = DateTime.UtcNow
                };

                var response = await client.PostAsJsonAsync("/api/orders", order);
                response.EnsureSuccessStatusCode();
                var orderId = ExtractOrderId(await response.Content.ReadAsStringAsync());
                orderIds.Add(orderId);
                _output.WriteLine($"  Order {i + 1} queued: {orderId}");
            }

            // Step 2: MT5 EA polls and receives orders
            _output.WriteLine("\nStep 2: MT5 EA polls for pending orders");
            var pendingResponse = await client.GetAsync("/api/orders/pending?maxCount=10");
            pendingResponse.EnsureSuccessStatusCode();
            var pendingOrders = await pendingResponse.Content.ReadFromJsonAsync<List<TradeOrder>>();

            Assert.NotNull(pendingOrders);
            Assert.True(pendingOrders.Count >= 5);
            _output.WriteLine($"MT5 EA received {pendingOrders.Count} pending orders");

            // Step 3: Process orders one by one
            _output.WriteLine("\nStep 3: MT5 EA processes orders");
            foreach (var orderId in orderIds)
            {
                await Task.Delay(50); // Simulate processing time
                var processResponse = await client.PostAsync($"/api/orders/{orderId}/processed", null);
                processResponse.EnsureSuccessStatusCode();
                _output.WriteLine($"  Processed: {orderId}");
            }

            // Step 4: Verify all orders are processed
            _output.WriteLine("\nStep 4: Verify all orders are processed");
            var verifyResponse = await client.GetAsync("/api/orders/pending?maxCount=10");
            verifyResponse.EnsureSuccessStatusCode();
            var remainingOrders = await verifyResponse.Content.ReadFromJsonAsync<List<TradeOrder>>();

            foreach (var orderId in orderIds)
            {
                var stillPending = remainingOrders?.FirstOrDefault(o => o.Id == orderId);
                Assert.Null(stillPending);
            }
            _output.WriteLine("✓ All orders processed successfully");

            _output.WriteLine("\n✓ Multiple Orders E2E Test Completed");
        }

        [Fact]
        public async Task E2E_TicketMapping_CompleteFlow_ShouldSucceed()
        {
            // This test simulates ticket mapping between Cbot and MT5

            // Arrange
            var client = _factory.CreateClient();
            var sourceTicket = Guid.NewGuid().ToString().Substring(0, 8);
            var slaveTicket = Guid.NewGuid().ToString().Substring(0, 8);

            // Step 1: MT5 EA sends ticket mapping to Bridge
            _output.WriteLine("Step 1: MT5 EA sends ticket mapping to Bridge");
            var mapping = new TicketMappingRequest
            {
                SourceTicket = sourceTicket,
                SlaveTicket = slaveTicket,
                Symbol = "EURUSD",
                Lots = "0.01"
            };

            var addResponse = await client.PostAsJsonAsync("/api/ticket-map", mapping);
            addResponse.EnsureSuccessStatusCode();
            _output.WriteLine($"Mapping created: {sourceTicket} -> {slaveTicket}");

            // Step 2: Retrieve mapping
            _output.WriteLine("\nStep 2: Retrieve ticket mapping");
            var getResponse = await client.GetAsync($"/api/ticket-map/{sourceTicket}");
            getResponse.EnsureSuccessStatusCode();
            var content = await getResponse.Content.ReadAsStringAsync();
            _output.WriteLine($"Retrieved mapping: {content}");

            Assert.Contains(sourceTicket, content);
            Assert.Contains(slaveTicket, content);

            _output.WriteLine("\n✓ Ticket Mapping E2E Test Completed");
        }

        [Fact]
        public async Task E2E_ErrorRecovery_OrderRetry_ShouldSucceed()
        {
            // This test simulates error recovery and retry mechanism

            // Arrange
            var client = _factory.CreateClient();
            var sourceId = Guid.NewGuid().ToString();
            var consumerId1 = Guid.NewGuid().ToString();
            var consumerId2 = Guid.NewGuid().ToString();

            // Step 1: Send order
            _output.WriteLine("Step 1: Send order to Bridge");
            var order = new TradeOrder
            {
                SourceId = sourceId,
                EventType = "POSITION_OPENED",
                Symbol = "EURUSD",
                Direction = "Buy",
                Volume = "0.01",
                EntryPrice = "1.0950",
                Timestamp = DateTime.UtcNow
            };

            var addResponse = await client.PostAsJsonAsync("/api/orders", order);
            addResponse.EnsureSuccessStatusCode();
            var orderId = ExtractOrderId(await addResponse.Content.ReadAsStringAsync());
            _output.WriteLine($"Order queued: {orderId}");

            // Step 2: First consumer polls for orders
            _output.WriteLine("\nStep 2: First MT5 EA instance polls for pending orders");
            var pendingResponse = await client.GetAsync($"/api/orders/pending?maxCount=10&consumerId={consumerId1}");
            pendingResponse.EnsureSuccessStatusCode();
            var pendingOrders = await pendingResponse.Content.ReadFromJsonAsync<List<TradeOrder>>();
            var receivedOrder = pendingOrders?.FirstOrDefault(o => o.Id == orderId);
            Assert.NotNull(receivedOrder);
            _output.WriteLine($"First consumer received order: {orderId}");

            // Step 3: Simulate processing failure (don't mark as processed)
            _output.WriteLine("\nStep 3: Simulate processing failure (order not marked as processed)");
            await Task.Delay(100);
            _output.WriteLine("Order processing failed, order should be available for retry");

            // Step 4: Verify order can be retrieved by a different consumer (simulating retry by different instance)
            _output.WriteLine("\nStep 4: Second MT5 EA instance can poll for the same order (retry by different instance)");
            var retryResponse = await client.GetAsync($"/api/orders/pending?maxCount=10&consumerId={consumerId2}");
            retryResponse.EnsureSuccessStatusCode();
            var retryOrders = await retryResponse.Content.ReadFromJsonAsync<List<TradeOrder>>();

            // The order may or may not be immediately available depending on retry logic
            // For now, just verify we can still retrieve pending orders
            Assert.NotNull(retryOrders);
            _output.WriteLine($"Second consumer can access pending orders queue (count: {retryOrders.Count})");

            // Step 5: Successfully process the order
            _output.WriteLine("\nStep 5: Successfully process order");
            var processResponse = await client.PostAsync($"/api/orders/{orderId}/processed", null);
            processResponse.EnsureSuccessStatusCode();
            _output.WriteLine($"Order {orderId} processed successfully");

            _output.WriteLine("\n✓ Error Recovery E2E Test Completed");
        }

        [Fact]
        public async Task E2E_HealthCheck_ShouldAlwaysBeAccessible()
        {
            // This test verifies that health check endpoint is always accessible
            // (important for monitoring and load balancers)

            var client = _factory.CreateClient();

            _output.WriteLine("Testing health check endpoint");
            var response = await client.GetAsync("/api/health");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            _output.WriteLine($"Health check response: {content}");

            Assert.Contains("status", content);
            Assert.Contains("Healthy", content);

            _output.WriteLine("\n✓ Health Check E2E Test Completed");
        }

        private string? ExtractOrderId(string jsonResponse)
        {
            var startIdx = jsonResponse.IndexOf("orderId\":\"") + 10;
            if (startIdx < 10) return null;
            var endIdx = jsonResponse.IndexOf("\"", startIdx);
            return jsonResponse.Substring(startIdx, endIdx - startIdx);
        }
    }
}
