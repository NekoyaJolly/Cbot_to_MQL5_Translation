# Quick Start Guide / クイックスタートガイド

このガイドでは、システムの基本的なセットアップと動作確認を最短で行います。

This guide provides the fastest way to set up and verify the system.

## Prerequisites / 前提条件

✅ .NET 8.0 SDK installed / .NET 8.0 SDKがインストール済み  
✅ Ctrader installed / Ctraderがインストール済み  
✅ MT5 installed / MT5がインストール済み

## Step 1: Start Bridge Server / ブリッジサーバーを起動

```bash
# Clone the repository / リポジトリをクローン
git clone https://github.com/NekoyaJolly/Cbot_to_MQL5_Translation.git
cd Cbot_to_MQL5_Translation

# Start Bridge Server / ブリッジサーバーを起動
cd Bridge
dotnet restore
dotnet run
```

You should see: / 以下のように表示されます：
```
Now listening on: http://0.0.0.0:5000
Application started. Press Ctrl+C to shut down.
```

**Keep this terminal open! / このターミナルは開いたままにしてください！**

## Step 2: Verify Bridge Server / ブリッジサーバーの動作確認

Open a new terminal and test: / 新しいターミナルを開いてテスト：

```bash
# Health check / ヘルスチェック
curl http://localhost:5000/api/health

# Expected output / 期待される出力:
# {"status":"Healthy","timestamp":"..."}
```

✅ If you see this output, Bridge Server is working! / この出力が表示されればOK！

## Step 3: Setup Ctrader cBot / Ctrader cBotのセットアップ

1. Open Ctrader / Ctraderを開く
2. Click **"Automate"** tab / 「Automate」タブをクリック
3. Click **"New"** → **"cBot"** / 「New」→「cBot」をクリック
4. Name it `TradeSyncBot`
5. Copy the entire content of `CtraderBot/TradeSyncBot.cs` / `CtraderBot/TradeSyncBot.cs`の内容を全てコピー
6. Paste into the code editor / コードエディタに貼り付け
7. Click **"Build"** / 「Build」をクリック
8. ✅ Should build successfully / ビルドが成功するはず

### Add to Chart / チャートに追加

1. Drag `TradeSyncBot` onto any chart / 任意のチャートに`TradeSyncBot`をドラッグ
2. Verify parameters: / パラメータを確認：
   - Bridge Server URL: `http://localhost:5000`
   - Enable Sync: `true`
3. Click **"Start"** / 「Start」をクリック
4. ✅ Check the log for: `TradeSyncBot started` / ログに「TradeSyncBot started」が表示されることを確認

## Step 4: Setup MT5 EA / MT5 EAのセットアップ

### Install Files / ファイルをインストール

1. In MT5, go to **File → Open Data Folder** / MT5で「ファイル」→「データフォルダを開く」
2. Navigate to `MQL5/Experts/` / `MQL5/Experts/`に移動
3. Copy `MT5EA/TradeSyncReceiver.mq5` to this folder / `MT5EA/TradeSyncReceiver.mq5`をコピー
4. Navigate to `MQL5/Include/` / `MQL5/Include/`に移動
5. Copy `MT5EA/JAson.mqh` to this folder / `MT5EA/JAson.mqh`をコピー
6. In MT5, press **F4** to open MetaEditor / MT5で**F4**を押してMetaEditorを開く
7. Open `TradeSyncReceiver.mq5` / `TradeSyncReceiver.mq5`を開く
8. Click **"Compile"** / 「コンパイル」をクリック
9. ✅ Should compile successfully / コンパイルが成功するはず

### Enable WebRequest / WebRequestを有効化

**IMPORTANT! / 重要！**

1. In MT5: **Tools → Options → Expert Advisors** / MT5で：「ツール」→「オプション」→「エキスパートアドバイザー」
2. Check ✅ **"Allow WebRequest for listed URLs"** / 「指定したURLリストでのWebRequestを許可する」をチェック
3. Add URL: `http://localhost:5000` / URLを追加：`http://localhost:5000`
4. Click **"OK"**
5. **Restart MT5** / **MT5を再起動**

### Add EA to Chart / チャートにEAを追加

1. Drag `TradeSyncReceiver` onto any chart / 任意のチャートに`TradeSyncReceiver`をドラッグ
2. Verify parameters: / パラメータを確認：
   - Bridge URL: `http://localhost:5000`
   - Poll Interval: `1000`
   - Enable Sync: `true`
3. Click **"OK"**
4. Click the **"Algo Trading"** button (should turn green) / 「アルゴリズム取引」ボタンをクリック（緑色になるはず）
5. ✅ Check the Expert Advisors tab for: `TradeSyncReceiver EA started` / エキスパートアドバイザータブで「TradeSyncReceiver EA started」を確認

## Step 5: Test the System / システムをテスト

### Manual Test / 手動テスト

In Ctrader: / Ctraderで：
1. Open a demo trade (BUY or SELL) / デモトレードを開く（買いまたは売り）
2. Wait 1-2 seconds / 1-2秒待つ
3. Check MT5 - the same trade should appear! / MT5を確認 - 同じトレードが表示されるはず！

### Automated Test / 自動テスト

If you can't or don't want to place a real trade, test with curl:

```bash
# Send a test order / テスト注文を送信
curl -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "EventType": "POSITION_OPENED",
    "Symbol": "EURUSD",
    "Direction": "Buy",
    "Volume": 0.01,
    "EntryPrice": 1.0950,
    "StopLoss": 1.0900,
    "TakeProfit": 1.1000
  }'

# Expected output / 期待される出力:
# {"orderId":"1","status":"Queued"}
```

Check MT5: / MT5を確認：
- Within 1-2 seconds, a BUY order for EURUSD should be placed / 1-2秒以内にEURUSDの買い注文が発注されるはず
- Check the Expert Advisors log for: `Position opened: EURUSD Buy` / エキスパートアドバイザーログで「Position opened: EURUSD Buy」を確認

## Troubleshooting / トラブルシューティング

### Bridge Server not starting / ブリッジサーバーが起動しない

```bash
# Check .NET version / .NETバージョンを確認
dotnet --version
# Should be 8.0.0 or higher / 8.0.0以上である必要があります

# If not installed, download from:
# https://dotnet.microsoft.com/download
```

### Ctrader cBot not connecting / Ctrader cBotが接続しない

- Check Bridge Server is running / ブリッジサーバーが起動しているか確認
- Check firewall is not blocking port 5000 / ファイアウォールがポート5000をブロックしていないか確認
- Check the URL is exactly `http://localhost:5000` / URLが正確に`http://localhost:5000`であるか確認

### MT5 EA not working / MT5 EAが動作しない

Common issues: / よくある問題：

1. **Algo Trading not enabled** / アルゴリズム取引が有効化されていない
   - Click the Algo Trading button in MT5 toolbar / MT5ツールバーのアルゴリズム取引ボタンをクリック

2. **WebRequest not allowed** / WebRequestが許可されていない
   - Go to Tools → Options → Expert Advisors / ツール → オプション → エキスパートアドバイザー
   - Add `http://localhost:5000` to allowed URLs / `http://localhost:5000`を許可URLに追加
   - **Restart MT5** / **MT5を再起動**

3. **Error 4060: "Function is not allowed"** / エラー4060：「関数は許可されていません」
   - This means WebRequest is not allowed / WebRequestが許可されていないことを意味します
   - Follow step 2 above / 上記のステップ2に従ってください

4. **Symbol not found** / シンボルが見つからない
   - Symbol names might differ between Ctrader and MT5 / CtraderとMT5でシンボル名が異なる場合があります
   - Customize `NormalizeSymbol()` function in the EA / EAの`NormalizeSymbol()`関数をカスタマイズ

## Monitoring / 監視

### Check Bridge Server Status / ブリッジサーバーのステータス確認

```bash
# Get statistics / 統計情報を取得
curl http://localhost:5000/api/statistics

# Output / 出力:
# {
#   "TotalOrders": 10,
#   "PendingOrders": 2,
#   "ProcessedOrders": 8,
#   "OrdersLast5Min": 5
# }
```

### Check Logs / ログを確認

- **Bridge Server**: Terminal output / ターミナル出力
- **Ctrader cBot**: Automate → Log / Automate → Log
- **MT5 EA**: Experts tab / エキスパートタブ

## Next Steps / 次のステップ

Once everything is working: / すべてが動作したら：

1. ✅ Test different order types / さまざまな注文タイプをテスト
2. ✅ Test modifying orders (SL/TP) / 注文の変更（SL/TP）をテスト
3. ✅ Test closing positions / ポジションのクローズをテスト
4. 📖 Read full documentation: `docs/README_JA.md` or `docs/README_EN.md`
5. ⚙️ Customize configuration: `docs/CONFIGURATION.md`

## Success Indicators / 成功の指標

You've successfully set up the system if: / 以下ができればセットアップ成功：

- ✅ Bridge Server is running on port 5000 / ブリッジサーバーがポート5000で起動
- ✅ Ctrader cBot logs "TradeSyncBot started" / Ctrader cBotが「TradeSyncBot started」をログ出力
- ✅ MT5 EA logs "TradeSyncReceiver EA started" / MT5 EAが「TradeSyncReceiver EA started」をログ出力
- ✅ Trades in Ctrader appear in MT5 within 1-2 seconds / Ctraderの取引が1-2秒以内にMT5に表示

## Support / サポート

If you encounter issues not covered here: / ここにない問題が発生した場合：

- Check full documentation / 完全なドキュメントを確認: `docs/README_JA.md` or `docs/README_EN.md`
- Check troubleshooting guide / トラブルシューティングガイドを確認
- Report on GitHub Issues / GitHub Issuesで報告

---

**Congratulations! You're now synchronizing trades from Ctrader to MT5! 🎉**

**おめでとうございます！CtraderからMT5へのトレード同期が完了しました！🎉**
