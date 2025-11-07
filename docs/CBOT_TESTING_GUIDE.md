# cBot Testing Guide

This guide provides detailed test scenarios for validating the cBot's robust failure handling, persistence, and retry mechanisms.

## Overview

The cBot has been enhanced with the following features that require testing:

1. **File Persistence**: Single append file (`failed_queue.log`) for failed messages
2. **File Rotation**: Automatic rotation when file size exceeds 100MB
3. **Exponential Backoff**: Retry delays increase exponentially (1s, 2s, 4s, 8s, 16s, 32s, 60s max)
4. **LotSize Calculation**: Uses broker's LotSize instead of hardcoded 100,000
5. **Queue Management**: Configurable max queue size (default 10,000)
6. **API Key Validation**: Handles 401 responses and persists failed messages
7. **Circuit Breaker**: Automatic cooldown after consecutive failures

## Prerequisites

- cTrader with cAlgo installed
- Bridge server configured (can be stopped for testing)
- Test account or demo account with permissions to trade
- Access to the cBot parameters

## Configuration Parameters

The cBot includes the following configurable parameters:

| Parameter | Default | Description |
|-----------|---------|-------------|
| Bridge Server URL | http://localhost:5000 | URL of the Bridge server |
| Enable Sync | true | Enable/disable synchronization |
| Bridge API Key | (empty) | API key for Bridge authentication |
| Master Label | MASTER | Label for identifying master orders |
| Max Queue Size | 10000 | Maximum number of messages in queue |
| Max Persist File Size MB | 100 | Maximum size of persist file before rotation |

## Test Scenarios

### Test 1: Basic Persistence with Bridge Offline

**Objective**: Verify that messages are persisted to disk when Bridge is unavailable.

**Steps**:
1. Stop the Bridge server
2. Start the cBot on cTrader
3. Execute 5-10 trades (market orders or pending orders)
4. Check the persist directory

**Expected Results**:
- Directory `persist/failed/` is created
- File `persist/failed/failed_queue.log` exists
- File contains one JSON object per line
- Each line represents a failed trade event
- Console shows "Message persisted" logs with EventType and SourceId

**Verification**:
```bash
# On the cBot machine/directory
cat persist/failed/failed_queue.log
# Should show multiple JSON lines
```

### Test 2: Message Replay after Bridge Recovery

**Objective**: Verify that persisted messages are replayed when Bridge comes back online.

**Steps**:
1. Continue from Test 1 (Bridge offline, messages persisted)
2. Start the Bridge server
3. Observe cBot console logs
4. Wait for retry cycle (every 60 seconds, first retry after 10 seconds)

**Expected Results**:
- Console shows "Loaded X failed messages for retry"
- Messages are sent with exponential backoff
- Console shows "Order sent successfully" with RetryCount
- Console shows "Retry cycle completed: X messages sent, 0 remaining in queue"
- File `persist/failed/failed_queue.log` is cleared (empty)

**Verification**:
```bash
# Check Bridge logs
tail -f logs/bridge-*.log
# Should show incoming orders

# Check MT5 EA
# Orders should appear in MT5 terminal
```

### Test 3: API Key Authentication Failure

**Objective**: Verify that 401 responses are handled correctly and messages are persisted.

**Steps**:
1. Configure Bridge with an API key in `appsettings.json`:
   ```json
   {
     "Bridge": {
       "ApiKey": "test-secret-key-12345"
     }
   }
   ```
2. Start Bridge server
3. Start cBot with **incorrect** API key parameter (e.g., "wrong-key")
4. Execute 5 trades

**Expected Results**:
- Console shows "Failed to send order" with Status=401
- Console shows "Message persisted" for each failed attempt
- File `persist/failed/failed_queue.log` contains the failed messages
- Consecutive failure counter increases

**Verification**:
```bash
# Bridge logs should show API key warnings
grep "Invalid API key" logs/bridge-*.log
```

**Recovery**:
5. Stop cBot
6. Update cBot parameter with **correct** API key
7. Restart cBot

**Expected Results**:
- Console shows "Loaded X failed messages for retry"
- Messages are successfully sent to Bridge
- Console shows Status=200/OK

### Test 4: High Volume and Queue Limits

**Objective**: Test queue behavior under high load and verify queue size limits.

**Steps**:
1. Stop Bridge server
2. Set Max Queue Size to a low value (e.g., 100) for testing
3. Execute rapid trades (100+ trades) or use a script to generate events
4. Observe console logs

**Expected Results**:
- Queue fills up to Max Queue Size
- Console shows "Warning: Queue size exceeded {MaxQueueSize}, dropping oldest message"
- Oldest messages are dropped when queue is full
- File continues to grow but older messages are lost from queue

**Note**: This is expected behavior to prevent memory exhaustion. In production, use default 10,000 or higher.

### Test 5: File Rotation and Size Limits

**Objective**: Verify automatic file rotation when size limit is reached.

**Steps**:
1. Set Max Persist File Size MB to a low value (e.g., 1) for testing
2. Stop Bridge server
3. Generate many trade events until file size exceeds 1MB
4. Observe file system and console logs

**Expected Results**:
- Console shows "Persist file rotated to: persist/failed/failed_queue_{timestamp}.log.bak"
- A new empty `failed_queue.log` file is created
- Backup file exists with timestamp in name
- Old backups are cleaned up (only last 10 kept)

**Verification**:
```bash
ls -lh persist/failed/
# Should show failed_queue.log and backup files
```

### Test 6: Exponential Backoff Verification

**Objective**: Verify that retry delays increase exponentially.

**Steps**:
1. Stop Bridge server
2. Execute a single trade
3. Start Bridge server but configure it to reject requests (e.g., invalid endpoint)
4. Observe retry timing in console logs

**Expected Results**:
- First retry: immediate (0s delay)
- Second retry: ~1s delay
- Third retry: ~2s delay
- Fourth retry: ~4s delay
- Fifth retry: ~8s delay
- Sixth retry: ~16s delay
- Seventh retry: ~32s delay
- Eighth+ retry: ~60s delay (capped)

**Verification**:
Look for timestamps in console logs:
```
16:00:00 - Message persisted
16:00:10 - Retry cycle (immediate)
16:01:11 - Retry cycle (1s delay)
16:02:13 - Retry cycle (2s delay)
...
```

### Test 7: LotSize Calculation Verification

**Objective**: Verify that volume is calculated using broker's LotSize, not hardcoded value.

**Steps**:
1. Start Bridge and cBot with correct API key
2. Execute a trade with known volume (e.g., 100,000 units)
3. Check the JSON payload sent to Bridge

**Expected Results**:
- If LotSize = 100,000: Volume should be "1.00000"
- If LotSize = 1,000: Volume should be "100.00000"
- Volume calculation: `VolumeInUnits / Symbol.LotSize`

**Verification**:
Check Bridge logs or database:
```bash
# Check Bridge database
sqlite3 bridge.db "SELECT Symbol, Volume, EventType FROM Orders ORDER BY Timestamp DESC LIMIT 10;"
```

### Test 8: Circuit Breaker Activation

**Objective**: Verify circuit breaker triggers after consecutive failures.

**Steps**:
1. Stop Bridge server
2. Start cBot
3. Execute multiple trades until consecutive failures reach 10 (MAX_CONSECUTIVE_FAILURES)
4. Continue executing trades

**Expected Results**:
- After 10 consecutive failures, circuit breaker activates
- Console shows "Circuit breaker reset - attempting to reconnect to bridge" after 5-minute cooldown
- During cooldown, new messages are persisted but not immediately retried
- After cooldown, circuit breaker resets and retries resume

### Test 9: Backward Compatibility

**Objective**: Verify that old-style failed_*.log files are loaded correctly.

**Steps**:
1. Manually create some old-style files in `persist/failed/`:
   ```bash
   echo '{"EventType":"TEST","SourceId":"123"}' > persist/failed/failed_20231107123456.log
   ```
2. Start cBot
3. Observe console logs

**Expected Results**:
- Console shows "Loaded X failed messages for retry"
- Old-style files are loaded into queue
- Old-style files are deleted after loading
- Messages are included in retry cycle

### Test 10: Concurrent Operations and Thread Safety

**Objective**: Test thread safety under concurrent load.

**Steps**:
1. Stop Bridge server
2. Start cBot
3. Execute multiple trades simultaneously (open multiple positions)
4. Observe for any errors or race conditions

**Expected Results**:
- No "file in use" errors
- All messages are persisted successfully
- File lock prevents concurrent write issues
- Queue operations are thread-safe (ConcurrentQueue)

## Troubleshooting

### Issue: Persist directory not created

**Cause**: AccessRights not set to FullAccess

**Solution**: Verify Robot attribute is set to `AccessRights.FullAccess`

### Issue: File rotation not working

**Cause**: File size not reaching threshold

**Solution**: Lower Max Persist File Size MB parameter for testing (e.g., 1 MB)

### Issue: Messages not replaying

**Cause**: File may be locked or corrupted

**Solution**: 
1. Stop cBot
2. Check file contents: `cat persist/failed/failed_queue.log`
3. Verify JSON format
4. Restart cBot

### Issue: API key errors persist after correction

**Cause**: HttpClient headers cached

**Solution**: Restart cBot after changing API key parameter

## Performance Benchmarks

Expected performance metrics:

- **Persistence speed**: 1000+ messages/second
- **Memory usage**: ~10MB for 10,000 queued messages
- **File size**: ~500 bytes per message (average)
- **Retry cycle**: Processes up to 10 messages per 60-second cycle
- **First retry**: 10 seconds after cBot start
- **Subsequent retries**: Every 60 seconds

## Production Recommendations

1. **Max Queue Size**: Keep default 10,000 or higher
2. **Max Persist File Size**: Keep default 100MB
3. **API Key**: Always configure for production
4. **Bridge URL**: Use HTTPS in production
5. **Monitoring**: Watch for circuit breaker activations (indicates Bridge issues)
6. **Disk Space**: Ensure sufficient space for persist files (estimate: 10 backups × 100MB = 1GB)
7. **Log Analysis**: Regularly check for "Queue size exceeded" warnings

## Conclusion

These tests ensure the cBot handles failures gracefully and maintains data integrity across restarts. All trade events should eventually reach the Bridge and MT5, even after network outages or Bridge downtime.

For production deployment, refer to `PRODUCTION_BEST_PRACTICES.md` and `PRODUCTION_CONFIGURATION.md`.
