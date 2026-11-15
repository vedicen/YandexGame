using Godot;

public abstract partial class MeleeWeapon : Weapon
{
    [ExportGroup("Attack Animations")]
    [Export] public PackedScene Attack1AnimationScene { get; set; }
    [Export] public PackedScene Attack2AnimationScene { get; set; }

}
