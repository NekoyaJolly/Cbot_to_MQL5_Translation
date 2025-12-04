using Microsoft.Data.Sqlite;

var dbPath = @"c:\Users\Nekoya2\appprojects\Cbot_to_MQL5_Translation\Bridge\bridge.db";
Console.WriteLine($"Reading database: {dbPath}");
Console.WriteLine();

using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();

var cmd = conn.CreateCommand();
cmd.CommandText = "SELECT Id, SourceId, EventType, Symbol, Direction, Volume, StopLoss, TakeProfit, Processed FROM Orders ORDER BY rowid";
var reader = cmd.ExecuteReader();

Console.WriteLine("=== Bridge Database Orders ===");
Console.WriteLine();

int count = 0;
while(reader.Read()) 
{
    count++;
    Console.WriteLine($"--- Order {count} ---");
    Console.WriteLine($"  Id:        {reader["Id"]}");
    Console.WriteLine($"  SourceId:  {reader["SourceId"]}");
    Console.WriteLine($"  EventType: {reader["EventType"]}");
    Console.WriteLine($"  Symbol:    {reader["Symbol"]}");
    Console.WriteLine($"  Direction: {reader["Direction"]}");
    Console.WriteLine($"  Volume:    {reader["Volume"]}");
    Console.WriteLine($"  StopLoss:  {reader["StopLoss"]}");
    Console.WriteLine($"  TakeProfit:{reader["TakeProfit"]}");
    Console.WriteLine($"  Processed: {reader["Processed"]}");
    Console.WriteLine();
}

Console.WriteLine($"Total orders: {count}");
