using Godot;

public partial class ItemUI : PanelContainer
{
    public InventoryComponent Inventory;
    public Player Player;
    public int Index;
    private Node3D _pivot;

    public void Bind(Player player, InventoryComponent inventory, int index)
    {
        Player = player;
        Inventory = inventory;
        Index = index;
        if (IsInsideTree())
            Refresh();
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        Refresh();
    }

    private void Refresh()
    {
        var slot = Inventory?.Slots[Index];
        var item = slot?.Item;
        var style = new StyleBoxFlat { BgColor = new Color("182331"), BorderColor = item?.LightColor ?? new Color("344255"),
            BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6 };
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
        TooltipText = $"{item.DisplayName}\n{item.Description}\nВес: {item.Weight:0.#}" +
            (item is Weapon weaponInfo ? $"\nУрон: {weaponInfo.Damage:0.#}" : "") +
            $"\n{RarityName(item.Rarity)} • {slot.ItemCount}/{item.MaxStackSize}" +
            (item is UsableItem ? "\nПКМ — использовать" : "");

        _pivot = GetNode<Node3D>("Content/ModelContainer/ItemViewport/ModelPivot");
        ClearModel();
        if (item.Model != null) _pivot.AddChild(item.Model.Instantiate<Node3D>());
        GetNode<Camera3D>("Content/ModelContainer/ItemViewport/ItemCamera").LookAt(Vector3.Zero);
        OmniLight3D light = GetNode<OmniLight3D>("Content/ModelContainer/ItemViewport/ItemLight");
        light.LightColor = item.LightColor;
        light.LightEnergy = item.LightEnergy;

        GetNode<Label>("Content/NameLabel").Text = item.DisplayName;
        GetNode<Label>("Content/CountLabel").Text = $"×{slot.ItemCount}";
    }

    private void ClearModel()
    {
        _pivot ??= GetNodeOrNull<Node3D>("Content/ModelContainer/ItemViewport/ModelPivot");
        if (_pivot == null)
            return;

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
        if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } && Inventory != null)
        {
            InventoryService.Instance.Send("use", Player, Inventory.GetPath(), Index);
            AcceptEvent();
        }
    }
    public override Variant _GetDragData(Vector2 position)
    {
        if (Inventory?.Slots[Index] == null) return default;
        SetDragPreview(new Label { Text = Inventory.Slots[Index].Item.DisplayName });
        return new Godot.Collections.Dictionary
        {
            ["source"] = Inventory.GetPath().ToString(),
            ["index"] = Index,
            ["revision"] = InventoryService.Instance.Revision,
            ["is_weapon"] = Inventory.Slots[Index].Item is Weapon
        };
    }
    public override bool _CanDropData(Vector2 position, Variant data)
    {
        if (Inventory == null || !IsStackDrag(data))
            return false;

        if (Inventory == Player?.EquipmentInventory)
        {
            var drag = data.AsGodotDictionary();
            return drag.ContainsKey("is_weapon") && drag["is_weapon"].AsBool();
        }

        return true;
    }
    public static bool IsStackDrag(Variant data) => data.VariantType == Variant.Type.Dictionary &&
        data.AsGodotDictionary().ContainsKey("source") && data.AsGodotDictionary().ContainsKey("index") && data.AsGodotDictionary().ContainsKey("revision");
    public override void _DropData(Vector2 position, Variant data)
    {
        var drag = data.AsGodotDictionary();
        InventoryService.Instance.Send("move", Player, drag["source"].AsString(), drag["index"].AsInt32(), Inventory.GetPath(), Index, drag["revision"].AsInt32());
    }
}
