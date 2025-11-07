//+------------------------------------------------------------------+
//|                                              TradeSyncReceiver.mq5 |
//|                                  Copyright 2024, NekoyaJolly      |
//|                                                                    |
//+------------------------------------------------------------------+
#property copyright "Copyright 2024, NekoyaJolly"
#property link      ""
#property version   "1.00"
#property strict

#include <Trade\Trade.mqh>
#include <JAson.mqh>

//--- Input parameters
input string BridgeUrl = "http://localhost:5000";  // Bridge Server URL
input string BridgeApiKey = "";                      // Bridge API Key (X-API-KEY header)
input int    PollInterval = 1000;                   // Poll interval in milliseconds
input bool   EnableSync = true;                     // Enable synchronization
input double SlippagePoints = 10;                   // Slippage in points
input int    MagicNumber = 123456;                  // Magic number for orders
input bool   DryRun = false;                         // Dry run mode (log only, no actual trades)

//--- Global variables
CTrade trade;
datetime lastPollTime;
int requestCount = 0;
int consecutiveFailures = 0;  // Track consecutive failures for backoff
datetime lastSuccessTime;

// Ticket mapping: sourceId -> MT5 ticket
string g_sourceIds[];
ulong g_tickets[];

// Failed requests log file
string g_failedRequestsFile = "TradeSyncReceiver_Failed.log";

// Ticket mapping persistence file
string g_ticketMappingFile = "TradeSyncReceiver_TicketMap.dat";

// Rate limiting
#define MAX_REQUESTS_PER_MINUTE 60
int requestsThisMinute = 0;
datetime lastMinuteReset;

// Sanity check limits for file persistence
#define MAX_TICKET_MAPPINGS 10000  // Maximum number of mappings to load from file
#define MAX_SOURCE_ID_LENGTH 1000  // Maximum length of sourceId string

//+------------------------------------------------------------------+
//| Expert initialization function                                   |
//+------------------------------------------------------------------+
int OnInit()
{
    Print("TradeSyncReceiver EA started");
    Print("Bridge URL: ", BridgeUrl);
    Print("Poll Interval: ", PollInterval, "ms");
    Print("Dry Run Mode: ", DryRun ? "ENABLED (No actual trades)" : "DISABLED");
    
    // IMPORTANT: WebRequest Configuration Required
    // Go to Tools -> Options -> Expert Advisors
    // Add the Bridge URL to the "Allow WebRequest for listed URL" list
    // Example: http://localhost:5000
    Print("NOTICE: Ensure Bridge URL is in WebRequest allowed list (Tools->Options->Expert Advisors)");
    
    // Configure trade object
    trade.SetDeviationInPoints((ulong)SlippagePoints);
    trade.SetExpertMagicNumber(MagicNumber);
    trade.SetTypeFilling(ORDER_FILLING_RETURN);  // Use RETURN instead of FOK for better compatibility
    trade.SetAsyncMode(false);
    
    lastPollTime = TimeCurrent();
    lastSuccessTime = TimeCurrent();
    lastMinuteReset = TimeCurrent();
    consecutiveFailures = 0;
    requestsThisMinute = 0;
    
    // Initialize ticket mapping arrays
    ArrayResize(g_sourceIds, 0);
    ArrayResize(g_tickets, 0);
    
    // Load ticket mappings from file
    LoadTicketMappingsFromFile();
    
    return(INIT_SUCCEEDED);
}

//+------------------------------------------------------------------+
//| Expert deinitialization function                                 |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
    Print("TradeSyncReceiver EA stopped. Reason: ", reason);
}

//+------------------------------------------------------------------+
//| Expert tick function                                              |
//+------------------------------------------------------------------+
void OnTick()
{
    if(!EnableSync)
        return;
    
    // Poll at specified interval
    datetime currentTime = TimeCurrent();
    if((currentTime - lastPollTime) * 1000 < PollInterval)
        return;
    
    lastPollTime = currentTime;
    
    // Fetch pending orders from bridge
    PollBridgeForOrders();
}

//+------------------------------------------------------------------+
//| Poll bridge server for pending orders                            |
//+------------------------------------------------------------------+
void PollBridgeForOrders()
{
    // Reset rate limit counter every minute
    datetime currentTime = TimeCurrent();
    if((currentTime - lastMinuteReset) >= 60)
    {
        requestsThisMinute = 0;
        lastMinuteReset = currentTime;
    }
    
    // Rate limiting: don't exceed MAX_REQUESTS_PER_MINUTE
    if(requestsThisMinute >= MAX_REQUESTS_PER_MINUTE)
    {
        if(requestsThisMinute == MAX_REQUESTS_PER_MINUTE)
        {
            Print("Rate limit reached (", MAX_REQUESTS_PER_MINUTE, " requests/minute). Throttling requests.");
            requestsThisMinute++; // Increment to avoid printing this message repeatedly
        }
        return;
    }
    
    // Exponential backoff after consecutive failures
    if(consecutiveFailures > 0)
    {
        int backoffSeconds = (int)MathPow(2, MathMin(consecutiveFailures, 5)); // Max 32 seconds
        if((currentTime - lastSuccessTime) < backoffSeconds)
        {
            return; // Still in backoff period
        }
    }
    
    string url = BridgeUrl + "/api/orders/pending?maxCount=10";
    string headers = "Content-Type: application/json\r\n";
    
    char data[];
    char result[];
    string resultHeaders;
    
    int timeout = 5000; // 5 seconds timeout
    
    // Make HTTP GET request
    requestsThisMinute++;
    int res = WebRequest("GET", url, headers, timeout, data, result, resultHeaders);
    
    if(res == -1)
    {
        int errorCode = GetLastError();
        if(errorCode != 0)
        {
            consecutiveFailures++;
            Print("WebRequest error: ", errorCode, ". Make sure URL is in allowed list. Failures: ", consecutiveFailures);
        }
        return;
    }
    
    if(res == 200)
    {
        consecutiveFailures = 0; // Reset failure counter on success
        lastSuccessTime = currentTime;
        
        string response = CharArrayToString(result);
        
        if(StringLen(response) > 2) // More than just "[]"
        {
            ProcessOrders(response);
            requestCount++;
            
            if(requestCount % 100 == 0)
                Print("Processed ", requestCount, " requests");
        }
    }
    else
    {
        consecutiveFailures++;
        Print("HTTP Error: ", res, ". Failures: ", consecutiveFailures);
    }
}

//+------------------------------------------------------------------+
//| Process orders from JSON response                                |
//+------------------------------------------------------------------+
void ProcessOrders(string jsonResponse)
{
    // Validate JSON response
    if(StringLen(jsonResponse) == 0)
    {
        Print("Empty JSON response received");
        return;
    }
    
    // Parse JSON array
    CJAVal json;
    if(!json.Deserialize(jsonResponse))
    {
        Print("Failed to parse JSON response: ", StringSubstr(jsonResponse, 0, MathMin(100, StringLen(jsonResponse))));
        return;
    }
    
    // Validate it's an array
    if(json.Size() == 0)
    {
        return; // Empty array, nothing to process
    }
    
    // Process each order
    for(int i = 0; i < json.Size(); i++)
    {
        CJAVal* order = json.GetArrayItem(i);
        if(order == NULL)
        {
            Print("Failed to get array item at index ", i);
            continue;
        }
        
        string orderId = order.GetStringByKey("Id");
        string eventType = order.GetStringByKey("EventType");
        string symbol = order.GetStringByKey("Symbol");
        
        Print("Processing order: ", orderId, " - ", eventType, " for ", symbol);
        
        bool success = false;
        
        if(eventType == "POSITION_OPENED")
        {
            success = ProcessPositionOpened(order);
        }
        else if(eventType == "POSITION_CLOSED")
        {
            success = ProcessPositionClosed(order);
        }
        else if(eventType == "POSITION_MODIFIED")
        {
            success = ProcessPositionModified(order);
        }
        else if(eventType == "PENDING_ORDER_CREATED")
        {
            success = ProcessPendingOrderCreated(order);
        }
        else if(eventType == "PENDING_ORDER_CANCELLED")
        {
            success = ProcessPendingOrderCancelled(order);
        }
        else if(eventType == "PENDING_ORDER_FILLED")
        {
            // Already handled by position opened
            success = true;
        }
        else
        {
            Print("Unknown event type: ", eventType);
            // Mark unknown event types as processed to avoid infinite loop
            success = true;
        }
        
        // Only mark order as processed if successfully handled
        if(success)
        {
            MarkOrderAsProcessed(orderId);
        }
        else
        {
            Print("Failed to process order ", orderId, " - will retry on next poll");
            // Log failed request to file for manual intervention if needed
            LogFailedRequest(orderId, eventType, "Processing failed", "");
        }
    }
}

//+------------------------------------------------------------------+
//| Process position opened event                                     |
//+------------------------------------------------------------------+
bool ProcessPositionOpened(CJAVal* order)
{
    string sourceId = order.GetStringByKey("Id");
    string symbol = order.GetStringByKey("Symbol");
    string direction = order.GetStringByKey("Direction");
    double volume = order.GetDoubleByKey("Volume");
    double stopLoss = order.GetDoubleByKey("StopLoss");
    double takeProfit = order.GetDoubleByKey("TakeProfit");
    string originalComment = order.GetStringByKey("Comment");
    
    // Create comment with sourceId for tracking
    string comment = "SRC:" + sourceId;
    if(StringLen(originalComment) > 0)
        comment = comment + "|" + originalComment;
    
    // Validate required fields
    if(StringLen(symbol) == 0)
    {
        Print("Missing symbol in position opened event");
        return false;
    }
    
    if(StringLen(direction) == 0)
    {
        Print("Missing direction in position opened event");
        return false;
    }
    
    if(volume <= 0)
    {
        Print("Invalid volume: ", volume);
        return false;
    }
    
    // Normalize symbol name if needed
    symbol = NormalizeSymbol(symbol);
    
    if(symbol == "")
    {
        Print("Invalid or unsupported symbol");
        return false;
    }
    
    // Validate volume
    double volumeMin = SymbolInfoDouble(symbol, SYMBOL_VOLUME_MIN);
    double volumeMax = SymbolInfoDouble(symbol, SYMBOL_VOLUME_MAX);
    double volumeStep = SymbolInfoDouble(symbol, SYMBOL_VOLUME_STEP);
    
    if(volume < volumeMin)
    {
        Print("Volume ", volume, " is below minimum ", volumeMin);
        volume = volumeMin;
    }
    else if(volume > volumeMax)
    {
        Print("Volume ", volume, " exceeds maximum ", volumeMax);
        volume = volumeMax;
    }
    
    // Round volume to valid step
    if(volumeStep > 0)
        volume = MathRound(volume / volumeStep) * volumeStep;
    
    // Convert direction to trade type
    ENUM_ORDER_TYPE orderType;
    if(direction == "Buy")
        orderType = ORDER_TYPE_BUY;
    else if(direction == "Sell")
        orderType = ORDER_TYPE_SELL;
    else
    {
        Print("Invalid direction: ", direction);
        return false;
    }
    
    // Get current price
    double price = 0;
    if(orderType == ORDER_TYPE_BUY)
        price = SymbolInfoDouble(symbol, SYMBOL_ASK);
    else
        price = SymbolInfoDouble(symbol, SYMBOL_BID);
    
    // Normalize SL/TP
    if(stopLoss > 0)
        stopLoss = NormalizeDouble(stopLoss, (int)SymbolInfoInteger(symbol, SYMBOL_DIGITS));
    if(takeProfit > 0)
        takeProfit = NormalizeDouble(takeProfit, (int)SymbolInfoInteger(symbol, SYMBOL_DIGITS));
    
    // Dry run mode - log only, don't execute
    if(DryRun)
    {
        Print("[DRY RUN] Would open position: ", symbol, " ", direction, " ", volume, " lots, SL=", stopLoss, " TP=", takeProfit);
        return true; // Consider success in dry run mode
    }
    
    // Open position
    bool result;
    if(orderType == ORDER_TYPE_BUY)
        result = trade.Buy(volume, symbol, price, stopLoss, takeProfit, comment);
    else
        result = trade.Sell(volume, symbol, price, stopLoss, takeProfit, comment);
    
    if(result)
    {
        ulong ticket = trade.ResultOrder();
        Print("Position opened: ", symbol, " ", direction, " ", volume, " lots, ticket: ", ticket);
        
        // Store ticket mapping for future reference
        if(ticket > 0)
        {
            AddTicketMapping(sourceId, ticket);
            // Send mapping to Bridge for persistence
            SendTicketMappingToBridge(sourceId, ticket, symbol, DoubleToString(volume));
        }
        
        return true;
    }
    else
    {
        uint resultCode = trade.ResultRetcode();
        string errorDesc = trade.ResultRetcodeDescription();
        Print("Failed to open position: Code=", resultCode, " Desc=", errorDesc);
        
        // Log detailed error information
        LogFailedRequest(sourceId, "POSITION_OPENED", 
                        "RetCode=" + IntegerToString(resultCode) + " " + errorDesc,
                        "Symbol=" + symbol + " Dir=" + direction + " Vol=" + DoubleToString(volume));
        
        return false;
    }
}

//+------------------------------------------------------------------+
//| Process position closed event                                     |
//+------------------------------------------------------------------+
bool ProcessPositionClosed(CJAVal* order)
{
    string sourceId = order.GetStringByKey("Id");
    string symbol = order.GetStringByKey("Symbol");
    long positionId = order.GetIntByKey("PositionId");
    
    symbol = NormalizeSymbol(symbol);
    
    // Dry run mode - log only
    if(DryRun)
    {
        Print("[DRY RUN] Would close position: ", symbol);
        return true;
    }
    
    // First try to find by sourceId in ticket_map
    ulong ticket = GetTicketBySourceId(sourceId);
    if(ticket > 0)
    {
        if(PositionSelectByTicket(ticket))
        {
            if(trade.PositionClose(ticket))
            {
                Print("Position closed by ticket: ", ticket);
                RemoveTicketMapping(sourceId);
                return true;
            }
            else
            {
                Print("Failed to close position by ticket: ", trade.ResultRetcodeDescription());
                LogFailedRequest(sourceId, "POSITION_CLOSED", 
                                trade.ResultRetcodeDescription(),
                                "Ticket=" + IntegerToString(ticket));
                return false;
            }
        }
    }
    
    // Fallback: Find position by symbol (for backward compatibility)
    // Note: This may not work correctly with multiple positions on same symbol
    if(PositionSelect(symbol))
    {
        if(trade.PositionClose(symbol))
        {
            Print("Position closed by symbol: ", symbol);
            RemoveTicketMapping(sourceId);
            return true;
        }
        else
        {
            Print("Failed to close position: ", trade.ResultRetcodeDescription());
            LogFailedRequest(sourceId, "POSITION_CLOSED", 
                            trade.ResultRetcodeDescription(),
                            "Symbol=" + symbol);
            return false;
        }
    }
    else
    {
        Print("Position not found: ", symbol, " (ticket: ", ticket, ")");
        // Position might already be closed, consider this a success to avoid infinite retry
        RemoveTicketMapping(sourceId);
        return true;
    }
}

//+------------------------------------------------------------------+
//| Process position modified event                                   |
//+------------------------------------------------------------------+
bool ProcessPositionModified(CJAVal* order)
{
    string sourceId = order.GetStringByKey("Id");
    string symbol = order.GetStringByKey("Symbol");
    double stopLoss = order.GetDoubleByKey("StopLoss");
    double takeProfit = order.GetDoubleByKey("TakeProfit");
    
    symbol = NormalizeSymbol(symbol);
    
    // Dry run mode - log only
    if(DryRun)
    {
        Print("[DRY RUN] Would modify position: ", symbol, " SL=", stopLoss, " TP=", takeProfit);
        return true;
    }
    
    // Try to find by sourceId first
    ulong ticket = GetTicketBySourceId(sourceId);
    if(ticket > 0 && PositionSelectByTicket(ticket))
    {
        // Normalize SL/TP
        if(stopLoss > 0)
            stopLoss = NormalizeDouble(stopLoss, (int)SymbolInfoInteger(symbol, SYMBOL_DIGITS));
        if(takeProfit > 0)
            takeProfit = NormalizeDouble(takeProfit, (int)SymbolInfoInteger(symbol, SYMBOL_DIGITS));
        
        if(trade.PositionModify(ticket, stopLoss, takeProfit))
        {
            Print("Position modified by ticket: ", ticket, " SL=", stopLoss, " TP=", takeProfit);
            return true;
        }
        else
        {
            Print("Failed to modify position: ", trade.ResultRetcodeDescription());
            LogFailedRequest(sourceId, "POSITION_MODIFIED", 
                            trade.ResultRetcodeDescription(),
                            "Ticket=" + IntegerToString(ticket) + " SL=" + DoubleToString(stopLoss) + " TP=" + DoubleToString(takeProfit));
            return false;
        }
    }
    
    // Fallback: Find by symbol
    if(PositionSelect(symbol))
    {
        // Normalize SL/TP
        if(stopLoss > 0)
            stopLoss = NormalizeDouble(stopLoss, (int)SymbolInfoInteger(symbol, SYMBOL_DIGITS));
        if(takeProfit > 0)
            takeProfit = NormalizeDouble(takeProfit, (int)SymbolInfoInteger(symbol, SYMBOL_DIGITS));
        
        if(trade.PositionModify(symbol, stopLoss, takeProfit))
        {
            Print("Position modified: ", symbol, " SL=", stopLoss, " TP=", takeProfit);
            return true;
        }
        else
        {
            Print("Failed to modify position: ", trade.ResultRetcodeDescription());
            LogFailedRequest(sourceId, "POSITION_MODIFIED", 
                            trade.ResultRetcodeDescription(),
                            "Symbol=" + symbol);
            return false;
        }
    }
    else
    {
        Print("Position not found: ", symbol);
        LogFailedRequest(sourceId, "POSITION_MODIFIED", 
                        "Position not found",
                        "Symbol=" + symbol);
        return false;
    }
}

//+------------------------------------------------------------------+
//| Process pending order created event                               |
//+------------------------------------------------------------------+
bool ProcessPendingOrderCreated(CJAVal* order)
{
    string sourceId = order.GetStringByKey("Id");
    string symbol = order.GetStringByKey("Symbol");
    string orderTypeStr = order.GetStringByKey("OrderType");
    string direction = order.GetStringByKey("Direction");
    double volume = order.GetDoubleByKey("Volume");
    double targetPrice = order.GetDoubleByKey("TargetPrice");
    double stopLoss = order.GetDoubleByKey("StopLoss");
    double takeProfit = order.GetDoubleByKey("TakeProfit");
    string originalComment = order.GetStringByKey("Comment");
    
    // Create comment with sourceId for tracking
    string comment = "SRC:" + sourceId;
    if(StringLen(originalComment) > 0)
        comment = comment + "|" + originalComment;
    
    // Validate required fields
    if(StringLen(symbol) == 0 || StringLen(orderTypeStr) == 0 || StringLen(direction) == 0)
    {
        Print("Missing required fields in pending order");
        return false;
    }
    
    if(volume <= 0 || targetPrice <= 0)
    {
        Print("Invalid volume or target price");
        return false;
    }
    
    symbol = NormalizeSymbol(symbol);
    
    if(symbol == "")
    {
        Print("Invalid or unsupported symbol");
        return false;
    }
    
    // Validate volume
    double volumeMin = SymbolInfoDouble(symbol, SYMBOL_VOLUME_MIN);
    double volumeMax = SymbolInfoDouble(symbol, SYMBOL_VOLUME_MAX);
    double volumeStep = SymbolInfoDouble(symbol, SYMBOL_VOLUME_STEP);
    
    if(volume < volumeMin)
    {
        Print("Volume ", volume, " is below minimum ", volumeMin);
        volume = volumeMin;
    }
    else if(volume > volumeMax)
    {
        Print("Volume ", volume, " exceeds maximum ", volumeMax);
        volume = volumeMax;
    }
    
    if(volumeStep > 0)
        volume = MathRound(volume / volumeStep) * volumeStep;
    
    // Determine order type
    ENUM_ORDER_TYPE orderType;
    if(orderTypeStr == "Limit" && direction == "Buy")
        orderType = ORDER_TYPE_BUY_LIMIT;
    else if(orderTypeStr == "Limit" && direction == "Sell")
        orderType = ORDER_TYPE_SELL_LIMIT;
    else if(orderTypeStr == "Stop" && direction == "Buy")
        orderType = ORDER_TYPE_BUY_STOP;
    else if(orderTypeStr == "Stop" && direction == "Sell")
        orderType = ORDER_TYPE_SELL_STOP;
    else
    {
        Print("Unsupported order type: ", orderTypeStr, " ", direction);
        return false;
    }
    
    // Normalize prices
    int digits = (int)SymbolInfoInteger(symbol, SYMBOL_DIGITS);
    targetPrice = NormalizeDouble(targetPrice, digits);
    if(stopLoss > 0)
        stopLoss = NormalizeDouble(stopLoss, digits);
    if(takeProfit > 0)
        takeProfit = NormalizeDouble(takeProfit, digits);
    
    // Dry run mode - log only
    if(DryRun)
    {
        Print("[DRY RUN] Would create pending order: ", symbol, " ", orderTypeStr, " ", direction, " at ", targetPrice, " SL=", stopLoss, " TP=", takeProfit);
        return true;
    }
    
    // Place pending order
    if(trade.OrderOpen(symbol, orderType, volume, 0, targetPrice, stopLoss, takeProfit, 
                       ORDER_TIME_GTC, 0, comment))
    {
        ulong ticket = trade.ResultOrder();
        Print("Pending order created: ", symbol, " ", orderTypeStr, " at ", targetPrice, " ticket: ", ticket);
        
        // Store ticket mapping
        if(ticket > 0)
        {
            AddTicketMapping(sourceId, ticket);
            // Send mapping to Bridge for persistence
            SendTicketMappingToBridge(sourceId, ticket, symbol, DoubleToString(volume));
        }
        
        return true;
    }
    else
    {
        Print("Failed to create pending order: ", trade.ResultRetcodeDescription());
        LogFailedRequest(sourceId, "PENDING_ORDER_CREATED", 
                        trade.ResultRetcodeDescription(),
                        "Symbol=" + symbol + " Type=" + orderTypeStr + " Price=" + DoubleToString(targetPrice));
        return false;
    }
}

//+------------------------------------------------------------------+
//| Process pending order cancelled event                             |
//+------------------------------------------------------------------+
bool ProcessPendingOrderCancelled(CJAVal* order)
{
    string sourceId = order.GetStringByKey("Id");
    string symbol = order.GetStringByKey("Symbol");
    long orderId = order.GetIntByKey("OrderId");
    
    symbol = NormalizeSymbol(symbol);
    
    // Dry run mode - log only
    if(DryRun)
    {
        Print("[DRY RUN] Would cancel pending order: ", symbol);
        return true;
    }
    
    // Try to find by sourceId first
    ulong ticket = GetTicketBySourceId(sourceId);
    if(ticket > 0)
    {
        if(OrderSelect(ticket))
        {
            if(trade.OrderDelete(ticket))
            {
                Print("Pending order cancelled by ticket: ", ticket);
                RemoveTicketMapping(sourceId);
                return true;
            }
            else
            {
                Print("Failed to cancel pending order: ", trade.ResultRetcodeDescription());
                LogFailedRequest(sourceId, "PENDING_ORDER_CANCELLED", 
                                trade.ResultRetcodeDescription(),
                                "Ticket=" + IntegerToString(ticket));
                return false;
            }
        }
    }
    
    // Fallback: Find and cancel pending orders for this symbol
    int total = OrdersTotal();
    for(int i = total - 1; i >= 0; i--)
    {
        ulong orderTicket = OrderGetTicket(i);
        if(OrderSelect(orderTicket))
        {
            if(OrderGetString(ORDER_SYMBOL) == symbol)
            {
                if(trade.OrderDelete(orderTicket))
                {
                    Print("Pending order cancelled: ", symbol, " ticket: ", orderTicket);
                    RemoveTicketMapping(sourceId);
                    return true;
                }
                else
                {
                    Print("Failed to cancel pending order: ", trade.ResultRetcodeDescription());
                    LogFailedRequest(sourceId, "PENDING_ORDER_CANCELLED", 
                                    trade.ResultRetcodeDescription(),
                                    "Symbol=" + symbol);
                    return false;
                }
            }
        }
    }
    
    Print("Pending order not found: ", symbol);
    // Order might already be cancelled/filled, consider this success
    RemoveTicketMapping(sourceId);
    return true;
}

//+------------------------------------------------------------------+
//| Mark order as processed on bridge                                |
//+------------------------------------------------------------------+
void MarkOrderAsProcessed(string orderId)
{
    string url = BridgeUrl + "/api/orders/" + orderId + "/processed";
    string headers = "Content-Type: application/json\r\n";
    
    // Add API Key header if configured
    if(StringLen(BridgeApiKey) > 0)
    {
        headers += "X-API-KEY: " + BridgeApiKey + "\r\n";
    }
    
    char data[];
    char result[];
    string resultHeaders;
    
    int timeout = 5000;
    
    int res = WebRequest("POST", url, headers, timeout, data, result, resultHeaders);
    
    if(res != 200)
    {
        Print("Failed to mark order as processed: ", orderId, " HTTP Status: ", res);
    }
}

//+------------------------------------------------------------------+
//| Normalize symbol name for MT5                                     |
//+------------------------------------------------------------------+
string NormalizeSymbol(string symbol)
{
    // Remove any suffixes or prefixes that might differ between platforms
    // You may need to customize this based on your broker
    
    // Check if symbol exists in MT5
    if(SymbolSelect(symbol, true))
        return symbol;
    
    // Try common variations
    string variations[];
    ArrayResize(variations, 0);
    
    // Add original
    ArrayResize(variations, 1);
    variations[0] = symbol;
    
    // Try with suffixes
    ArrayResize(variations, ArraySize(variations) + 3);
    variations[ArraySize(variations) - 3] = symbol + ".";
    variations[ArraySize(variations) - 2] = symbol + "m";
    variations[ArraySize(variations) - 1] = symbol + ".raw";
    
    for(int i = 0; i < ArraySize(variations); i++)
    {
        if(SymbolSelect(variations[i], true))
            return variations[i];
    }
    
    Print("Symbol not found: ", symbol);
    return "";
}

//+------------------------------------------------------------------+
//| Add sourceId to ticket mapping                                    |
//+------------------------------------------------------------------+
void AddTicketMapping(string sourceId, ulong ticket)
{
    int size = ArraySize(g_sourceIds);
    ArrayResize(g_sourceIds, size + 1);
    ArrayResize(g_tickets, size + 1);
    g_sourceIds[size] = sourceId;
    g_tickets[size] = ticket;
    
    Print("Ticket mapping added: ", sourceId, " -> ", ticket);
    
    // Save to file for persistence across EA restarts
    SaveTicketMappingsToFile();
}

//+------------------------------------------------------------------+
//| Get ticket by sourceId                                            |
//+------------------------------------------------------------------+
ulong GetTicketBySourceId(string sourceId)
{
    for(int i = 0; i < ArraySize(g_sourceIds); i++)
    {
        if(g_sourceIds[i] == sourceId)
            return g_tickets[i];
    }
    return 0;
}

//+------------------------------------------------------------------+
//| Remove ticket mapping                                             |
//+------------------------------------------------------------------+
void RemoveTicketMapping(string sourceId)
{
    for(int i = 0; i < ArraySize(g_sourceIds); i++)
    {
        if(g_sourceIds[i] == sourceId)
        {
            // Shift array elements
            for(int j = i; j < ArraySize(g_sourceIds) - 1; j++)
            {
                g_sourceIds[j] = g_sourceIds[j + 1];
                g_tickets[j] = g_tickets[j + 1];
            }
            ArrayResize(g_sourceIds, ArraySize(g_sourceIds) - 1);
            ArrayResize(g_tickets, ArraySize(g_tickets) - 1);
            
            // Save updated mapping to file
            SaveTicketMappingsToFile();
            break;
        }
    }
}

//+------------------------------------------------------------------+
//| Log failed request to file                                        |
//+------------------------------------------------------------------+
void LogFailedRequest(string orderId, string eventType, string reason, string jsonData)
{
    // Use FILE_COMMON for shared access across terminal instances
    int fileHandle = FileOpen(g_failedRequestsFile, FILE_WRITE|FILE_READ|FILE_SHARE_READ|FILE_TXT|FILE_ANSI|FILE_COMMON);
    if(fileHandle != INVALID_HANDLE)
    {
        FileSeek(fileHandle, 0, SEEK_END);
        string logEntry = TimeToString(TimeCurrent(), TIME_DATE|TIME_SECONDS) + 
                         " | OrderId: " + orderId + 
                         " | EventType: " + eventType + 
                         " | Reason: " + reason + 
                         " | Data: " + StringSubstr(jsonData, 0, MathMin(200, StringLen(jsonData))) + 
                         "\r\n";
        FileWriteString(fileHandle, logEntry);
        FileClose(fileHandle);
        Print("Failed request logged to file: ", orderId);
    }
    else
    {
        Print("Failed to open log file: ", GetLastError());
    }
}

//+------------------------------------------------------------------+
//| Save ticket mappings to file (FILE_COMMON for persistence)       |
//+------------------------------------------------------------------+
void SaveTicketMappingsToFile()
{
    int fileHandle = FileOpen(g_ticketMappingFile, FILE_WRITE|FILE_BIN|FILE_COMMON);
    if(fileHandle != INVALID_HANDLE)
    {
        int count = ArraySize(g_sourceIds);
        FileWriteInteger(fileHandle, count, INT_VALUE);
        
        for(int i = 0; i < count; i++)
        {
            // Write sourceId length and string
            int sourceIdLen = StringLen(g_sourceIds[i]);
            FileWriteInteger(fileHandle, sourceIdLen, INT_VALUE);
            FileWriteString(fileHandle, g_sourceIds[i], sourceIdLen);
            
            // Write ticket
            FileWriteLong(fileHandle, (long)g_tickets[i]);
        }
        
        FileClose(fileHandle);
        Print("Ticket mappings saved to file: ", count, " entries");
    }
    else
    {
        Print("Failed to save ticket mappings: ", GetLastError());
    }
}

//+------------------------------------------------------------------+
//| Load ticket mappings from file (FILE_COMMON)                     |
//+------------------------------------------------------------------+
void LoadTicketMappingsFromFile()
{
    int fileHandle = FileOpen(g_ticketMappingFile, FILE_READ|FILE_BIN|FILE_COMMON);
    if(fileHandle != INVALID_HANDLE)
    {
        int count = FileReadInteger(fileHandle, INT_VALUE);
        
        if(count > 0 && count < MAX_TICKET_MAPPINGS) // Sanity check
        {
            ArrayResize(g_sourceIds, 0);
            ArrayResize(g_tickets, 0);
            
            int validCount = 0;
            for(int i = 0; i < count; i++)
            {
                // Read sourceId length
                int sourceIdLen = FileReadInteger(fileHandle, INT_VALUE);
                
                // Read sourceId string
                string sourceId = "";
                if(sourceIdLen > 0 && sourceIdLen < MAX_SOURCE_ID_LENGTH)
                {
                    sourceId = FileReadString(fileHandle, sourceIdLen);
                }
                else if(sourceIdLen > 0)
                {
                    // Skip invalid sourceId
                    FileSeek(fileHandle, sourceIdLen, SEEK_CUR);
                }
                
                // Read ticket
                ulong ticket = (ulong)FileReadLong(fileHandle);
                
                // Only add to arrays if sourceId is valid
                if(StringLen(sourceId) > 0 && ticket > 0)
                {
                    int size = ArraySize(g_sourceIds);
                    ArrayResize(g_sourceIds, size + 1);
                    ArrayResize(g_tickets, size + 1);
                    g_sourceIds[size] = sourceId;
                    g_tickets[size] = ticket;
                    validCount++;
                }
            }
            
            Print("Ticket mappings loaded from file: ", validCount, " valid entries out of ", count);
        }
        
        FileClose(fileHandle);
    }
    else
    {
        Print("No existing ticket mapping file found (will be created on first mapping)");
    }
}

//+------------------------------------------------------------------+
//| Escape string for JSON                                            |
//+------------------------------------------------------------------+
string EscapeJsonString(string str)
{
    string result = "";
    int len = StringLen(str);
    
    for(int i = 0; i < len; i++)
    {
        ushort ch = StringGetCharacter(str, i);
        
        if(ch == '"')
            result += "\\\"";
        else if(ch == '\\')
            result += "\\\\";
        else if(ch == '\n')
            result += "\\n";
        else if(ch == '\r')
            result += "\\r";
        else if(ch == '\t')
            result += "\\t";
        else
            result += CharToString(ch);
    }
    
    return result;
}

//+------------------------------------------------------------------+
//| Prepare JSON string for WebRequest (convert to char array)       |
//+------------------------------------------------------------------+
bool PrepareJsonDataForWebRequest(string jsonBody, char &data[])
{
    // Use explicit length parameter and UTF8 encoding
    int len = StringToCharArray(jsonBody, data, 0, WHOLE_ARRAY, CP_UTF8);
    if(len > 0 && data[len-1] == 0)
        ArrayResize(data, len - 1); // Remove null terminator if present
    
    return len > 0;
}

//+------------------------------------------------------------------+
//| Send ticket mapping to Bridge server for persistence             |
//+------------------------------------------------------------------+
void SendTicketMappingToBridge(string sourceTicket, ulong slaveTicket, string symbol, string lots)
{
    string url = BridgeUrl + "/api/ticket-map";
    string headers = "Content-Type: application/json\r\n";
    
    // Add API Key header if configured
    if(StringLen(BridgeApiKey) > 0)
    {
        headers += "X-API-KEY: " + BridgeApiKey + "\r\n";
    }
    
    // Escape strings for JSON
    string escapedSourceTicket = EscapeJsonString(sourceTicket);
    string escapedSymbol = EscapeJsonString(symbol);
    string escapedLots = EscapeJsonString(lots);
    
    // Create JSON body with escaped strings
    // Use IntegerToString for better portability across MQL5 versions
    string jsonBody = StringFormat(
        "{\"SourceTicket\":\"%s\",\"SlaveTicket\":\"%s\",\"Symbol\":\"%s\",\"Lots\":\"%s\"}",
        escapedSourceTicket, IntegerToString(slaveTicket), escapedSymbol, escapedLots
    );
    
    char data[];
    if(!PrepareJsonDataForWebRequest(jsonBody, data))
    {
        Print("Failed to prepare JSON data for ticket mapping");
        return;
    }
    
    char result[];
    string resultHeaders;
    
    int timeout = 5000;
    
    int res = WebRequest("POST", url, headers, timeout, data, result, resultHeaders);
    
    if(res == 200)
    {
        Print("Ticket mapping sent to Bridge: ", sourceTicket, " -> ", slaveTicket);
    }
    else
    {
        Print("Failed to send ticket mapping to Bridge: HTTP Status ", res);
    }
}
//+------------------------------------------------------------------+
