using Godot;

[GlobalClass]
public partial class Hammer : HandItem
{
    [Export] public PackedScene BuildAnimationScene { get; set; }
    [Export] public Godot.Collections.Array<BuildingRecipe> BuildingRecipes { get; set; } = new();
}
