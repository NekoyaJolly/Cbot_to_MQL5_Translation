# Production Configuration Guide

This guide explains how to configure the Cbot to MQL5 Translation system for production use with resilience and security features.

## Overview

The system now includes:
- **Persistent storage**: SQLite-based queue prevents message loss
- **Idempotency**: Prevents duplicate order processing
- **Authentication**: X-API-KEY for secure communication
- **Retry mechanism**: Automatic retry of failed messages
- **Structured logging**: Serilog with file and console output
- **Metrics**: Prometheus metrics for monitoring

## cBot Configuration

### Required Parameters

Add these parameters when configuring the cBot in cTrader:

| Parameter | Description | Default | Required |
|-----------|-------------|---------|----------|
| Bridge Server URL | URL of the Bridge server | http://localhost:5000 | Yes |
| Enable Sync | Enable/disable synchronization | true | Yes |
| Bridge API Key | API key for authentication | (empty) | No* |
| Master Label | Label to identify master trades | MASTER | Yes |

*Required if Bridge is configured with API key authentication

### Persistent Queue

The cBot automatically creates a `persist/failed/` directory to store failed messages. 

**Important**: 
- Ensure the cBot has write permissions to create this directory
- Failed messages are stored as JSON log files: `failed_<timestamp>.log`
- On restart, the cBot automatically loads and retries failed messages
- Successfully sent messages are removed from the queue

### Example Configuration

```csharp
// In cTrader Automate, set these parameters:
Bridge Server URL: https://bridge.example.com:5000
Enable Sync: true
Bridge API Key: your-secret-api-key-here
Master Label: MASTER-ACCOUNT-1
```

## Bridge Server Configuration

### appsettings.json

Configure the Bridge server using `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Bridge": {
    "ApiKey": "",
    "DatabasePath": "bridge.db",
    "ListenUrl": "http://0.0.0.0:5000",
    "EnableHttps": false,
    "CertificatePath": "",
    "CertificatePassword": "",
    "MaxOrderAge": "01:00:00",
    "CleanupInterval": "00:10:00"
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/bridge-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30
        }
      }
    ]
  }
}
```

### Configuration Parameters

#### Bridge Section

| Parameter | Description | Default | Production Value |
|-----------|-------------|---------|------------------|
| ApiKey | Secret key for API authentication | (empty) | **Required** - Set a strong secret key |
| DatabasePath | Path to SQLite database file | bridge.db | ./data/bridge.db |
| ListenUrl | URL to listen on | http://0.0.0.0:5000 | http://0.0.0.0:5000 or https:// |
| EnableHttps | Enable HTTPS (requires certificate) | false | true (recommended) |
| CertificatePath | Path to SSL certificate | (empty) | /path/to/cert.pfx |
| CertificatePassword | Certificate password | (empty) | (your password) |
| MaxOrderAge | How long to keep processed orders | 01:00:00 | 01:00:00 to 24:00:00 |
| CleanupInterval | How often to cleanup old orders | 00:10:00 | 00:10:00 to 01:00:00 |

### Environment Variables

You can also use environment variables (recommended for production):

```bash
export Bridge__ApiKey="your-secret-key-here"
export Bridge__DatabasePath="/var/lib/bridge/bridge.db"
export Bridge__ListenUrl="http://0.0.0.0:5000"
```

## Security Configuration

### API Key Authentication

**Highly Recommended for Production**

1. Generate a strong API key:
   ```bash
   # Generate a random API key
   openssl rand -base64 32
   ```

2. Set the API key in Bridge configuration:
   ```json
   {
     "Bridge": {
       "ApiKey": "YourGeneratedKeyHere"
     }
   }
   ```

3. Configure the cBot to use the same API key:
   - Set the "Bridge API Key" parameter in cTrader

### HTTPS/TLS Configuration

**Required for Production**

1. Obtain an SSL certificate (e.g., from Let's Encrypt)

2. Configure the Bridge:
   ```json
   {
     "Bridge": {
       "EnableHttps": true,
       "ListenUrl": "https://0.0.0.0:5000",
       "CertificatePath": "/path/to/certificate.pfx",
       "CertificatePassword": "your-cert-password"
     }
   }
   ```

3. Update cBot URL to use HTTPS:
   ```
   Bridge Server URL: https://bridge.example.com:5000
   ```

## Monitoring

### Prometheus Metrics

The Bridge server exposes Prometheus metrics at `/metrics`:

```bash
curl http://localhost:5000/metrics
```

**Key Metrics:**
- `http_request_duration_seconds` - Request duration histogram
- HTTP request counts by endpoint, method, and status code

### Logs

Logs are written to:
- **Console**: Real-time monitoring
- **File**: `logs/bridge-YYYYMMDD.log` (rotates daily, keeps 30 days)

**Log Levels:**
- Information: Normal operations
- Warning: Non-critical issues
- Error: Critical errors

Example log entry:
```
[2025-11-06 22:20:28 INF] Database initialized at Data Source=bridge.db
[2025-11-06 22:20:28 INF] CleanupService started: MaxAge=01:00:00, Interval=00:10:00
```

## Database Management

### SQLite Database

The Bridge uses SQLite for persistent storage.

**Location**: Configured via `Bridge:DatabasePath`

**Backup**:
```bash
# Stop the Bridge server
# Copy the database file
cp bridge.db bridge-backup-$(date +%Y%m%d).db
# Restart the Bridge server
```

**Schema**:
- `Orders` table stores all trade orders
- Indexes on `Processed`, `SourceId`, and `Timestamp` for performance

### Cleanup

Old processed orders are automatically cleaned up based on:
- `MaxOrderAge`: How old orders must be before cleanup (default: 1 hour)
- `CleanupInterval`: How often cleanup runs (default: 10 minutes)

## Troubleshooting

### cBot Issues

**Failed messages not retrying:**
- Check if `persist/failed/` directory exists
- Check cBot logs for file permission errors
- Verify JSON log files exist in the directory

**Authentication failures:**
- Verify API key matches between cBot and Bridge
- Check Bridge logs for authentication errors

### Bridge Issues

**Database errors:**
- Check file permissions on database file
- Ensure sufficient disk space
- Check logs for specific SQLite errors

**Port already in use:**
- Change `ListenUrl` to use a different port
- Check for other processes using the port: `lsof -i :5000`

## Production Checklist

- [ ] Set a strong API key for authentication
- [ ] Enable HTTPS/TLS with valid certificate
- [ ] Configure appropriate `MaxOrderAge` and `CleanupInterval`
- [ ] Set up log rotation and monitoring
- [ ] Configure Prometheus scraping for metrics
- [ ] Set up database backups
- [ ] Configure firewall rules to restrict access
- [ ] Test failover and recovery scenarios
- [ ] Document runbook for common issues

## Performance Tuning

### Database

- Default SQLite settings work well for most use cases
- For high-frequency trading, consider:
  - Using a faster disk (SSD recommended)
  - Increasing WAL mode cache size
  - Tuning `MaxOrderAge` to reduce database size

### Network

- Use HTTP/2 for better performance (automatic with HTTPS)
- Configure appropriate timeouts in cBot (default: 5 seconds)
- Use persistent connections when possible

### Monitoring Recommendations

- Monitor pending order queue size
- Alert on consecutive failures
- Monitor disk space for database and logs
- Track request duration percentiles (p50, p95, p99)
