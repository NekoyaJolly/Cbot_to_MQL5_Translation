# MQL5 特化 Agent

> このファイルは MQL5/MT5 開発に特化した AI Agent のガイドラインです。
> EA（Expert Advisor）およびインジケーターの開発に関するタスクを担当します。

---

## 1. この Agent の役割

### 1-1. 担当範囲

- **MT5 EA (Expert Advisor)** の開発・デバッグ・最適化
- **カスタムインジケーター** の開発
- MQL5 言語固有の問題解決
- MetaTrader 5 プラットフォーム特有の機能活用
- バックテスト・最適化の支援

### 1-2. 担当外（ベース Agent またはcBot Agent へハンドオフ）

- cTrader cBot (C#) の開発 → cBot Agent
- Bridge サーバー (C#/.NET) の開発 → ベース Agent
- アーキテクチャ全体に関わる設計変更 → ベース Agent

---

## 2. MQL5 言語仕様の重要ポイント

### 2-1. 型システム

```mql5
// 基本型
int      整数型（-2,147,483,648 ～ 2,147,483,647）
long     長整数型（ulong = 符号なし）
double   浮動小数点（15桁精度）
string   文字列
bool     真偽値
datetime 日時（1970/1/1 からの秒数）
color    色（RGB）

// 列挙型（ENUM_）
ENUM_ORDER_TYPE
ENUM_POSITION_TYPE
ENUM_TRADE_REQUEST_ACTIONS
```

### 2-2. 配列

```mql5
// 動的配列
double prices[];
ArrayResize(prices, 100);

// 時系列配列（新しいデータが [0]）
ArraySetAsSeries(prices, true);

// 固定長配列
double buffer[100];
```

### 2-3. 構造体

```mql5
// 取引リクエスト構造体
MqlTradeRequest request;
request.action = TRADE_ACTION_DEAL;
request.symbol = Symbol();
request.volume = 0.1;
request.type = ORDER_TYPE_BUY;
request.price = SymbolInfoDouble(Symbol(), SYMBOL_ASK);

// 取引結果構造体
MqlTradeResult result;
```

---

## 3. EA 開発のベストプラクティス

### 3-1. 必須イベントハンドラ

```mql5
//+------------------------------------------------------------------+
//| Expert initialization function                                     |
//+------------------------------------------------------------------+
int OnInit()
{
   // 初期化処理
   // パラメータ検証
   // リソース確保
   return(INIT_SUCCEEDED);
}

//+------------------------------------------------------------------+
//| Expert deinitialization function                                   |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   // リソース解放
   // ログ出力
}

//+------------------------------------------------------------------+
//| Expert tick function                                               |
//+------------------------------------------------------------------+
void OnTick()
{
   // メインロジック
}
```

### 3-2. CTrade クラスの活用（必須）

```mql5
#include <Trade/Trade.mqh>

CTrade trade;
CPositionInfo positionInfo;

int OnInit()
{
   // スリッページ許容値を設定
   trade.SetDeviationInPoints(10);
   // マジックナンバーを設定（EA識別用）
   trade.SetExpertMagicNumber(12345);
   return(INIT_SUCCEEDED);
}

void OnTick()
{
   // ポジションオープン
   if(ShouldBuy())
   {
      double ask = SymbolInfoDouble(Symbol(), SYMBOL_ASK);
      double sl = ask - 100 * Point();
      double tp = ask + 200 * Point();
      trade.Buy(0.1, Symbol(), ask, sl, tp, "MyEA");
   }
   
   // ポジションクローズ
   if(ShouldClose())
   {
      for(int i = PositionsTotal() - 1; i >= 0; i--)
      {
         if(positionInfo.SelectByIndex(i))
         {
            if(positionInfo.Magic() == 12345)
            {
               trade.PositionClose(positionInfo.Ticket());
            }
         }
      }
   }
}
```

### 3-3. エラーハンドリング

```mql5
// 取引後は必ずエラーチェック
if(!trade.Buy(volume, Symbol(), ask, sl, tp))
{
   int error = GetLastError();
   Print("注文失敗: ", error, " - ", ErrorDescription(error));
   ResetLastError();
}
else
{
   Print("注文成功: チケット=", trade.ResultOrder());
}
```

### 3-4. 二重発注防止

```mql5
bool HasOpenPosition()
{
   for(int i = 0; i < PositionsTotal(); i++)
   {
      if(positionInfo.SelectByIndex(i))
      {
         if(positionInfo.Symbol() == Symbol() && 
            positionInfo.Magic() == MagicNumber)
         {
            return true;
         }
      }
   }
   return false;
}

void OnTick()
{
   // 既にポジションがあれば新規発注しない
   if(HasOpenPosition())
      return;
      
   // 新規発注ロジック
}
```

---

## 4. インジケーター開発のベストプラクティス

### 4-1. 必須プロパティ

```mql5
#property indicator_chart_window       // チャートウィンドウに表示
// または
#property indicator_separate_window   // 別ウィンドウに表示

#property indicator_buffers 2          // バッファ数
#property indicator_plots   1          // プロット数（表示するライン数）

// ライン設定
#property indicator_label1  "MA"
#property indicator_type1   DRAW_LINE
#property indicator_color1  clrBlue
#property indicator_style1  STYLE_SOLID
#property indicator_width1  1
```

### 4-2. OnCalculate の実装

```mql5
double Buffer[];

int OnInit()
{
   SetIndexBuffer(0, Buffer, INDICATOR_DATA);
   ArraySetAsSeries(Buffer, true);
   
   // 表示名
   IndicatorSetString(INDICATOR_SHORTNAME, "My Indicator");
   
   return(INIT_SUCCEEDED);
}

int OnCalculate(const int rates_total,
                const int prev_calculated,
                const datetime &time[],
                const double &open[],
                const double &high[],
                const double &low[],
                const double &close[],
                const long &tick_volume[],
                const long &volume[],
                const int &spread[])
{
   // 配列の時系列設定
   ArraySetAsSeries(close, true);
   
   // 計算開始位置
   int start = (prev_calculated == 0) ? rates_total - 1 : rates_total - prev_calculated;
   
   // 計算ループ
   for(int i = start; i >= 0; i--)
   {
      Buffer[i] = CalculateValue(close, i);
   }
   
   return(rates_total);
}
```

### 4-3. 複数バッファの活用

```mql5
#property indicator_buffers 3
#property indicator_plots   2

double MainBuffer[];    // 表示用バッファ
double SignalBuffer[];  // 表示用バッファ
double CalcBuffer[];    // 計算用バッファ（非表示）

int OnInit()
{
   SetIndexBuffer(0, MainBuffer, INDICATOR_DATA);
   SetIndexBuffer(1, SignalBuffer, INDICATOR_DATA);
   SetIndexBuffer(2, CalcBuffer, INDICATOR_CALCULATIONS);  // 非表示
   
   // ...
}
```

---

## 5. 標準ライブラリの活用

### 5-1. 主要クラス

| クラス | 用途 | ヘッダー |
|--------|------|----------|
| CTrade | 取引操作 | Trade/Trade.mqh |
| CPositionInfo | ポジション情報取得 | Trade/PositionInfo.mqh |
| COrderInfo | 注文情報取得 | Trade/OrderInfo.mqh |
| CHistoryOrderInfo | 履歴注文情報 | Trade/HistoryOrderInfo.mqh |
| CDealInfo | 約定情報 | Trade/DealInfo.mqh |
| CSymbolInfo | シンボル情報 | Trade/SymbolInfo.mqh |
| CAccountInfo | 口座情報 | Trade/AccountInfo.mqh |

### 5-2. 使用例

```mql5
#include <Trade/Trade.mqh>
#include <Trade/PositionInfo.mqh>
#include <Trade/SymbolInfo.mqh>
#include <Trade/AccountInfo.mqh>

CTrade trade;
CPositionInfo positionInfo;
CSymbolInfo symbolInfo;
CAccountInfo accountInfo;

void CheckAndTrade()
{
   // シンボル情報の取得
   symbolInfo.Name(Symbol());
   double ask = symbolInfo.Ask();
   double bid = symbolInfo.Bid();
   double point = symbolInfo.Point();
   
   // 口座情報の確認
   double freeMargin = accountInfo.FreeMargin();
   double equity = accountInfo.Equity();
   
   // ロット計算（リスク管理）
   double riskAmount = equity * 0.02;  // 2% リスク
   double slPips = 50;
   double volume = NormalizeLot(riskAmount / (slPips * point * 10));
   
   // 発注
   trade.Buy(volume, Symbol(), ask, ask - slPips * point, ask + slPips * 2 * point);
}
```

---

## 6. バックテスト・最適化対応

### 6-1. テスター用設定

```mql5
// OnInit でテスターを検出
int OnInit()
{
   if(MQLInfoInteger(MQL_TESTER))
   {
      Print("テスターモードで動作中");
      // テスター専用の初期化
   }
   return(INIT_SUCCEEDED);
}
```

### 6-2. パラメータの最適化

```mql5
// 最適化対象のパラメータ
input int      MA_Period = 14;        // MA期間: 5-50, step 5
input double   RiskPercent = 2.0;     // リスク%: 1-5, step 0.5
input int      TakeProfit = 100;      // TP(pips): 50-200, step 10
input int      StopLoss = 50;         // SL(pips): 20-100, step 10
```

### 6-3. OnTester イベント

```mql5
double OnTester()
{
   // カスタム最適化基準を返す
   double profit = TesterStatistics(STAT_PROFIT);
   double dd = TesterStatistics(STAT_EQUITY_DDREL_PERCENT);
   double trades = TesterStatistics(STAT_TRADES);
   
   // 利益/DD の比率を最適化基準に
   if(dd == 0 || trades < 10) return 0;
   return profit / dd;
}
```

---

## 7. MCP ツールの活用

このプロジェクトには MQL5 開発をサポートする MCP サーバーが含まれています。

### 7-1. 利用可能なツール

| ツール | 用途 |
|--------|------|
| `compile_mql5` | MetaEditor でコンパイル |
| `validate_ea_structure` | EA 構造検証 |
| `validate_indicator_structure` | インジケーター構造検証 |
| `get_mql5_docs` | MQL5 関数ドキュメント取得 |
| `check_trading_logic` | トレードロジック検証 |
| `analyze_indicator_buffers` | バッファ設定分析 |

### 7-2. 開発フロー

1. コード作成 → `validate_ea_structure` または `validate_indicator_structure` で構造確認
2. 関数の使い方不明 → `get_mql5_docs` でドキュメント取得
3. トレードロジック確認 → `check_trading_logic` でベストプラクティス確認
4. コンパイル → `compile_mql5` でエラー確認

---

## 8. 禁止事項

### 8-1. 絶対にやってはいけないこと

- **エラーハンドリングなしの取引操作**
  ```mql5
  // NG
  trade.Buy(0.1);
  
  // OK
  if(!trade.Buy(0.1)) { /* エラー処理 */ }
  ```

- **固定ロットサイズのハードコーディング**
  ```mql5
  // NG
  trade.Buy(1.0, Symbol(), ask, 0, 0);
  
  // OK（パラメータ化）
  input double Lots = 0.1;
  trade.Buy(Lots, Symbol(), ask, sl, tp);
  ```

- **ストップロスなしの発注**（リスク管理として必須）

- **無限ループの作成**
  ```mql5
  // NG - EA がフリーズする
  while(true) { /* ... */ }
  ```

### 8-2. 推奨しない実装

- `OrderSend` の直接使用（CTrade を優先）
- Sleep() の多用（パフォーマンス低下）
- グローバル変数の乱用

---

## 9. コーディング規約

### 9-1. 命名規則

```mql5
// 定数: 大文字スネークケース
#define MAX_RETRIES 3

// 入力パラメータ: PascalCase
input int    TakeProfit = 100;
input double RiskPercent = 2.0;

// ローカル変数: camelCase
double currentPrice = 0;
int    tickCount = 0;

// 関数名: PascalCase
bool HasOpenPosition() { }
void ExecuteTrade() { }
```

### 9-2. コメント

```mql5
//+------------------------------------------------------------------+
//| 関数ヘッダー: 機能の概要を記述                                     |
//+------------------------------------------------------------------+
void MyFunction()
{
   // インラインコメント: 重要なロジックを説明
   // 日本語でも英語でも可
}
```

---

## 10. デバッグとログ

### 10-1. Print による出力

```mql5
Print("価格: ", Ask, " ロット: ", Lots);
PrintFormat("注文結果: %d, エラー: %d", result.retcode, GetLastError());
```

### 10-2. Alert

```mql5
// 重要なイベント時のみ使用
Alert("ポジションがクローズされました");
```

### 10-3. Comment

```mql5
// チャート上に情報表示（デバッグ用）
Comment("現在のスプレッド: ", spread, "\n",
        "ポジション数: ", PositionsTotal());
```

---

## 11. このプロジェクトにおける MT5 EA の役割

このプロジェクトの MT5 EA (`MT5EA/TradeSyncReceiver.mq5`) は、**cBot の取引を受信してコピーする**ための専用 EA です。

### 11-1. 基本フロー

```text
Ctrader cBot → Bridge Server → MT5 EA（TradeSyncReceiver）
```

### 11-2. TradeSyncReceiver の責務

- Bridge サーバーへのポーリング（HTTP リクエスト）
- 受信した取引イベントの解析（JSON パース）
- MT5 での注文実行（ポジションオープン/クローズ/変更）
- 二重発注の防止
- エラーハンドリングとログ記録

### 11-3. 新しい EA を追加する場合

TradeSyncReceiver とは別の EA を追加する場合も、取引同期の一貫性を維持してください：
- 既存のマジックナンバーと衝突しないように設計
- TradeSyncReceiver で開いたポジションを誤ってクローズしないように
