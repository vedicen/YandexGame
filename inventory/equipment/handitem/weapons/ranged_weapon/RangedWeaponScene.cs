using Godot;

public partial class RangedWeaponScene : Node3D
{
    [Export] public NodePath ProjectileSpawnPath { get; set; } = new("ProjectileSpawn");

    public Marker3D ProjectileSpawn => GetNodeOrNull<Marker3D>(ProjectileSpawnPath);
}
