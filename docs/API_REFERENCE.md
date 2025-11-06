# Bridge API Reference

Complete API reference for the Bridge service with all endpoints.

## Authentication

All endpoints (except `/api/health` and `/metrics`) require API key authentication via the `X-API-KEY` header.

```bash
X-API-KEY: your-api-key-here
```

Configure the API key in `appsettings.json`:
```json
{
  "Bridge": {
    "ApiKey": "your-secure-api-key"
  }
}
```

## Endpoints

### Order Management

#### Receive Order from cTrader
```http
POST /api/orders
Content-Type: application/json
X-API-KEY: your-api-key

{
  "SourceId": "12345",
  "EventType": "POSITION_OPENED",
  "Timestamp": "2025-01-01T00:00:00Z",
  "PositionId": 12345,
  "Symbol": "EURUSD",
  "Direction": "Buy",
  "OrderType": "Market",
  "Volume": "0.01",
  "EntryPrice": "1.1000",
  "StopLoss": "1.0950",
  "TakeProfit": "1.1100",
  "Comment": "Test order"
}
```

**Response:**
```json
{
  "OrderId": "guid",
  "Status": "Queued"
}
```

**Valid Event Types:**
- `POSITION_OPENED`
- `POSITION_CLOSED`
- `POSITION_MODIFIED`
- `PENDING_ORDER_CREATED`
- `PENDING_ORDER_CANCELLED`
- `PENDING_ORDER_FILLED`

#### Get Pending Orders for MT5
```http
GET /api/orders/pending?maxCount=10
X-API-KEY: your-api-key
```

**Response:**
```json
[
  {
    "Id": "guid",
    "SourceId": "12345",
    "EventType": "POSITION_OPENED",
    "Symbol": "EURUSD",
    ...
  }
]
```

#### Mark Order as Processed
```http
POST /api/orders/{orderId}/processed
X-API-KEY: your-api-key
```

**Response:**
```json
{
  "Status": "Processed"
}
```

#### Get Order by ID
```http
GET /api/orders/{orderId}
X-API-KEY: your-api-key
```

**Response:**
```json
{
  "Id": "guid",
  "SourceId": "12345",
  "EventType": "POSITION_OPENED",
  "Processed": false,
  ...
}
```

### Ticket Mapping

#### Add Ticket Mapping
```http
POST /api/ticket-map
Content-Type: application/json
X-API-KEY: your-api-key

{
  "SourceTicket": "12345",
  "SlaveTicket": "67890",
  "Symbol": "EURUSD",
  "Lots": "0.01"
}
```

**Response:**
```json
{
  "Status": "Mapping added"
}
```

#### Get Ticket Mapping
```http
GET /api/ticket-map/{sourceTicket}
X-API-KEY: your-api-key
```

**Response:**
```json
{
  "SourceTicket": "12345",
  "SlaveTicket": "67890"
}
```

### Monitoring & Management

#### Get System Status
```http
GET /api/status
X-API-KEY: your-api-key
```

**Response:**
```json
{
  "Status": "Running",
  "Timestamp": "2025-01-01T00:00:00Z",
  "Version": "1.0.0",
  "QueueStatistics": {
    "TotalOrders": 1000,
    "PendingOrders": 5,
    "ProcessedOrders": 995,
    "OrdersLast5Min": 12
  },
  "Uptime": "1.05:30:15"
}
```

#### Get Queue Details
```http
GET /api/queue?maxCount=100
X-API-KEY: your-api-key
```

**Response:**
```json
{
  "Count": 5,
  "Orders": [...]
}
```

#### Get Queue Statistics
```http
GET /api/statistics
X-API-KEY: your-api-key
```

**Response:**
```json
{
  "TotalOrders": 1000,
  "PendingOrders": 5,
  "ProcessedOrders": 995,
  "OrdersLast5Min": 12
}
```

#### Retry Failed Order
```http
POST /api/retry/{orderId}
X-API-KEY: your-api-key
```

**Response:**
```json
{
  "Status": "Scheduled for retry"
}
```

#### Health Check (No Auth Required)
```http
GET /api/health
```

**Response:**
```json
{
  "Status": "Healthy",
  "Timestamp": "2025-01-01T00:00:00Z"
}
```

#### Prometheus Metrics (No Auth Required)
```http
GET /metrics
```

Returns Prometheus-formatted metrics.

## Error Responses

All endpoints return standard error responses:

**400 Bad Request:**
```json
{
  "Error": "Error message"
}
```

**401 Unauthorized:**
```json
{
  "Error": "API Key is required"
}
```

**404 Not Found:**
```json
{
  "Error": "Order not found"
}
```

**429 Too Many Requests:**
```json
{
  "Error": "Rate limit exceeded"
}
```

**500 Internal Server Error:**
```json
{
  "Error": "Internal server error"
}
```

## Features

### Idempotency

Orders are deduplicated using `SourceId + EventType`. Submitting the same order multiple times returns the existing order ID with 200 OK.

### Retry Mechanism

Failed orders can be retried:
- Manual retry via `/api/retry/{orderId}`
- Automatic retry with exponential backoff (configured in `appsettings.json`)

**Configuration:**
```json
{
  "Bridge": {
    "Retry": {
      "MaxRetryCount": 3,
      "InitialDelaySeconds": 10,
      "MaxDelaySeconds": 300
    }
  }
}
```

### Rate Limiting

IP-based rate limiting to prevent abuse.

**Configuration:**
```json
{
  "Bridge": {
    "RateLimiting": {
      "Enabled": true,
      "MaxRequestsPerMinute": 60,
      "WhitelistedIPs": [
        "192.168.1.100"
      ]
    }
  }
}
```

### Ticket Mapping

Maps source tickets (cTrader) to slave tickets (MT5) for position tracking.

### Alert System

Sends alerts via Slack, Telegram, or Email.

**Configuration:**
```json
{
  "Bridge": {
    "Alerts": {
      "Enabled": true,
      "SlackWebhookUrl": "https://hooks.slack.com/...",
      "TelegramBotToken": "bot-token",
      "TelegramChatId": "chat-id",
      "EmailSmtpHost": "smtp.gmail.com",
      "EmailSmtpPort": 587,
      "EmailUsername": "alerts@example.com",
      "EmailPassword": "password",
      "EmailTo": "admin@example.com"
    }
  }
}
```

## Data Persistence

### SQLite Database

Two tables:
- `Orders`: Queue of trade orders
- `TicketMap`: Mappings between source and slave tickets

### Database Schema

**Orders Table:**
```sql
CREATE TABLE Orders (
    Id TEXT PRIMARY KEY,
    SourceId TEXT NOT NULL,
    EventType TEXT NOT NULL,
    Timestamp TEXT NOT NULL,
    Symbol TEXT NOT NULL,
    Direction TEXT,
    OrderType TEXT,
    Volume TEXT,
    EntryPrice TEXT,
    StopLoss TEXT,
    TakeProfit TEXT,
    Processed INTEGER NOT NULL DEFAULT 0,
    ProcessedAt TEXT,
    CreatedAt TEXT NOT NULL,
    RetryCount INTEGER NOT NULL DEFAULT 0,
    LastRetryAt TEXT,
    NextRetryAt TEXT,
    UNIQUE(SourceId, EventType)
);
```

**TicketMap Table:**
```sql
CREATE TABLE TicketMap (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SourceTicket TEXT NOT NULL,
    SlaveTicket TEXT NOT NULL,
    Symbol TEXT NOT NULL,
    Lots TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    UNIQUE(SourceTicket)
);
```

## Security Features

1. **API Key Authentication**: Required for all sensitive endpoints
2. **Rate Limiting**: Prevents abuse and DoS attacks
3. **Input Sanitization**: Removes control characters to prevent injection
4. **Input Validation**: Validates all request parameters
5. **HTTPS Support**: Via nginx reverse proxy or direct HTTPS
6. **IP Whitelisting**: Allows trusted IPs to bypass rate limits
7. **Structured Logging**: Prevents log forging attacks

## Usage Examples

### cURL Examples

**Submit Order:**
```bash
curl -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -H "X-API-KEY: your-key" \
  -d '{
    "SourceId": "12345",
    "EventType": "POSITION_OPENED",
    "Timestamp": "2025-01-01T00:00:00Z",
    "Symbol": "EURUSD",
    "Direction": "Buy",
    "Volume": "0.01"
  }'
```

**Get Pending Orders:**
```bash
curl http://localhost:5000/api/orders/pending \
  -H "X-API-KEY: your-key"
```

**Mark as Processed:**
```bash
curl -X POST http://localhost:5000/api/orders/{orderId}/processed \
  -H "X-API-KEY: your-key"
```

**Add Ticket Mapping:**
```bash
curl -X POST http://localhost:5000/api/ticket-map \
  -H "Content-Type: application/json" \
  -H "X-API-KEY: your-key" \
  -d '{
    "SourceTicket": "12345",
    "SlaveTicket": "67890",
    "Symbol": "EURUSD",
    "Lots": "0.01"
  }'
```

**Check Status:**
```bash
curl http://localhost:5000/api/status \
  -H "X-API-KEY: your-key"
```

### C# Example (MT5 EA)

```csharp
// Get pending orders
string url = "http://localhost:5000/api/orders/pending?maxCount=10";
string headers = "X-API-KEY: your-key\r\n";
char data[];
string result;
int timeout = 5000;

int res = WebRequest("GET", url, headers, timeout, data, result);
if (res == 200) {
    // Parse JSON response
    // Process orders
}

// Mark order as processed
url = "http://localhost:5000/api/orders/" + orderId + "/processed";
res = WebRequest("POST", url, headers, timeout, data, result);

// Add ticket mapping
url = "http://localhost:5000/api/ticket-map";
string body = "{\"SourceTicket\":\"12345\",\"SlaveTicket\":\"67890\",\"Symbol\":\"EURUSD\",\"Lots\":\"0.01\"}";
StringToCharArray(body, data);
headers += "Content-Type: application/json\r\n";
res = WebRequest("POST", url, headers, timeout, data, result);
```

## Monitoring

### Health Check

Regular health checks should be performed:
```bash
*/1 * * * * curl http://localhost:5000/api/health || echo "Bridge is down!"
```

### Prometheus Metrics

Integrate with Prometheus/Grafana for monitoring:
```yaml
scrape_configs:
  - job_name: 'bridge'
    static_configs:
      - targets: ['localhost:5000']
```

### Log Monitoring

Monitor logs for errors:
```bash
tail -f logs/bridge-*.log | grep "ERROR"
```
