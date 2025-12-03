#!/usr/bin/env node

/**
 * MQL5 特化 MCP (Model Context Protocol) サーバー
 *
 * MT5/MQL5 開発をサポートするツールを提供します：
 * - compile_mql5: MetaEditor CLI でコンパイル実行
 * - validate_ea_structure: EA の必須関数チェック
 * - validate_indicator_structure: インジケーターの必須構造チェック
 * - get_mql5_docs: MQL5 公式リファレンスから関数説明取得
 * - check_trading_logic: トレードロジックの妥当性検証
 * - analyze_indicator_buffers: インジケーターバッファ設定の検証
 */

import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";
import fs from "fs/promises";

import { compileMql5, formatCompileResult } from "./tools/compile.js";
import {
  validateEAStructure,
  validateEAContent,
  validateIndicatorStructure,
  validateIndicatorContent,
  formatValidationResult,
} from "./tools/validate.js";
import {
  getMql5Docs,
  listFunctions,
  searchFunctions,
  formatDoc,
} from "./tools/docs.js";

/**
 * MCP サーバーのインスタンスを作成
 */
const server = new Server(
  {
    name: "mql5-mcp-server",
    version: "1.0.0",
  },
  {
    capabilities: {
      tools: {},
    },
  }
);

/**
 * 利用可能なツールの一覧を返す
 */
server.setRequestHandler(ListToolsRequestSchema, async () => {
  return {
    tools: [
      {
        name: "compile_mql5",
        description:
          "MQL5 ファイルを MetaEditor CLI でコンパイルします。コンパイルエラーや警告を取得できます。",
        inputSchema: {
          type: "object",
          properties: {
            filePath: {
              type: "string",
              description: "コンパイル対象の .mq5 ファイルパス",
            },
            metaeditorPath: {
              type: "string",
              description:
                "MetaEditor CLI のパス（省略時はデフォルトパスを使用）",
            },
          },
          required: ["filePath"],
        },
      },
      {
        name: "validate_ea_structure",
        description:
          "EA (Expert Advisor) の構造を検証します。OnInit, OnTick 等の必須関数の存在をチェックし、改善提案を行います。",
        inputSchema: {
          type: "object",
          properties: {
            filePath: {
              type: "string",
              description: "検証対象の EA ファイルパス",
            },
            content: {
              type: "string",
              description:
                "直接検証する MQL5 コード（filePath の代わりに使用可能）",
            },
          },
        },
      },
      {
        name: "validate_indicator_structure",
        description:
          "インジケーターの構造を検証します。OnCalculate, indicator_buffers 等の必須要素をチェックし、改善提案を行います。",
        inputSchema: {
          type: "object",
          properties: {
            filePath: {
              type: "string",
              description: "検証対象のインジケーターファイルパス",
            },
            content: {
              type: "string",
              description:
                "直接検証する MQL5 コード（filePath の代わりに使用可能）",
            },
          },
        },
      },
      {
        name: "get_mql5_docs",
        description:
          "MQL5 組み込み関数のドキュメントを取得します。関数名を指定するか、キーワードで検索できます。",
        inputSchema: {
          type: "object",
          properties: {
            functionName: {
              type: "string",
              description: "取得する関数名（例: OrderSend, OnTick）",
            },
            keyword: {
              type: "string",
              description: "検索キーワード（functionName の代わりに使用可能）",
            },
            listAll: {
              type: "boolean",
              description: "true で利用可能な全関数を一覧表示",
            },
          },
        },
      },
      {
        name: "check_trading_logic",
        description:
          "EA のトレードロジックを検証します。リスク管理、エラーハンドリング、二重発注防止などのベストプラクティスをチェックします。",
        inputSchema: {
          type: "object",
          properties: {
            filePath: {
              type: "string",
              description: "検証対象の EA ファイルパス",
            },
            content: {
              type: "string",
              description:
                "直接検証する MQL5 コード（filePath の代わりに使用可能）",
            },
          },
        },
      },
      {
        name: "analyze_indicator_buffers",
        description:
          "インジケーターのバッファ設定を分析します。バッファ数、SetIndexBuffer の呼び出し、プロパティ設定を検証します。",
        inputSchema: {
          type: "object",
          properties: {
            filePath: {
              type: "string",
              description: "分析対象のインジケーターファイルパス",
            },
            content: {
              type: "string",
              description:
                "直接分析する MQL5 コード（filePath の代わりに使用可能）",
            },
          },
        },
      },
    ],
  };
});

/**
 * ツール実行リクエストを処理する
 */
server.setRequestHandler(CallToolRequestSchema, async (request) => {
  const { name, arguments: args } = request.params;

  try {
    switch (name) {
      case "compile_mql5": {
        const result = await compileMql5(args.filePath, {
          metaeditorPath: args.metaeditorPath,
        });
        return {
          content: [{ type: "text", text: formatCompileResult(result) }],
        };
      }

      case "validate_ea_structure": {
        let result;
        if (args.content) {
          result = validateEAContent(args.content, args.filePath || "inline");
        } else if (args.filePath) {
          result = await validateEAStructure(args.filePath);
        } else {
          throw new Error("filePath または content が必要です");
        }
        return {
          content: [{ type: "text", text: formatValidationResult(result) }],
        };
      }

      case "validate_indicator_structure": {
        let result;
        if (args.content) {
          result = validateIndicatorContent(
            args.content,
            args.filePath || "inline"
          );
        } else if (args.filePath) {
          result = await validateIndicatorStructure(args.filePath);
        } else {
          throw new Error("filePath または content が必要です");
        }
        return {
          content: [{ type: "text", text: formatValidationResult(result) }],
        };
      }

      case "get_mql5_docs": {
        if (args.listAll) {
          const functions = listFunctions();
          let text = "## MQL5 関数一覧\n\n";
          const byCategory = {};
          for (const func of functions) {
            if (!byCategory[func.category]) {
              byCategory[func.category] = [];
            }
            byCategory[func.category].push(func);
          }
          for (const [cat, funcs] of Object.entries(byCategory)) {
            text += `### ${cat}\n`;
            for (const func of funcs) {
              text += `- **${func.name}**: ${func.description}\n`;
            }
            text += "\n";
          }
          return { content: [{ type: "text", text }] };
        }

        if (args.keyword) {
          const results = searchFunctions(args.keyword);
          if (results.length === 0) {
            return {
              content: [
                {
                  type: "text",
                  text: `"${args.keyword}" に一致する関数が見つかりませんでした。`,
                },
              ],
            };
          }
          let text = `## "${args.keyword}" の検索結果\n\n`;
          for (const func of results) {
            text += `- **${func.name}** (${func.category}): ${func.description}\n`;
          }
          return { content: [{ type: "text", text }] };
        }

        if (args.functionName) {
          const { found, doc } = getMql5Docs(args.functionName);
          if (!found) {
            return {
              content: [
                {
                  type: "text",
                  text: `関数 "${args.functionName}" のドキュメントが見つかりませんでした。\nキーワード検索を試してください。`,
                },
              ],
            };
          }
          return {
            content: [{ type: "text", text: formatDoc(doc, args.functionName) }],
          };
        }

        return {
          content: [
            {
              type: "text",
              text: "functionName, keyword, または listAll を指定してください。",
            },
          ],
        };
      }

      case "check_trading_logic": {
        const content = args.content || (await readFileContent(args.filePath));
        if (!content) {
          throw new Error("filePath または content が必要です");
        }
        const result = checkTradingLogic(content);
        return { content: [{ type: "text", text: result }] };
      }

      case "analyze_indicator_buffers": {
        const content = args.content || (await readFileContent(args.filePath));
        if (!content) {
          throw new Error("filePath または content が必要です");
        }
        const result = analyzeIndicatorBuffers(content);
        return { content: [{ type: "text", text: result }] };
      }

      default:
        throw new Error(`Unknown tool: ${name}`);
    }
  } catch (error) {
    return {
      content: [{ type: "text", text: `エラー: ${error.message}` }],
      isError: true,
    };
  }
});

/**
 * ファイルからコンテンツを読み取る
 * @param {string} filePath - ファイルパス
 * @returns {Promise<string|null>} ファイル内容、または null
 */
async function readFileContent(filePath) {
  if (!filePath) return null;
  try {
    return await fs.readFile(filePath, "utf-8");
  } catch (error) {
    throw new Error(`ファイルを読み込めません: ${filePath} - ${error.message}`);
  }
}

/**
 * トレードロジックの妥当性を検証する
 */
function checkTradingLogic(content) {
  const issues = [];
  const suggestions = [];

  // リスク管理のチェック
  if (!/StopLoss|sl\s*[=<>]/i.test(content)) {
    issues.push("⚠️ ストップロス設定が見つかりません");
  }

  // エラーハンドリングのチェック
  if (!/GetLastError\s*\(\s*\)/.test(content)) {
    issues.push("⚠️ エラーハンドリング (GetLastError) が見つかりません");
  }

  // 二重発注防止のチェック
  if (
    /OrderSend|PositionOpen/.test(content) &&
    !/PositionsTotal|PositionSelect/.test(content)
  ) {
    issues.push(
      "⚠️ ポジション確認なしで発注している可能性があります（二重発注のリスク）"
    );
  }

  // スリッページ設定のチェック
  if (/OrderSend/.test(content) && !/deviation/.test(content)) {
    suggestions.push("💡 スリッページ許容値 (deviation) の設定を推奨します");
  }

  // マジックナンバーのチェック
  if (/OrderSend|PositionOpen/.test(content) && !/magic/.test(content)) {
    suggestions.push(
      "💡 マジックナンバーの設定を推奨します（EA の識別用）"
    );
  }

  // CTrade クラスの使用チェック
  if (/OrderSend/.test(content) && !/CTrade/.test(content)) {
    suggestions.push(
      "💡 CTrade クラスの使用を推奨します（コードの簡素化）"
    );
  }

  // ログ出力のチェック
  if (!/Print\s*\(/.test(content)) {
    suggestions.push(
      "💡 Print() によるログ出力を追加すると、デバッグが容易になります"
    );
  }

  let result = "## トレードロジック検証結果\n\n";

  if (issues.length === 0 && suggestions.length === 0) {
    result += "✅ 主要なトレードロジックのベストプラクティスが守られています。\n";
  } else {
    if (issues.length > 0) {
      result += "### 問題点\n";
      result += issues.join("\n") + "\n\n";
    }
    if (suggestions.length > 0) {
      result += "### 改善提案\n";
      result += suggestions.join("\n") + "\n";
    }
  }

  return result;
}

/**
 * インジケーターバッファ設定を分析する
 */
function analyzeIndicatorBuffers(content) {
  const analysis = {
    bufferCount: 0,
    plotCount: 0,
    setIndexBufferCalls: [],
    properties: [],
    issues: [],
    suggestions: [],
  };

  // indicator_buffers の解析
  const bufferMatch = content.match(/#property\s+indicator_buffers\s+(\d+)/);
  if (bufferMatch) {
    analysis.bufferCount = parseInt(bufferMatch[1], 10);
  } else {
    analysis.issues.push("❌ #property indicator_buffers が見つかりません");
  }

  // indicator_plots の解析
  const plotMatch = content.match(/#property\s+indicator_plots\s+(\d+)/);
  if (plotMatch) {
    analysis.plotCount = parseInt(plotMatch[1], 10);
  }

  // SetIndexBuffer の呼び出し解析
  const setIndexBufferRegex = /SetIndexBuffer\s*\(\s*(\d+)/g;
  let match;
  while ((match = setIndexBufferRegex.exec(content)) !== null) {
    analysis.setIndexBufferCalls.push(parseInt(match[1], 10));
  }

  // バッファ関連プロパティの解析
  const propertyRegex =
    /#property\s+indicator_(color|type|style|width|label)\d*\s+.+/g;
  while ((match = propertyRegex.exec(content)) !== null) {
    analysis.properties.push(match[0]);
  }

  // 検証
  if (
    analysis.bufferCount > 0 &&
    analysis.setIndexBufferCalls.length < analysis.bufferCount
  ) {
    analysis.issues.push(
      `⚠️ 宣言されたバッファ数 (${analysis.bufferCount}) に対し、SetIndexBuffer の呼び出しが ${analysis.setIndexBufferCalls.length} 回しかありません`
    );
  }

  // ArraySetAsSeries のチェック
  if (!/ArraySetAsSeries\s*\(/.test(content)) {
    analysis.suggestions.push(
      "💡 バッファ配列に ArraySetAsSeries(buffer, true) を設定することを推奨します"
    );
  }

  // INDICATOR_CALCULATIONS のチェック
  if (
    analysis.bufferCount > analysis.plotCount &&
    !/INDICATOR_CALCULATIONS/.test(content)
  ) {
    analysis.suggestions.push(
      "💡 非表示バッファには INDICATOR_CALCULATIONS タイプを使用してください"
    );
  }

  // 結果フォーマット
  let result = "## インジケーターバッファ分析\n\n";
  result += `- **宣言バッファ数**: ${analysis.bufferCount}\n`;
  result += `- **プロット数**: ${analysis.plotCount}\n`;
  result += `- **SetIndexBuffer 呼び出し**: ${analysis.setIndexBufferCalls.length} 回\n`;

  if (analysis.properties.length > 0) {
    result += `\n### プロパティ設定\n`;
    for (const prop of analysis.properties) {
      result += `- \`${prop}\`\n`;
    }
  }

  if (analysis.issues.length > 0) {
    result += "\n### 問題点\n";
    result += analysis.issues.join("\n") + "\n";
  }

  if (analysis.suggestions.length > 0) {
    result += "\n### 改善提案\n";
    result += analysis.suggestions.join("\n") + "\n";
  }

  if (analysis.issues.length === 0 && analysis.suggestions.length === 0) {
    result += "\n✅ バッファ設定に問題はありません。\n";
  }

  return result;
}

/**
 * サーバーを起動する
 */
async function main() {
  const transport = new StdioServerTransport();
  await server.connect(transport);
  console.error("MQL5 MCP サーバーが起動しました");
}

main().catch((error) => {
  console.error("サーバー起動エラー:", error);
  process.exit(1);
});
