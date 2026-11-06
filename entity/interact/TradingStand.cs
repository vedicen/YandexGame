using Godot;

public partial class TradingStand : interact_obj
{
    [Export] public NPC npc_owner { get; set; }
    [Export] public PackedScene DialogueScene { get; set; }

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
        playerUi.AddChild(dialog);
    }

    private void EndTrading(Player player, NPC npc, Control dialog)
    {
        npc.EndTrading(this, player);
        player.EndTrading(this);
        if (GodotObject.IsInstanceValid(dialog))
            dialog.QueueFree();
    }
}
