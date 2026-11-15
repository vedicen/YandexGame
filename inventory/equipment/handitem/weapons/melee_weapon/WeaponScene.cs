
using Godot;

public partial class WeaponScene : Node3D
{
    [Export] public NodePath TrailPath { get; set; } = new("SlashTrail");

    [Export] public NodePath BladeBasePath { get; set; }
        = new("Model/BladeBase");

    [Export] public NodePath BladeTipPath { get; set; }
        = new("Model/BladeTip");

    private WeaponDamageZone _damageZone;
    private SwordTrail _trail;

    public override void _Ready()
    {
        _damageZone = GetNodeOrNull<WeaponDamageZone>("DamageZone");
        _trail = GetNodeOrNull<SwordTrail>(TrailPath);

        Node3D bladeBase = GetNodeOrNull<Node3D>(
            BladeBasePath
        );

        Node3D bladeTip = GetNodeOrNull<Node3D>(
            BladeTipPath
        );

        if (bladeBase == null || bladeTip == null)
        {
            GD.PushError(
                $"WeaponScene '{Name}': BladeBase or BladeTip not found."
            );
            return;
        }

        if (_trail == null)
        {
            GD.PushError(
                $"WeaponScene '{Name}' requires a SwordTrail node."
            );
            return;
        }
        _trail.Configure(bladeBase, bladeTip);
    }

    public void ConfigureCombat(
        AnimatedEntity attacker,
        Weapon weapon
    )
    {
        _damageZone ??= GetNodeOrNull<WeaponDamageZone>("DamageZone");
        _damageZone?.Configure(attacker, weapon);
    }

    public void BeginSwing()
    {
        _trail?.BeginTrail();
        _damageZone?.BeginSwing();
    }

    public void EndSwing()
    {
        _damageZone?.EndSwing();
        _trail?.EndTrail();
    }
}
