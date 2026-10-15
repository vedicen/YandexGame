using Godot;

public partial class InventoryUI : CanvasLayer
{
    public Player Player;
    private Control _root;
    private HBoxContainer _grids;
    private Chest _chest;
    private bool _dirty;
    public override void _Ready()
    {
        Layer = 10;
        _root = new PanelContainer();
        _root.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.035f, 0.05f, 0.08f, 0.96f) });
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 18);
        _root.AddChild(margin);
        var box = new VBoxContainer();
        margin.AddChild(box);
        box.AddChild(new Label { Text = "ИНВЕНТАРЬ    •    ЛКМ: перенос    •    ПКМ: использовать    •    Tab / Esc: закрыть" });
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        box.AddChild(scroll);
        _grids = new HBoxContainer();
        _grids.AddThemeConstantOverride("separation", 24);
        scroll.AddChild(_grids);
        box.AddChild(new DropZone { Player = Player, Text = "↓  Перетащите сюда стак из рюкзака, чтобы выбросить",
            CustomMinimumSize = new Vector2(0, 56), MouseFilter = Control.MouseFilterEnum.Stop,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Text = "Закрыть" };
        close.Pressed += Player.CloseInventory;
        box.AddChild(close);
        Player.Inventory.Changed += MarkDirty;
        _root.Hide();
    }
    private void MarkDirty() => _dirty = true;
    public void Open(Chest chest)
    {
        if (_chest != null) _chest.Inventory.Changed -= MarkDirty;
        _chest = chest;
        if (_chest != null) _chest.Inventory.Changed += MarkDirty;
        _root.Show();
        MarkDirty();
    }
    public void Close()
    {
        if (_chest != null && GodotObject.IsInstanceValid(_chest)) _chest.Inventory.Changed -= MarkDirty;
        _chest = null;
        _root.Hide();
    }
    public override void _ExitTree()
    {
        Player.Inventory.Changed -= MarkDirty;
        if (GodotObject.IsInstanceValid(_chest)) _chest.Inventory.Changed -= MarkDirty;
    }
    public override void _Process(double delta)
    {
        if (!_dirty || !_root.Visible || GetViewport().GuiIsDragging()) return;
        _dirty = false;
        foreach (var child in _grids.GetChildren()) { _grids.RemoveChild(child); child.QueueFree(); }
        AddGrid("Рюкзак", Player.Inventory);
        if (_chest != null) AddGrid("Сундук", _chest.Inventory);
        var equipment = new VBoxContainer();
        equipment.AddChild(new Label { Text = "Экипировка\n(заготовка)" });
        equipment.AddChild(new ItemUI());
        _grids.AddChild(equipment);
    }
    private void AddGrid(string title, InventoryComponent inventory)
    {
        var column = new VBoxContainer();
        _grids.AddChild(column);
        column.AddChild(new Label { Text = $"{title} • {inventory.MaxSlots} слотов" });
        var grid = new GridContainer { Columns = 4 };
        column.AddChild(grid);
        for (int i = 0; i < inventory.Slots.Count; i++) grid.AddChild(new ItemUI { Player = Player, Inventory = inventory, Index = i });
    }
}
