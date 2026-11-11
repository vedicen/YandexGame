using Godot;

[GlobalClass]
public partial class Hammer : HandItem
{
    [Export] public Godot.Collections.Array<BuildingRecipe> BuildingRecipes { get; set; } = new();
}
