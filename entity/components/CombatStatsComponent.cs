using Godot;

public partial class CombatStatsComponent : Node
{
    [Export(PropertyHint.Range, "0,10000,0.1")] public float Strength { get; set; } = 10.0f;
    [Export(PropertyHint.Range, "0,10000,0.1")] public float Constitution { get; set; } = 2.0f;
}
