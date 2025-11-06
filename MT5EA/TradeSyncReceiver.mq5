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
input int    PollInterval = 1000;                   // Poll interval in milliseconds
input bool   EnableSync = true;                     // Enable synchronization
input double SlippagePoints = 10;                   // Slippage in points
input int    MagicNumber = 123456;                  // Magic number for orders

//--- Global variables
CTrade trade;
datetime lastPollTime;
int requestCount = 0;

//+------------------------------------------------------------------+
//| Expert initialization function                                   |
//+------------------------------------------------------------------+
int OnInit()
{
    Print("TradeSyncReceiver EA started");
    Print("Bridge URL: ", BridgeUrl);
    Print("Poll Interval: ", PollInterval, "ms");
    
    // Configure trade object
    trade.SetDeviationInPoints((ulong)SlippagePoints);
    trade.SetExpertMagicNumber(MagicNumber);
    trade.SetTypeFilling(ORDER_FILLING_FOK);
    trade.SetAsyncMode(false);
    
    lastPollTime = TimeCurrent();
    
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
    string url = BridgeUrl + "/api/orders/pending?maxCount=10";
    string headers = "Content-Type: application/json\r\n";
    
    char data[];
    char result[];
    string resultHeaders;
    
    int timeout = 5000; // 5 seconds timeout
    
    // Make HTTP GET request
    int res = WebRequest("GET", url, headers, timeout, data, result, resultHeaders);
    
    if(res == -1)
    {
        int errorCode = GetLastError();
        if(errorCode != 0)
            Print("WebRequest error: ", errorCode, ". Make sure URL is in allowed list.");
        return;
    }
    
    if(res == 200)
    {
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
        Print("HTTP Error: ", res);
    }
}

//+------------------------------------------------------------------+
//| Process orders from JSON response                                |
//+------------------------------------------------------------------+
void ProcessOrders(string jsonResponse)
{
    // Parse JSON array
    CJAVal json;
    if(!json.Deserialize(jsonResponse))
    {
        Print("Failed to parse JSON response");
        return;
    }
    
    // Process each order
    for(int i = 0; i < json.Size(); i++)
    {
        CJAVal order = json[i];
        
        string orderId = order["Id"].ToStr();
        string eventType = order["EventType"].ToStr();
        string symbol = order["Symbol"].ToStr();
        
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
        
        // Mark order as processed
        if(success || true) // Always mark as processed to avoid reprocessing
        {
            MarkOrderAsProcessed(orderId);
        }
    }
}

//+------------------------------------------------------------------+
//| Process position opened event                                     |
//+------------------------------------------------------------------+
bool ProcessPositionOpened(CJAVal &order)
{
    string symbol = order["Symbol"].ToStr();
    string direction = order["Direction"].ToStr();
    double volume = order["Volume"].ToDbl();
    double stopLoss = order["StopLoss"].ToDbl();
    double takeProfit = order["TakeProfit"].ToDbl();
    string comment = order["Comment"].ToStr();
    
    // Normalize symbol name if needed
    symbol = NormalizeSymbol(symbol);
    
    if(symbol == "")
    {
        Print("Invalid or unsupported symbol");
        return false;
    }
    
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
    
    // Open position
    bool result;
    if(orderType == ORDER_TYPE_BUY)
        result = trade.Buy(volume, symbol, price, stopLoss, takeProfit, comment);
    else
        result = trade.Sell(volume, symbol, price, stopLoss, takeProfit, comment);
    
    if(result)
    {
        Print("Position opened: ", symbol, " ", direction, " ", volume, " lots");
        return true;
    }
    else
    {
        Print("Failed to open position: ", trade.ResultRetcodeDescription());
        return false;
    }
}

//+------------------------------------------------------------------+
//| Process position closed event                                     |
//+------------------------------------------------------------------+
bool ProcessPositionClosed(CJAVal &order)
{
    string symbol = order["Symbol"].ToStr();
    long positionId = order["PositionId"].ToInt();
    
    symbol = NormalizeSymbol(symbol);
    
    // Find and close position by symbol
    if(PositionSelect(symbol))
    {
        if(trade.PositionClose(symbol))
        {
            Print("Position closed: ", symbol);
            return true;
        }
        else
        {
            Print("Failed to close position: ", trade.ResultRetcodeDescription());
            return false;
        }
    }
    else
    {
        Print("Position not found: ", symbol);
        return false;
    }
}

//+------------------------------------------------------------------+
//| Process position modified event                                   |
//+------------------------------------------------------------------+
bool ProcessPositionModified(CJAVal &order)
{
    string symbol = order["Symbol"].ToStr();
    double stopLoss = order["StopLoss"].ToDbl();
    double takeProfit = order["TakeProfit"].ToDbl();
    
    symbol = NormalizeSymbol(symbol);
    
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
            return false;
        }
    }
    else
    {
        Print("Position not found: ", symbol);
        return false;
    }
}

//+------------------------------------------------------------------+
//| Process pending order created event                               |
//+------------------------------------------------------------------+
bool ProcessPendingOrderCreated(CJAVal &order)
{
    string symbol = order["Symbol"].ToStr();
    string orderTypeStr = order["OrderType"].ToStr();
    string direction = order["Direction"].ToStr();
    double volume = order["Volume"].ToDbl();
    double targetPrice = order["TargetPrice"].ToDbl();
    double stopLoss = order["StopLoss"].ToDbl();
    double takeProfit = order["TakeProfit"].ToDbl();
    string comment = order["Comment"].ToStr();
    
    symbol = NormalizeSymbol(symbol);
    
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
    
    // Place pending order
    if(trade.OrderOpen(symbol, orderType, volume, 0, targetPrice, stopLoss, takeProfit, 
                       ORDER_TIME_GTC, 0, comment))
    {
        Print("Pending order created: ", symbol, " ", orderTypeStr, " at ", targetPrice);
        return true;
    }
    else
    {
        Print("Failed to create pending order: ", trade.ResultRetcodeDescription());
        return false;
    }
}

//+------------------------------------------------------------------+
//| Process pending order cancelled event                             |
//+------------------------------------------------------------------+
bool ProcessPendingOrderCancelled(CJAVal &order)
{
    string symbol = order["Symbol"].ToStr();
    long orderId = order["OrderId"].ToInt();
    
    symbol = NormalizeSymbol(symbol);
    
    // Find and cancel pending orders for this symbol
    int total = OrdersTotal();
    for(int i = total - 1; i >= 0; i--)
    {
        ulong ticket = OrderGetTicket(i);
        if(OrderSelect(ticket))
        {
            if(OrderGetString(ORDER_SYMBOL) == symbol)
            {
                if(trade.OrderDelete(ticket))
                {
                    Print("Pending order cancelled: ", symbol);
                    return true;
                }
                else
                {
                    Print("Failed to cancel pending order: ", trade.ResultRetcodeDescription());
                    return false;
                }
            }
        }
    }
    
    Print("Pending order not found: ", symbol);
    return false;
}

//+------------------------------------------------------------------+
//| Mark order as processed on bridge                                |
//+------------------------------------------------------------------+
void MarkOrderAsProcessed(string orderId)
{
    string url = BridgeUrl + "/api/orders/" + orderId + "/processed";
    string headers = "Content-Type: application/json\r\n";
    
    char data[];
    char result[];
    string resultHeaders;
    
    int timeout = 5000;
    
    int res = WebRequest("POST", url, headers, timeout, data, result, resultHeaders);
    
    if(res != 200)
    {
        Print("Failed to mark order as processed: ", orderId);
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
