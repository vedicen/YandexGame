using Godot;
using System;

public partial class TradingStand : interact_obj
{
    [Export] public NPC npc_owner { get; set; }
    [Export] public PackedScene DialogueScene { get; set; }
    private Action<int> _moneyChangedHandler;

    public override void interact(Player player)
    {
        if (!GodotObject.IsInstanceValid(npc_owner))
        {
            GD.PushError($"Trading stand '{Name}' requires a valid npc_owner.");
            return;
        }

        if (npc_owner.IsTrading)
        {
            if (npc_owner.ActiveTradingStand == this && npc_owner.TradingPlayer == player)
                GD.PushWarning($"Player is already trading at '{Name}'.");
            return;
        }

        if (!player.BeginTrading(this))
            return;

        if (!npc_owner.BeginTrading(this, player))
        {
            player.EndTrading(this);
            return;
        }
        InventoryService.Instance?.BeginTradeSession(player, this);

        PackedScene scene = DialogueScene ?? GD.Load<PackedScene>("res://Dialogue/DialogueWindow.tscn");
        if (scene == null)
        {
            GD.PushError("Could not load res://Dialogue/DialogueWindow.tscn.");
            EndTrading(player, npc_owner, null);
            return;
        }

        Control playerUi = player.GetNodeOrNull<Control>("UI");
        if (playerUi == null)
        {
            GD.PushError($"Player '{player.Name}' is missing its UI node.");
            EndTrading(player, npc_owner, null);
            return;
        }

        Control dialog = scene.Instantiate<Control>();
        dialog.Connect("dialog_closed", Callable.From(() => EndTrading(player, npc_owner, dialog)));
        dialog.Connect("request_action", Callable.From<int, int>((id, price) =>
            InventoryService.Instance?.RequestTradeOffer(player, this, id, price)));
        dialog.Connect("purchase_decided", Callable.From<int, int, bool>((id, price, accepted) =>
            InventoryService.Instance?.RequestTradePurchase(player, this, id, price, accepted)));
        _moneyChangedHandler = amount =>
        {
            if (GodotObject.IsInstanceValid(dialog))
                dialog.Call("set_money", amount);
        };
        player.MoneyChanged += _moneyChangedHandler;
        player.SetTradeDialog(dialog);
        playerUi.AddChild(dialog);
        dialog.Call("configure", npc_owner.GetTradeCatalog(), player.Money);
    }

    private void EndTrading(Player player, NPC npc, Control dialog)
    {
        if (GodotObject.IsInstanceValid(npc))
            npc.EndTrading(this, player);
        InventoryService.Instance?.EndTradeSession(player, this);
        if (_moneyChangedHandler != null)
        {
            player.MoneyChanged -= _moneyChangedHandler;
            _moneyChangedHandler = null;
        }
        player.SetTradeDialog(null);
        player.EndTrading(this);
        player.EndInteraction(this);
        if (GodotObject.IsInstanceValid(dialog))
            dialog.QueueFree();
    }
}
