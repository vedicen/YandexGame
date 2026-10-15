using Godot;

public partial class ItemUI : PanelContainer
{
    public InventoryComponent Inventory;
    public Player Player;
    public int Index;
    private Node3D _pivot;
    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(96, 120);
        MouseFilter = MouseFilterEnum.Stop;
        var slot = Inventory?.Slots[Index];
        var item = slot?.Item;
        var style = new StyleBoxFlat { BgColor = new Color("182331"), BorderColor = item?.LightColor ?? new Color("344255"),
            BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6 };
        AddThemeStyleboxOverride("panel", style);
        if (item == null) return;
        TooltipText = $"{item.DisplayName}\n{RarityName(item.Rarity)} • {slot.ItemCount}/{item.MaxStackSize}" +
            (item is UsableItem ? "\nПКМ — использовать" : "");
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(box);
        var container = new SubViewportContainer { CustomMinimumSize = new Vector2(84, 72), Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
        box.AddChild(container);
        var viewport = new SubViewport { Size = new Vector2I(84, 72), TransparentBg = true, OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always, GuiDisableInput = true };
        container.AddChild(viewport);
        _pivot = new Node3D();
        viewport.AddChild(_pivot);
        if (item.Model != null) _pivot.AddChild(item.Model.Instantiate<Node3D>());
        var camera = new Camera3D { Position = new Vector3(0, 0.5f, 2.3f), Current = true, Fov = 38 };
        viewport.AddChild(camera);
        camera.LookAt(Vector3.Zero);
        viewport.AddChild(new OmniLight3D { Position = new Vector3(0.4f, 1, 1), LightColor = item.LightColor,
            LightEnergy = item.LightEnergy, OmniRange = 5 });
        viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, -30, 0), LightEnergy = 0.65f });
        var name = new Label { Text = item.DisplayName, HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore, ClipText = true };
        name.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(name);
        var count = new Label { Text = $"×{slot.ItemCount}", HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore };
        count.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(count);
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
        return new Godot.Collections.Dictionary { ["source"] = Inventory.GetPath().ToString(), ["index"] = Index,
            ["revision"] = InventoryService.Instance.Revision };
    }
    public override bool _CanDropData(Vector2 position, Variant data) => Inventory != null && IsStackDrag(data);
    public static bool IsStackDrag(Variant data) => data.VariantType == Variant.Type.Dictionary &&
        data.AsGodotDictionary().ContainsKey("source") && data.AsGodotDictionary().ContainsKey("index") && data.AsGodotDictionary().ContainsKey("revision");
    public override void _DropData(Vector2 position, Variant data)
    {
        var drag = data.AsGodotDictionary();
        InventoryService.Instance.Send("move", Player, drag["source"].AsString(), drag["index"].AsInt32(), Inventory.GetPath(), Index, drag["revision"].AsInt32());
    }
}
