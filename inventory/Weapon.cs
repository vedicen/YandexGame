using Godot;

public abstract partial class Weapon : Equipment
{
    [Export(PropertyHint.Range, "0,10000,0.1")] public float Damage { get; set; } = 10.0f;
    [ExportGroup("Attack Animations")]
    [Export] public PackedScene Attack1AnimationScene { get; set; }
    [Export] public PackedScene Attack2AnimationScene { get; set; }
    [Export] public PackedScene WeaponModel { get; set; }
    [Export] public Vector3 EquipPosition { get; set; }
    [Export] public Vector3 EquipRotationDegrees { get; set; }
    [Export] public Vector3 EquipScale { get; set; } = Vector3.One;
}
