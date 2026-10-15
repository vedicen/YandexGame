using Godot;

public partial class PickUpObj : interact_obj
{
    [Export] public Stack Slot { get; set; }
    public override void _Ready()
    {
        base._Ready();
        AddToGroup("pickups");
        Slot = Slot?.Copy();
        if (Slot?.Item?.Model != null)
        {
            AddChild(Slot.Item.Model.Instantiate<Node3D>());
            AddChild(new OmniLight3D { LightColor = Slot.Item.LightColor,
                LightEnergy = Slot.Item.LightEnergy, OmniRange = 2, Position = new Vector3(0, 0.6f, 0) });
        }
    }
    public override void interact(Player player) => InventoryService.Instance?.Send("pickup", player, GetPath());
}
