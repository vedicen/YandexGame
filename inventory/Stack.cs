using Godot;

[GlobalClass]
public partial class Stack : Resource
{
    [Export] public int ItemCount { get; set; } = 1;
    [Export] public Item Item { get; set; }
    public Stack Copy() => new() { Item = Item, ItemCount = ItemCount };
}
