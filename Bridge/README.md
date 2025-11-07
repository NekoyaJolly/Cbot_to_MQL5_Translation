# Bridge Service - Central Order Broker

Bridge is a .NET 8.0 service that acts as a central broker between cTrader master and MT5 slave instances. It provides persistent storage, idempotency, retry mechanisms, and monitoring capabilities.

## Features

### Core Functionality
- ✅ **Persistent Order Queue** - SQLite-based storage for reliability
- ✅ **Atomic Order Fetching** - Transaction-based locking prevents duplicate processing
- ✅ **Idempotency** - Duplicate orders (same SourceId + EventType) are automatically rejected
- ✅ **Automatic Retry** - Failed orders are automatically retried with exponential backoff
- ✅ **Ticket Mapping** - Maps source tickets to slave tickets for tracking
- ✅ **Stale Order Recovery** - Automatically releases stuck orders after timeout
- ✅ **Health Monitoring** - Database connectivity checks
- ✅ **Prometheus Metrics** - Comprehensive metrics export for monitoring

### Security
- ✅ **API Key Authentication** - X-API-KEY header validation (optional but recommended)
- ✅ **Rate Limiting** - Configurable per-IP rate limits
- ✅ **Input Sanitization** - Prevents log forging and injection attacks
- ✅ **CORS Protection** - Configurable allowed origins

## API Endpoints

### Order Management

#### POST /api/orders
Receive a new order from cTrader master.

**Request Body:**
```json
{
  "sourceId": "12345",
  "eventType": "POSITION_OPENED",
  "symbol": "EURUSD",
  "volume": "0.01",
  "entryPrice": "1.0850",
  "stopLoss": "1.0800",
  "takeProfit": "1.0900",
  "timestamp": "2025-11-07T16:00:00Z"
}
```

**Response:**
```json
{
  "orderId": "guid",
  "status": "Queued"
}
```

**Notes:**
- `sourceId` is required for idempotency
- Duplicate orders with the same `sourceId` and `eventType` return the existing order ID
- Numeric fields are stored as strings to preserve exact formatting

#### GET /api/orders/pending
Fetch pending orders for MT5 slave processing.

**Query Parameters:**
- `maxCount` (optional, default: 10, max: 100) - Maximum number of orders to fetch
- `consumerId` (optional) - Consumer identifier for tracking

**Response:**
```json
[
  {
    "id": "guid",
    "sourceId": "12345",
    "eventType": "POSITION_OPENED",
    "symbol": "EURUSD",
    "volume": "0.01",
    ...
  }
]
```

**Notes:**
- Orders are atomically locked during fetch to prevent duplicate processing
- Each order is marked with `Processing=1`, `ProcessingBy=consumerId`, `ProcessingAt=timestamp`
- Orders not marked as processed will be automatically retried after timeout

#### POST /api/orders/{orderId}/processed
Mark an order as successfully processed by MT5 slave.

**Response:**
```json
{
  "status": "Processed"
}
```

**Notes:**
- Must be called after successful order execution
- Clears the processing lock and prevents retry

#### GET /api/orders/{orderId}
Get details of a specific order.

**Response:** Returns the full order object or 404 if not found.

### Ticket Mapping

#### POST /api/ticket-map
Store a mapping between source and slave tickets.

**Request Body:**
```json
{
  "sourceTicket": "12345",
  "slaveTicket": "67890",
  "symbol": "EURUSD",
  "lots": "0.01"
}
```

#### GET /api/ticket-map/{sourceTicket}
Retrieve slave ticket for a given source ticket.

**Response:**
```json
{
  "sourceTicket": "12345",
  "slaveTicket": "67890"
}
```

### Monitoring

#### GET /api/health
Health check endpoint with database connectivity test.

**Response:**
```json
{
  "status": "Healthy",
  "timestamp": "2025-11-07T16:00:00Z",
  "database": "Connected"
}
```

#### GET /api/status
Detailed system status and metrics.

**Response:**
```json
{
  "status": "Running",
  "timestamp": "2025-11-07T16:00:00Z",
  "version": "1.0.0",
  "queueStatistics": {
    "totalOrders": 100,
    "pendingOrders": 10,
    "processedOrders": 90,
    "ordersLast5Min": 5
  },
  "uptime": "01:23:45"
}
```

#### GET /api/statistics
Get queue statistics only.

#### GET /api/queue
View current queue state (for debugging).

**Query Parameters:**
- `maxCount` (optional, default: 100, max: 100)

#### GET /metrics
Prometheus metrics endpoint.

**Available Metrics:**
- `bridge_orders_received_total` - Total orders received
- `bridge_orders_processed_total` - Total orders processed successfully
- `bridge_orders_pending` - Current pending orders count
- `bridge_orders_failed_total` - Orders that failed after max retries
- `bridge_retry_queue_size` - Orders waiting for retry
- `bridge_duplicate_orders_total` - Duplicate orders rejected

## Configuration

Configuration is managed via `appsettings.json` and can be overridden with environment variables.

### Key Configuration Options

```json
{
  "Bridge": {
    "ApiKey": "",  // Set to enable API key authentication (recommended for production)
    "DatabasePath": "bridge.db",
    "ListenUrl": "http://0.0.0.0:5000",
    "MaxOrderAge": "01:00:00",  // How long to keep processed orders
    "CleanupInterval": "00:10:00",  // How often to cleanup old orders
    "StaleProcessingTimeout": "00:05:00",  // Timeout for releasing stuck orders
    "RateLimiting": {
      "Enabled": false,  // Enable in production
      "MaxRequestsPerMinute": 60,
      "WhitelistedIPs": []
    },
    "Retry": {
      "MaxRetryCount": 3,
      "InitialDelaySeconds": 10,
      "MaxDelaySeconds": 300
    }
  }
}
```

### Environment Variables

You can override any configuration using environment variables:

```bash
export Bridge__ApiKey="your-secret-api-key"
export Bridge__DatabasePath="/data/bridge.db"
export Bridge__RateLimiting__Enabled=true
```

## Deployment

### Development
```bash
cd Bridge
dotnet run
```

### Production with systemd (Linux)

1. Build the application:
```bash
dotnet publish -c Release -o /opt/bridge
```

2. Create systemd service file `/etc/systemd/system/bridge.service`:
```ini
[Unit]
Description=Bridge Order Broker
After=network.target

[Service]
Type=notify
WorkingDirectory=/opt/bridge
ExecStart=/usr/bin/dotnet /opt/bridge/Bridge.dll
Restart=always
RestartSec=10
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=Bridge__ApiKey=your-secret-key

[Install]
WantedBy=multi-user.target
```

3. Enable and start:
```bash
sudo systemctl enable bridge
sudo systemctl start bridge
sudo systemctl status bridge
```

### Production with Windows Service

Use `sc.exe` or NSSM to run Bridge as a Windows service.

### Nginx Reverse Proxy (Recommended)

```nginx
server {
    listen 443 ssl http2;
    server_name bridge.example.com;

    ssl_certificate /path/to/cert.pem;
    ssl_certificate_key /path/to/key.pem;

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

## Authentication

When API key authentication is enabled, all requests (except `/api/health` and `/metrics`) must include the `X-API-KEY` header:

```bash
curl -H "X-API-KEY: your-secret-key" http://localhost:5000/api/orders/pending
```

## Monitoring with Prometheus

Add to your `prometheus.yml`:

```yaml
scrape_configs:
  - job_name: 'bridge'
    static_configs:
      - targets: ['localhost:5000']
    metrics_path: '/metrics'
```

## Database

Bridge uses SQLite for simplicity and reliability. The database includes:

### Orders Table
- Full order details with processing state
- Automatic retry tracking (RetryCount, NextRetryAt)
- Processing locks (Processing, ProcessingBy, ProcessingAt)
- Idempotency enforcement (UNIQUE constraint on SourceId + EventType)

### TicketMap Table
- Maps source tickets to slave tickets
- Used for tracking and querying order status

### Database Migration

The service automatically migrates the database on startup if needed, adding new columns:
- `Processing` (INTEGER)
- `ProcessingBy` (TEXT)
- `ProcessingAt` (TEXT)

## Background Services

### CleanupService
- Runs every 10 minutes (configurable)
- Removes processed orders older than MaxOrderAge
- Prevents database bloat

### RetryService
- Runs every 30 seconds
- Releases stale processing orders (timeout after 5 minutes)
- Marks orders exceeding max retry count as failed
- Enables automatic recovery from consumer crashes

## Testing

Run integration tests:
```bash
cd Bridge.Tests
dotnet test
```

### Test Coverage
- ✅ Health check endpoint
- ✅ Order submission and retrieval
- ✅ Idempotency (duplicate SourceId rejection)
- ✅ Atomic fetch (no duplicates with concurrent consumers)
- ✅ Mark as processed
- ✅ Ticket mapping
- ✅ Statistics and queue endpoints
- ✅ Prometheus metrics endpoint

## Troubleshooting

### Orders stuck in "Processing" state
- The RetryService will automatically release them after 5 minutes
- Check logs for consumer crashes or network issues

### Database locked errors
- SQLite uses file-level locking
- Ensure only one Bridge instance accesses the database
- For high-concurrency scenarios, consider migrating to PostgreSQL

### High memory usage
- Adjust MaxOrderAge and CleanupInterval to remove old orders more frequently
- Monitor the `bridge_orders_pending` metric

## Architecture

```
┌─────────────┐          ┌──────────────┐          ┌──────────────┐
│  cTrader    │          │    Bridge    │          │     MT5      │
│   Master    │──POST───▶│   Service    │◀──GET────│    Slave     │
│             │          │   (SQLite)   │          │  (Consumer)  │
└─────────────┘          └──────────────┘          └──────────────┘
                               │
                               │ Metrics
                               ▼
                         ┌──────────────┐
                         │  Prometheus  │
                         │   Grafana    │
                         └──────────────┘
```

## Security Best Practices

1. **Enable API Key Authentication** - Set `Bridge:ApiKey` in production
2. **Enable Rate Limiting** - Set `Bridge:RateLimiting:Enabled=true`
3. **Use HTTPS** - Deploy behind Nginx with TLS
4. **Restrict CORS** - Configure allowed origins in Program.cs
5. **Monitor Logs** - Watch for authentication failures and rate limit violations
6. **Backup Database** - Regularly backup `bridge.db`
7. **Rotate API Keys** - Change API keys periodically
8. **Use Environment Variables** - Never commit secrets to git

## License

[Your License Here]

## Support

For issues and questions, please open a GitHub issue.
