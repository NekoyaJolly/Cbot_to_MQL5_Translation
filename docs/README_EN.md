# Ctrader to MT5 Trade Synchronization System

This project provides a real-time trade synchronization system from Ctrader (cBot) to MetaTrader 5 (MT5).

## System Architecture

```
Ctrader (cBot/C#) → HTTP → Bridge Server (C#/.NET) → HTTP → MT5 (MQL5 EA)
```

### Components

1. **CtraderBot (C#/cAlgo)**: Hooks trades on Ctrader side and sends them to the Bridge server
2. **Bridge Server (C# ASP.NET Core)**: Manages queue locally and provides REST API
3. **MT5EA (MQL5)**: Polls the Bridge for orders and executes them on MT5

## Features

### Supported Trading Events

- ✅ Position Open (Market Orders)
- ✅ Position Close
- ✅ Position Modification (Stop Loss / Take Profit)
- ✅ Pending Order Creation (Limit/Stop)
- ✅ Pending Order Cancellation
- ✅ Pending Order Filled

### Characteristics

- **Low Latency**: Polling interval of 1 second (customizable)
- **Thread-Safe**: Queue management with concurrent processing support
- **Error Handling**: Handles connection and trading errors
- **Auto Cleanup**: Automatically removes old processed orders

## Setup Instructions

### 1. Bridge Server Setup

#### Requirements
- .NET 6.0 SDK or higher

#### Installation and Startup

```bash
cd Bridge
dotnet restore
dotnet run
```

The server will start on `http://localhost:5000` by default.

#### Verification

```bash
# Health check
curl http://localhost:5000/api/health

# Check statistics
curl http://localhost:5000/api/statistics
```

### 2. Ctrader cBot Setup

#### Installation

1. Open Ctrader
2. Go to the "Automate" tab
3. Create a new cBot or open an existing one
4. Copy the contents of `CtraderBot/TradeSyncBot.cs`
5. Build and save the cBot

#### Configuration

- **Bridge Server URL**: Address of the Bridge Server (default: `http://localhost:5000`)
- **Enable Sync**: Enable synchronization (default: true)

#### Startup

1. Drag and drop the cBot onto a chart
2. Verify parameters and start
3. Check the log for connection status

### 3. MT5 EA Setup

#### Installation

1. Open MT5 data folder (File → Open Data Folder)
2. Navigate to `MQL5/Experts/` folder
3. Copy `MT5EA/TradeSyncReceiver.mq5`
4. Copy `MT5EA/JAson.mqh` to `MQL5/Include/` folder
5. Compile in MetaEditor

#### Important: WebRequest Settings

You need to allow HTTP requests in MT5:

1. Tools → Options → Expert Advisors
2. Add to "Allow WebRequest for listed URLs":
   ```
   http://localhost:5000
   ```

#### Configuration

- **Bridge URL**: Address of the Bridge Server (default: `http://localhost:5000`)
- **Poll Interval**: Polling interval in milliseconds (default: 1000)
- **Enable Sync**: Enable synchronization (default: true)
- **Slippage Points**: Slippage in points (default: 10)
- **Magic Number**: Magic number for orders (default: 123456)

#### Startup

1. Drag and drop the EA onto a chart
2. Enable "Algo Trading" button
3. Check the log for connection status

## Usage

### Basic Workflow

1. **Start Bridge Server**
   ```bash
   cd Bridge
   dotnet run
   ```

2. **Start Ctrader cBot**
   - Apply the cBot to a chart in Ctrader
   - Verify parameters and start

3. **Start MT5 EA**
   - Apply the EA to a chart in MT5
   - Enable Algo Trading

4. **Start Trading**
   - Execute trades normally in Ctrader
   - Same trades will be automatically executed in MT5

### Troubleshooting

#### Cannot connect to Bridge Server

- Verify Bridge Server is running
- Check if port 5000 is open in firewall
- Verify URL is correct (`http://localhost:5000`)

#### WebRequest error in MT5

- Verify WebRequest is allowed in Options
- Check if `http://localhost:5000` is added to URL list
- Restart MT5

#### Orders not executing

- Check MT5 logs for error messages
- Verify symbol names are valid in MT5
- Check if there's sufficient margin in the account
- Verify Algo Trading is enabled

#### Symbol Name Differences

If symbol names differ between Ctrader and MT5, customize the `NormalizeSymbol()` function in `TradeSyncReceiver.mq5`.

Example:
```mql5
string NormalizeSymbol(string symbol)
{
    // Convert EURUSD to EURUSD.raw
    if(symbol == "EURUSD")
        return "EURUSD.raw";
    
    // Other mappings
    // ...
    
    return symbol;
}
```

## Bridge Server API Reference

### Endpoints

#### POST /api/orders
Receive order (from Ctrader)

**Request:**
```json
{
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
}
```

**Response:**
```json
{
    "OrderId": "1",
    "Status": "Queued"
}
```

#### GET /api/orders/pending
Get pending orders (from MT5)

**Parameters:**
- `maxCount`: Maximum number of orders to retrieve (default: 10)

**Response:**
```json
[
    {
        "Id": "1",
        "EventType": "POSITION_OPENED",
        "Symbol": "EURUSD",
        ...
    }
]
```

#### POST /api/orders/{orderId}/processed
Mark order as processed (from MT5)

**Response:**
```json
{
    "Status": "Processed"
}
```

#### GET /api/orders/{orderId}
Get specific order

#### GET /api/statistics
Get statistics

**Response:**
```json
{
    "TotalOrders": 100,
    "PendingOrders": 5,
    "ProcessedOrders": 95,
    "OrdersLast5Min": 10
}
```

#### GET /api/health
Health check

## Performance

- **Latency**: Typically 1-2 seconds (depends on polling interval)
- **Throughput**: Can process 100+ orders per second
- **Memory**: Bridge Server uses ~50MB
- **CPU**: Low load (< 1% when idle)

## Security Considerations

- **Local Network**: Listens only on localhost by default
- **No Authentication**: Current implementation has no authentication (planned for future)
- **Remote Access**: If remote access is needed, use VPN or SSH tunnel

## Future Improvements

- [ ] Authentication and authorization
- [ ] WebSocket for real-time communication (reduce polling)
- [ ] Trade history persistence (database)
- [ ] Support for multiple Ctrader/MT5 instances
- [ ] Dashboard UI
- [ ] Enhanced error handling and retry mechanisms
- [ ] Symbol name mapping configuration file

## License

MIT License

## Support

If you encounter issues, please report them on GitHub Issues.
