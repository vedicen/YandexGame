using Godot;

public abstract partial class Weapon : Equipment
{
    [Export] public PackedScene WeaponModel { get; set; }
    [Export] public Vector3 EquipPosition { get; set; }
    [Export] public Vector3 EquipRotationDegrees { get; set; }
    [Export] public Vector3 EquipScale { get; set; } = Vector3.One;
}
