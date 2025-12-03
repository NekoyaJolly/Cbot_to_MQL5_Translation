# MQL5 MCP サーバー

MT5/MQL5 開発に特化した MCP (Model Context Protocol) サーバーです。

## 概要

このサーバーは、MQL5 言語での EA（Expert Advisor）およびインジケーター開発をサポートするためのツールを提供します。VS Code や他の MCP 対応クライアントから使用できます。

## 機能

| ツール名 | 説明 |
|---------|------|
| `compile_mql5` | MetaEditor CLI でコンパイル実行 |
| `validate_ea_structure` | EA の OnInit/OnTick 等の必須関数チェック |
| `validate_indicator_structure` | インジケーターの必須構造チェック |
| `get_mql5_docs` | MQL5 公式リファレンスから関数説明取得 |
| `check_trading_logic` | トレードロジックの妥当性検証 |
| `analyze_indicator_buffers` | インジケーターバッファ設定の検証 |

## セットアップ

### 前提条件

- Node.js 18.0.0 以上
- MetaTrader 5（コンパイル機能を使用する場合）

### インストール

```bash
cd mcp-servers
npm install
```

### VS Code での設定

`.vscode/mcp.json` に以下を追加します（リポジトリルートに既に設定済み）：

```json
{
  "servers": {
    "mql5": {
      "type": "stdio",
      "command": "node",
      "args": ["mcp-servers/mql5-server.js"]
    }
  }
}
```

## 使用方法

### compile_mql5

MQL5 ファイルをコンパイルします。

```json
{
  "name": "compile_mql5",
  "arguments": {
    "filePath": "C:/Users/xxx/AppData/Roaming/MetaQuotes/Terminal/.../MQL5/Experts/MyEA.mq5"
  }
}
```

**注意**: コンパイルには MetaEditor CLI（metaeditor64.exe）が必要です。MetaTrader 5 をデフォルトパスにインストールしていない場合は、`metaeditorPath` パラメータでパスを指定してください。

### validate_ea_structure

EA の構造を検証します。

```json
{
  "name": "validate_ea_structure",
  "arguments": {
    "filePath": "path/to/MyEA.mq5"
  }
}
```

または、コードを直接検証：

```json
{
  "name": "validate_ea_structure",
  "arguments": {
    "content": "int OnInit() { return INIT_SUCCEEDED; }\nvoid OnTick() { }"
  }
}
```

**チェック項目**:
- `OnInit()` 関数の存在（必須）
- `OnTick()` 関数の存在（必須）
- `OnDeinit()` 関数の存在（推奨）
- CTrade/CPositionInfo クラスの使用（推奨）
- エラーハンドリングの実装（推奨）

### validate_indicator_structure

インジケーターの構造を検証します。

```json
{
  "name": "validate_indicator_structure",
  "arguments": {
    "filePath": "path/to/MyIndicator.mq5"
  }
}
```

**チェック項目**:
- `OnInit()` 関数の存在（必須）
- `OnCalculate()` 関数の存在（必須）
- `#property indicator_chart_window` または `indicator_separate_window`（必須）
- `#property indicator_buffers`（必須）
- `SetIndexBuffer()` の呼び出し数
- `ArraySetAsSeries()` の使用（推奨）

### get_mql5_docs

MQL5 関数のドキュメントを取得します。

**特定の関数のドキュメントを取得**:
```json
{
  "name": "get_mql5_docs",
  "arguments": {
    "functionName": "OrderSend"
  }
}
```

**キーワードで検索**:
```json
{
  "name": "get_mql5_docs",
  "arguments": {
    "keyword": "position"
  }
}
```

**全関数一覧**:
```json
{
  "name": "get_mql5_docs",
  "arguments": {
    "listAll": true
  }
}
```

### check_trading_logic

トレードロジックのベストプラクティスを検証します。

```json
{
  "name": "check_trading_logic",
  "arguments": {
    "filePath": "path/to/MyEA.mq5"
  }
}
```

**チェック項目**:
- ストップロス設定の有無
- エラーハンドリング（GetLastError）の実装
- 二重発注防止のチェック
- スリッページ許容値の設定
- マジックナンバーの使用
- ログ出力の有無

### analyze_indicator_buffers

インジケーターのバッファ設定を詳細に分析します。

```json
{
  "name": "analyze_indicator_buffers",
  "arguments": {
    "filePath": "path/to/MyIndicator.mq5"
  }
}
```

**分析内容**:
- 宣言されたバッファ数
- プロット数
- SetIndexBuffer の呼び出し回数
- バッファプロパティ設定
- INDICATOR_CALCULATIONS タイプの使用

## 開発

### ディレクトリ構造

```
mcp-servers/
├── mql5-server.js      # MCP サーバー本体
├── package.json        # 依存関係
├── README.md           # このファイル
└── tools/
    ├── compile.js      # コンパイルツール
    ├── validate.js     # 構造検証ツール
    └── docs.js         # ドキュメント取得ツール
```

### サーバーの起動（デバッグ用）

```bash
npm start
```

### ドキュメントの拡張

`tools/docs.js` の `MQL5_DOCS` オブジェクトに新しい関数を追加することで、ドキュメントを拡張できます。

## トラブルシューティング

### コンパイルエラー「MetaEditor の起動に失敗しました」

- MetaTrader 5 がインストールされていることを確認してください
- デフォルトパス以外にインストールしている場合は、`metaeditorPath` を指定してください

### Node.js のバージョンエラー

Node.js 18.0.0 以上が必要です。

```bash
node --version
```

## ライセンス

MIT License
