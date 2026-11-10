using Godot;

[GlobalClass]
public partial class HandItem : Equipment
{
    [Export] public Vector3 EquipPosition { get; set; }
    [Export] public Vector3 EquipRotationDegrees { get; set; }
    [Export] public Vector3 EquipScale { get; set; } = Vector3.One;
}
