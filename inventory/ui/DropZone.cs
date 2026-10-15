using Godot;

public partial class DropZone : Label
{
    public Player Player;
    public override bool _CanDropData(Vector2 position, Variant data) => ItemUI.IsStackDrag(data) &&
        data.AsGodotDictionary()["source"].AsString() == Player.Inventory.GetPath().ToString();
    public override void _DropData(Vector2 position, Variant data)
    {
        var drag = data.AsGodotDictionary();
        InventoryService.Instance.Send("drop", Player, drag["source"].AsString(), drag["index"].AsInt32(), revision: drag["revision"].AsInt32());
    }
}
