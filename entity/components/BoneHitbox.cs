using Godot;

public partial class BoneHitbox : Area3D
{
    public AnimatedEntity Target { get; private set; }
    public string BoneName { get; private set; }

    public void Initialize(AnimatedEntity target, string boneName)
    {
        Target = target;
        BoneName = boneName;
        CollisionLayer = 1u << 1;
        CollisionMask = 0;
        Monitoring = false;
        Monitorable = true;

        var collision = new CollisionShape3D
        {
            Name = "CollisionShape3D",
            Shape = new SphereShape3D { Radius = GetRadius(boneName) }
        };
        AddChild(collision);
    }

    private static float GetRadius(string boneName)
    {
        if (boneName.Contains("head", System.StringComparison.OrdinalIgnoreCase))
            return 0.208f;
        if (boneName.Contains("hand", System.StringComparison.OrdinalIgnoreCase) ||
            boneName.Contains("foot", System.StringComparison.OrdinalIgnoreCase))
            return 0.13f;
        return 0.169f;
    }
}
