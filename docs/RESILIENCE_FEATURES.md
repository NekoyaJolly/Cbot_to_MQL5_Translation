# Resilience Features

This document describes the production-grade resilience features implemented in the Cbot to MQL5 Translation system.

## Core Principles

The system is designed around these key principles:

1. **No Message Loss**: All trade events are persisted to disk
2. **Idempotency**: Duplicate events are automatically detected and ignored
3. **Automatic Recovery**: Failed operations are automatically retried
4. **Security**: Authentication and input validation throughout
5. **Observability**: Comprehensive logging and metrics

## Features

### 1. Persistent Message Queue

**Problem**: Network failures or Bridge downtime could cause trade events to be lost.

**Solution**: cBot persists failed messages to local disk and retries them automatically.

**Implementation**:
- Failed HTTP requests are saved to `persist/failed/failed_<timestamp>.log`
- Each line contains a complete JSON message
- On startup, cBot loads all failed messages and retries them
- Successfully sent messages are removed from disk
- Background timer retries failed messages every 60 seconds

**Benefits**:
- Zero message loss even during extended outages
- Automatic recovery without manual intervention
- Survives cBot restarts

### 2. Idempotency (Duplicate Prevention)

**Problem**: Retries or network issues could cause the same trade event to be processed multiple times.

**Solution**: Bridge uses SourceId + EventType as a unique constraint to detect duplicates.

**Implementation**:
- Each trade event includes a `SourceId` (PositionId or OrderId from cTrader)
- Bridge SQLite database has UNIQUE constraint on (SourceId, EventType)
- Duplicate submissions return the original order ID
- MT5 EA receives each event exactly once

**Benefits**:
- Prevents duplicate trades in MT5
- Safe to retry any operation
- Maintains consistency across systems

**Example**:
```json
{
  "EventType": "POSITION_OPENED",
  "SourceId": "12345",
  "Symbol": "EURUSD",
  "Timestamp": "2025-11-06T22:00:00Z"
}
```

If this message is sent twice, the second attempt returns the same order ID without creating a duplicate.

### 3. API Key Authentication

**Problem**: Unauthorized access to the Bridge could allow malicious trade injection.

**Solution**: Optional X-API-KEY header authentication.

**Implementation**:
- Bridge checks for `X-API-KEY` header on all API endpoints
- Health and metrics endpoints are exempt for monitoring
- Configurable via `Bridge:ApiKey` in appsettings.json
- If not configured, authentication is disabled (local development)

**Benefits**:
- Prevents unauthorized access
- Simple to configure and use
- Can be changed without code modifications

**Usage**:
```bash
# cBot automatically adds the header
curl -H "X-API-KEY: your-key" http://bridge/api/orders
```

### 4. Structured Logging

**Problem**: Plain text logs are hard to parse and analyze.

**Solution**: Serilog structured logging with file and console outputs.

**Implementation**:
- Logs written to `logs/bridge-YYYYMMDD.log` (daily rotation)
- Console output for real-time monitoring
- Structured fields enable easy querying
- Automatic log file cleanup (keeps 30 days)

**Log Levels**:
- **Information**: Normal operations (order received, processed, etc.)
- **Warning**: Recoverable issues (authentication failures, etc.)
- **Error**: Critical errors (database issues, unexpected exceptions)

**Example**:
```
[2025-11-06 22:20:28 INF] Order added: Id=abc123, SourceId=12345, EventType=POSITION_OPENED
```

**Benefits**:
- Easy troubleshooting with searchable logs
- Integration with log aggregation tools (ELK, Splunk, etc.)
- Automatic retention management

### 5. Prometheus Metrics

**Problem**: Need visibility into system health and performance.

**Solution**: Built-in Prometheus metrics endpoint.

**Metrics Provided**:
- HTTP request duration (histogram)
- Request counts by endpoint, method, status
- System resource usage

**Endpoint**: `http://bridge:5000/metrics`

**Benefits**:
- Integration with Prometheus/Grafana
- Real-time performance monitoring
- Alerting on anomalies

### 6. SQLite Persistent Storage

**Problem**: In-memory queue loses data on restart.

**Solution**: SQLite database for durable storage.

**Implementation**:
- All orders stored in `Orders` table
- Indexes on key fields for performance
- Automatic cleanup of old processed orders
- Transaction safety for consistency

**Schema**:
```sql
CREATE TABLE Orders (
    Id TEXT PRIMARY KEY,
    SourceId TEXT NOT NULL,
    EventType TEXT NOT NULL,
    Timestamp TEXT NOT NULL,
    -- ... other fields ...
    Processed INTEGER NOT NULL DEFAULT 0,
    ProcessedAt TEXT,
    UNIQUE(SourceId, EventType)
);
```

**Benefits**:
- Survives Bridge restarts
- Enables audit trail of all trades
- Fast queries with proper indexing

### 7. Circuit Breaker Pattern

**Problem**: Repeated failures could overwhelm the system.

**Solution**: cBot implements circuit breaker to temporarily stop sending after too many failures.

**Implementation**:
- Tracks consecutive failures
- After 10 consecutive failures, enters "cooldown" period (5 minutes)
- Failed messages are persisted for later retry
- Automatically resets after cooldown

**Benefits**:
- Prevents resource exhaustion
- Graceful degradation
- Automatic recovery

### 8. Retry Mechanism

**Problem**: Transient network errors should not result in lost messages.

**Solution**: Multiple retry strategies at different levels.

**cBot Retries**:
- Immediate retry on network errors (already in HTTP client)
- Background retry every 60 seconds for persisted failures
- Exponential backoff on repeated failures

**Benefits**:
- Handles transient errors automatically
- No manual intervention required
- Configurable retry intervals

### 9. Input Validation and Sanitization

**Problem**: Malicious input could cause security vulnerabilities.

**Solution**: Comprehensive validation at all entry points.

**Validations**:
- Required fields checked (EventType, Symbol, SourceId)
- String length limits enforced
- EventType validated against whitelist
- Control characters removed from logs (prevents log forging)
- Input sanitized before storage

**Benefits**:
- Prevents injection attacks
- Prevents log forging
- Validates data integrity

### 10. Invariant Culture Formatting

**Problem**: Different locales format numbers differently, causing parsing errors.

**Solution**: All numeric values use invariant culture formatting.

**Implementation**:
```csharp
// cBot sends
Volume = (position.VolumeInUnits / 100000.0)
    .ToString("F5", CultureInfo.InvariantCulture)
// Always produces "0.01000" regardless of locale
```

**Benefits**:
- Consistent formatting across systems
- No locale-dependent parsing errors
- ISO8601 timestamps for dates

## Architecture Diagram

```
┌─────────────────────────────────────────────────────┐
│ cTrader (cBot)                                       │
│ ┌─────────────────────────────────────────────────┐ │
│ │ Trade Events                                     │ │
│ └────────────┬────────────────────────────────────┘ │
│              │                                        │
│              v                                        │
│ ┌─────────────────────────────────────────────────┐ │
│ │ HTTP Client (with retry)                        │ │
│ │ - X-API-KEY header                              │ │
│ │ - Timeout: 5s                                   │ │
│ └────────────┬────────────────────────────────────┘ │
│              │                                        │
│              │ On Failure                             │
│              v                                        │
│ ┌─────────────────────────────────────────────────┐ │
│ │ Persistent Queue (Local Disk)                   │ │
│ │ persist/failed/failed_*.log                     │ │
│ └────────────┬────────────────────────────────────┘ │
│              │                                        │
│              │ Background Retry (60s interval)        │
│              └────────────────┐                      │
└───────────────────────────────┼──────────────────────┘
                                │
                                v
┌─────────────────────────────────────────────────────┐
│ Bridge Server                                        │
│ ┌─────────────────────────────────────────────────┐ │
│ │ API Key Auth Middleware                         │ │
│ │ - Validates X-API-KEY                           │ │
│ └────────────┬────────────────────────────────────┘ │
│              v                                        │
│ ┌─────────────────────────────────────────────────┐ │
│ │ Input Validation & Sanitization                 │ │
│ │ - Required fields                               │ │
│ │ - EventType whitelist                           │ │
│ │ - Length limits                                 │ │
│ └────────────┬────────────────────────────────────┘ │
│              v                                        │
│ ┌─────────────────────────────────────────────────┐ │
│ │ Idempotency Check                               │ │
│ │ - UNIQUE(SourceId, EventType)                   │ │
│ └────────────┬────────────────────────────────────┘ │
│              v                                        │
│ ┌─────────────────────────────────────────────────┐ │
│ │ SQLite Persistent Queue                         │ │
│ │ - Orders table                                   │ │
│ │ - Auto cleanup (1h retention)                   │ │
│ └────────────┬────────────────────────────────────┘ │
│              │                                        │
│              v                                        │
│ ┌─────────────────────────────────────────────────┐ │
│ │ Structured Logging (Serilog)                    │ │
│ │ - File: logs/bridge-*.log                       │ │
│ │ - Console output                                │ │
│ └──────────────────────────────────────────────────┘ │
│                                                       │
│ ┌─────────────────────────────────────────────────┐ │
│ │ Prometheus Metrics                              │ │
│ │ - /metrics endpoint                             │ │
│ └──────────────────────────────────────────────────┘ │
└───────────────────────┬───────────────────────────────┘
                        │
                        v
┌─────────────────────────────────────────────────────┐
│ MT5 EA                                               │
│ - Polls /api/orders/pending                          │
│ - Marks processed: /api/orders/{id}/processed        │
└─────────────────────────────────────────────────────┘
```

## Testing Resilience

### Test Scenarios

1. **Network Outage**:
   - Stop Bridge server
   - Execute trades in cTrader
   - Check `persist/failed/` directory for logged messages
   - Restart Bridge
   - Verify messages are retried and succeed

2. **Duplicate Detection**:
   - Send same order twice to Bridge
   - Verify second attempt returns same order ID
   - Check statistics show only one order

3. **Authentication**:
   - Configure API key in Bridge
   - Try to send without API key (should fail)
   - Try with wrong API key (should fail)
   - Try with correct API key (should succeed)

4. **Circuit Breaker**:
   - Simulate 10+ consecutive failures
   - Verify circuit breaker activates
   - Wait 5 minutes
   - Verify circuit breaker resets

5. **Database Persistence**:
   - Send orders to Bridge
   - Stop Bridge before MT5 processes them
   - Restart Bridge
   - Verify orders are still pending

## Monitoring Checklist

Monitor these metrics in production:

- [ ] Pending order queue size (should stay low)
- [ ] Consecutive failure count (should be 0)
- [ ] HTTP error rate (should be < 1%)
- [ ] Request duration p99 (should be < 1s)
- [ ] Database file size (should grow linearly)
- [ ] Log file size (should rotate daily)
- [ ] Failed message queue size in cBot

## Future Enhancements

Potential improvements for future versions:

1. **Multiple Bridge Instances**: Load balancing and high availability
2. **Message Encryption**: End-to-end encryption of trade data
3. **Webhook Support**: Push notifications instead of polling
4. **Advanced Retry Strategies**: Exponential backoff, jitter
5. **Distributed Tracing**: OpenTelemetry integration
6. **GraphQL API**: More flexible querying
7. **Message Compression**: Reduce bandwidth usage
8. **Redis Cache**: Faster duplicate detection
