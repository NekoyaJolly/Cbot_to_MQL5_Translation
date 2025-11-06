# Ctrader to MT5 Trade Synchronization System

このプロジェクトは、Ctrader（cBot）で行ったFXトレードをMT5にリアルタイムで同期するシステムです。

## システムアーキテクチャ

```
Ctrader (cBot/C#) → HTTP → Bridge Server (C#/.NET) → HTTP → MT5 (MQL5 EA)
```

### コンポーネント

1. **CtraderBot (C#/cAlgo)**: Ctrader側で取引をフックし、Bridgeサーバーに送信
2. **Bridge Server (C# ASP.NET Core)**: ローカルでキューを管理し、REST APIを提供
3. **MT5EA (MQL5)**: Bridgeをポーリングして注文を取得し、MT5で実行

## 機能

### サポートされている取引イベント

- ✅ ポジションオープン（成行注文）
- ✅ ポジションクローズ
- ✅ ポジション変更（ストップロス/テイクプロフィット）
- ✅ 指値/逆指値注文の作成
- ✅ 待機注文のキャンセル
- ✅ 待機注文の約定

### 特徴

- **低遅延**: ポーリング間隔は1秒（カスタマイズ可能）
- **スレッドセーフ**: 並行処理に対応したキュー管理
- **エラーハンドリング**: 接続エラーや取引エラーに対応
- **自動クリーンアップ**: 古い処理済み注文を自動削除

## セットアップ手順

### 1. Bridge Serverのセットアップ

#### 必要な環境
- .NET 8.0 SDK以上

#### インストールと起動

```bash
cd Bridge
dotnet restore
dotnet run
```

サーバーはデフォルトで `http://localhost:5000` で起動します。

#### 動作確認

```bash
# ヘルスチェック
curl http://localhost:5000/api/health

# 統計情報の確認
curl http://localhost:5000/api/statistics
```

### 2. Ctrader cBotのセットアップ

#### インストール

1. Ctraderを開く
2. 「Automate」タブに移動
3. 新しいcBotを作成または既存のものを開く
4. `CtraderBot/TradeSyncBot.cs` の内容をコピー
5. cBotをビルドして保存

#### 設定

- **Bridge Server URL**: Bridge Serverのアドレス（デフォルト: `http://localhost:5000`）
- **Enable Sync**: 同期を有効化（デフォルト: true）

#### 起動

1. チャートにcBotをドラッグ＆ドロップ
2. パラメータを確認して開始
3. ログで接続状態を確認

### 3. MT5 EAのセットアップ

#### インストール

1. MT5のデータフォルダを開く（ファイル → データフォルダを開く）
2. `MQL5/Experts/` フォルダに移動
3. `MT5EA/TradeSyncReceiver.mq5` をコピー
4. `MQL5/Include/` フォルダに `MT5EA/JAson.mqh` をコピー
5. MetaEditorでコンパイル

#### 重要: WebRequest設定

MT5でHTTPリクエストを許可する必要があります：

1. ツール → オプション → エキスパートアドバイザー
2. 「WebRequestを許可するURLリスト」に以下を追加:
   ```
   http://localhost:5000
   ```

#### 設定

- **Bridge URL**: Bridge Serverのアドレス（デフォルト: `http://localhost:5000`）
- **Poll Interval**: ポーリング間隔（ミリ秒、デフォルト: 1000）
- **Enable Sync**: 同期を有効化（デフォルト: true）
- **Slippage Points**: スリッページ（ポイント、デフォルト: 10）
- **Magic Number**: マジックナンバー（デフォルト: 123456）

#### 起動

1. チャートにEAをドラッグ＆ドロップ
2. 「アルゴリズム取引」ボタンを有効化
3. ログで接続状態を確認

## 使用方法

### 基本的な流れ

1. **Bridge Serverを起動**
   ```bash
   cd Bridge
   dotnet run
   ```

2. **Ctrader cBotを起動**
   - CtraderでチャートにcBotを適用
   - パラメータを確認して開始

3. **MT5 EAを起動**
   - MT5でチャートにEAを適用
   - アルゴリズム取引を有効化

4. **取引を開始**
   - Ctraderで通常通り取引を実行
   - MT5で自動的に同じ取引が実行される

### トラブルシューティング

#### Bridge Serverに接続できない

- Bridge Serverが起動しているか確認
- ファイアウォールでポート5000が開いているか確認
- URLが正しいか確認（`http://localhost:5000`）

#### MT5でWebRequestエラーが発生

- オプションでWebRequestが許可されているか確認
- URLリストに `http://localhost:5000` が追加されているか確認
- MT5を再起動

#### 注文が実行されない

- MT5のログを確認してエラーメッセージを確認
- シンボル名がMT5で有効か確認
- 口座に十分な証拠金があるか確認
- アルゴリズム取引が有効化されているか確認

#### シンボル名の違い

CtraderとMT5でシンボル名が異なる場合、`TradeSyncReceiver.mq5` の `NormalizeSymbol()` 関数をカスタマイズしてください。

例：
```mql5
string NormalizeSymbol(string symbol)
{
    // EURUSDをEURUSD.rawに変換
    if(symbol == "EURUSD")
        return "EURUSD.raw";
    
    // その他のマッピング
    // ...
    
    return symbol;
}
```

## Bridge Server API リファレンス

### エンドポイント

#### POST /api/orders
注文を受信（Ctraderから）

**リクエスト:**
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

**レスポンス:**
```json
{
    "OrderId": "1",
    "Status": "Queued"
}
```

#### GET /api/orders/pending
待機中の注文を取得（MT5から）

**パラメータ:**
- `maxCount`: 取得する最大件数（デフォルト: 10）

**レスポンス:**
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
注文を処理済みとしてマーク（MT5から）

**レスポンス:**
```json
{
    "Status": "Processed"
}
```

#### GET /api/orders/{orderId}
特定の注文を取得

#### GET /api/statistics
統計情報を取得

**レスポンス:**
```json
{
    "TotalOrders": 100,
    "PendingOrders": 5,
    "ProcessedOrders": 95,
    "OrdersLast5Min": 10
}
```

#### GET /api/health
ヘルスチェック

## パフォーマンス

- **遅延**: 通常1-2秒（ポーリング間隔に依存）
- **スループット**: 毎秒100注文以上を処理可能
- **メモリ**: Bridge Serverは約50MB使用
- **CPU**: 低負荷（アイドル時 < 1%）

## セキュリティ考慮事項

- **ローカルネットワーク**: デフォルトではlocalhostのみリッスン
- **認証なし**: 現在の実装では認証機能なし（将来のバージョンで追加予定）
- **リモートアクセス**: リモートアクセスが必要な場合は、VPNやSSHトンネルを使用することを推奨

## 今後の改善予定

- [ ] 認証・認可機能の追加
- [ ] WebSocketによるリアルタイム通信（ポーリングの削減）
- [ ] 取引履歴の永続化（データベース）
- [ ] 複数のCtrader/MT5インスタンスのサポート
- [ ] ダッシュボードUI
- [ ] より詳細なエラーハンドリングとリトライメカニズム
- [ ] シンボル名マッピングの設定ファイル化

## ライセンス

MIT License

## サポート

問題が発生した場合は、GitHubのIssuesで報告してください。
