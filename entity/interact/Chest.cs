using Godot;

public partial class Chest : interact_obj
{
    public InventoryComponent Inventory { get; private set; }
    public override void _Ready()
    {
        base._Ready();
        Inventory = GetNode<InventoryComponent>("InventoryComponent");
    }
    public override void interact(Player player)
    {
        if (InventoryService.Instance == null)
        {
            GD.PushError("Cannot open chest: InventoryService is unavailable.");
            return;
        }

        InventoryService.Instance.Send("open", player, GetPath());
    }
}
