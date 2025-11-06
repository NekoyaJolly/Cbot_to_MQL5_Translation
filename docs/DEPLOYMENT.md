# Bridge Deployment and Security Guide

This guide covers the production deployment requirements for the Bridge service.

## A. Authentication & TLS (Required)

### API Key Authentication

The bridge implements API key authentication via the `X-API-KEY` header.

**Configuration in appsettings.json:**
```json
{
  "Bridge": {
    "ApiKey": "your-secure-api-key-here"
  }
}
```

**Usage:**
All API requests (except `/api/health` and `/metrics`) must include the header:
```
X-API-KEY: your-secure-api-key-here
```

### HTTPS/TLS Setup

#### Option 1: Using nginx with Let's Encrypt (Recommended)

1. Install nginx and certbot:
```bash
sudo apt-get update
sudo apt-get install nginx certbot python3-certbot-nginx
```

2. Configure nginx reverse proxy (`/etc/nginx/sites-available/bridge`):
```nginx
server {
    listen 80;
    server_name your-domain.com;

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

3. Enable the site:
```bash
sudo ln -s /etc/nginx/sites-available/bridge /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl restart nginx
```

4. Get SSL certificate:
```bash
sudo certbot --nginx -d your-domain.com
```

#### Option 2: Cloud Load Balancer

When deploying behind AWS ALB, Azure Application Gateway, or GCP Load Balancer:
- Configure TLS termination at the load balancer
- The bridge can run on HTTP internally
- Ensure proper security groups/firewall rules

#### Option 3: Direct HTTPS in ASP.NET Core

**appsettings.json:**
```json
{
  "Bridge": {
    "ListenUrl": "https://0.0.0.0:5001",
    "EnableHttps": true,
    "CertificatePath": "/path/to/certificate.pfx",
    "CertificatePassword": "certificate-password"
  }
}
```

**Program.cs modification required:**
```csharp
if (context.Configuration.GetValue<bool>("Bridge:EnableHttps"))
{
    webBuilder.UseKestrel(options =>
    {
        options.ConfigureHttpsDefaults(httpsOptions =>
        {
            var certPath = context.Configuration["Bridge:CertificatePath"];
            var certPassword = context.Configuration["Bridge:CertificatePassword"];
            httpsOptions.ServerCertificate = new X509Certificate2(certPath, certPassword);
        });
    });
}
```

## B. Persistent Queue (Implemented)

The bridge uses SQLite for persistent queue storage with:
- FIFO ordering
- Retry mechanism with exponential backoff
- Delayed retry capability

**Configuration:**
```json
{
  "Bridge": {
    "DatabasePath": "bridge.db",
    "Retry": {
      "MaxRetryCount": 3,
      "InitialDelaySeconds": 10,
      "MaxDelaySeconds": 300
    }
  }
}
```

**Upgrading to Redis/RabbitMQ:**
For multi-instance deployments, replace SQLite with external queue:
- Redis with pub/sub
- RabbitMQ with durable queues
- AWS SQS / Azure Service Bus

## C. Idempotency (Implemented)

Orders are deduplicated using `SourceId + EventType`. Duplicate orders return 200 OK with existing order ID.

## D. Ticket Mapping (Implemented)

The `TicketMap` table stores mappings between source and slave tickets.

**API Endpoints:**
```bash
# Add mapping
POST /api/ticket-map
Content-Type: application/json
X-API-KEY: your-key

{
  "SourceTicket": "12345",
  "SlaveTicket": "67890",
  "Symbol": "EURUSD",
  "Lots": "0.01"
}

# Get mapping
GET /api/ticket-map/12345
X-API-KEY: your-key
```

## E. Management/Monitoring APIs (Implemented)

### Status Endpoint
```bash
GET /api/status
X-API-KEY: your-key
```

Returns:
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

### Queue Endpoint
```bash
GET /api/queue?maxCount=100
X-API-KEY: your-key
```

### Retry Endpoint
```bash
POST /api/retry/{orderId}
X-API-KEY: your-key
```

### Health Check (No Auth Required)
```bash
GET /api/health
```

### Prometheus Metrics (No Auth Required)
```bash
GET /metrics
```

## F. Operational Logs & Alerts

### Logging (Implemented)

Serilog is configured with:
- Console output
- Daily rolling file logs in `logs/bridge-.log`
- 30-day retention
- Structured logging with context

### Alert Integration (To Be Implemented)

**Configuration for alerts:**
```json
{
  "Bridge": {
    "Alerts": {
      "Enabled": true,
      "SlackWebhookUrl": "https://hooks.slack.com/services/...",
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

Alert triggers:
- Queue size exceeds threshold
- Order processing failure rate > 5%
- API errors
- Database connection failures

## G. Scaling Considerations

### Single Instance (Current)
- SQLite persistent queue
- Suitable for most use cases
- Simple deployment

### Multi-Instance (Future)
Requirements for horizontal scaling:
1. Replace SQLite with external queue (Redis/RabbitMQ)
2. Shared database for ticket mappings
3. Load balancer with session affinity
4. Distributed locking for queue operations

Example Redis configuration:
```json
{
  "Bridge": {
    "Queue": {
      "Type": "Redis",
      "ConnectionString": "localhost:6379"
    }
  }
}
```

## H. Rate Limiting & Authorization (Implemented)

### Rate Limiting

**Configuration:**
```json
{
  "Bridge": {
    "RateLimiting": {
      "Enabled": true,
      "MaxRequestsPerMinute": 60,
      "WhitelistedIPs": [
        "192.168.1.100",
        "10.0.0.50"
      ]
    }
  }
}
```

Features:
- IP-based rate limiting
- 429 Too Many Requests response
- IP whitelist for trusted clients
- Health and metrics endpoints excluded

### IP Whitelist
Add trusted IPs to bypass rate limiting (e.g., MT5 server IPs).

## I. Production Deployment Checklist

- [ ] Set secure API key in configuration
- [ ] Enable HTTPS (nginx + Let's Encrypt or cloud load balancer)
- [ ] Configure rate limiting
- [ ] Add MT5 server IPs to whitelist
- [ ] Set up log rotation and monitoring
- [ ] Configure alerts (Slack/Telegram/Email)
- [ ] Test failover and retry mechanisms
- [ ] Document backup procedures for SQLite database
- [ ] Set up health check monitoring
- [ ] Review security headers (CORS, HSTS, etc.)

## Systemd Service Configuration

Create `/etc/systemd/system/bridge.service`:
```ini
[Unit]
Description=Trading Bridge Service
After=network.target

[Service]
Type=notify
WorkingDirectory=/opt/bridge
ExecStart=/usr/bin/dotnet /opt/bridge/Bridge.dll
Restart=always
RestartSec=10
User=bridge
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

Enable and start:
```bash
sudo systemctl enable bridge
sudo systemctl start bridge
sudo systemctl status bridge
```

## Monitoring Commands

```bash
# Check logs
tail -f logs/bridge-*.log

# Check metrics
curl http://localhost:5000/metrics

# Check status
curl -H "X-API-KEY: your-key" http://localhost:5000/api/status

# Check queue
curl -H "X-API-KEY: your-key" http://localhost:5000/api/queue
```

## Backup and Recovery

### Backup SQLite Database
```bash
# Stop service
sudo systemctl stop bridge

# Backup database
cp bridge.db bridge.db.backup

# Start service
sudo systemctl start bridge
```

### Recovery
```bash
# Stop service
sudo systemctl stop bridge

# Restore database
cp bridge.db.backup bridge.db

# Start service
sudo systemctl start bridge
```

## Security Best Practices

1. **Strong API Key**: Use at least 32 characters with high entropy
2. **HTTPS Only**: Never expose HTTP in production
3. **Firewall Rules**: Restrict access to known IPs only
4. **Regular Updates**: Keep .NET runtime and dependencies updated
5. **Log Monitoring**: Monitor logs for suspicious activity
6. **Secrets Management**: Use environment variables or secret managers for sensitive data
7. **Backup Strategy**: Regular automated backups of SQLite database
8. **Principle of Least Privilege**: Run service with dedicated user account
