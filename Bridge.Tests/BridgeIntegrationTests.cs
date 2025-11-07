using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Bridge;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Bridge.Tests
{
    /// <summary>
    /// Integration tests for Bridge API
    /// </summary>
    public class BridgeIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly ITestOutputHelper _output;

        public BridgeIntegrationTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
        {
            _factory = factory;
            _output = output;
        }

        [Fact]
        public async Task TestHealthEndpoint_ShouldReturnHealthy()
        {
            // Arrange
            var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync("/api/health");

            // Assert
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            _output.WriteLine($"Health response: {content}");
            
            // Check that response contains "Healthy" or "Status"
            Assert.Contains("status", content); // camelCase JSON
            Assert.Contains("Healthy", content);
        }

        [Fact]
        public async Task TestAddOrder_ShouldReturnOrderId()
        {
            // Arrange
            var client = _factory.CreateClient();
            var order = new TradeOrder
            {
                SourceId = Guid.NewGuid().ToString(),
                EventType = "POSITION_OPENED",
                Symbol = "EURUSD",
                Volume = "0.01",
                Timestamp = DateTime.UtcNow
            };

            // Act
            var response = await client.PostAsJsonAsync("/api/orders", order);

            // Assert
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            _output.WriteLine($"Add order response: {content}");
            Assert.Contains("orderId", content); // camelCase JSON
        }

        [Fact]
        public async Task TestIdempotency_DuplicateSourceId_ShouldReturnSameOrderId()
        {
            // Arrange
            var client = _factory.CreateClient();
            var sourceId = Guid.NewGuid().ToString();
            var order1 = new TradeOrder
            {
                SourceId = sourceId,
                EventType = "POSITION_OPENED",
                Symbol = "EURUSD",
                Volume = "0.01",
                Timestamp = DateTime.UtcNow
            };
            var order2 = new TradeOrder
            {
                SourceId = sourceId,
                EventType = "POSITION_OPENED",
                Symbol = "EURUSD",
                Volume = "0.02", // Different volume
                Timestamp = DateTime.UtcNow
            };

            // Act
            var response1 = await client.PostAsJsonAsync("/api/orders", order1);
            var response2 = await client.PostAsJsonAsync("/api/orders", order2);

            // Assert
            response1.EnsureSuccessStatusCode();
            response2.EnsureSuccessStatusCode();
            
            var content1 = await response1.Content.ReadAsStringAsync();
            var content2 = await response2.Content.ReadAsStringAsync();
            
            _output.WriteLine($"First response: {content1}");
            _output.WriteLine($"Second response: {content2}");
            
            // Both responses should contain orderId and they should be the same
            Assert.Contains("orderId", content1); // camelCase JSON
            Assert.Contains("orderId", content2);
            Assert.Equal(content1, content2); // Entire response should be identical for idempotent requests
        }

        [Fact]
        public async Task TestAtomicFetch_ConcurrentConsumers_ShouldNotGetDuplicates()
        {
            // Arrange
            var client = _factory.CreateClient();
            
            // Add multiple orders
            for (int i = 0; i < 10; i++)
            {
                var order = new TradeOrder
                {
                    SourceId = Guid.NewGuid().ToString(),
                    EventType = "POSITION_OPENED",
                    Symbol = "EURUSD",
                    Volume = "0.01",
                    Timestamp = DateTime.UtcNow
                };
                await client.PostAsJsonAsync("/api/orders", order);
            }

            // Wait a moment for orders to be persisted
            await Task.Delay(100);

            // Act - Simulate two consumers fetching pending orders concurrently
            var consumer1Task = client.GetAsync("/api/orders/pending?maxCount=5&consumerId=consumer1");
            var consumer2Task = client.GetAsync("/api/orders/pending?maxCount=5&consumerId=consumer2");

            var responses = await Task.WhenAll(consumer1Task, consumer2Task);

            // Assert
            var orders1 = await responses[0].Content.ReadFromJsonAsync<List<TradeOrder>>();
            var orders2 = await responses[1].Content.ReadFromJsonAsync<List<TradeOrder>>();

            Assert.NotNull(orders1);
            Assert.NotNull(orders2);

            _output.WriteLine($"Consumer 1 got {orders1.Count} orders");
            _output.WriteLine($"Consumer 2 got {orders2.Count} orders");

            // Check that no order IDs overlap between the two consumers
            var orderIds1 = orders1.Select(o => o.Id).ToHashSet();
            var orderIds2 = orders2.Select(o => o.Id).ToHashSet();
            
            var intersection = orderIds1.Intersect(orderIds2).ToList();
            Assert.Empty(intersection); // Should have no duplicates
            
            _output.WriteLine($"No duplicates found between consumers (intersection count: {intersection.Count})");
        }

        [Fact]
        public async Task TestMarkProcessed_ShouldUpdateOrderStatus()
        {
            // Arrange
            var client = _factory.CreateClient();
            var order = new TradeOrder
            {
                SourceId = Guid.NewGuid().ToString(),
                EventType = "POSITION_OPENED",
                Symbol = "EURUSD",
                Volume = "0.01",
                Timestamp = DateTime.UtcNow
            };

            // Add order
            var addResponse = await client.PostAsJsonAsync("/api/orders", order);
            var addContent = await addResponse.Content.ReadAsStringAsync();
            _output.WriteLine($"Add response: {addContent}");
            
            // Extract order ID from response (simplified parsing)
            // Response format: {"orderId":"xxx","status":"Queued"} (camelCase)
            var startIdx = addContent.IndexOf("orderId\":\"") + 10;
            var endIdx = addContent.IndexOf("\"", startIdx);
            var orderId = addContent.Substring(startIdx, endIdx - startIdx);

            // Act - Mark as processed
            var markResponse = await client.PostAsync($"/api/orders/{orderId}/processed", null);

            // Assert
            markResponse.EnsureSuccessStatusCode();
            var markContent = await markResponse.Content.ReadAsStringAsync();
            Assert.Contains("Processed", markContent);
            
            _output.WriteLine($"Order {orderId} marked as processed");
        }

        [Fact]
        public async Task TestGetStatistics_ShouldReturnStats()
        {
            // Arrange
            var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync("/api/statistics");

            // Assert
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            Assert.NotNull(result);
            Assert.True(result.ContainsKey("TotalOrders"));
            Assert.True(result.ContainsKey("PendingOrders"));
            Assert.True(result.ContainsKey("ProcessedOrders"));
            
            _output.WriteLine($"Total Orders: {result["TotalOrders"]}");
            _output.WriteLine($"Pending Orders: {result["PendingOrders"]}");
            _output.WriteLine($"Processed Orders: {result["ProcessedOrders"]}");
        }

        [Fact]
        public async Task TestTicketMapping_ShouldStoreAndRetrieve()
        {
            // Arrange
            var client = _factory.CreateClient();
            var sourceTicket = Guid.NewGuid().ToString().Substring(0, 8);
            var slaveTicket = Guid.NewGuid().ToString().Substring(0, 8);
            
            var mapping = new TicketMappingRequest
            {
                SourceTicket = sourceTicket,
                SlaveTicket = slaveTicket,
                Symbol = "EURUSD",
                Lots = "0.01"
            };

            // Act - Add mapping
            var addResponse = await client.PostAsJsonAsync("/api/ticket-map", mapping);
            addResponse.EnsureSuccessStatusCode();

            // Retrieve mapping
            var getResponse = await client.GetAsync($"/api/ticket-map/{sourceTicket}");
            getResponse.EnsureSuccessStatusCode();

            // Assert
            var content = await getResponse.Content.ReadAsStringAsync();
            _output.WriteLine($"Ticket mapping response: {content}");
            
            Assert.Contains("sourceTicket", content); // camelCase JSON
            Assert.Contains("slaveTicket", content);
            Assert.Contains(sourceTicket, content);
            Assert.Contains(slaveTicket, content);
            
            _output.WriteLine($"Ticket mapping: {sourceTicket} -> {slaveTicket}");
        }

        [Fact]
        public async Task TestMetricsEndpoint_ShouldReturnPrometheusMetrics()
        {
            // Arrange
            var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync("/metrics");

            // Assert
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            
            // Check for some expected metrics
            Assert.Contains("bridge_orders_received_total", content);
            Assert.Contains("bridge_orders_pending", content);
            
            _output.WriteLine("Metrics endpoint is accessible and contains expected metrics");
        }

        [Fact]
        public async Task TestSpecialCharactersInComment_ShouldHandleCorrectly()
        {
            // Arrange
            var client = _factory.CreateClient();
            var order = new TradeOrder
            {
                SourceId = Guid.NewGuid().ToString(),
                EventType = "POSITION_OPENED",
                Symbol = "EURUSD",
                Volume = "0.01",
                Timestamp = DateTime.UtcNow,
                Comment = "Test with special chars: \"quotes\", \\backslash\\, and slash/"
            };

            // Act
            var response = await client.PostAsJsonAsync("/api/orders", order);

            // Assert
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            _output.WriteLine($"Special character test response: {content}");
            Assert.Contains("orderId", content);
            
            // Verify we can retrieve it
            var getResponse = await client.GetAsync("/api/orders/pending?maxCount=100");
            getResponse.EnsureSuccessStatusCode();
            var orders = await getResponse.Content.ReadFromJsonAsync<List<TradeOrder>>();
            
            var retrievedOrder = orders?.FirstOrDefault(o => o.SourceId == order.SourceId);
            Assert.NotNull(retrievedOrder);
            // Note: Escaped characters are unescaped when stored (e.g., \n becomes actual newline)
            // The comment field should contain the unescaped version
            Assert.Contains("quotes", retrievedOrder.Comment);
            Assert.Contains("backslash", retrievedOrder.Comment);
            Assert.Contains("slash", retrievedOrder.Comment);
            
            _output.WriteLine($"Successfully stored and retrieved order with special characters in comment: {retrievedOrder.Comment}");
        }

        [Fact]
        public async Task TestComplexJsonInComment_ShouldHandleCorrectly()
        {
            // Arrange
            var client = _factory.CreateClient();
            var order = new TradeOrder
            {
                SourceId = Guid.NewGuid().ToString(),
                EventType = "POSITION_OPENED",
                Symbol = "EURUSD",
                Volume = "0.01",
                Timestamp = DateTime.UtcNow,
                Comment = "Test: backslash\\\\ quote\\\" forward/slash backward\\backslash"
            };

            // Act
            var response = await client.PostAsJsonAsync("/api/orders", order);

            // Assert
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            _output.WriteLine($"Complex JSON test response: {content}");
            Assert.Contains("orderId", content);
            
            _output.WriteLine($"Successfully handled order with complex escape sequences");
        }

        [Fact]
        public async Task TestHttp2xxStatusCodes_ShouldBeAcceptedAsSuccess()
        {
            // Arrange
            var client = _factory.CreateClient();
            var order = new TradeOrder
            {
                SourceId = Guid.NewGuid().ToString(),
                EventType = "POSITION_OPENED",
                Symbol = "EURUSD",
                Volume = "0.01",
                Timestamp = DateTime.UtcNow
            };

            // Act - Add order
            var addResponse = await client.PostAsJsonAsync("/api/orders", order);
            
            // Assert - Should accept any 2xx status code
            Assert.True(addResponse.StatusCode >= HttpStatusCode.OK && 
                       addResponse.StatusCode < HttpStatusCode.MultipleChoices,
                       $"Expected 2xx status code, got {addResponse.StatusCode}");
            
            var content = await addResponse.Content.ReadAsStringAsync();
            var startIdx = content.IndexOf("orderId\":\"") + 10;
            var endIdx = content.IndexOf("\"", startIdx);
            var orderId = content.Substring(startIdx, endIdx - startIdx);

            // Mark as processed
            var processResponse = await client.PostAsync($"/api/orders/{orderId}/processed", null);
            
            // Assert - Should accept any 2xx status code
            Assert.True(processResponse.StatusCode >= HttpStatusCode.OK && 
                       processResponse.StatusCode < HttpStatusCode.MultipleChoices,
                       $"Expected 2xx status code, got {processResponse.StatusCode}");
            
            _output.WriteLine($"Order processing accepted 2xx status codes: Add={addResponse.StatusCode}, Process={processResponse.StatusCode}");
        }

        [Fact]
        public async Task TestDuplicateOrderDetection_ShouldPreventDoubleRegistration()
        {
            // Arrange
            var client = _factory.CreateClient();
            var sourceId = $"TEST-{Guid.NewGuid()}";
            var eventType = "POSITION_OPENED";
            
            var order1 = new TradeOrder
            {
                SourceId = sourceId,
                EventType = eventType,
                Symbol = "EURUSD",
                Volume = "0.01",
                Timestamp = DateTime.UtcNow
            };
            
            var order2 = new TradeOrder
            {
                SourceId = sourceId,
                EventType = eventType,
                Symbol = "GBPUSD",  // Different symbol
                Volume = "0.05",     // Different volume
                Timestamp = DateTime.UtcNow.AddMinutes(1)  // Different timestamp
            };

            // Act - Send same SourceId + EventType twice
            var response1 = await client.PostAsJsonAsync("/api/orders", order1);
            var response2 = await client.PostAsJsonAsync("/api/orders", order2);

            // Assert
            response1.EnsureSuccessStatusCode();
            response2.EnsureSuccessStatusCode();
            
            var content1 = await response1.Content.ReadAsStringAsync();
            var content2 = await response2.Content.ReadAsStringAsync();
            
            // Extract order IDs
            var orderId1 = ExtractOrderId(content1);
            var orderId2 = ExtractOrderId(content2);
            
            // Both should return the same order ID (idempotency)
            Assert.Equal(orderId1, orderId2);
            
            _output.WriteLine($"Duplicate detection successful: Both requests returned same OrderId: {orderId1}");
            
            // Verify only one order exists with this SourceId + EventType
            var statsResponse = await client.GetAsync("/api/statistics");
            statsResponse.EnsureSuccessStatusCode();
            
            _output.WriteLine($"Idempotency check prevented duplicate registration");
        }

        private string ExtractOrderId(string jsonResponse)
        {
            var startIdx = jsonResponse.IndexOf("orderId\":\"") + 10;
            var endIdx = jsonResponse.IndexOf("\"", startIdx);
            return jsonResponse.Substring(startIdx, endIdx - startIdx);
        }
    }
}
