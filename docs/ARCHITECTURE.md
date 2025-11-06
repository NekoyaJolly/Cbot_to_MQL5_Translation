# System Architecture Diagram / システムアーキテクチャ図

## High-Level Architecture / 高レベルアーキテクチャ

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Trade Synchronization Flow                    │
│                        トレード同期フロー                              │
└─────────────────────────────────────────────────────────────────────┘

┌──────────────┐          ┌──────────────┐          ┌──────────────┐
│              │   HTTP   │              │   HTTP   │              │
│   Ctrader   │  POST    │    Bridge    │   GET    │     MT5      │
│    cBot     │  ──────> │    Server    │  <────── │     EA       │
│  (C#/cAlgo) │          │  (ASP.NET)   │          │   (MQL5)     │
│              │          │              │          │              │
└──────────────┘          └──────────────┘          └──────────────┘
      │                          │                          │
      │                          │                          │
      ▼                          ▼                          ▼
  Trade Events              Queue Manager              Execute Orders
  取引イベント              キュー管理                  注文実行
```

## Detailed Component Interaction / 詳細なコンポーネント間の相互作用

```
Ctrader Platform                Bridge Server                MT5 Platform
───────────────                ───────────────              ────────────

┌─────────────┐                                           
│ User Places │                                           
│   Trade     │                                           
│ ユーザー注文 │                                           
└──────┬──────┘                                           
       │                                                  
       ▼                                                  
┌─────────────┐                                           
│ TradeSyncBot│                                           
│  Hooks Event│                                           
│ イベントフック│                                           
└──────┬──────┘                                           
       │                                                  
       │ POST /api/orders                                
       │ {EventType, Symbol, Volume...}                  
       │                                                  
       └──────────────────────┐                          
                              │                          
                              ▼                          
                       ┌─────────────┐                   
                       │OrdersController                 
                       │  Receives Order                 
                       │  注文を受信                      
                       └──────┬──────┘                   
                              │                          
                              ▼                          
                       ┌─────────────┐                   
                       │OrderQueueManager                
                       │  Adds to Queue                  
                       │  キューに追加                    
                       │                                 
                       │ ConcurrentQueue                 
                       │ ConcurrentDictionary            
                       └──────┬──────┘                   
                              │                          
                              │                          
                              │ GET /api/orders/pending  
                       ┌──────┴──────────────────────────┐
                       │                                 │
                       │                                 ▼
                       │                          ┌─────────────┐
                       │                          │TradeSyncReceiver
                       │                          │  Polls Every 1s
                       │                          │ 1秒ごとにポーリング
                       │                          └──────┬──────┘
                       │                                 │
                       │                                 ▼
                       │                          ┌─────────────┐
                       │                          │Parse Orders │
                       │                          │ JAson.mqh   │
                       │                          │ JSON解析     │
                       │                          └──────┬──────┘
                       │                                 │
                       │                                 ▼
                       │                          ┌─────────────┐
                       │                          │ CTrade Class│
                       │                          │Execute Order│
                       │                          │ 注文を実行   │
                       │                          └──────┬──────┘
                       │                                 │
                       │ POST /api/orders/{id}/processed │
                       │ ◄───────────────────────────────┘
                       │                                 │
                       ▼                                 ▼
                ┌─────────────┐                  ┌─────────────┐
                │Mark Processed│                  │  Order in   │
                │処理済みマーク │                  │  MT5 Account│
                │              │                  │MT5口座に注文 │
                └─────────────┘                  └─────────────┘
```

## Event Types / イベントタイプ

```
Ctrader Event           Bridge Queue            MT5 Action
───────────────        ────────────            ──────────

POSITION_OPENED    →    Queued    →    OrderSend() (Market)
                                       成行注文

POSITION_CLOSED    →    Queued    →    OrderClose()
                                       ポジションクローズ

POSITION_MODIFIED  →    Queued    →    OrderModify()
                                       SL/TP変更

PENDING_ORDER      →    Queued    →    OrderSend() (Pending)
 _CREATED                              指値/逆指値注文

PENDING_ORDER      →    Queued    →    OrderDelete()
 _CANCELLED                            注文キャンセル
```

## Data Flow Example / データフローの例

```
Step 1: User buys EURUSD in Ctrader / ユーザーがCtraderでEURUSDを買う
┌────────────────────────────────────────────────────────────┐
│ Ctrader: ExecuteMarketOrder(EURUSD, Buy, 0.1 lots)       │
└────────────────────────────────────────────────────────────┘
                            │
                            ▼
Step 2: cBot hooks the event / cBotがイベントをフック
┌────────────────────────────────────────────────────────────┐
│ TradeSyncBot.OnPositionOpened()                           │
│ Creates JSON: {                                           │
│   EventType: "POSITION_OPENED",                           │
│   Symbol: "EURUSD",                                       │
│   Direction: "Buy",                                       │
│   Volume: 0.1,                                            │
│   EntryPrice: 1.0950,                                     │
│   StopLoss: 1.0900,                                       │
│   TakeProfit: 1.1000                                      │
│ }                                                         │
└────────────────────────────────────────────────────────────┘
                            │
                            ▼
Step 3: Send to Bridge / Bridgeに送信
┌────────────────────────────────────────────────────────────┐
│ HTTP POST http://localhost:5000/api/orders                │
└────────────────────────────────────────────────────────────┘
                            │
                            ▼
Step 4: Bridge queues order / Bridgeが注文をキューイング
┌────────────────────────────────────────────────────────────┐
│ OrderQueueManager.AddOrder()                              │
│ Assigns ID: "1"                                           │
│ Status: Queued                                            │
└────────────────────────────────────────────────────────────┘
                            │
                            ▼
Step 5: MT5 EA polls (every 1 second) / MT5 EAがポーリング (1秒毎)
┌────────────────────────────────────────────────────────────┐
│ HTTP GET http://localhost:5000/api/orders/pending         │
│ Receives: [{ Id: "1", EventType: "POSITION_OPENED", ...}]│
└────────────────────────────────────────────────────────────┘
                            │
                            ▼
Step 6: MT5 executes order / MT5が注文を実行
┌────────────────────────────────────────────────────────────┐
│ ProcessPositionOpened()                                   │
│ trade.Buy(0.1, "EURUSD", price, SL, TP)                  │
│ Result: Order placed successfully                         │
└────────────────────────────────────────────────────────────┘
                            │
                            ▼
Step 7: Mark as processed / 処理済みとしてマーク
┌────────────────────────────────────────────────────────────┐
│ HTTP POST http://localhost:5000/api/orders/1/processed    │
└────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌────────────────────────────────────────────────────────────┐
│ COMPLETE: Same position in both Ctrader and MT5          │
│ 完了: CtraderとMT5の両方に同じポジション                    │
└────────────────────────────────────────────────────────────┘
```

## Technology Stack / 技術スタック

```
┌─────────────────────────────────────────────────────────────┐
│                       Ctrader cBot                          │
├─────────────────────────────────────────────────────────────┤
│ Language: C#                                                │
│ Framework: cAlgo API / AutomateAPI                          │
│ Libraries: System.Net.Http, Newtonsoft.Json                │
│ Events: Positions.Opened/Closed/Modified                    │
│         PendingOrders.Created/Cancelled/Filled              │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│                      Bridge Server                          │
├─────────────────────────────────────────────────────────────┤
│ Language: C#                                                │
│ Framework: ASP.NET Core 8.0                                 │
│ Architecture: REST API                                      │
│ Threading: ConcurrentDictionary, ConcurrentQueue           │
│ Services: OrdersController, CleanupService                  │
│ Port: 5000 (HTTP)                                          │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│                         MT5 EA                              │
├─────────────────────────────────────────────────────────────┤
│ Language: MQL5                                              │
│ Libraries: Trade.mqh, JAson.mqh (custom)                   │
│ Classes: CTrade                                             │
│ Method: Polling (OnTick with interval check)               │
│ Features: Symbol normalization, error handling              │
└─────────────────────────────────────────────────────────────┘
```

## Performance Characteristics / パフォーマンス特性

```
Metric                  Value                Notes
──────────────────────  ───────────────────  ─────────────────────
Latency                 1-2 seconds          Depends on poll interval
                        1-2秒                 ポーリング間隔に依存

Throughput              100+ orders/sec      Bridge server capacity
                        100以上の注文/秒      ブリッジサーバー容量

Memory Usage            ~50MB                Bridge server
                        約50MB               ブリッジサーバー

CPU Usage (Idle)        < 1%                 All components
                        1%未満               全コンポーネント

Reliability             High                 Auto-retry on network errors
                        高い                 ネットワークエラー時自動リトライ
```

## Security Model / セキュリティモデル

```
┌─────────────────────────────────────────────────────────────┐
│                    Current Implementation                    │
│                    現在の実装                                 │
├─────────────────────────────────────────────────────────────┤
│ Transport:         HTTP (localhost only)                    │
│ Authentication:    None (planned for future)                │
│ Authorization:     None (planned for future)                │
│ Network:           Localhost (127.0.0.1)                    │
│ Firewall:          Port 5000 (internal only)                │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│              Recommended for Production                      │
│              本番環境での推奨事項                              │
├─────────────────────────────────────────────────────────────┤
│ Transport:         HTTPS with SSL/TLS                       │
│ Authentication:    API Key or JWT                           │
│ Authorization:     Role-based access control                │
│ Network:           VPN or SSH tunnel for remote access      │
│ Firewall:          IP whitelist, rate limiting              │
└─────────────────────────────────────────────────────────────┘
```
