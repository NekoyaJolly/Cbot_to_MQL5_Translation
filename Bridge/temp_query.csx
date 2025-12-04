using Microsoft.Data.Sqlite;
var conn = new SqliteConnection("Data Source=bridge.db");
conn.Open();
var cmd = conn.CreateCommand();
cmd.CommandText = "SELECT Id, SourceId, EventType, Symbol, StopLoss, TakeProfit, Processed FROM Orders";
var reader = cmd.ExecuteReader();
while(reader.Read()) {
    Console.WriteLine($"{reader["SourceId"]} | {reader["EventType"]} | {reader["Symbol"]} | SL:{reader["StopLoss"]} | TP:{reader["TakeProfit"]} | Processed:{reader["Processed"]}");
}
