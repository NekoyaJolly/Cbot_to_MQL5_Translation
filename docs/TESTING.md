# Test Script for Bridge Server

This script demonstrates how to test the Bridge Server API without running Ctrader or MT5.

## Prerequisites

- Bridge Server running on http://localhost:5000
- curl installed

## Test Commands

### 1. Health Check

```bash
curl http://localhost:5000/api/health
```

Expected response:
```json
{"Status":"Healthy","Timestamp":"2024-01-01T12:00:00Z"}
```

### 2. Send a Position Opened Event

```bash
curl -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "EventType": "POSITION_OPENED",
    "Timestamp": "2024-01-01T12:00:00Z",
    "PositionId": 12345,
    "Symbol": "EURUSD",
    "Direction": "Buy",
    "Volume": 0.1,
    "EntryPrice": 1.0950,
    "StopLoss": 1.0900,
    "TakeProfit": 1.1000,
    "Comment": "Test trade"
  }'
```

Expected response:
```json
{"OrderId":"1","Status":"Queued"}
```

### 3. Get Pending Orders

```bash
curl http://localhost:5000/api/orders/pending?maxCount=10
```

Expected response:
```json
[
  {
    "Id": "1",
    "EventType": "POSITION_OPENED",
    "Timestamp": "2024-01-01T12:00:00Z",
    "PositionId": 12345,
    "Symbol": "EURUSD",
    "Direction": "Buy",
    "Volume": 0.1,
    "EntryPrice": 1.095,
    "StopLoss": 1.09,
    "TakeProfit": 1.1,
    "Comment": "Test trade",
    "Processed": false
  }
]
```

### 4. Mark Order as Processed

```bash
curl -X POST http://localhost:5000/api/orders/1/processed
```

Expected response:
```json
{"Status":"Processed"}
```

### 5. Get Order by ID

```bash
curl http://localhost:5000/api/orders/1
```

### 6. Get Statistics

```bash
curl http://localhost:5000/api/statistics
```

Expected response:
```json
{
  "TotalOrders": 1,
  "PendingOrders": 0,
  "ProcessedOrders": 1,
  "OrdersLast5Min": 1
}
```

## Full Test Sequence

Run this complete test sequence to verify the Bridge Server:

```bash
#!/bin/bash

echo "=== Bridge Server Test ==="
echo ""

echo "1. Health Check"
curl -s http://localhost:5000/api/health | jq
echo ""

echo "2. Send Position Opened"
ORDER_RESPONSE=$(curl -s -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "EventType": "POSITION_OPENED",
    "PositionId": 12345,
    "Symbol": "EURUSD",
    "Direction": "Buy",
    "Volume": 0.1,
    "EntryPrice": 1.0950,
    "StopLoss": 1.0900,
    "TakeProfit": 1.1000
  }')
echo $ORDER_RESPONSE | jq
ORDER_ID=$(echo $ORDER_RESPONSE | jq -r '.OrderId')
echo ""

echo "3. Get Pending Orders"
curl -s http://localhost:5000/api/orders/pending | jq
echo ""

echo "4. Mark Order as Processed"
curl -s -X POST http://localhost:5000/api/orders/$ORDER_ID/processed | jq
echo ""

echo "5. Get Statistics"
curl -s http://localhost:5000/api/statistics | jq
echo ""

echo "6. Send Position Modified"
curl -s -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "EventType": "POSITION_MODIFIED",
    "PositionId": 12345,
    "Symbol": "EURUSD",
    "StopLoss": 1.0850,
    "TakeProfit": 1.1050
  }' | jq
echo ""

echo "7. Send Position Closed"
curl -s -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "EventType": "POSITION_CLOSED",
    "PositionId": 12345,
    "Symbol": "EURUSD",
    "NetProfit": 50.0
  }' | jq
echo ""

echo "8. Final Statistics"
curl -s http://localhost:5000/api/statistics | jq
echo ""

echo "=== Test Complete ==="
```

Save this as `test_bridge.sh`, make it executable with `chmod +x test_bridge.sh`, and run it with `./test_bridge.sh`.

Note: This script requires `jq` for JSON formatting. Install it with:
- Ubuntu/Debian: `sudo apt-get install jq`
- macOS: `brew install jq`
- Windows: Download from https://stedolan.github.io/jq/

## Testing Without jq

If you don't have jq, you can run the commands without it:

```bash
#!/bin/bash

echo "Health Check"
curl http://localhost:5000/api/health
echo ""

echo "Send Order"
curl -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{"EventType":"POSITION_OPENED","Symbol":"EURUSD","Direction":"Buy","Volume":0.1}'
echo ""

echo "Get Pending"
curl http://localhost:5000/api/orders/pending
echo ""

echo "Get Statistics"
curl http://localhost:5000/api/statistics
echo ""
```

## Load Testing

To test the Bridge Server under load:

```bash
#!/bin/bash

echo "Load test: Sending 100 orders..."

for i in {1..100}
do
  curl -s -X POST http://localhost:5000/api/orders \
    -H "Content-Type: application/json" \
    -d "{
      \"EventType\":\"POSITION_OPENED\",
      \"Symbol\":\"EURUSD\",
      \"Direction\":\"Buy\",
      \"Volume\":0.1,
      \"EntryPrice\":1.095
    }" > /dev/null
  
  if [ $((i % 10)) -eq 0 ]; then
    echo "$i orders sent"
  fi
done

echo "Done. Checking statistics..."
curl -s http://localhost:5000/api/statistics | jq
```
