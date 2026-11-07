using Godot;

[GlobalClass]
public partial class RangedWeapon : Weapon
{
    [Export(PropertyHint.Range, "0.01,1000,0.1")]
    public float ProjectileSpeed { get; set; } = 25.0f;

    [Export]
    public WeaponProjectile RequiredProjectile { get; set; }

    [Export]
    public PackedScene ProjectileScene { get; set; }

    [Export(PropertyHint.Range, "0,60,0.01")]
    public float MinChargeTime { get; set; } = 0.2f;

    [Export(PropertyHint.Range, "0.01,60,0.01")]
    public float MaxChargeTime { get; set; } = 1.5f;

    public bool HasValidConfiguration =>
        RequiredProjectile != null &&
        ProjectileScene != null &&
        float.IsFinite(ProjectileSpeed) &&
        ProjectileSpeed > 0 &&
        float.IsFinite(MinChargeTime) &&
        float.IsFinite(MaxChargeTime) &&
        MinChargeTime >= 0 &&
        MaxChargeTime > MinChargeTime;

    public WeaponProjectileScene SpawnProjectile(
        AnimatedEntity attacker,
        Node parent,
        string projectileName,
        WeaponProjectile projectile,
        Vector3 origin,
        Vector3 direction,
        float damage)
    {
        if (ProjectileScene == null || projectile?.Model == null || parent == null)
        {
            GD.PushError(
                $"Ranged weapon '{DisplayName}' cannot spawn a projectile: " +
                $"weapon={ResourcePath}, scene={ProjectileScene?.ResourcePath ?? "<null>"}, " +
                $"projectile={projectile?.ResourcePath ?? "<null>"}, " +
                $"model={projectile?.Model?.ResourcePath ?? "<null>"}, parent={parent?.GetPath().ToString() ?? "<null>"}.");
            return null;
        }

        WeaponProjectileScene instance = ProjectileScene.Instantiate<WeaponProjectileScene>();
        if (instance == null)
        {
            GD.PushError($"Projectile scene '{ProjectileScene.ResourcePath}' has an invalid root.");
            return null;
        }

        instance.Name = projectileName;
        instance.Initialize(attacker, projectile, direction, ProjectileSpeed, damage);
        parent.AddChild(instance);
        instance.GlobalPosition = origin;
        return instance;
    }
}
