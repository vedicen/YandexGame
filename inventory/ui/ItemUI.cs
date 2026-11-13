using Godot;

public partial class ItemUI : PanelContainer
{
    public InventoryComponent Inventory;
    public Player Player;
    public int Index;
    private Node3D _pivot;
    private bool _tradeMode;
    private bool _isTradeOffer;
    private TradingStand _stand;
    private int _tradeId;
    private int _tradePrice;
    private Item _displayItem;
    private bool _canAfford;
    private bool _hotbarPreview;

    public void Bind(Player player, InventoryComponent inventory, int index, bool tradeMode = false)
    {
        _hotbarPreview = false;
        Player = player;
        Inventory = inventory;
        Index = index;
        _tradeMode = tradeMode;
        if (IsInsideTree()) Refresh();
    }

    public void BindHotbar(Player player, InventoryComponent inventory, int index)
    {
        _hotbarPreview = true;
        MouseFilter = MouseFilterEnum.Ignore;
        Bind(player, inventory, index);
        _hotbarPreview = true;
        if (IsInsideTree()) Refresh();
    }

    public void BindTradeOffer(Player player, TradingStand stand, int id, Item item, int price, bool canAfford)
    {
        Player = player;
        _stand = stand;
        _tradeId = id;
        _tradePrice = price;
        _displayItem = item;
        _canAfford = canAfford;
        _isTradeOffer = true;
        if (IsInsideTree()) Refresh();
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        Refresh();
    }

    private void Refresh()
    {
        Stack slot = Inventory != null && Inventory.Valid(Index) ? Inventory.Slots[Index] : null;
        Item item = _isTradeOffer ? _displayItem : slot?.Item;
        bool canSell = !_tradeMode || item?.SalePrice > 0;
        var style = new StyleBoxFlat
        {
            BgColor = (_isTradeOffer && !_canAfford) || !canSell ? new Color("111820") : new Color("182331"),
            BorderColor = item == null || !canSell ? new Color("344255") : item.LightColor,
            BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6
        };
        AddThemeStyleboxOverride("panel", style);
        Control content = GetNode<Control>("Content");
        if (item == null)
        {
            TooltipText = "";
            ClearModel();
            content.Hide();
            return;
        }

        content.Show();
        int count = _isTradeOffer ? 1 : slot.ItemCount;
        TooltipText = $"{item.DisplayName}\n{item.Description}\nВес: {item.Weight:0.#}" +
            (item is Weapon weapon ? $"\nУрон: {weapon.Damage:0.#}" : "") +
            $"\n{RarityName(item.Rarity)} • {count}/{item.MaxStackSize}" +
            (_isTradeOffer ? $"\nЦена: {_tradePrice} золотых" :
                _tradeMode && item.SalePrice <= 0 ? "\nНельзя продать: цена продажи равна 0" :
                item is UsableItem && !_tradeMode ? "\nПКМ — использовать" : "");

        _pivot = GetNode<Node3D>("Content/ModelContainer/ItemViewport/ModelPivot");
        ClearModel();
        if (item.Model != null) _pivot.AddChild(item.Model.Instantiate<Node3D>());
        GetNode<Camera3D>("Content/ModelContainer/ItemViewport/ItemCamera").LookAt(Vector3.Zero);
        OmniLight3D light = GetNode<OmniLight3D>("Content/ModelContainer/ItemViewport/ItemLight");
        light.LightColor = item.LightColor;
        light.LightEnergy = item.LightEnergy;
        Label nameLabel = GetNode<Label>("Content/NameLabel");
        nameLabel.Text = item.DisplayName;
        nameLabel.Visible = !_hotbarPreview;
        GetNode<Label>("Content/CountLabel").Text = _isTradeOffer ? $"{_tradePrice} золотых" : $"×{count}";
        Modulate = ((_isTradeOffer && !_canAfford) || !canSell)
            ? new Color(0.52f, 0.56f, 0.62f, 0.78f)
            : Colors.White;
    }

    private void ClearModel()
    {
        _pivot ??= GetNodeOrNull<Node3D>("Content/ModelContainer/ItemViewport/ModelPivot");
        if (_pivot == null) return;
        foreach (Node child in _pivot.GetChildren())
        {
            _pivot.RemoveChild(child);
            child.QueueFree();
        }
    }

    public static string RarityName(Rarity rarity) => rarity switch {
        Rarity.Rare => "Редкий", Rarity.Epic => "Эпический", Rarity.Mythic => "Мифический",
        Rarity.Legendary => "Легендарный", _ => "Обычный" };

    public override void _Process(double delta) { if (_pivot != null) _pivot.RotateY((float)delta * 0.8f); }

    public override void _GuiInput(InputEvent ev)
    {
        if (_hotbarPreview)
            return;
        if (_isTradeOffer && ev is InputEventMouseButton
            { Pressed: true, ButtonIndex: MouseButton.Left, ShiftPressed: true })
        {
            if (_canAfford) InventoryService.Instance?.RequestTradeBuy(Player, _stand, _tradeId);
            AcceptEvent();
            return;
        }
        if (_isTradeOffer) return;

        if (_tradeMode && Inventory?.Valid(Index) == true && Inventory.Slots[Index]?.Item?.SalePrice <= 0 &&
            ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left, ShiftPressed: true })
        {
            AcceptEvent();
            return;
        }

        if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left, ShiftPressed: true } &&
            Inventory != null && Player != null && InventoryService.Instance != null)
        {
            InventoryService.Instance.Send("quick_move", Player, Inventory.GetPath(), Index);
            AcceptEvent();
            return;
        }

        if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } &&
            Inventory != null && !_tradeMode)
        {
            InventoryService.Instance?.Send("use", Player, Inventory.GetPath(), Index);
            AcceptEvent();
        }
    }

    public override Variant _GetDragData(Vector2 position)
    {
        if (_hotbarPreview)
            return default;
        if (_isTradeOffer)
        {
            SetDragPreview(new Label { Text = $"{_displayItem.DisplayName} • {_tradePrice} золотых" });
            return new Godot.Collections.Dictionary
            {
                ["trade_offer"] = true,
                ["id"] = _tradeId,
                ["stand"] = _stand.GetPath().ToString(),
                ["can_afford"] = _canAfford
            };
        }

        if (_isTradeOffer || Inventory?.Valid(Index) != true || Inventory.Slots[Index] == null ||
            _tradeMode && Inventory.Slots[Index].Item.SalePrice <= 0) return default;
        SetDragPreview(new Label { Text = Inventory.Slots[Index].Item.DisplayName });
        return new Godot.Collections.Dictionary
        {
            ["source"] = Inventory.GetPath().ToString(),
            ["index"] = Index,
            ["revision"] = InventoryService.Instance?.Revision ?? 0,
            ["is_hand_item"] = Inventory.Slots[Index].Item is HandItem
        };
    }

    public override bool _CanDropData(Vector2 position, Variant data)
    {
        if (Inventory == null) return false;
        if (IsTradeOfferDrag(data))
            return Inventory == Player?.Inventory &&
                data.AsGodotDictionary()["stand"].AsString() == Player?.ActiveTradingStand?.GetPath().ToString() &&
                data.AsGodotDictionary()["can_afford"].AsBool();
        if (!IsStackDrag(data)) return false;
        var drag = data.AsGodotDictionary();
        string sourcePath = drag["source"].AsString();

        if (Inventory == Player?.Equipment && Index == 0)
            return drag.ContainsKey("is_hand_item") && drag["is_hand_item"].AsBool();

        if (Inventory.IsTradeSaleStorage)
        {
            if (sourcePath == Inventory.GetPath().ToString()) return true;
            if (sourcePath != Player?.Inventory?.GetPath().ToString()) return false;
            int index = drag["index"].AsInt32();
            return index >= Player.Inventory.FirstItemSlot && Player.Inventory.Valid(index) &&
                Player.Inventory.Slots[index]?.Item?.SalePrice > 0;
        }

        return true;
    }

    public static bool IsStackDrag(Variant data) => data.VariantType == Variant.Type.Dictionary &&
        data.AsGodotDictionary().ContainsKey("source") && data.AsGodotDictionary().ContainsKey("index") &&
        data.AsGodotDictionary().ContainsKey("revision");

    private static bool IsTradeOfferDrag(Variant data) => data.VariantType == Variant.Type.Dictionary &&
        data.AsGodotDictionary().ContainsKey("trade_offer") && data.AsGodotDictionary()["trade_offer"].AsBool();

    public override void _DropData(Vector2 position, Variant data)
    {
        if (IsTradeOfferDrag(data))
        {
            var offer = data.AsGodotDictionary();
            if (Inventory == Player?.Inventory && offer["can_afford"].AsBool() &&
                offer["stand"].AsString() == Player?.ActiveTradingStand?.GetPath().ToString())
                InventoryService.Instance?.RequestTradeBuy(Player, Player.ActiveTradingStand, offer["id"].AsInt32());
            return;
        }
        if (Inventory == null || !IsStackDrag(data)) return;
        var drag = data.AsGodotDictionary();
        InventoryService.Instance?.Send("move", Player, drag["source"].AsString(), drag["index"].AsInt32(),
            Inventory.GetPath(), Index, drag["revision"].AsInt32());
    }
}
