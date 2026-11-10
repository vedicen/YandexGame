using Godot;

[GlobalClass]
public partial class BuildingRecipe : Resource
{
    [Export] public string DisplayName { get; set; } = "Строение";
    [Export] public PackedScene BuildingScene { get; set; }
    [Export] public Godot.Collections.Array<Stack> Costs { get; set; } = new();
}
