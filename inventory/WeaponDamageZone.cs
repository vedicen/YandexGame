
using Godot;

public partial class WeaponDamageZone : Area3D
{
    private static long _nextAttackId;
    private AnimatedEntity _attacker;
    private Weapon _weapon;
    private float _impulse;
    private bool _swingActive;
    private long _attackId;

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 1u << 1;

        Monitoring = false;
        Monitorable = false;

        AreaEntered += OnAreaEntered;

        GD.Print($"[Combat] Damage zone ready: {GetPath()}, mask={CollisionMask}.");
    }

    public void Configure(AnimatedEntity attacker, Weapon weapon)
    {
        _attacker = attacker;
        _weapon = weapon;
    }

    public void BeginSwing()
    {
        _swingActive = false;
        _impulse = 0;

        if (!IsServerAuthority())
            return;

        if (_attacker == null || _weapon is not MeleeWeapon)
        {
            GD.Print("[Combat] Swing rejected: missing attacker or melee weapon.");
            SetActive(false);
            return;
        }

        _impulse = _attacker.CombatStats.Strength - _weapon.Weight;

        if (_impulse <= 0)
        {
            GD.Print("[Combat] Swing rejected: non-positive impulse.");
            SetActive(false);
            return;
        }

        _swingActive = true;
        _attackId = ++_nextAttackId;
        SetActive(true);

        GD.Print($"[Combat] Swing started: attacker={_attacker.Name}, " +
            $"weapon={_weapon.DisplayName}, impulse={_impulse:0.##}.");
    }

    public void EndSwing()
    {
        if (_swingActive)
            GD.Print($"[Combat] Swing ended: remaining impulse={_impulse:0.##}.");

        _swingActive = false;
        _impulse = 0;

        SetActive(false);
    }

    private void OnAreaEntered(Area3D area)
    {
        if (!_swingActive || _impulse <= 0)
            return;

        if (!IsServerAuthority())
            return;

        if (area is not BoneHitbox boneHitbox)
            return;

        AnimatedEntity target = boneHitbox.Target;

        if (target == null || target == _attacker)
            return;

        HealthComponent health = target.Health;

        if (health == null || health.CurrentHealth <= 0)
            return;

        float damage = _weapon.Damage;

        health.TakeDamage(damage);
        Vector3 knockbackDirection = target.GlobalPosition - _attacker.GlobalPosition;
        knockbackDirection.Y = 0;
        if (knockbackDirection.LengthSquared() < 0.0001f)
            knockbackDirection = target.GlobalBasis.Z;
        target.OnBoneHit(boneHitbox.BoneName, _attackId, knockbackDirection.Normalized());

        _impulse -= target.CombatStats?.Constitution ?? 0;

        GD.Print($"[Combat] HIT: attacker={_attacker.Name}, " +
            $"target={target.Name}, bone={boneHitbox.BoneName}, " +
            $"damage={damage:0.##}, remainingImpulse={_impulse:0.##}.");

        InventoryService.Instance?.Publish();

        if (_impulse <= 0)
        {
            _swingActive = false;
            SetActive(false);

            GD.Print("[Combat] Damage zone disabled: impulse depleted.");
        }
    }

    private bool IsServerAuthority() =>
        !Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer();

    private void SetActive(bool active)
    {
        SetDeferred("monitoring", active);
        SetDeferred("monitorable", active);
    }
}
