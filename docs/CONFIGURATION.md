# Configuration Examples

## Bridge Server Configuration

### Default Configuration (appsettings.json)

Create this file in the Bridge directory for custom configuration:

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
    "Port": 5000,
    "CleanupInterval": 600,
    "MaxOrderAge": 3600
  }
}
```

### Environment Variables

You can also configure using environment variables:

```bash
# Set Bridge port
export ASPNETCORE_URLS="http://0.0.0.0:5000"

# Set log level
export Logging__LogLevel__Default="Debug"
```

## Ctrader cBot Configuration

Parameters available in Ctrader UI:

- **Bridge Server URL**: Default `http://localhost:5000`
  - Change to remote server: `http://192.168.1.100:5000`
- **Enable Sync**: Default `true`
  - Set to `false` to temporarily disable sync

## MT5 EA Configuration

Parameters available in MT5 EA settings:

- **BridgeUrl**: Default `http://localhost:5000`
  - Change to remote server: `http://192.168.1.100:5000`
- **PollInterval**: Default `1000` (1 second)
  - Lower for faster sync (minimum 100ms)
  - Higher to reduce CPU usage
- **EnableSync**: Default `true`
- **SlippagePoints**: Default `10`
- **MagicNumber**: Default `123456`
  - Change if you have multiple EAs

## Symbol Mapping Configuration

If your broker uses different symbol names between Ctrader and MT5, customize the `NormalizeSymbol()` function in `TradeSyncReceiver.mq5`:

```mql5
string NormalizeSymbol(string symbol)
{
    // Map Ctrader symbols to MT5 symbols
    if(symbol == "EURUSD") return "EURUSD.raw";
    if(symbol == "GBPUSD") return "GBPUSD.raw";
    if(symbol == "USDJPY") return "USDJPY.raw";
    
    // Try adding common suffixes
    if(SymbolSelect(symbol + ".raw", true))
        return symbol + ".raw";
    
    // Return original if no mapping needed
    return symbol;
}
```

## Network Configuration

### For Remote Bridge Server

If you want to run the Bridge Server on a different machine:

1. **Bridge Server**: Change bind address in `Program.cs`
   ```csharp
   webBuilder.UseUrls("http://0.0.0.0:5000");
   ```

2. **Ctrader cBot**: Update Bridge URL parameter
   ```
   http://192.168.1.100:5000
   ```

3. **MT5 EA**: 
   - Update BridgeUrl parameter: `http://192.168.1.100:5000`
   - Add to MT5 allowed URLs list

4. **Firewall**: Open port 5000 on Bridge Server machine

### Security Recommendations for Remote Access

- Use HTTPS with SSL certificate
- Add authentication (API key or JWT)
- Use VPN or SSH tunnel for encryption
- Restrict firewall to specific IP addresses

## Performance Tuning

### Low Latency Setup

```
Bridge: No changes needed
Ctrader: No changes needed
MT5 EA: Set PollInterval to 100 (0.1 second)
```

### Low Resource Setup

```
Bridge: Set CleanupInterval to 300 (5 minutes)
Ctrader: No changes needed
MT5 EA: Set PollInterval to 5000 (5 seconds)
```

### High Volume Setup

```
Bridge: Increase MaxOrderAge to 7200 (2 hours)
Ctrader: No changes needed
MT5 EA: Set maxCount parameter to 50 in polling logic
```
