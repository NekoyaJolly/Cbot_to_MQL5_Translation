/**
 * MQL5 コンパイラ連携ツール
 * MetaEditor CLI を使用して MQL5 ファイルをコンパイルする
 */

import { spawn } from "child_process";
import path from "path";

/**
 * MetaEditor CLI のデフォルトパス
 * 環境に応じて設定を変更可能
 */
const DEFAULT_METAEDITOR_PATH =
  "C:\\Program Files\\MetaTrader 5\\metaeditor64.exe";

/**
 * MQL5 ファイルをコンパイルする
 * @param {string} filePath - コンパイル対象の .mq5 ファイルパス
 * @param {object} options - オプション
 * @param {string} options.metaeditorPath - MetaEditor CLI のパス（省略時はデフォルト）
 * @param {string} options.logPath - コンパイルログの出力先（省略時は自動生成）
 * @returns {Promise<{success: boolean, output: string, errors: string[]}>}
 */
export async function compileMql5(filePath, options = {}) {
  const metaeditorPath = options.metaeditorPath || DEFAULT_METAEDITOR_PATH;
  const logPath = options.logPath || filePath.replace(/\.mq5$/i, ".log");

  return new Promise((resolve) => {
    // MetaEditor CLI の引数
    // /compile: コンパイル実行
    // /log: ログファイル出力
    const args = ["/compile:" + filePath, "/log:" + logPath];

    const process = spawn(metaeditorPath, args);

    let stdout = "";
    let stderr = "";

    process.stdout.on("data", (data) => {
      stdout += data.toString();
    });

    process.stderr.on("data", (data) => {
      stderr += data.toString();
    });

    process.on("close", (code) => {
      const errors = extractErrors(stdout + stderr);
      resolve({
        success: code === 0 && errors.length === 0,
        output: stdout,
        errors: errors,
        logPath: logPath,
      });
    });

    process.on("error", (err) => {
      resolve({
        success: false,
        output: "",
        errors: [
          `MetaEditor の起動に失敗しました: ${err.message}`,
          `パス: ${metaeditorPath}`,
          "MetaTrader 5 がインストールされていることを確認してください。",
        ],
        logPath: null,
      });
    });
  });
}

/**
 * コンパイルログからエラーを抽出する
 * @param {string} output - コンパイル出力
 * @returns {string[]} エラーメッセージの配列
 */
function extractErrors(output) {
  const errors = [];
  const lines = output.split("\n");

  for (const line of lines) {
    // エラーパターン: filename(line,column) : error XXX: message
    if (/:\s*error\s+\d+:/i.test(line) || /:\s*error:/i.test(line)) {
      errors.push(line.trim());
    }
    // 警告も含める場合
    // if (/:\s*warning\s+\d+:/i.test(line)) {
    //   errors.push(line.trim());
    // }
  }

  return errors;
}

/**
 * コンパイル結果をフォーマットして返す
 * @param {object} result - compileMql5 の結果
 * @returns {string} フォーマット済みの結果文字列
 */
export function formatCompileResult(result) {
  if (result.success) {
    return `✅ コンパイル成功\nログファイル: ${result.logPath}`;
  } else {
    let message = `❌ コンパイル失敗\n`;
    if (result.errors.length > 0) {
      message += `\nエラー:\n${result.errors.map((e) => "  - " + e).join("\n")}`;
    }
    if (result.logPath) {
      message += `\n\nログファイル: ${result.logPath}`;
    }
    return message;
  }
}

export default {
  compileMql5,
  formatCompileResult,
};
