using Godot;

public partial class Chest : interact_obj
{
    public InventoryComponent Inventory { get; private set; }
    public override void _Ready()
    {
        base._Ready();
        Inventory = GetNode<InventoryComponent>("InventoryComponent");
    }
    public override void interact(Player player) => InventoryService.Instance?.Send("open", player, GetPath());
}
