/**
 * EA / インジケーター構造検証ツール
 * MQL5 ファイルの必須関数やプロパティの存在をチェックする
 */

import fs from "fs/promises";

/**
 * EA（Expert Advisor）の必須関数リスト
 */
const EA_REQUIRED_FUNCTIONS = [
  {
    name: "OnInit",
    signature: /int\s+OnInit\s*\(\s*\)/,
    description: "EA 初期化関数（必須）",
  },
  {
    name: "OnDeinit",
    signature: /void\s+OnDeinit\s*\(\s*const\s+int\s+reason\s*\)/,
    description: "EA 終了関数（推奨）",
    optional: true,
  },
  {
    name: "OnTick",
    signature: /void\s+OnTick\s*\(\s*\)/,
    description: "ティックイベントハンドラ（必須）",
  },
];

/**
 * インジケーターの必須関数リスト
 */
const INDICATOR_REQUIRED_FUNCTIONS = [
  {
    name: "OnInit",
    signature: /int\s+OnInit\s*\(\s*\)/,
    description: "インジケーター初期化関数（必須）",
  },
  {
    name: "OnCalculate",
    signature: /int\s+OnCalculate\s*\(/,
    description: "計算関数（必須）",
  },
];

/**
 * インジケーターの必須プロパティ
 */
const INDICATOR_REQUIRED_PROPERTIES = [
  {
    name: "#property indicator_chart_window / indicator_separate_window",
    pattern: /#property\s+indicator_(chart_window|separate_window)/,
    description: "表示位置の指定（必須）",
  },
  {
    name: "#property indicator_buffers",
    pattern: /#property\s+indicator_buffers\s+\d+/,
    description: "バッファ数の宣言（必須）",
  },
];

/**
 * EA の構造を検証する
 * @param {string} filePath - 検証対象の .mq5 ファイルパス
 * @returns {Promise<{valid: boolean, issues: object[], suggestions: string[]}>}
 */
export async function validateEAStructure(filePath) {
  try {
    const content = await fs.readFile(filePath, "utf-8");
    return validateEAContent(content, filePath);
  } catch (err) {
    return {
      valid: false,
      issues: [
        {
          type: "error",
          message: `ファイルを読み込めません: ${err.message}`,
        },
      ],
      suggestions: [],
    };
  }
}

/**
 * EA のコンテンツを検証する（テスト用にも使用可能）
 * @param {string} content - MQL5 コード
 * @param {string} filePath - ファイルパス（エラーメッセージ用）
 * @returns {{valid: boolean, issues: object[], suggestions: string[]}}
 */
export function validateEAContent(content, filePath = "unknown") {
  const issues = [];
  const suggestions = [];

  // 必須関数のチェック
  for (const func of EA_REQUIRED_FUNCTIONS) {
    const found = func.signature.test(content);
    if (!found) {
      if (func.optional) {
        suggestions.push(
          `${func.name}: ${func.description} - 実装を推奨します`
        );
      } else {
        issues.push({
          type: "error",
          message: `必須関数 ${func.name} が見つかりません: ${func.description}`,
        });
      }
    }
  }

  // 推奨パターンのチェック
  if (!/CTrade\s+\w+;/.test(content)) {
    suggestions.push("CTrade クラスの使用を推奨します（標準ライブラリ）");
  }

  if (!/CPositionInfo\s+\w+;/.test(content)) {
    suggestions.push(
      "CPositionInfo クラスの使用を推奨します（ポジション管理）"
    );
  }

  // エラーハンドリングのチェック
  if (!/GetLastError\s*\(\s*\)/.test(content)) {
    suggestions.push(
      "GetLastError() によるエラーハンドリングを追加することを推奨します"
    );
  }

  return {
    valid: issues.filter((i) => i.type === "error").length === 0,
    issues,
    suggestions,
  };
}

/**
 * インジケーターの構造を検証する
 * @param {string} filePath - 検証対象の .mq5 ファイルパス
 * @returns {Promise<{valid: boolean, issues: object[], suggestions: string[]}>}
 */
export async function validateIndicatorStructure(filePath) {
  try {
    const content = await fs.readFile(filePath, "utf-8");
    return validateIndicatorContent(content, filePath);
  } catch (err) {
    return {
      valid: false,
      issues: [
        {
          type: "error",
          message: `ファイルを読み込めません: ${err.message}`,
        },
      ],
      suggestions: [],
    };
  }
}

/**
 * インジケーターのコンテンツを検証する（テスト用にも使用可能）
 * @param {string} content - MQL5 コード
 * @param {string} filePath - ファイルパス（エラーメッセージ用）
 * @returns {{valid: boolean, issues: object[], suggestions: string[]}}
 */
export function validateIndicatorContent(content, filePath = "unknown") {
  const issues = [];
  const suggestions = [];

  // 必須関数のチェック
  for (const func of INDICATOR_REQUIRED_FUNCTIONS) {
    const found = func.signature.test(content);
    if (!found) {
      issues.push({
        type: "error",
        message: `必須関数 ${func.name} が見つかりません: ${func.description}`,
      });
    }
  }

  // 必須プロパティのチェック
  for (const prop of INDICATOR_REQUIRED_PROPERTIES) {
    const found = prop.pattern.test(content);
    if (!found) {
      issues.push({
        type: "error",
        message: `${prop.name} が見つかりません: ${prop.description}`,
      });
    }
  }

  // バッファ設定のチェック
  const bufferMatch = content.match(/#property\s+indicator_buffers\s+(\d+)/);
  if (bufferMatch) {
    const bufferCount = parseInt(bufferMatch[1], 10);
    const setIndexBufferCalls = (content.match(/SetIndexBuffer\s*\(/g) || [])
      .length;

    if (setIndexBufferCalls < bufferCount) {
      issues.push({
        type: "warning",
        message: `宣言されたバッファ数 (${bufferCount}) に対し、SetIndexBuffer の呼び出しが不足しています (${setIndexBufferCalls} 回)`,
      });
    }
  }

  // ArraySetAsSeries の使用チェック
  if (!/ArraySetAsSeries\s*\(/.test(content)) {
    suggestions.push(
      "バッファ配列に ArraySetAsSeries() を設定することを推奨します"
    );
  }

  return {
    valid: issues.filter((i) => i.type === "error").length === 0,
    issues,
    suggestions,
  };
}

/**
 * 検証結果をフォーマットして返す
 * @param {object} result - 検証結果
 * @returns {string} フォーマット済みの結果文字列
 */
export function formatValidationResult(result) {
  let message = result.valid ? "✅ 検証成功\n" : "❌ 検証失敗\n";

  if (result.issues.length > 0) {
    message += "\n問題点:\n";
    for (const issue of result.issues) {
      const icon = issue.type === "error" ? "❌" : "⚠️";
      message += `  ${icon} ${issue.message}\n`;
    }
  }

  if (result.suggestions.length > 0) {
    message += "\n改善提案:\n";
    for (const suggestion of result.suggestions) {
      message += `  💡 ${suggestion}\n`;
    }
  }

  return message;
}

export default {
  validateEAStructure,
  validateEAContent,
  validateIndicatorStructure,
  validateIndicatorContent,
  formatValidationResult,
};
