using Godot;
using System.Collections.Generic;
using System.Text.Json;

public sealed class TradeItemRecord
{
    public int Id { get; set; }
    public string ItemPath { get; set; } = "";
    public int Price { get; set; }
    public Item Item { get; set; }
}

public static class TradeItemDatabase
{
    private const string DatabasePath = "res://inventory/items/trade_items.json";
    private static Dictionary<int, TradeItemRecord> _records;

    public static bool TryGet(int id, out TradeItemRecord record)
    {
        LoadIfNeeded();
        return _records.TryGetValue(id, out record);
    }

    private static void LoadIfNeeded()
    {
        if (_records != null)
            return;

        _records = new Dictionary<int, TradeItemRecord>();
        using var file = FileAccess.Open(DatabasePath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushError($"Could not open trade database '{DatabasePath}': {FileAccess.GetOpenError()}.");
            return;
        }

        List<TradeItemRecord> entries = JsonSerializer.Deserialize<List<TradeItemRecord>>(file.GetAsText());
        if (entries == null)
        {
            GD.PushError($"Trade database '{DatabasePath}' is empty or invalid.");
            return;
        }

        foreach (TradeItemRecord entry in entries)
        {
            if (entry.Id < 0 || entry.Price <= 0 || string.IsNullOrWhiteSpace(entry.ItemPath))
            {
                GD.PushError($"Invalid trade database entry for item ID {entry.Id}.");
                continue;
            }

            if (_records.ContainsKey(entry.Id))
            {
                GD.PushError($"Duplicate item ID {entry.Id} in trade database.");
                continue;
            }

            entry.Item = GD.Load<Item>(entry.ItemPath);
            if (entry.Item == null)
            {
                GD.PushError($"Could not load item '{entry.ItemPath}' for trade ID {entry.Id}.");
                continue;
            }

            _records.Add(entry.Id, entry);
        }
    }
}
