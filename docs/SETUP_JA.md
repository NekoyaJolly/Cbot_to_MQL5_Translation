# ブリッジサービス セットアップガイド

このガイドは、ブリッジサービスのセットアップと設定について説明します。

## 必須要件

### A. 認証 & TLS（必須）

#### API キー認証

すべてのAPIリクエストには `X-API-KEY` ヘッダーが必要です（`/api/health` と `/metrics` を除く）。

**appsettings.json での設定:**
```json
{
  "Bridge": {
    "ApiKey": "32文字以上の安全なAPIキー"
  }
}
```

**使用方法:**
```bash
curl -H "X-API-KEY: your-api-key" http://localhost:5000/api/status
```

#### HTTPS/TLS 設定

本番環境では必ず HTTPS を使用してください。

**オプション1: nginx + Let's Encrypt（推奨）**

1. nginx と certbot のインストール:
```bash
sudo apt-get update
sudo apt-get install nginx certbot python3-certbot-nginx
```

2. nginx の設定 (`/etc/nginx/sites-available/bridge`):
```nginx
server {
    listen 80;
    server_name your-domain.com;

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

3. サイトの有効化:
```bash
sudo ln -s /etc/nginx/sites-available/bridge /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl restart nginx
```

4. SSL証明書の取得:
```bash
sudo certbot --nginx -d your-domain.com
```

### B. 永続キュー（実装済み）

SQLite を使用した永続キューが実装されています:
- FIFO 順序保証
- リトライ機能（指数バックオフ）
- 遅延再試行

**設定:**
```json
{
  "Bridge": {
    "DatabasePath": "bridge.db",
    "Retry": {
      "MaxRetryCount": 3,
      "InitialDelaySeconds": 10,
      "MaxDelaySeconds": 300
    }
  }
}
```

### C. 冪等性（実装済み）

`SourceId + EventType` を使用して注文の重複を防止します。重複した注文は既存の注文IDで200 OKを返します。

### D. マッピング DB（実装済み）

`TicketMap` テーブルでソースチケットとスレーブチケットのマッピングを管理します。

**APIエンドポイント:**

マッピングの追加:
```bash
curl -X POST http://localhost:5000/api/ticket-map \
  -H "Content-Type: application/json" \
  -H "X-API-KEY: your-key" \
  -d '{
    "SourceTicket": "12345",
    "SlaveTicket": "67890",
    "Symbol": "EURUSD",
    "Lots": "0.01"
  }'
```

マッピングの取得:
```bash
curl -H "X-API-KEY: your-key" \
  http://localhost:5000/api/ticket-map/12345
```

### E. 管理/モニタリング API（実装済み）

#### システムステータス
```bash
GET /api/status
```

レスポンス例:
```json
{
  "Status": "Running",
  "Timestamp": "2025-01-01T00:00:00Z",
  "Version": "1.0.0",
  "QueueStatistics": {
    "TotalOrders": 1000,
    "PendingOrders": 5,
    "ProcessedOrders": 995,
    "OrdersLast5Min": 12
  },
  "Uptime": "1.05:30:15"
}
```

#### キュー詳細
```bash
GET /api/queue?maxCount=100
```

#### 再試行
```bash
POST /api/retry/{orderId}
```

#### ヘルスチェック（認証不要）
```bash
GET /api/health
```

#### Prometheus メトリクス（認証不要）
```bash
GET /metrics
```

## 中優先度の機能

### F. 運用ログ & アラート（実装済み）

#### ログ設定

Serilog が設定されています:
- コンソール出力
- 日次ローテーションログファイル (`logs/bridge-.log`)
- 30日間保持
- 構造化ログ

#### アラート統合

Slack、Telegram、メールでアラートを送信できます。

**設定:**
```json
{
  "Bridge": {
    "Alerts": {
      "Enabled": true,
      "SlackWebhookUrl": "https://hooks.slack.com/services/...",
      "TelegramBotToken": "bot-token",
      "TelegramChatId": "chat-id",
      "EmailSmtpHost": "smtp.gmail.com",
      "EmailSmtpPort": 587,
      "EmailUsername": "alerts@example.com",
      "EmailPassword": "password",
      "EmailTo": "admin@example.com"
    }
  }
}
```

### G. スケーリング

#### 単一インスタンス（現在）
- SQLite 永続キュー
- ほとんどのユースケースに適しています
- シンプルなデプロイ

#### 複数インスタンス（将来）
水平スケーリングの要件:
1. SQLite を外部キュー（Redis/RabbitMQ）に置き換える
2. チケットマッピング用の共有データベース
3. セッションアフィニティ付きロードバランサー
4. キュー操作の分散ロック

### H. レート制限 & 認証（実装済み）

**設定:**
```json
{
  "Bridge": {
    "RateLimiting": {
      "Enabled": true,
      "MaxRequestsPerMinute": 60,
      "WhitelistedIPs": [
        "192.168.1.100",
        "10.0.0.50"
      ]
    }
  }
}
```

機能:
- IPベースのレート制限
- 429 Too Many Requests レスポンス
- 信頼できるクライアント用のIPホワイトリスト
- ヘルスとメトリクスエンドポイントは除外

### I. TLS & リバースプロキシ

前述の nginx + Let's Encrypt 設定を参照してください。

## クイックスタート

1. **設定ファイルの編集:**
```bash
cd Bridge
nano appsettings.json
```

以下を設定:
- `ApiKey`: 安全なAPIキー（32文字以上）
- `RateLimiting.WhitelistedIPs`: MT5サーバーのIPアドレス
- その他必要に応じて設定

2. **ビルド:**
```bash
dotnet build
```

3. **実行:**
```bash
dotnet run
```

または本番環境:
```bash
dotnet publish -c Release
cd bin/Release/net8.0/publish
dotnet Bridge.dll
```

4. **systemd サービスとして実行:**

`/etc/systemd/system/bridge.service` を作成:
```ini
[Unit]
Description=Trading Bridge Service
After=network.target

[Service]
Type=notify
WorkingDirectory=/opt/bridge
ExecStart=/usr/bin/dotnet /opt/bridge/Bridge.dll
Restart=always
RestartSec=10
User=bridge
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

有効化と起動:
```bash
sudo systemctl enable bridge
sudo systemctl start bridge
sudo systemctl status bridge
```

## 本番環境デプロイチェックリスト

- [ ] 安全なAPIキーを設定
- [ ] HTTPSを有効化（nginx + Let's Encrypt またはクラウドロードバランサー）
- [ ] レート制限を設定
- [ ] MT5サーバーIPをホワイトリストに追加
- [ ] ログローテーションとモニタリングを設定
- [ ] アラートを設定（Slack/Telegram/Email）
- [ ] フェイルオーバーとリトライメカニズムをテスト
- [ ] SQLiteデータベースのバックアップ手順を文書化
- [ ] ヘルスチェックモニタリングを設定
- [ ] セキュリティヘッダーを確認（CORS、HSTS など）

## モニタリングコマンド

```bash
# ログの確認
tail -f logs/bridge-*.log

# メトリクスの確認
curl http://localhost:5000/metrics

# ステータスの確認
curl -H "X-API-KEY: your-key" http://localhost:5000/api/status

# キューの確認
curl -H "X-API-KEY: your-key" http://localhost:5000/api/queue
```

## バックアップとリカバリー

### SQLiteデータベースのバックアップ
```bash
# サービス停止
sudo systemctl stop bridge

# データベースバックアップ
cp bridge.db bridge.db.backup

# サービス開始
sudo systemctl start bridge
```

### リカバリー
```bash
# サービス停止
sudo systemctl stop bridge

# データベース復元
cp bridge.db.backup bridge.db

# サービス開始
sudo systemctl start bridge
```

## トラブルシューティング

### サービスが起動しない
```bash
# ログを確認
sudo journalctl -u bridge -n 50

# 手動で起動してエラーを確認
cd /opt/bridge
dotnet Bridge.dll
```

### データベースエラー
```bash
# データベースの整合性チェック
sqlite3 bridge.db "PRAGMA integrity_check;"

# テーブル構造の確認
sqlite3 bridge.db ".schema"
```

### APIエラー
```bash
# ログレベルを Debug に変更
# appsettings.json:
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  }
}
```

## 追加リソース

- [API リファレンス](API_REFERENCE.md)
- [デプロイメントガイド](DEPLOYMENT.md)
- [メインREADME](../README.md)

## サポート

問題が発生した場合は、GitHub Issues で報告してください。
