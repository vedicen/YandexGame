using Godot;
using System;

public partial class TradingStand : interact_obj
{
    [Export] public NPC npc_owner { get; set; }
    [Export] public PackedScene DialogueScene { get; set; }
    public InventoryComponent SaleStorage => GetNodeOrNull<InventoryComponent>("TradeSaleStorage");

    private Action<int> _moneyChangedHandler;
    private Control _activeDialog;
    private Player _activePlayer;

    public override void _Ready()
    {
        base._Ready();
        if (SaleStorage != null)
        {
            SaleStorage.MaxSlots = 4;
            SaleStorage.IsTradeSaleStorage = true;
        }
    }

    public override void interact(Player player)
    {
        if (!GodotObject.IsInstanceValid(npc_owner))
        {
            GD.PushError($"Trading stand '{Name}' requires a valid npc_owner.");
            return;
        }
        if (npc_owner.IsTrading || !player.BeginTrading(this)) return;
        if (!npc_owner.BeginTrading(this, player))
        {
            player.EndTrading(this);
            return;
        }

        _activePlayer = player;
        InventoryService.Instance?.BeginTradeSession(player, this);

        if (!CanUseDialogueModel())
        {
            OpenFallbackMenu(player);
            return;
        }

        PackedScene scene = DialogueScene ?? GD.Load<PackedScene>("res://Dialogue/DialogueWindow.tscn");
        Control playerUi = player.GetNodeOrNull<Control>("UI");
        if (scene == null || playerUi == null)
        {
            OpenFallbackMenu(player);
            return;
        }

        Control dialog = scene.Instantiate<Control>();
        if (dialog == null)
        {
            OpenFallbackMenu(player);
            return;
        }
        _activeDialog = dialog;
        dialog.Connect("dialog_closed", Callable.From(() => EndTrading(player, npc_owner, dialog)));
        dialog.Connect("model_unavailable", Callable.From(() => SwitchToFallback(player, dialog)));
        dialog.Connect("request_action", Callable.From<int, int>((id, price) =>
            InventoryService.Instance?.RequestTradeOffer(player, this, id, price)));
        dialog.Connect("purchase_decided", Callable.From<int, int, bool>((id, price, accepted) =>
            InventoryService.Instance?.RequestTradePurchase(player, this, id, price, accepted)));
        _moneyChangedHandler = amount =>
        {
            if (GodotObject.IsInstanceValid(dialog)) dialog.Call("set_money", amount);
        };
        player.MoneyChanged += _moneyChangedHandler;
        player.SetTradeDialog(dialog);
        playerUi.AddChild(dialog);
        dialog.Call("configure", npc_owner.GetTradeCatalog(), player.Money);
    }

    private static bool CanUseDialogueModel() =>
        ClassDB.ClassExists("NobodyWhoChat") &&
        FileAccess.FileExists("res://models/Qwen_Qwen3-0.6B-Q4_K_M.gguf");

    private void OpenFallbackMenu(Player player)
    {
        player.SetTradeDialog(null);
        player.InventoryUI?.OpenTrading(this);
    }

    private void SwitchToFallback(Player player, Control dialog)
    {
        if (_activePlayer != player || !GodotObject.IsInstanceValid(player)) return;
        if (GodotObject.IsInstanceValid(dialog)) dialog.QueueFree();
        _activeDialog = null;
        player.SetTradeDialog(null);
        OpenFallbackMenu(player);
    }

    public void CloseFallbackMenu(Player player)
    {
        if (_activePlayer == player) EndTrading(player, npc_owner, _activeDialog);
    }

    private void EndTrading(Player player, NPC npc, Control dialog)
    {
        if (GodotObject.IsInstanceValid(npc)) npc.EndTrading(this, player);
        InventoryService.Instance?.EndTradeSession(player, this);
        if (_moneyChangedHandler != null)
        {
            player.MoneyChanged -= _moneyChangedHandler;
            _moneyChangedHandler = null;
        }
        if (_activeDialog == dialog) _activeDialog = null;
        _activePlayer = null;
        player.SetTradeDialog(null);
        player.InventoryUI?.Close();
        player.EndTrading(this);
        player.EndInteraction(this);
        if (GodotObject.IsInstanceValid(dialog)) dialog.QueueFree();
    }
}
