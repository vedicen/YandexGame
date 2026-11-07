
using Godot;

public partial class WeaponScene : Node3D
{
    [Export] public PackedScene ModelScene { get; set; }

    [Export] public NodePath BladeBasePath { get; set; }
        = new("BladeBase");

    [Export] public NodePath BladeTipPath { get; set; }
        = new("BladeTip");

    private WeaponDamageZone _damageZone;
    private SwordTrail _trail;

    public override void _Ready()
    {
        _damageZone = GetNodeOrNull<WeaponDamageZone>("DamageZone");
        _trail = GetNodeOrNull<SwordTrail>("SlashTrail");

        if (ModelScene == null)
        {
            GD.PushError(
                $"WeaponScene '{Name}' requires a weapon model scene."
            );
            return;
        }

        Node3D model = ModelScene.Instantiate<Node3D>();
        model.Name = "Model";
        AddChild(model);

        Node3D bladeBase = model.GetNodeOrNull<Node3D>(
            BladeBasePath
        );

        Node3D bladeTip = model.GetNodeOrNull<Node3D>(
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
        BeginSwing();
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
    GD.Print("[WeaponScene] 1. BeginSwing");

    GD.Print($"[WeaponScene] 2. Trail null: {_trail == null}");

    if (_trail != null)
    {
        GD.Print("[WeaponScene] 3. Calling BeginTrail");
        _trail.BeginTrail();
        GD.Print("[WeaponScene] 4. BeginTrail returned");
    }
    else
    {
        GD.PushError("[WeaponScene] Trail is NULL!");
    }

    _damageZone?.BeginSwing();
}

public void EndSwing()
{
    GD.Print($"[WeaponScene] EndSwing: {Name}");

    _damageZone?.EndSwing();
    _trail?.EndTrail();
}
}
