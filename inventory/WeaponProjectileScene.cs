using Godot;

public partial class WeaponProjectileScene : Area3D
{
    private AnimatedEntity _attacker;
    private WeaponProjectile _projectile;
    private Vector3 _direction;
    private float _speed;
    private float _damage;
    private float _lifetime;
    private bool _initialized;
    private bool _resolved;

    public void Initialize(
        AnimatedEntity attacker,
        WeaponProjectile projectile,
        Vector3 direction,
        float speed,
        float damage)
    {
        if (projectile?.Model == null)
        {
            GD.PushError("WeaponProjectileScene requires a projectile resource with a model.");
            return;
        }

        if (direction.LengthSquared() < 0.0001f ||
            !float.IsFinite(speed) || speed <= 0 ||
            !float.IsFinite(damage) || damage < 0)
        {
            GD.PushError("WeaponProjectileScene received invalid flight or damage values.");
            return;
        }

        _attacker = attacker;
        _projectile = projectile;
        _direction = direction.Normalized();
        _speed = speed;
        _damage = damage;

        Node3D model = projectile.Model.Instantiate<Node3D>();
        if (model == null)
        {
            GD.PushError($"Projectile model '{projectile.Model.ResourcePath}' is not a Node3D scene.");
            return;
        }

        AddChild(model);
        LookAt(GlobalPosition + _direction, Vector3.Up);
        _initialized = true;
    }

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = (1u << 0) | (1u << 1);
        Monitoring = true;
        Monitorable = false;
        AreaEntered += OnAreaEntered;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_initialized)
            return;

        Vector3 previousPosition = GlobalPosition;
        Vector3 nextPosition = previousPosition + _direction * _speed * (float)delta;
        if (IsServerAuthority())
        {
            var query = PhysicsRayQueryParameters3D.Create(
                previousPosition, nextPosition, (1u << 0) | (1u << 1));
            query.CollideWithAreas = true;
            query.CollideWithBodies = true;
            Godot.Collections.Dictionary result = GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (result.TryGetValue("collider", out Variant collider))
            {
                if (collider.AsGodotObject() is BoneHitbox hitbox)
                    ApplyHit(hitbox);
                else
                    RemoveFromPeers();
            }

            _lifetime += (float)delta;
            if (!_resolved && _lifetime >= 10.0f)
                RemoveFromPeers();
        }

        if (GodotObject.IsInstanceValid(this) && !IsQueuedForDeletion())
            GlobalPosition = nextPosition;
    }

    private void OnAreaEntered(Area3D area)
    {
        if (!_initialized || !IsServerAuthority() || area is not BoneHitbox hitbox)
            return;

        ApplyHit(hitbox);
    }

    private void ApplyHit(BoneHitbox hitbox)
    {
        if (_resolved)
            return;

        AnimatedEntity target = hitbox.Target;
        if (target == null || target == _attacker || target.Health == null ||
            target.Health.CurrentHealth <= 0)
            return;

        target.Health.TakeDamage(_damage);
        InventoryService.Instance?.Publish();
        RemoveFromPeers();
    }

    private void RemoveFromPeers()
    {
        if (_resolved)
            return;
        _resolved = true;
        if (Multiplayer.HasMultiplayerPeer())
            Rpc(nameof(RemoveProjectile), Name);
        else
            QueueFree();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RemoveProjectile(string projectileName)
    {
        if (Multiplayer.HasMultiplayerPeer())
        {
            long sender = Multiplayer.GetRemoteSenderId();
            if (sender != 0 && sender != 1)
                return;
        }

        if (Name == projectileName)
            QueueFree();
    }

    private bool IsServerAuthority() =>
        !Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer();
}
