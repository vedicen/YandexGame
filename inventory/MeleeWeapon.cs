using Godot;

public abstract partial class MeleeWeapon : Weapon
{
    [Export] public Shader TrailShader { get; set; }
}
