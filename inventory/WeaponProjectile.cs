using Godot;

[GlobalClass]
public partial class WeaponProjectile : Item
{
    [Export]
    public Shader TrailShader { get; set; }
}
