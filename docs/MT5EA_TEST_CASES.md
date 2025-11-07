# MT5 EA Test Cases

## JSON Parser Edge Case Tests

The JAson.mqh parser has been improved to handle escaped characters correctly. Here are test cases to verify:

### Test 1: Escaped Quotes in Comment
```json
[
  {
    "Id": "test-001",
    "EventType": "POSITION_OPENED",
    "Symbol": "EURUSD",
    "Direction": "Buy",
    "Volume": "0.01",
    "StopLoss": "0",
    "TakeProfit": "0",
    "Comment": "Quote test: \"Hello World\""
  }
]
```

**Expected**: Comment should be `Quote test: "Hello World"` (quotes unescaped)

### Test 2: Newlines in Comment
```json
[
  {
    "Id": "test-002",
    "EventType": "POSITION_OPENED",
    "Symbol": "EURUSD",
    "Direction": "Buy",
    "Volume": "0.01",
    "StopLoss": "0",
    "TakeProfit": "0",
    "Comment": "Line 1\\nLine 2\\nLine 3"
  }
]
```

**Expected**: Comment should contain actual newlines (not literal \n)

### Test 3: Japanese/Unicode Characters
```json
[
  {
    "Id": "test-003",
    "EventType": "POSITION_OPENED",
    "Symbol": "USDJPY",
    "Direction": "Buy",
    "Volume": "0.01",
    "StopLoss": "0",
    "TakeProfit": "0",
    "Comment": "テストコメント"
  }
]
```

**Expected**: Comment should display Japanese characters correctly

### Test 4: Backslashes
```json
[
  {
    "Id": "test-004",
    "EventType": "POSITION_OPENED",
    "Symbol": "EURUSD",
    "Direction": "Buy",
    "Volume": "0.01",
    "StopLoss": "0",
    "TakeProfit": "0",
    "Comment": "Path: C:\\\\Users\\\\Trader"
  }
]
```

**Expected**: Comment should be `Path: C:\Users\Trader` (backslashes unescaped)

### Test 5: Mixed Escapes
```json
[
  {
    "Id": "test-005",
    "EventType": "POSITION_OPENED",
    "Symbol": "GBPUSD",
    "Direction": "Sell",
    "Volume": "0.01",
    "StopLoss": "0",
    "TakeProfit": "0",
    "Comment": "Test: \\\"Quote\\\" and \\\\Backslash\\\\ and \\nNewline"
  }
]
```

**Expected**: Comment should be `Test: "Quote" and \Backslash\ and [newline]Newline`

## Manual Testing Steps

### Setup
1. Start the Bridge server
2. Configure MT5 EA with:
   - Bridge URL
   - API Key (if configured in Bridge)
   - DryRun = true (recommended for initial testing)
3. Attach EA to a chart

### Test Dry Run Mode
1. Set `DryRun = true` in EA parameters
2. Send test orders from cTrader or directly to Bridge
3. Verify EA logs show `[DRY RUN]` messages
4. Verify no actual trades are executed
5. Verify orders are still marked as processed in Bridge

### Test API Key Authentication
1. Configure `Bridge:ApiKey` in Bridge's appsettings.json
2. Set matching `BridgeApiKey` in EA parameters
3. Send orders and verify they are processed
4. Change EA's BridgeApiKey to incorrect value
5. Verify orders fail to mark as processed (HTTP 401)

### Test Ticket Mapping Persistence
1. Open position through Bridge (EA will execute it)
2. Check MT5 terminal for new position and ticket number
3. Verify `TradeSyncReceiver_TicketMap.dat` file is created in `MQL5/Files/Common/`
4. Restart EA
5. Check logs to verify mappings were loaded from file
6. Close position through Bridge using sourceId
7. Verify correct position is closed

### Test Failed Request Logging
1. Send order with invalid data (e.g., negative volume)
2. Verify EA rejects order
3. Check `TradeSyncReceiver_Failed.log` in `MQL5/Files/Common/`
4. Verify log contains timestamp, order ID, event type, and error reason

### Test Rate Limiting
1. Configure EA with PollInterval = 100 (fast polling)
2. Let EA run for 1 minute
3. Verify rate limit message appears after ~60 requests
4. Verify polling resumes after 1 minute

### Test Exponential Backoff
1. Stop the Bridge server
2. Observe EA logs showing connection failures
3. Verify delays between retries increase: 2s, 4s, 8s, 16s, 32s
4. Restart Bridge server
5. Verify EA resumes normal operation

### Test Unknown Event Types
1. Manually POST order to Bridge with unknown EventType
2. Verify EA logs "Unknown event type" message
3. Verify order is still marked as processed (to avoid infinite loop)

## Integration Testing

### End-to-End Test
1. Start Bridge server
2. Start MT5 with EA (DryRun = false)
3. Open position in cTrader
4. Verify position opens in MT5 within polling interval
5. Verify ticket mapping is created
6. Verify mapping is sent to Bridge
7. Modify position SL/TP in cTrader
8. Verify modification applies in MT5
9. Close position in cTrader
10. Verify position closes in MT5

### Multi-Position Test
1. Open 2 positions on same symbol (EURUSD) in cTrader
2. Verify both positions open in MT5 with unique tickets
3. Close one position by sourceId in cTrader
4. Verify only the correct position closes in MT5
5. Verify other position remains open

### EA Restart Test
1. Open position through Bridge
2. Note the sourceId and MT5 ticket
3. Restart MT5 EA
4. Send modify or close command for that position
5. Verify EA can still find and modify/close the position using saved ticket map

## Performance Testing

### Load Test
1. Queue 100 orders in Bridge
2. Start EA
3. Monitor CPU and memory usage
4. Verify all orders are processed correctly
5. Verify no memory leaks (check memory after processing)

### Stress Test
1. Configure PollInterval = 100 (10 requests/second)
2. Continuously send orders for 10 minutes
3. Monitor for crashes or errors
4. Verify rate limiting prevents overload
5. Check log files for any issues

## Security Testing

### API Key Validation
1. Configure Bridge with API key
2. Test EA without API key → should fail with 401
3. Test EA with wrong API key → should fail with 401
4. Test EA with correct API key → should succeed

### Input Validation
1. Send order with extremely long symbol name → should be rejected
2. Send order with negative volume → should be rejected/adjusted
3. Send order with invalid EventType → should be handled safely

## Edge Cases

### Empty Queue
1. Start EA with empty Bridge queue
2. Verify EA polls normally without errors
3. Verify no unnecessary log messages

### Network Issues
1. Disconnect network temporarily
2. Verify EA handles timeout gracefully
3. Verify exponential backoff engages
4. Reconnect network
5. Verify EA resumes normal operation

### Bridge Restart
1. Start EA and Bridge
2. Process some orders
3. Restart Bridge server
4. Verify EA reconnects and continues processing

### Symbol Mapping
1. Send order for symbol that doesn't exist in MT5
2. Verify EA logs error appropriately
3. Verify order is retried (not marked as processed)
4. Verify no crash occurs
