# Production Best Practices / 本番環境のベストプラクティス

## Overview / 概要

This document outlines best practices for deploying and running the Cbot to MQL5 Translation system in production environments (cTrader and MT5).

このドキュメントは、Cbot to MQL5 Translation システムを本番環境（cTraderとMT5）で展開・実行するためのベストプラクティスを説明します。

---

## 1. cTrader cBot Best Practices / cTrader cBot のベストプラクティス

### Error Handling / エラーハンドリング

✅ **Implemented** - All event handlers now include:
- Null checks for all position and order objects
- Circuit breaker pattern to prevent excessive failed requests
- Specific exception handling (HttpRequestException, TaskCanceledException)
- Graceful degradation when Bridge is unavailable

**実装済み** - すべてのイベントハンドラに以下を含む：
- すべてのポジションと注文オブジェクトのnullチェック
- 過剰な失敗リクエストを防ぐサーキットブレーカーパターン
- 特定の例外ハンドリング（HttpRequestException、TaskCanceledException）
- Bridgeが利用不可能な場合のグレースフル・デグレデーション

### Configuration / 設定

**Recommended Settings:**
```csharp
[Parameter("Bridge Server URL", DefaultValue = "http://localhost:5000")]
public string BridgeUrl { get; set; }

[Parameter("Enable Sync", DefaultValue = true)]
public bool EnableSync { get; set; }
```

**推奨設定:**
- Bridge URLは環境に応じて変更してください
- 初期テストではEnable Sync = falseで開始し、動作確認後にtrueに変更

### Testing / テスト

Before production:
1. Test with demo account first
2. Verify all event types are properly sent to Bridge
3. Monitor log output for any errors
4. Test circuit breaker by intentionally stopping Bridge

本番前に：
1. デモアカウントで最初にテスト
2. すべてのイベントタイプがBridgeに正しく送信されることを確認
3. エラーのログ出力を監視
4. Bridgeを意図的に停止してサーキットブレーカーをテスト

---

## 2. MT5 EA Best Practices / MT5 EA のベストプラクティス

### Critical Fixes Applied / 適用された重要な修正

✅ **Volume Validation** - All volume values are now validated against:
- Minimum volume (SYMBOL_VOLUME_MIN)
- Maximum volume (SYMBOL_VOLUME_MAX)
- Volume step (SYMBOL_VOLUME_STEP)

✅ **ボリューム検証** - すべてのボリューム値を以下に対して検証：
- 最小ボリューム（SYMBOL_VOLUME_MIN）
- 最大ボリューム（SYMBOL_VOLUME_MAX）
- ボリュームステップ（SYMBOL_VOLUME_STEP）

✅ **Input Validation** - All order data is validated before processing:
- Symbol name validation
- Direction validation
- Required field checks

✅ **入力検証** - すべての注文データを処理前に検証：
- シンボル名の検証
- 方向の検証
- 必須フィールドのチェック

✅ **Order Filling Policy** - Changed from FOK to RETURN for better broker compatibility

✅ **注文充填ポリシー** - ブローカー互換性向上のためFOKからRETURNに変更

### Configuration / 設定

**Recommended Input Parameters:**
```mql5
input string BridgeUrl = "http://localhost:5000";     // Bridge Server URL
input int    PollInterval = 1000;                      // Poll interval (ms)
input bool   EnableSync = true;                        // Enable sync
input double SlippagePoints = 10;                      // Slippage (points)
input int    MagicNumber = 123456;                     // Magic number
```

**推奨入力パラメータ:**
- PollInterval: 1000-3000ms推奨（高頻度すぎるとブローカーに拒否される可能性）
- SlippagePoints: ブローカーとシンボルに応じて調整
- MagicNumber: 他のEAと重複しないユニークな番号を使用

### Pre-Deployment Checklist / デプロイ前チェックリスト

Before deploying to live account:

1. ✅ Add Bridge URL to MT5 WebRequest allowed list:
   - Tools → Options → Expert Advisors → Allow WebRequest for listed URL
   - Add: `http://localhost:5000` (or your Bridge server URL)

2. ✅ Copy files to correct locations:
   - `TradeSyncReceiver.mq5` → `MQL5/Experts/`
   - `JAson.mqh` → `MQL5/Include/`

3. ✅ Compile the EA in MetaEditor:
   - Open `TradeSyncReceiver.mq5`
   - Press F7 or click Compile
   - Ensure no errors

4. ✅ Test on demo account:
   - Apply EA to chart
   - Verify connection to Bridge in Experts tab
   - Test with small position from cTrader
   - Monitor for errors

ライブアカウントにデプロイする前に：

1. ✅ Bridge URLをMT5 WebRequest許可リストに追加：
   - ツール → オプション → エキスパートアドバイザー → リストされたURLのWebRequestを許可
   - 追加: `http://localhost:5000`（またはBridgeサーバーのURL）

2. ✅ ファイルを正しい場所にコピー：
   - `TradeSyncReceiver.mq5` → `MQL5/Experts/`
   - `JAson.mqh` → `MQL5/Include/`

3. ✅ MetaEditorでEAをコンパイル：
   - `TradeSyncReceiver.mq5`を開く
   - F7を押すかコンパイルをクリック
   - エラーがないことを確認

4. ✅ デモアカウントでテスト：
   - チャートにEAを適用
   - エキスパートタブでBridgeへの接続を確認
   - cTraderから小さなポジションでテスト
   - エラーを監視

### Symbol Mapping / シンボルマッピング

Different brokers may use different symbol naming conventions. The `NormalizeSymbol()` function attempts common variations, but you may need to customize it:

```mql5
string NormalizeSymbol(string symbol)
{
    // Add your broker-specific symbol mappings here
    // Example: if your broker uses "EURUSDm" instead of "EURUSD"
    
    if(SymbolSelect(symbol, true))
        return symbol;
    
    // Try common variations
    string variations[];
    ArrayResize(variations, 4);
    variations[0] = symbol;
    variations[1] = symbol + ".";
    variations[2] = symbol + "m";
    variations[3] = symbol + ".raw";
    
    for(int i = 0; i < ArraySize(variations); i++)
    {
        if(SymbolSelect(variations[i], true))
            return variations[i];
    }
    
    return ""; // Symbol not found
}
```

異なるブローカーは異なるシンボル命名規則を使用する可能性があります。`NormalizeSymbol()`関数は一般的なバリエーションを試みますが、カスタマイズが必要な場合があります。

### Error Recovery / エラー回復

The EA now marks all orders as processed to prevent infinite retry loops. Failed orders should be handled through:

1. Manual review of logs
2. Bridge statistics monitoring
3. Implementing a separate retry mechanism if needed

EAは無限リトライループを防ぐために、すべての注文を処理済みとしてマークするようになりました。失敗した注文は以下を通じて処理すべきです：

1. ログの手動レビュー
2. Bridge統計の監視
3. 必要に応じて別のリトライメカニズムの実装

---

## 3. Bridge Server Best Practices / Bridgeサーバーのベストプラクティス

### Security Improvements / セキュリティ改善

✅ **Implemented:**
- Input sanitization to prevent injection attacks
- Request size validation
- JSON depth limits (max 32 levels)
- Restrictive CORS policy (localhost only by default)
- Error messages don't expose internal details

✅ **実装済み:**
- インジェクション攻撃を防ぐ入力サニタイゼーション
- リクエストサイズ検証
- JSON深度制限（最大32レベル）
- 制限的なCORSポリシー（デフォルトではlocalhostのみ）
- エラーメッセージが内部詳細を露出しない

### Configuration / 設定

**Production Configuration:**

1. **CORS Policy** - Adjust based on your network setup:

```csharp
services.AddCors(options =>
{
    options.AddDefaultPolicy(builder =>
    {
        // For production, specify exact origins
        builder.WithOrigins(
            "http://localhost", 
            "http://127.0.0.1",
            "http://your-ctrader-machine-ip"  // Add if needed
        )
        .AllowAnyMethod()
        .AllowAnyHeader();
    });
});
```

**本番設定:**

1. **CORSポリシー** - ネットワーク設定に基づいて調整：
   - ローカルネットワークの場合、cTraderマシンのIPを追加
   - インターネット経由の場合、HTTPS使用を強く推奨

2. **Cleanup Interval** - Currently set to 10 minutes, processes older than 1 hour are removed:

```csharp
// In CleanupService.ExecuteAsync
_queueManager.CleanupOldOrders(TimeSpan.FromHours(1));
await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
```

2. **クリーンアップ間隔** - 現在10分に設定、1時間以上古い処理済みデータを削除：
   - 高頻度取引の場合、間隔を短縮することを検討
   - ストレージが懸念される場合、保持時間を短縮

### Monitoring / 監視

**Key Metrics to Monitor:**

1. Queue statistics (`/api/statistics`):
   - TotalOrders: Total orders received
   - PendingOrders: Orders waiting to be processed
   - ProcessedOrders: Successfully processed orders
   - OrdersLast5Min: Recent activity

2. Application logs:
   - Connection errors from cTrader
   - Failed order processing
   - Cleanup service activity

**監視すべき主要メトリクス:**

1. キュー統計（`/api/statistics`）：
   - TotalOrders: 受信した総注文数
   - PendingOrders: 処理待ちの注文
   - ProcessedOrders: 正常に処理された注文
   - OrdersLast5Min: 最近のアクティビティ

2. アプリケーションログ：
   - cTraderからの接続エラー
   - 注文処理の失敗
   - クリーンアップサービスのアクティビティ

### Deployment / デプロイ

**Running as a Service (Linux):**

Create a systemd service file `/etc/systemd/system/bridge.service`:

```ini
[Unit]
Description=Trade Sync Bridge Server
After=network.target

[Service]
Type=notify
WorkingDirectory=/path/to/Bridge
ExecStart=/usr/bin/dotnet /path/to/Bridge/bin/Release/net8.0/Bridge.dll
Restart=always
RestartSec=10
User=your-user
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

**サービスとして実行（Linux）:**

systemdサービスファイルを作成：`/etc/systemd/system/bridge.service`

その後：
```bash
sudo systemctl daemon-reload
sudo systemctl enable bridge
sudo systemctl start bridge
sudo systemctl status bridge
```

**Running as a Service (Windows):**

Use NSSM (Non-Sucking Service Manager) or create a Windows Service.

**サービスとして実行（Windows）:**

NSSM（Non-Sucking Service Manager）を使用するか、Windowsサービスを作成します。

### Backup and Recovery / バックアップと復旧

The Bridge server uses in-memory queue management. Consider:

1. **Persistent Storage** (Future Enhancement):
   - Add database for order history
   - Enable recovery after restart

2. **High Availability**:
   - Run multiple Bridge instances behind a load balancer
   - Use shared storage for order queue

Bridgeサーバーはインメモリキュー管理を使用します。検討事項：

1. **永続ストレージ**（将来の拡張）：
   - 注文履歴用のデータベースを追加
   - 再起動後の復旧を有効化

2. **高可用性**：
   - ロードバランサーの背後で複数のBridgeインスタンスを実行
   - 注文キュー用の共有ストレージを使用

---

## 4. Network and Infrastructure / ネットワークとインフラストラクチャ

### Network Requirements / ネットワーク要件

1. **Low Latency Required:**
   - Bridge should be on same network as cTrader and MT5
   - Recommended: < 10ms latency between components
   - Use wired connections, not WiFi

2. **Firewall Configuration:**
   - Allow port 5000 (or configured port) for Bridge
   - Ensure MT5 can reach Bridge URL
   - Ensure cTrader can reach Bridge URL

**必要な低レイテンシ:**
- Bridgeはcトレーダーとmt5と同じネットワーク上にあるべき
- 推奨：コンポーネント間のレイテンシ< 10ms
- WiFiではなく有線接続を使用

### Failover Strategy / フェイルオーバー戦略

**Circuit Breaker in cTrader cBot:**
- After 10 consecutive failures, stops sending for 5 minutes
- Automatically retries after cooldown period
- Monitor logs for circuit breaker activation

**cTrader cBotのサーキットブレーカー:**
- 10回連続失敗後、5分間送信を停止
- クールダウン期間後に自動的に再試行
- サーキットブレーカーのアクティベーションをログで監視

**MT5 EA Polling:**
- Continues polling even if orders fail
- Always marks orders as processed to prevent stale data
- Manual intervention required for failed orders

**MT5 EAポーリング:**
- 注文が失敗してもポーリングを継続
- 古いデータを防ぐために常に注文を処理済みとしてマーク
- 失敗した注文には手動介入が必要

---

## 5. Testing Strategy / テスト戦略

### Pre-Production Testing / 本番前テスト

**Phase 1: Component Testing**
1. ✅ Test Bridge server independently (use curl commands)
2. ✅ Test cTrader cBot with demo account
3. ✅ Test MT5 EA with demo account

**Phase 2: Integration Testing**
1. Test full flow: cTrader → Bridge → MT5
2. Test all event types:
   - Position opened
   - Position closed
   - Position modified
   - Pending order created
   - Pending order cancelled
   - Pending order filled

**Phase 3: Stress Testing**
1. Test rapid order submission
2. Test Bridge restart during operation
3. Test network interruption
4. Test invalid data handling

**フェーズ1: コンポーネントテスト**
1. ✅ Bridgeサーバーを独立してテスト（curlコマンドを使用）
2. ✅ デモアカウントでcTrader cBotをテスト
3. ✅ デモアカウントでMT5 EAをテスト

**フェーズ2: 統合テスト**
1. 完全なフローをテスト：cTrader → Bridge → MT5
2. すべてのイベントタイプをテスト：
   - ポジションオープン
   - ポジションクローズ
   - ポジション変更
   - ペンディングオーダー作成
   - ペンディングオーダーキャンセル
   - ペンディングオーダー約定

**フェーズ3: ストレステスト**
1. 高速注文送信をテスト
2. 動作中のBridge再起動をテスト
3. ネットワーク中断をテスト
4. 無効なデータ処理をテスト

### Load Testing Script / 負荷テストスクリプト

See `docs/TESTING.md` for detailed load testing scripts.

詳細な負荷テストスクリプトについては`docs/TESTING.md`を参照してください。

---

## 6. Common Issues and Solutions / 一般的な問題と解決策

### Issue 1: MT5 WebRequest Error -1

**Problem:** MT5 cannot connect to Bridge server

**Solution:**
1. Verify URL is in allowed list (Tools → Options → Expert Advisors)
2. Check Bridge server is running (`curl http://localhost:5000/api/health`)
3. Check firewall allows connection
4. Verify URL format (http://, not https://)

**問題:** MT5がBridgeサーバーに接続できない

**解決策:**
1. URLが許可リストにあることを確認（ツール → オプション → エキスパートアドバイザー）
2. Bridgeサーバーが実行中であることを確認
3. ファイアウォールが接続を許可していることを確認
4. URL形式を確認（https://ではなくhttp://）

### Issue 2: cTrader Circuit Breaker Activated

**Problem:** cBot stops sending orders after multiple failures

**Solution:**
1. Check Bridge server is running
2. Wait 5 minutes for automatic reset
3. Restart cBot if needed
4. Check network connectivity

**問題:** cBotが複数の失敗後に注文送信を停止

**解決策:**
1. Bridgeサーバーが実行中であることを確認
2. 自動リセットのために5分待つ
3. 必要に応じてcBotを再起動
4. ネットワーク接続を確認

### Issue 3: Symbol Not Found in MT5

**Problem:** MT5 cannot find symbol from cTrader

**Solution:**
1. Check symbol exists in MT5 Market Watch
2. Customize `NormalizeSymbol()` function for your broker
3. Add symbol mapping in code if needed
4. Verify symbol naming convention matches

**問題:** MT5がcTraderからのシンボルを見つけられない

**解決策:**
1. MT5マーケットウォッチにシンボルが存在することを確認
2. ブローカー用に`NormalizeSymbol()`関数をカスタマイズ
3. 必要に応じてコードにシンボルマッピングを追加
4. シンボル命名規則が一致することを確認

### Issue 4: Orders Not Processing

**Problem:** Orders stuck in pending state

**Solution:**
1. Check Bridge statistics (`/api/statistics`)
2. Check MT5 EA is running and polling
3. Check MT5 logs for errors
4. Verify MT5 account has sufficient margin
5. Check volume is within broker limits

**問題:** 注文が保留状態でスタック

**解決策:**
1. Bridge統計を確認（`/api/statistics`）
2. MT5 EAが実行中でポーリングしていることを確認
3. MT5ログでエラーを確認
4. MT5アカウントに十分な証拠金があることを確認
5. ボリュームがブローカーの制限内であることを確認

---

## 7. Performance Optimization / パフォーマンス最適化

### Recommended Settings / 推奨設定

**For High-Frequency Trading:**
- PollInterval: 1000ms (minimum recommended)
- Bridge cleanup: Every 5 minutes
- Order retention: 30 minutes

**For Normal Trading:**
- PollInterval: 2000-3000ms
- Bridge cleanup: Every 10 minutes
- Order retention: 1 hour

**高頻度取引の場合:**
- PollInterval: 1000ms（推奨最小値）
- Bridgeクリーンアップ: 5分ごと
- 注文保持: 30分

**通常の取引の場合:**
- PollInterval: 2000-3000ms
- Bridgeクリーンアップ: 10分ごと
- 注文保持: 1時間

### Resource Usage / リソース使用量

**Typical Resource Requirements:**
- Bridge Server: < 100 MB RAM, < 5% CPU
- cTrader cBot: Minimal overhead
- MT5 EA: < 10 MB RAM, < 1% CPU

**一般的なリソース要件:**
- Bridgeサーバー: < 100 MB RAM、< 5% CPU
- cTrader cBot: 最小限のオーバーヘッド
- MT5 EA: < 10 MB RAM、< 1% CPU

---

## 8. Compliance and Risk Management / コンプライアンスとリスク管理

### Risk Warnings / リスク警告

⚠️ **IMPORTANT:** This system synchronizes trades between platforms. Errors in synchronization could lead to:
- Duplicate positions
- Missed stop losses
- Excessive leverage
- Account violations

**重要:** このシステムはプラットフォーム間で取引を同期します。同期エラーは以下につながる可能性があります：
- 重複ポジション
- ストップロス漏れ
- 過剰なレバレッジ
- アカウント違反

### Recommended Safety Measures / 推奨される安全対策

1. **Always test on demo accounts first**
2. **Start with small position sizes**
3. **Monitor both platforms continuously**
4. **Keep manual control available**
5. **Set account-level stop loss limits**
6. **Use proper risk management**

1. **常に最初にデモアカウントでテスト**
2. **小さなポジションサイズから開始**
3. **両プラットフォームを継続的に監視**
4. **手動制御を利用可能に保つ**
5. **アカウントレベルのストップロス制限を設定**
6. **適切なリスク管理を使用**

### Logging and Audit Trail / ログと監査証跡

**What to Log:**
- All orders sent from cTrader
- All orders processed by MT5
- All errors and exceptions
- Bridge server statistics

**Keep logs for:**
- Debugging issues
- Compliance requirements
- Performance analysis
- Incident investigation

**ログに記録すべき内容:**
- cTraderから送信されたすべての注文
- MT5で処理されたすべての注文
- すべてのエラーと例外
- Bridgeサーバー統計

**ログの保管理由:**
- 問題のデバッグ
- コンプライアンス要件
- パフォーマンス分析
- インシデント調査

---

## 9. Maintenance / メンテナンス

### Regular Maintenance Tasks / 定期メンテナンスタスク

**Daily:**
- Check Bridge server status
- Review error logs
- Verify synchronization accuracy

**Weekly:**
- Review performance metrics
- Clean up old logs
- Update to latest version if available

**Monthly:**
- Full system test on demo account
- Review and update configuration
- Backup configuration files

**毎日:**
- Bridgeサーバーのステータスを確認
- エラーログをレビュー
- 同期精度を確認

**毎週:**
- パフォーマンスメトリクスをレビュー
- 古いログをクリーンアップ
- 利用可能な場合は最新バージョンに更新

**毎月:**
- デモアカウントで完全なシステムテスト
- 設定をレビューして更新
- 設定ファイルをバックアップ

### Version Updates / バージョンアップデート

Before updating:
1. Test new version on demo account
2. Review changelog for breaking changes
3. Backup current configuration
4. Plan maintenance window
5. Have rollback plan ready

更新前に：
1. デモアカウントで新バージョンをテスト
2. 破壊的変更のためにチェンジログをレビュー
3. 現在の設定をバックアップ
4. メンテナンスウィンドウを計画
5. ロールバック計画を準備

---

## 10. Support and Troubleshooting / サポートとトラブルシューティング

### Debug Mode / デバッグモード

**Enable Verbose Logging:**

**Bridge:**
```json
// appsettings.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft": "Information"
    }
  }
}
```

**MT5 EA:**
- Check "Experts" tab in Terminal window
- All operations are logged with Print() statements

**cTrader cBot:**
- Check "Log" tab in cTrader
- All operations are logged with Print() statements

### Getting Help / ヘルプを得る

For issues:
1. Check this document first
2. Review error logs
3. Check GitHub Issues: https://github.com/NekoyaJolly/Cbot_to_MQL5_Translation/issues
4. Provide detailed information:
   - Error messages
   - Log excerpts
   - Configuration settings
   - Steps to reproduce

問題がある場合：
1. 最初にこのドキュメントを確認
2. エラーログをレビュー
3. GitHub Issuesを確認
4. 詳細情報を提供：
   - エラーメッセージ
   - ログの抜粋
   - 設定
   - 再現手順

---

## Conclusion / 結論

This system has been designed with production reliability in mind. All critical issues have been addressed:

✅ Robust error handling
✅ Input validation
✅ Circuit breaker patterns
✅ Security improvements
✅ Volume validation
✅ Symbol mapping

However, **always test thoroughly on demo accounts** before using in production with real money.

このシステムは本番環境の信頼性を念頭に置いて設計されています。すべての重要な問題が対処されています：

✅ 堅牢なエラーハンドリング
✅ 入力検証
✅ サーキットブレーカーパターン
✅ セキュリティ改善
✅ ボリューム検証
✅ シンボルマッピング

ただし、**実際のお金で本番環境で使用する前に、必ずデモアカウントで徹底的にテストしてください**。

---

**Last Updated:** 2025-11-06
**Version:** 1.1.0
