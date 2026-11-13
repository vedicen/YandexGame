using Godot;

[GlobalClass]
public partial class HandItem : Equipment
{
    public enum HandRequirement
    {
        BothAttachToLeft,
        BothAttachToRight,
        Right,
        Left
    }

    [Export] public HandRequirement RequiredHand { get; set; } = HandRequirement.Right;

    [ExportGroup("Action Animation")]
    [Export] public PackedScene ActionAnimationScene { get; set; }
    [Export] public Vector3 EquipPosition { get; set; }
    [Export] public Vector3 EquipRotationDegrees { get; set; }
    [Export] public Vector3 EquipScale { get; set; } = Vector3.One;
}
