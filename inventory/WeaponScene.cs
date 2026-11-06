using Godot;

public partial class WeaponScene : Node3D
{
    [Export] public PackedScene ModelScene { get; set; }
    private WeaponDamageZone _damageZone;

    public override void _Ready()
    {
        _damageZone = GetNodeOrNull<WeaponDamageZone>("DamageZone");
        if (ModelScene == null)
        {
            GD.PushError($"WeaponScene '{Name}' requires a weapon model scene.");
            return;
        }

        AddChild(ModelScene.Instantiate<Node3D>());
    }

    public void ConfigureCombat(AnimatedEntity attacker, Weapon weapon)
    {
        _damageZone ??= GetNodeOrNull<WeaponDamageZone>("DamageZone");
        _damageZone?.Configure(attacker, weapon);
    }

    public void BeginSwing() => _damageZone?.BeginSwing();
    public void EndSwing() => _damageZone?.EndSwing();
}
