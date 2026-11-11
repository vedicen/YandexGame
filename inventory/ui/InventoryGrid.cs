using Godot;

public partial class InventoryGrid : VBoxContainer
{
    [Export] public PackedScene ItemScene { get; set; }
    [Export] public int Columns { get; set; } = 4;

    public string Title { get; set; }
    public InventoryComponent Inventory { get; set; }
    public Player Player { get; set; }
    public bool TradeMode { get; set; }

    public override void _Ready()
    {
        GetNode<Label>("Title").Text = $"{Title} • {Inventory.MaxSlots} слотов";

        GridContainer slots = GetNode<GridContainer>("Slots");
        slots.Columns = Columns;
        for (int i = 0; i < Inventory.Slots.Count; i++)
        {
            ItemUI item = ItemScene.Instantiate<ItemUI>();
            item.Bind(Player, Inventory, i, TradeMode);
            slots.AddChild(item);
        }
    }
}
