# Cbot to MQL5 Translation / Ctraderから MT5への取引同期システム

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

リアルタイムでCtrader（cBot）の取引をMT5に同期するシステムです。

A real-time trade synchronization system from Ctrader (cBot) to MetaTrader 5.

## 概要 / Overview

このプロジェクトは、Ctraderで行ったFX取引をMT5にリアルタイムで反映させるためのシステムです。3つのコンポーネント（Ctrader cBot、Bridgeサーバー、MT5 EA）で構成されています。

This project provides a system to synchronize FX trades from Ctrader to MT5 in real-time. It consists of three components: Ctrader cBot, Bridge server, and MT5 EA.

## アーキテクチャ / Architecture

```
Ctrader (cBot/C#) → HTTP → Bridge Server (C#/.NET) → HTTP → MT5 (MQL5 EA)
```

- **Ctrader cBot**: 取引イベントをフックしてBridgeに送信 / Hooks trade events and sends to Bridge
- **Bridge Server**: キュー管理とREST API提供 / Queue management and REST API
- **MT5 EA**: Bridgeをポーリングして注文実行 / Polls Bridge and executes orders

## 主な機能 / Key Features

✅ ポジションオープン・クローズ / Position Open/Close  
✅ ポジション変更（SL/TP） / Position Modification (SL/TP)  
✅ 指値・逆指値注文 / Limit and Stop Orders  
✅ 低遅延（1秒ポーリング） / Low Latency (1s polling)  
✅ スレッドセーフなキュー管理 / Thread-safe Queue Management  

## クイックスタート / Quick Start

### 1. Bridge Serverを起動 / Start Bridge Server

```bash
cd Bridge
dotnet restore
dotnet run
```

### 2. Ctrader cBotをインストール / Install Ctrader cBot

`CtraderBot/TradeSyncBot.cs` をCtraderのAutomateに追加して起動

Add `CtraderBot/TradeSyncBot.cs` to Ctrader Automate and start

### 3. MT5 EAをインストール / Install MT5 EA

1. `MT5EA/TradeSyncReceiver.mq5` を `MQL5/Experts/` にコピー
2. `MT5EA/JAson.mqh` を `MQL5/Include/` にコピー
3. MT5で `http://localhost:5000` をWebRequest許可リストに追加
4. チャートにEAを適用して起動

1. Copy `MT5EA/TradeSyncReceiver.mq5` to `MQL5/Experts/`
2. Copy `MT5EA/JAson.mqh` to `MQL5/Include/`
3. Add `http://localhost:5000` to WebRequest allowed URLs in MT5
4. Apply EA to chart and start

## ドキュメント / Documentation

- [日本語ドキュメント](docs/README_JA.md)
- [English Documentation](docs/README_EN.md)

詳細なセットアップ手順、トラブルシューティング、API リファレンスは上記ドキュメントを参照してください。

For detailed setup instructions, troubleshooting, and API reference, please refer to the documentation above.

## 必要な環境 / Requirements

- .NET 8.0 SDK以上 / .NET 8.0 SDK or higher
- Ctrader with cAlgo
- MetaTrader 5

## ライセンス / License

MIT License

## サポート / Support

問題が発生した場合は、GitHubのIssuesで報告してください。

If you encounter issues, please report them on GitHub Issues.