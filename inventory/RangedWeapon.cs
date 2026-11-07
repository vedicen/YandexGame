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

    /// <summary>
    /// Поправка поворота модели стрелы (в градусах), если "нос" модели смотрит не по -Z.
    /// Например: модель смотрит по +Z -> (0, 180, 0); по +X -> (0, 90, 0); по -X -> (0, -90, 0).
    /// </summary>
    [Export]
    public Vector3 ProjectileRotationOffsetDegrees { get; set; } = Vector3.Zero;

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
        if (ProjectileScene == null || projectile?.Model == null)
        {
            GD.PushError(
                $"Ranged weapon '{DisplayName}' cannot spawn a projectile: " +
                $"weapon={ResourcePath}, scene={ProjectileScene?.ResourcePath ?? "<null>"}, " +
                $"projectile={projectile?.ResourcePath ?? "<null>"}, " +
                $"model={projectile?.Model?.ResourcePath ?? "<null>"}.");
            return null;
        }

        if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y) || !float.IsFinite(direction.Z) ||
            direction.LengthSquared() <= 0.0001f)
        {
            GD.PushError($"Ranged weapon '{DisplayName}': projectile direction is invalid ({direction}).");
            return null;
        }

        Node spawnParent = ResolveSpawnParent(attacker, parent);
        if (spawnParent == null)
        {
            GD.PushError(
                $"Ranged weapon '{DisplayName}': no valid parent found for projectile '{projectileName}' " +
                $"(passed parent={DescribeNode(parent)}, attacker={DescribeNode(attacker)}).");
            return null;
        }

        if (!ReferenceEquals(spawnParent, parent))
        {
            GD.Print($"[Ranged] Passed parent was invalid ({DescribeNode(parent)}); " +
                $"using fallback parent {spawnParent.GetPath()}.");
        }

        try
        {
            Node node = ProjectileScene.Instantiate();
            if (node is not WeaponProjectileScene instance)
            {
                GD.PushError(
                    $"Projectile scene '{ProjectileScene.ResourcePath}' root is '{node?.GetType().Name ?? "<null>"}', " +
                    "expected WeaponProjectileScene (на корне сцены должен висеть скрипт WeaponProjectileScene).");
                node?.QueueFree();
                return null;
            }

            Vector3 normalizedDirection = direction.Normalized();

            instance.Name = projectileName;
            instance.Initialize(attacker, projectile, normalizedDirection, ProjectileSpeed, damage);
            spawnParent.AddChild(instance, true);
            instance.GlobalTransform = BuildProjectileTransform(origin, normalizedDirection, instance.Scale);
            return instance;
        }
        catch (System.Exception e)
        {
            GD.PushError($"Ranged weapon '{DisplayName}': exception while spawning projectile '{projectileName}': {e}");
            return null;
        }
    }

    private Transform3D BuildProjectileTransform(Vector3 origin, Vector3 direction, Vector3 scale)
    {
        // Если летим строго вверх/вниз, "вверх" для LookingAt не может совпадать с направлением.
        Vector3 up = Mathf.Abs(direction.Dot(Vector3.Up)) > 0.999f ? Vector3.Forward : Vector3.Up;

        // LookingAt: ось -Z смотрит вдоль direction.
        Basis basis = Basis.LookingAt(direction, up);

        if (ProjectileRotationOffsetDegrees != Vector3.Zero)
        {
            Vector3 offset = new(
                Mathf.DegToRad(ProjectileRotationOffsetDegrees.X),
                Mathf.DegToRad(ProjectileRotationOffsetDegrees.Y),
                Mathf.DegToRad(ProjectileRotationOffsetDegrees.Z));
            basis *= Basis.FromEuler(offset);
        }

        return new Transform3D(basis.Scaled(scale), origin);
    }

    private static Node ResolveSpawnParent(AnimatedEntity attacker, Node requestedParent)
    {
        if (IsUsable(requestedParent))
            return requestedParent;

        if (!IsUsable(attacker))
            return null;

        SceneTree tree = attacker.GetTree();
        Node root = tree.Root;

        Node container = root.GetNodeOrNull("BaseScene/Projectiles");
        if (IsUsable(container))
            return container;

        Node baseScene = root.GetNodeOrNull("BaseScene");
        if (IsUsable(baseScene))
            return baseScene;

        if (IsUsable(tree.CurrentScene))
            return tree.CurrentScene;

        Node grandParent = attacker.GetParent()?.GetParent();
        if (IsUsable(grandParent))
            return grandParent;

        return root;
    }

    private static bool IsUsable(Node node) =>
        node != null && GodotObject.IsInstanceValid(node) && node.IsInsideTree();

    private static string DescribeNode(Node node)
    {
        if (node == null)
            return "<null>";
        if (!GodotObject.IsInstanceValid(node))
            return "<freed>";
        return node.IsInsideTree() ? node.GetPath().ToString() : $"{node.Name} (not in tree)";
    }
}