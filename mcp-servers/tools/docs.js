/**
 * MQL5 ドキュメント取得ツール
 * MQL5 公式リファレンスから関数説明を取得する
 */

/**
 * MQL5 組み込み関数のドキュメントデータベース
 * オフラインで参照可能な主要関数の説明
 */
const MQL5_DOCS = {
  // 取引関数
  OrderSend: {
    category: "取引関数",
    description:
      "取引リクエストをサーバーに送信します。成行注文、指値注文、逆指値注文の発注に使用します。",
    syntax: "bool OrderSend(MqlTradeRequest& request, MqlTradeResult& result)",
    parameters: [
      {
        name: "request",
        type: "MqlTradeRequest&",
        description: "取引リクエストの構造体",
      },
      {
        name: "result",
        type: "MqlTradeResult&",
        description: "取引結果を受け取る構造体",
      },
    ],
    returns: "bool - 成功時 true、失敗時 false",
    example: `MqlTradeRequest request;
MqlTradeResult result;
request.action = TRADE_ACTION_DEAL;
request.symbol = Symbol();
request.volume = 0.1;
request.type = ORDER_TYPE_BUY;
request.price = Ask;
if(OrderSend(request, result))
   Print("注文成功: ", result.order);`,
    seeAlso: ["OrderSendAsync", "PositionOpen", "CTrade"],
  },

  PositionOpen: {
    category: "CTrade クラス",
    description:
      "指定したシンボルで新しいポジションを開きます。CTrade クラスのメソッドです。",
    syntax:
      "bool CTrade::PositionOpen(string symbol, ENUM_ORDER_TYPE type, double volume, double price, double sl, double tp, string comment)",
    parameters: [
      { name: "symbol", type: "string", description: "取引シンボル" },
      { name: "type", type: "ENUM_ORDER_TYPE", description: "注文タイプ" },
      { name: "volume", type: "double", description: "取引ロット数" },
      { name: "price", type: "double", description: "約定価格" },
      { name: "sl", type: "double", description: "ストップロス価格" },
      { name: "tp", type: "double", description: "テイクプロフィット価格" },
      { name: "comment", type: "string", description: "注文コメント" },
    ],
    returns: "bool - 成功時 true",
    example: `#include <Trade/Trade.mqh>
CTrade trade;
trade.PositionOpen(Symbol(), ORDER_TYPE_BUY, 0.1, Ask, Ask-100*Point(), Ask+200*Point(), "MyEA");`,
    seeAlso: ["PositionClose", "PositionModify", "OrderSend"],
  },

  PositionClose: {
    category: "CTrade クラス",
    description:
      "指定したポジションをクローズします。CTrade クラスのメソッドです。",
    syntax: "bool CTrade::PositionClose(ulong ticket, ulong deviation)",
    parameters: [
      { name: "ticket", type: "ulong", description: "ポジションチケット" },
      {
        name: "deviation",
        type: "ulong",
        description: "許容スリッページ（ポイント）",
      },
    ],
    returns: "bool - 成功時 true",
    example: `CTrade trade;
if(trade.PositionClose(ticket))
   Print("ポジションクローズ成功");`,
    seeAlso: ["PositionOpen", "PositionModify"],
  },

  PositionModify: {
    category: "CTrade クラス",
    description:
      "既存ポジションの SL/TP を変更します。CTrade クラスのメソッドです。",
    syntax: "bool CTrade::PositionModify(ulong ticket, double sl, double tp)",
    parameters: [
      { name: "ticket", type: "ulong", description: "ポジションチケット" },
      { name: "sl", type: "double", description: "新しいストップロス価格" },
      { name: "tp", type: "double", description: "新しいテイクプロフィット価格" },
    ],
    returns: "bool - 成功時 true",
    example: `CTrade trade;
trade.PositionModify(ticket, newSL, newTP);`,
    seeAlso: ["PositionOpen", "PositionClose"],
  },

  // ポジション情報関数
  PositionSelect: {
    category: "取引関数",
    description:
      "指定したシンボルのポジションを選択し、以降の操作で使用できるようにします。",
    syntax: "bool PositionSelect(string symbol)",
    parameters: [
      { name: "symbol", type: "string", description: "シンボル名" },
    ],
    returns: "bool - ポジションが存在すれば true",
    example: `if(PositionSelect(Symbol()))
{
   double profit = PositionGetDouble(POSITION_PROFIT);
   Print("現在の損益: ", profit);
}`,
    seeAlso: ["PositionGetDouble", "PositionGetInteger", "PositionGetString"],
  },

  PositionGetDouble: {
    category: "取引関数",
    description:
      "選択されたポジションの double 型プロパティを取得します。事前に PositionSelect が必要です。",
    syntax: "double PositionGetDouble(ENUM_POSITION_PROPERTY_DOUBLE property)",
    parameters: [
      {
        name: "property",
        type: "ENUM_POSITION_PROPERTY_DOUBLE",
        description: "取得するプロパティ（POSITION_VOLUME, POSITION_PROFIT など）",
      },
    ],
    returns: "double - プロパティの値",
    example: `PositionSelect(Symbol());
double volume = PositionGetDouble(POSITION_VOLUME);
double profit = PositionGetDouble(POSITION_PROFIT);`,
    seeAlso: ["PositionSelect", "PositionGetInteger"],
  },

  // インジケーター関数
  SetIndexBuffer: {
    category: "インジケーター関数",
    description:
      "配列をインジケーターバッファとして設定します。OnInit で使用します。",
    syntax:
      "bool SetIndexBuffer(int index, double buffer[], ENUM_INDEXBUFFER_TYPE data_type)",
    parameters: [
      { name: "index", type: "int", description: "バッファインデックス（0から開始）" },
      { name: "buffer", type: "double[]", description: "バッファとして使用する配列" },
      {
        name: "data_type",
        type: "ENUM_INDEXBUFFER_TYPE",
        description: "バッファのタイプ（INDICATOR_DATA など）",
      },
    ],
    returns: "bool - 成功時 true",
    example: `double Buffer[];
int OnInit()
{
   SetIndexBuffer(0, Buffer, INDICATOR_DATA);
   return INIT_SUCCEEDED;
}`,
    seeAlso: ["ArraySetAsSeries", "PlotIndexSetInteger"],
  },

  ArraySetAsSeries: {
    category: "配列関数",
    description:
      "配列のインデックス方向を設定します。true で新しい要素が [0] になります。",
    syntax: "bool ArraySetAsSeries(const void& array[], bool flag)",
    parameters: [
      { name: "array", type: "void[]", description: "対象の配列" },
      {
        name: "flag",
        type: "bool",
        description: "true: 逆順（新→旧）、false: 通常（旧→新）",
      },
    ],
    returns: "bool - 成功時 true",
    example: `double prices[];
ArraySetAsSeries(prices, true);`,
    seeAlso: ["SetIndexBuffer", "ArrayResize"],
  },

  // イベント関数
  OnInit: {
    category: "イベントハンドラ",
    description:
      "EA/インジケーターの初期化時に呼び出されます。リソースの初期化や設定を行います。",
    syntax: "int OnInit()",
    returns: "int - INIT_SUCCEEDED（成功）または INIT_FAILED（失敗）",
    example: `int OnInit()
{
   // 初期化処理
   if(!CheckParameters())
      return INIT_FAILED;
   return INIT_SUCCEEDED;
}`,
    seeAlso: ["OnDeinit", "OnTick", "OnCalculate"],
  },

  OnTick: {
    category: "イベントハンドラ",
    description:
      "EA 用。新しいティック（価格更新）ごとに呼び出されます。トレードロジックを実装します。",
    syntax: "void OnTick()",
    example: `void OnTick()
{
   // 取引ロジック
   if(ShouldBuy())
      ExecuteBuy();
}`,
    seeAlso: ["OnInit", "OnDeinit", "OnTimer"],
  },

  OnCalculate: {
    category: "イベントハンドラ",
    description:
      "インジケーター用。新しいバーや価格更新時に呼び出されます。計算ロジックを実装します。",
    syntax:
      "int OnCalculate(const int rates_total, const int prev_calculated, const datetime& time[], const double& open[], const double& high[], const double& low[], const double& close[], const long& tick_volume[], const long& volume[], const int& spread[])",
    returns: "int - 計算済みのバー数（次回の prev_calculated に渡される）",
    example: `int OnCalculate(const int rates_total,
                 const int prev_calculated,
                 const datetime &time[],
                 const double &open[],
                 const double &high[],
                 const double &low[],
                 const double &close[],
                 const long &tick_volume[],
                 const long &volume[],
                 const int &spread[])
{
   for(int i = prev_calculated; i < rates_total; i++)
   {
      Buffer[i] = close[i];
   }
   return rates_total;
}`,
    seeAlso: ["OnInit", "SetIndexBuffer"],
  },

  // エラーハンドリング
  GetLastError: {
    category: "エラー関数",
    description:
      "最後に発生したエラーコードを取得します。取引操作後のエラーチェックに必須です。",
    syntax: "int GetLastError()",
    returns: "int - エラーコード（0 はエラーなし）",
    example: `if(!OrderSend(request, result))
{
   int error = GetLastError();
   Print("エラー: ", error);
}`,
    seeAlso: ["ResetLastError"],
  },
};

/**
 * 関数名から MQL5 ドキュメントを取得する
 * @param {string} functionName - 関数名
 * @returns {{found: boolean, doc: object|null}}
 */
export function getMql5Docs(functionName) {
  const doc = MQL5_DOCS[functionName];
  if (doc) {
    return { found: true, doc };
  }
  return { found: false, doc: null };
}

/**
 * カテゴリ別に関数一覧を取得する
 * @param {string} category - カテゴリ名（省略時は全カテゴリ）
 * @returns {object[]} 関数リスト
 */
export function listFunctions(category = null) {
  const result = [];
  for (const [name, doc] of Object.entries(MQL5_DOCS)) {
    if (!category || doc.category === category) {
      result.push({
        name,
        category: doc.category,
        description: doc.description,
      });
    }
  }
  return result;
}

/**
 * 利用可能なカテゴリ一覧を取得する
 * @returns {string[]} カテゴリリスト
 */
export function listCategories() {
  const categories = new Set();
  for (const doc of Object.values(MQL5_DOCS)) {
    categories.add(doc.category);
  }
  return Array.from(categories).sort();
}

/**
 * キーワードで関数を検索する
 * @param {string} keyword - 検索キーワード
 * @returns {object[]} マッチした関数リスト
 */
export function searchFunctions(keyword) {
  const lowerKeyword = keyword.toLowerCase();
  const result = [];

  for (const [name, doc] of Object.entries(MQL5_DOCS)) {
    if (
      name.toLowerCase().includes(lowerKeyword) ||
      doc.description.toLowerCase().includes(lowerKeyword) ||
      doc.category.toLowerCase().includes(lowerKeyword)
    ) {
      result.push({
        name,
        category: doc.category,
        description: doc.description,
      });
    }
  }

  return result;
}

/**
 * ドキュメントをフォーマットして返す
 * @param {object} doc - ドキュメントオブジェクト
 * @param {string} functionName - 関数名
 * @returns {string} フォーマット済みのドキュメント
 */
export function formatDoc(doc, functionName) {
  let message = `## ${functionName}\n\n`;
  message += `**カテゴリ**: ${doc.category}\n\n`;
  message += `**説明**: ${doc.description}\n\n`;

  if (doc.syntax) {
    message += `**構文**:\n\`\`\`cpp\n${doc.syntax}\n\`\`\`\n\n`;
  }

  if (doc.parameters && doc.parameters.length > 0) {
    message += `**パラメータ**:\n`;
    for (const param of doc.parameters) {
      message += `- \`${param.name}\` (${param.type}): ${param.description}\n`;
    }
    message += "\n";
  }

  if (doc.returns) {
    message += `**戻り値**: ${doc.returns}\n\n`;
  }

  if (doc.example) {
    message += `**例**:\n\`\`\`cpp\n${doc.example}\n\`\`\`\n\n`;
  }

  if (doc.seeAlso && doc.seeAlso.length > 0) {
    message += `**関連**: ${doc.seeAlso.join(", ")}\n`;
  }

  return message;
}

export default {
  getMql5Docs,
  listFunctions,
  listCategories,
  searchFunctions,
  formatDoc,
};
