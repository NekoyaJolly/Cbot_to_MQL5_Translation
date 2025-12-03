# cBot 特化 Agent

> このファイルは cTrader cBot (C#) および Python Bot 開発に特化した AI Agent のガイドラインです。
> cBot の開発、Bridge サーバーとの連携、取引イベントの送信に関するタスクを担当します。

---

## 1. この Agent の役割

### 1-1. 担当範囲

- **cTrader Automate (cAlgo)** での C# cBot 開発
- **cTrader Open API** を使用した Python Bot 開発
- **Bridge サーバー** との HTTP 通信
- 取引イベントのフックと送信
- エラーハンドリングとリトライロジック

### 1-2. 担当外（他 Agent へハンドオフ）

- MT5 EA (MQL5) の開発 → MQL5 Agent
- Bridge サーバー本体の開発 → ベース Agent
- アーキテクチャ全体に関わる設計変更 → ベース Agent

---

## 2. cTrader Automate (cAlgo) - C# cBot 開発

### 2-1. 基本構造

```csharp
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(AccessRights = AccessRights.FullAccess)]
    public class TradeSyncBot : Robot
    {
        [Parameter("Bridge URL", DefaultValue = "http://localhost:5000")]
        public string BridgeUrl { get; set; }

        private HttpClient _httpClient;

        protected override void OnStart()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(5);
            
            // イベントのサブスクライブ
            Positions.Opened += OnPositionOpened;
            Positions.Closed += OnPositionClosed;
            Positions.Modified += OnPositionModified;
            
            Print("TradeSyncBot started. Bridge URL: " + BridgeUrl);
        }

        protected override void OnStop()
        {
            // イベントのアンサブスクライブ
            Positions.Opened -= OnPositionOpened;
            Positions.Closed -= OnPositionClosed;
            Positions.Modified -= OnPositionModified;
            
            _httpClient?.Dispose();
            Print("TradeSyncBot stopped.");
        }

        protected override void OnTick()
        {
            // 必要に応じてティックごとの処理
        }
    }
}
```

### 2-2. 取引イベントのフック

```csharp
private void OnPositionOpened(PositionOpenedEventArgs args)
{
    var position = args.Position;
    var tradeEvent = new
    {
        EventType = "PositionOpened",
        Symbol = position.SymbolName,
        Volume = position.VolumeInUnits,
        TradeType = position.TradeType.ToString(),
        EntryPrice = position.EntryPrice,
        StopLoss = position.StopLoss,
        TakeProfit = position.TakeProfit,
        PositionId = position.Id,
        Label = position.Label,
        Timestamp = DateTime.UtcNow.ToString("o")
    };
    
    SendToBridge(tradeEvent);
}

private void OnPositionClosed(PositionClosedEventArgs args)
{
    var position = args.Position;
    var tradeEvent = new
    {
        EventType = "PositionClosed",
        Symbol = position.SymbolName,
        Volume = position.VolumeInUnits,
        TradeType = position.TradeType.ToString(),
        ClosePrice = position.CurrentPrice,
        GrossProfit = position.GrossProfit,
        NetProfit = position.NetProfit,
        PositionId = position.Id,
        Reason = args.Reason.ToString(),
        Timestamp = DateTime.UtcNow.ToString("o")
    };
    
    SendToBridge(tradeEvent);
}

private void OnPositionModified(PositionModifiedEventArgs args)
{
    var position = args.Position;
    var tradeEvent = new
    {
        EventType = "PositionModified",
        Symbol = position.SymbolName,
        PositionId = position.Id,
        StopLoss = position.StopLoss,
        TakeProfit = position.TakeProfit,
        Timestamp = DateTime.UtcNow.ToString("o")
    };
    
    SendToBridge(tradeEvent);
}
```

### 2-3. Bridge サーバーへの送信

```csharp
private async void SendToBridge(object tradeEvent)
{
    try
    {
        var json = JsonSerializer.Serialize(tradeEvent);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        var response = await _httpClient.PostAsync($"{BridgeUrl}/api/events", content);
        
        if (response.IsSuccessStatusCode)
        {
            Print($"Event sent successfully: {tradeEvent.GetType().Name}");
        }
        else
        {
            Print($"Failed to send event: {response.StatusCode}");
        }
    }
    catch (Exception ex)
    {
        Print($"Error sending event: {ex.Message}");
    }
}
```

---

## 3. HTTP 通信パターン

### 3-1. リトライロジック

```csharp
private async Task<bool> SendWithRetry(object tradeEvent, int maxRetries = 3)
{
    var json = JsonSerializer.Serialize(tradeEvent);
    var content = new StringContent(json, Encoding.UTF8, "application/json");
    
    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            var response = await _httpClient.PostAsync($"{BridgeUrl}/api/events", content);
            
            if (response.IsSuccessStatusCode)
            {
                return true;
            }
            
            // 4xx エラーはリトライしない
            if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
            {
                Print($"Client error: {response.StatusCode}. Not retrying.");
                return false;
            }
        }
        catch (TaskCanceledException)
        {
            Print($"Attempt {attempt}: Request timeout");
        }
        catch (HttpRequestException ex)
        {
            Print($"Attempt {attempt}: Network error - {ex.Message}");
        }
        
        if (attempt < maxRetries)
        {
            // 指数バックオフ
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
        }
    }
    
    Print("All retry attempts failed.");
    return false;
}
```

### 3-2. イベントキュー（オフライン対応）

```csharp
using System.Collections.Concurrent;

private ConcurrentQueue<object> _eventQueue = new ConcurrentQueue<object>();
private bool _isOnline = true;

private void EnqueueEvent(object tradeEvent)
{
    _eventQueue.Enqueue(tradeEvent);
    ProcessQueue();
}

private async void ProcessQueue()
{
    while (_eventQueue.TryDequeue(out var evt))
    {
        var success = await SendWithRetry(evt);
        if (!success)
        {
            // 失敗時はキューの先頭に戻す
            var tempQueue = new ConcurrentQueue<object>();
            tempQueue.Enqueue(evt);
            while (_eventQueue.TryDequeue(out var remaining))
            {
                tempQueue.Enqueue(remaining);
            }
            _eventQueue = tempQueue;
            
            // 少し待ってから再試行
            await Task.Delay(TimeSpan.FromSeconds(30));
            ProcessQueue();
            break;
        }
    }
}
```

---

## 4. エラーハンドリング

### 4-1. 例外処理のベストプラクティス

```csharp
protected override void OnError(Error error)
{
    Print($"Bot error: {error.TradeResult?.Error}");
    
    // 致命的なエラーの場合は停止
    if (IsCriticalError(error))
    {
        Print("Critical error detected. Stopping bot.");
        Stop();
    }
}

private bool IsCriticalError(Error error)
{
    // 例: 接続エラーが連続した場合
    return false; // 実装に応じて判定
}
```

### 4-2. ログ出力

```csharp
// 通常ログ
Print($"[INFO] Position opened: {position.SymbolName}");

// 詳細ログ
Print($"[DEBUG] Request payload: {json}");

// 警告ログ
Print($"[WARN] Retry attempt {attempt}");

// エラーログ
Print($"[ERROR] Failed to send event: {ex.Message}");
```

---

## 5. Python Bot 開発 (cTrader Open API)

### 5-1. 接続設定

```python
from ctrader_open_api import Client
from ctrader_open_api.messages.OpenApiCommonMessages_pb2 import *
from ctrader_open_api.messages.OpenApiMessages_pb2 import *

class TradeSyncBot:
    def __init__(self, client_id: str, client_secret: str, access_token: str):
        self.client = Client(
            host="demo.ctraderapi.com",
            port=5035,
            clientId=client_id,
            clientSecret=client_secret
        )
        self.access_token = access_token
        self.bridge_url = "http://localhost:5000"
        
    async def connect(self):
        await self.client.startService()
        await self.authenticate()
        
    async def authenticate(self):
        request = ProtoOAApplicationAuthReq()
        request.clientId = self.client.clientId
        request.clientSecret = self.client.clientSecret
        await self.client.send(request)
```

### 5-2. イベントハンドラ

```python
import aiohttp
import json
from datetime import datetime

async def on_execution_event(self, message):
    """取引実行イベントのハンドラ"""
    event_data = {
        "EventType": "ExecutionEvent",
        "OrderId": message.order.orderId,
        "Symbol": message.symbolName,
        "Volume": message.order.tradedVolume / 100,  # センチロット → ロット
        "Side": "Buy" if message.order.orderSide == 1 else "Sell",
        "ExecutionPrice": message.order.executionPrice,
        "Timestamp": datetime.utcnow().isoformat()
    }
    
    await self.send_to_bridge(event_data)

async def send_to_bridge(self, event_data: dict):
    """Bridge サーバーにイベントを送信"""
    async with aiohttp.ClientSession() as session:
        try:
            async with session.post(
                f"{self.bridge_url}/api/events",
                json=event_data,
                timeout=aiohttp.ClientTimeout(total=5)
            ) as response:
                if response.status == 200:
                    print(f"Event sent: {event_data['EventType']}")
                else:
                    print(f"Failed: {response.status}")
        except Exception as e:
            print(f"Error: {e}")
```

### 5-3. リトライロジック（Python版）

```python
import asyncio
from typing import Any

async def send_with_retry(
    self, 
    event_data: dict, 
    max_retries: int = 3
) -> bool:
    """リトライ付きでイベントを送信"""
    for attempt in range(1, max_retries + 1):
        try:
            async with aiohttp.ClientSession() as session:
                async with session.post(
                    f"{self.bridge_url}/api/events",
                    json=event_data,
                    timeout=aiohttp.ClientTimeout(total=5)
                ) as response:
                    if response.status == 200:
                        return True
                    elif 400 <= response.status < 500:
                        print(f"Client error: {response.status}")
                        return False
        except asyncio.TimeoutError:
            print(f"Attempt {attempt}: Timeout")
        except aiohttp.ClientError as e:
            print(f"Attempt {attempt}: {e}")
        
        if attempt < max_retries:
            # 指数バックオフ
            await asyncio.sleep(2 ** attempt)
    
    print("All retries failed")
    return False
```

---

## 6. Bridge サーバーとの連携

### 6-1. API エンドポイント

| メソッド | エンドポイント | 用途 |
|---------|---------------|------|
| POST | `/api/events` | イベント送信 |
| GET | `/api/events` | 未処理イベント取得 |
| POST | `/api/events/{id}/ack` | イベント確認応答 |
| GET | `/api/health` | ヘルスチェック |

### 6-2. イベントフォーマット

```json
{
  "eventType": "PositionOpened",
  "symbol": "EURUSD",
  "volume": 0.1,
  "tradeType": "Buy",
  "entryPrice": 1.08123,
  "stopLoss": 1.07623,
  "takeProfit": 1.09123,
  "positionId": 12345678,
  "label": "TradeSyncBot",
  "timestamp": "2024-01-15T10:30:00.000Z"
}
```

### 6-3. 冪等性の確保

```csharp
// イベントに一意のIDを付与
var tradeEvent = new
{
    EventId = Guid.NewGuid().ToString(),  // 一意のイベントID
    EventType = "PositionOpened",
    // ...
};
```

---

## 7. このプロジェクトの cBot の役割

### 7-1. TradeSyncBot の責務

このプロジェクトの `CtraderBot/TradeSyncBot.cs` は：

1. **すべての取引イベントをキャプチャ**する
   - 手動取引も自動取引も区別なくキャプチャ
   - ポジションのオープン/クローズ/変更を検出

2. **Bridge サーバーに確実に送信**する
   - HTTP POST でイベントを送信
   - 失敗時はリトライ

3. **情報のロスなく伝達**する
   - 必要な情報を全て含める
   - 不要な変換・加工は避ける

### 7-2. フロー図

```text
┌─────────────────┐
│  cTrader        │
│  (手動/自動取引)│
└────────┬────────┘
         │ イベント発生
         ▼
┌─────────────────┐
│  TradeSyncBot   │ ← Positions.Opened/Closed/Modified
│  (cBot)         │
└────────┬────────┘
         │ HTTP POST
         ▼
┌─────────────────┐
│  Bridge Server  │
│  (REST API)     │
└────────┬────────┘
         │ HTTP GET (ポーリング)
         ▼
┌─────────────────┐
│  MT5 EA         │
│  (取引実行)     │
└─────────────────┘
```

---

## 8. 禁止事項

### 8-1. 絶対にやってはいけないこと

- **イベント情報の欠落**
  ```csharp
  // NG: 必要な情報が欠落
  new { Symbol = position.SymbolName };
  
  // OK: 必要な情報を全て含める
  new { 
      Symbol = position.SymbolName,
      Volume = position.VolumeInUnits,
      TradeType = position.TradeType.ToString(),
      // ...
  };
  ```

- **Bridge のバイパス**
  ```csharp
  // NG: Bridge を経由せずに直接 MT5 と通信
  // 必ず Bridge を経由すること
  ```

- **例外の握りつぶし**
  ```csharp
  // NG
  catch (Exception) { }
  
  // OK
  catch (Exception ex)
  {
      Print($"Error: {ex.Message}");
  }
  ```

### 8-2. 推奨しない実装

- 同期 HTTP 呼び出し（async/await を使用）
- 大量のログ出力（パフォーマンス低下）
- グローバル状態への過度な依存

---

## 9. コーディング規約

### 9-1. C# 命名規則

```csharp
// パブリックプロパティ: PascalCase
public string BridgeUrl { get; set; }

// プライベートフィールド: _camelCase
private HttpClient _httpClient;

// メソッド: PascalCase
private void OnPositionOpened(PositionOpenedEventArgs args)

// ローカル変数: camelCase
var tradeEvent = new { ... };
```

### 9-2. Python 命名規則

```python
# クラス: PascalCase
class TradeSyncBot:

# メソッド/関数: snake_case
async def send_to_bridge(self, event_data):

# 変数: snake_case
bridge_url = "http://localhost:5000"

# 定数: UPPER_SNAKE_CASE
MAX_RETRIES = 3
```

---

## 10. テスト

### 10-1. ユニットテスト（C#）

```csharp
[TestClass]
public class TradeSyncBotTests
{
    [TestMethod]
    public void CreateEvent_ShouldContainRequiredFields()
    {
        // Arrange
        var position = CreateMockPosition();
        
        // Act
        var evt = CreatePositionOpenedEvent(position);
        
        // Assert
        Assert.IsNotNull(evt.Symbol);
        Assert.IsTrue(evt.Volume > 0);
    }
}
```

### 10-2. 統合テスト

```bash
# E2E テストの実行
dotnet test E2ETests/
```

---

## 11. デバッグ

### 11-1. cTrader でのデバッグ

```csharp
// ログレベル付きの出力
Print("[DEBUG] Sending event: " + JsonSerializer.Serialize(tradeEvent));
Print("[INFO] Event sent successfully");
Print("[ERROR] Failed to send: " + ex.Message);
```

### 11-2. Bridge 接続確認

```csharp
protected override void OnStart()
{
    // 起動時に接続テスト
    try
    {
        var response = _httpClient.GetAsync($"{BridgeUrl}/api/health").Result;
        if (response.IsSuccessStatusCode)
        {
            Print("Bridge connection: OK");
        }
        else
        {
            Print("Bridge connection: FAILED");
        }
    }
    catch (Exception ex)
    {
        Print("Bridge connection error: " + ex.Message);
    }
}
```

---

## 12. パフォーマンス考慮

### 12-1. HTTP クライアントの再利用

```csharp
// NG: 毎回新しいクライアントを作成
private void Send()
{
    using var client = new HttpClient();  // 非効率
    // ...
}

// OK: クライアントを再利用
private HttpClient _httpClient;

protected override void OnStart()
{
    _httpClient = new HttpClient();
}
```

### 12-2. 非同期処理

```csharp
// NG: 同期呼び出し（ブロック）
var response = _httpClient.PostAsync(...).Result;

// OK: 非同期呼び出し
var response = await _httpClient.PostAsync(...);

// 注意: cBot のイベントハンドラは async void が許容される
private async void OnPositionOpened(PositionOpenedEventArgs args)
{
    await SendWithRetry(tradeEvent);
}
```
