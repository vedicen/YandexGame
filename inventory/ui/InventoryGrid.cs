using Godot;

public partial class InventoryGrid : VBoxContainer
{
    [Export] public PackedScene ItemScene { get; set; }
    [Export] public int Columns { get; set; } = 4;

    public string Title { get; set; }
    public InventoryComponent Inventory { get; set; }
    public int FirstSlotIndex { get; set; }
    public Player Player { get; set; }
    public bool TradeMode { get; set; }
    private bool _slotsDirty;

    public override void _Ready()
    {
        GetNode<Label>("Title").Text = $"{Title} • {Inventory.Slots.Count - FirstSlotIndex} слотов";

        GridContainer slots = GetNode<GridContainer>("Slots");
        slots.Columns = Columns;
        for (int i = FirstSlotIndex; i < Inventory.Slots.Count; i++)
        {
            ItemUI item = ItemScene.Instantiate<ItemUI>();
            item.Bind(Player, Inventory, i, TradeMode);
            slots.AddChild(item);
        }
        Inventory.Changed += OnInventoryChanged;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Inventory))
            Inventory.Changed -= OnInventoryChanged;
    }

    private void OnInventoryChanged() => _slotsDirty = true;

    public override void _Process(double delta)
    {
        if (!_slotsDirty || GetViewport().GuiIsDragging()) return;
        _slotsDirty = false;
        foreach (Node child in GetNode<GridContainer>("Slots").GetChildren())
            if (child is ItemUI item)
                item.RefreshDisplay();
    }
}
