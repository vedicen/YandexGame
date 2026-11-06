using Godot;
using System.Collections.Generic;

public partial class NPC : AnimatedEnemy
{
    [Export] public Godot.Collections.Array<int> TradeItemIds { get; set; } = new();

    public bool IsTrading { get; private set; }
    public TradingStand ActiveTradingStand { get; private set; }
    public Player TradingPlayer { get; private set; }

    public Godot.Collections.Array<Godot.Collections.Dictionary> GetTradeCatalog()
    {
        var catalog = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var addedIds = new HashSet<int>();
        foreach (int id in TradeItemIds)
        {
            if (!addedIds.Add(id))
            {
                GD.PushWarning($"NPC '{Name}' has duplicate trade item ID {id}.");
                continue;
            }

            if (!TradeItemDatabase.TryGet(id, out TradeItemRecord entry))
            {
                GD.PushError($"NPC '{Name}' references unknown trade item ID {id}.");
                continue;
            }

            catalog.Add(new Godot.Collections.Dictionary
            {
                ["id"] = entry.Id,
                ["name"] = entry.Item.DisplayName,
                ["description"] = entry.Item.Description,
                ["price"] = entry.Price
            });
        }

        return catalog;
    }

    public bool SellsItem(int id) => TradeItemIds.Contains(id);

    public bool BeginTrading(TradingStand stand, Player player)
    {
        if (IsTrading || stand == null || player == null)
            return false;

        ActiveTradingStand = stand;
        TradingPlayer = player;
        IsTrading = true;
        return true;
    }

    public void EndTrading(TradingStand stand, Player player)
    {
        if (!IsTrading || ActiveTradingStand != stand || TradingPlayer != player)
            return;

        ActiveTradingStand = null;
        TradingPlayer = null;
        IsTrading = false;
    }
}
