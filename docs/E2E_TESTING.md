# E2E Testing Guide

このドキュメントでは、Cbot to MQL5 Translation システムのEnd-to-End (E2E) テストについて説明します。

This document describes the End-to-End (E2E) testing approach for the Cbot to MQL5 Translation system.

## 概要 / Overview

E2Eテストは、Ctrader cBot → Bridge Server → MT5 EA の完全なフローをシミュレートして検証します。

The E2E tests simulate and verify the complete flow: Ctrader cBot → Bridge Server → MT5 EA.

## テストアーキテクチャ / Test Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      E2E Test Suite                         │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌──────────────┐     ┌──────────────┐     ┌────────────┐ │
│  │   Simulated  │────>│    Bridge    │<────│ Simulated  │ │
│  │  Cbot Client │ POST│    Server    │ GET │  MT5 EA    │ │
│  │  (Test Code) │     │  (Real API)  │     │(Test Code) │ │
│  └──────────────┘     └──────────────┘     └────────────┘ │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### コンポーネント / Components

1. **Simulated Cbot Client (テストコード)**
   - `HttpClient` を使用して Bridge API に POST リクエストを送信
   - 実際の cBot と同じ JSON ペイロードを送信

2. **Bridge Server (実際の API)**
   - `WebApplicationFactory` を使用してテスト内で実行
   - 実際の本番コードが動作（モックなし）
   - インメモリ SQLite データベースを使用

3. **Simulated MT5 EA (テストコード)**
   - `HttpClient` を使用して Bridge API から GET リクエストでポーリング
   - 注文を処理して processed としてマーク

## テストケース / Test Cases

### 1. E2E_PositionOpened_CompleteFlow_ShouldSucceed

ポジションオープンの完全なフローをテスト：
1. Cbot が POSITION_OPENED イベントを Bridge に送信
2. MT5 EA が Bridge から pending orders をポーリング
3. MT5 EA が注文を処理
4. MT5 EA が注文を processed としてマーク
5. 注文が pending queue から削除されたことを確認

Tests the complete position open flow:
1. Cbot sends POSITION_OPENED event to Bridge
2. MT5 EA polls Bridge for pending orders
3. MT5 EA processes the order
4. MT5 EA marks the order as processed
5. Verify the order is removed from pending queue

### 2. E2E_PositionModified_CompleteFlow_ShouldSucceed

ポジション変更（SL/TP 修正）のフローをテスト。

Tests position modification (SL/TP change) flow.

### 3. E2E_PositionClosed_CompleteFlow_ShouldSucceed

ポジションクローズのフローをテスト。

Tests position close flow.

### 4. E2E_MultipleOrders_ProcessedInOrder_ShouldSucceed

複数の注文が FIFO 順序で処理されることをテスト。

Tests that multiple orders are processed in FIFO order.

### 5. E2E_TicketMapping_CompleteFlow_ShouldSucceed

チケットマッピング（Cbot ticket → MT5 ticket）の機能をテスト。

Tests ticket mapping (Cbot ticket → MT5 ticket) functionality.

### 6. E2E_ErrorRecovery_OrderRetry_ShouldSucceed

エラーリカバリーとリトライメカニズムをテスト：
- 処理失敗時に注文が再試行可能であることを確認

Tests error recovery and retry mechanism:
- Verify orders can be retried after processing failure

### 7. E2E_HealthCheck_ShouldAlwaysBeAccessible

ヘルスチェックエンドポイントが常にアクセス可能であることをテスト。

Tests that health check endpoint is always accessible.

## GitHub Actions での実行 / Running in GitHub Actions

### ワークフロー / Workflow

`.github/workflows/e2e-tests.yml` ファイルで定義されています。

Defined in `.github/workflows/e2e-tests.yml`.

### トリガー / Triggers

- `main` または `develop` ブランチへの push
- `main` または `develop` ブランチへの pull request
- 手動実行（workflow_dispatch）

- Push to `main` or `develop` branches
- Pull requests to `main` or `develop` branches
- Manual execution (workflow_dispatch)

### 実行内容 / Execution Steps

1. ✅ コードのチェックアウト / Checkout code
2. ✅ .NET 9.0 のセットアップ / Setup .NET 9.0
3. ✅ 依存関係の復元 / Restore dependencies
4. ✅ Bridge プロジェクトのビルド / Build Bridge project
5. ✅ テストプロジェクトのビルド / Build test projects
6. ✅ ユニットテストの実行 / Run unit tests
7. ✅ E2E テストの実行 / Run E2E tests
8. ✅ テスト結果のアップロード / Upload test results
9. ✅ テストサマリーの表示 / Display test summary

## ローカルでの実行 / Running Locally

### 必要な環境 / Prerequisites

- .NET 9.0 SDK
- Git

### 実行方法 / How to Run

```bash
# リポジトリのクローン / Clone repository
git clone https://github.com/NekoyaJolly/Cbot_to_MQL5_Translation.git
cd Cbot_to_MQL5_Translation

# 依存関係の復元 / Restore dependencies
dotnet restore

# E2E テストの実行 / Run E2E tests
cd E2ETests
dotnet test --verbosity normal

# または特定のテストのみ実行 / Or run specific test
dotnet test --filter "FullyQualifiedName~E2E_PositionOpened"
```

### テスト結果の確認 / Viewing Test Results

```bash
# 詳細なログ付きで実行 / Run with detailed logs
dotnet test --logger "console;verbosity=detailed"

# TRX 形式でレポート出力 / Output report in TRX format
dotnet test --logger "trx;LogFileName=test-results.trx"
```

## テストのメリット / Benefits of E2E Tests

### 1. 本番環境に近い検証 / Production-like Validation

実際の Bridge API コードが動作するため、本番環境に近い状態で検証できます。

Tests run against real Bridge API code, providing production-like validation.

### 2. リグレッション防止 / Regression Prevention

コード変更が既存の機能を壊していないことを自動的に検証します。

Automatically verify that code changes don't break existing functionality.

### 3. CI/CD パイプライン統合 / CI/CD Pipeline Integration

GitHub Actions で自動実行されるため、Pull Request ごとに品質チェックが行われます。

Automatically runs in GitHub Actions, providing quality checks for every pull request.

### 4. ドキュメントとしての役割 / Documentation Role

テストコードがシステムの使用方法を示すドキュメントとして機能します。

Test code serves as documentation showing how to use the system.

### 5. 早期のバグ発見 / Early Bug Detection

統合の問題を開発段階で発見できます。

Detect integration issues during development.

## 制限事項 / Limitations

### 1. 実際の MT5 での動作検証は不可 / Cannot Test Real MT5

MQL5 コードは実際の MT5 プラットフォームでのみ動作するため、E2E テストではシミュレートのみ。

MQL5 code can only run on actual MT5 platform, so E2E tests can only simulate it.

### 解決策 / Solution

- ✅ MT5 EA の HTTP 通信部分は E2E テストでカバー
- ✅ 実際の MT5 での動作は手動テストまたはバックテストで確認
- ✅ MQL5 コードレビューで品質を担保

### 2. 実際の Ctrader での動作検証は不可 / Cannot Test Real Ctrader

Ctrader cBot も実際のプラットフォームでのみ動作。

Ctrader cBot can only run on actual Ctrader platform.

### 解決策 / Solution

- ✅ cBot の HTTP 通信部分は E2E テストでカバー
- ✅ 実際の Ctrader での動作は手動テストで確認
- ✅ C# コードレビューで品質を担保

### 3. ネットワーク遅延のシミュレートは不可 / Cannot Simulate Network Delays

テストは localhost で実行されるため、実際のネットワーク遅延をシミュレートできません。

Tests run on localhost, so real network delays cannot be simulated.

### 解決策 / Solution

- ✅ パフォーマンステストは別途実施
- ✅ 本番環境でのモニタリングで実際の遅延を確認

## トラブルシューティング / Troubleshooting

### テストが失敗する / Tests Fail

```bash
# 詳細ログで確認 / Check detailed logs
dotnet test --logger "console;verbosity=detailed"

# 特定のテストのみ実行 / Run specific test
dotnet test --filter "FullyQualifiedName~E2E_HealthCheck"
```

### データベースエラー / Database Errors

E2E テストはインメモリ SQLite を使用するため、テスト間でデータベースがリセットされます。

E2E tests use in-memory SQLite, which is reset between tests.

### ポート競合 / Port Conflicts

`WebApplicationFactory` は自動的に空いているポートを使用するため、ポート競合は発生しません。

`WebApplicationFactory` automatically uses available ports, so port conflicts don't occur.

## 今後の拡張 / Future Enhancements

### 1. パフォーマンステスト / Performance Tests

大量の注文を同時に処理するロードテストを追加。

Add load tests for processing large numbers of orders simultaneously.

### 2. セキュリティテスト / Security Tests

API キー認証、レート制限などのセキュリティ機能をテスト。

Test security features like API key authentication and rate limiting.

### 3. カオステスト / Chaos Tests

ネットワーク障害、データベース障害などをシミュレート。

Simulate network failures, database failures, etc.

### 4. 統合テストレポート / Integration Test Reports

テスト結果を視覚的に表示するダッシュボードを追加。

Add dashboard for visualizing test results.

## まとめ / Summary

E2E テストにより、Cbot to MQL5 Translation システムの主要な機能が正しく動作することを自動的に検証できます。GitHub Actions との統合により、コード変更のたびに品質チェックが行われ、本番環境へのデプロイ前にバグを発見できます。

E2E tests automatically verify that the core functionality of the Cbot to MQL5 Translation system works correctly. Integration with GitHub Actions provides quality checks for every code change, catching bugs before production deployment.

## 参考資料 / References

- [xUnit Documentation](https://xunit.net/)
- [ASP.NET Core Testing](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests)
- [GitHub Actions for .NET](https://docs.github.com/en/actions/automating-builds-and-tests/building-and-testing-net)
