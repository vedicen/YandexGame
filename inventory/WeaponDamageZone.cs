using Godot;

public partial class WeaponDamageZone : Area3D
{
    private AnimatedEntity _attacker;
    private Weapon _weapon;
    private float _impulse;
    private bool _swingActive;

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 1u << 1;
        Monitoring = false;
        Monitorable = false;
        AreaEntered += OnAreaEntered;
        GD.Print($"[Combat] Damage zone ready: {GetPath()}, mask={CollisionMask}.");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_swingActive || _impulse <= 0 ||
            Multiplayer.HasMultiplayerPeer() && !Multiplayer.IsServer())
            return;

        var overlaps = GetOverlappingAreas();
        foreach (Area3D area in overlaps)
            TryHit(area);
    }

    public void Configure(AnimatedEntity attacker, Weapon weapon)
    {
        _attacker = attacker;
        _weapon = weapon;
    }

    public void BeginSwing()
    {
        _impulse = (_attacker?.CombatStats?.Strength ?? 0) - (_weapon?.Weight ?? 0);
        GD.Print($"[Combat] Swing started: attacker={_attacker?.Name ?? "<missing>"}, " +
            $"weapon={_weapon?.DisplayName ?? "<missing>"}, strength={_attacker?.CombatStats?.Strength ?? 0:0.##}, " +
            $"weight={_weapon?.Weight ?? 0:0.##}, impulse={_impulse:0.##}, server={IsServerAuthority()}.");
        if (_impulse <= 0 || _attacker == null || _weapon is not MeleeWeapon)
        {
            GD.Print("[Combat] Swing rejected: no positive impulse, attacker, or melee weapon.");
            SetActive(false);
            return;
        }

        _swingActive = true;
        SetActive(true);
        GD.Print($"[Combat] Damage zone enabled: {GetPath()}, mask={CollisionMask}.");
    }

    public void EndSwing()
    {
        if (_swingActive)
            GD.Print($"[Combat] Swing ended: remaining impulse={_impulse:0.##}.");
        _swingActive = false;
        _impulse = 0;
        SetActive(false);
    }

    private void OnAreaEntered(Area3D area) => TryHit(area);

    private void TryHit(Area3D area)
    {
        if (area is not BoneHitbox boneHitbox)
            return;

        if (!_swingActive || _impulse <= 0)
            return;
        if (!IsServerAuthority())
        {
            GD.Print($"[Combat] Overlap ignored on non-server: {boneHitbox.Target?.Name ?? "<unknown>"}/{boneHitbox.BoneName}.");
            return;
        }
        if (boneHitbox.Target == null || boneHitbox.Target == _attacker)
            return;

        GD.Print($"[Combat] Bone overlap: attacker={_attacker?.Name}, target={boneHitbox.Target.Name}, " +
            $"bone={boneHitbox.BoneName}, impulse={_impulse:0.##}.");
        HealthComponent health = boneHitbox.Target.Health;
        if (health == null || health.CurrentHealth <= 0)
        {
            GD.Print($"[Combat] Hit rejected: target has no health or is dead ({health?.CurrentHealth.ToString("0.##") ?? "no HealthComponent"}).");
            return;
        }

        health.TakeDamage(_weapon.Damage);
        _impulse -= boneHitbox.Target.CombatStats?.Constitution ?? 0;
        GD.Print($"[Combat] HIT: damage={_weapon.Damage:0.##}, targetHealth={health.CurrentHealth:0.##}, " +
            $"remainingImpulse={_impulse:0.##}.");
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
