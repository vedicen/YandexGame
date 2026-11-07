using Godot;

public partial class BoneHitbox : Area3D
{
    public AnimatedEntity Target { get; private set; }
    public string BoneName { get; private set; }

    public void Initialize(AnimatedEntity target, string boneName, float radius)
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
            Shape = new SphereShape3D { Radius = Mathf.Max(radius, 0.001f) }
        };
        AddChild(collision);
    }

    public static bool ShouldCreateForBone(string boneName)
    {
        string normalized = boneName.Replace("_", "").Replace(".", "")
            .Replace("-", "").Replace(" ", "").Replace(":", "")
            .ToLowerInvariant();

        if (normalized.Contains("finger") || normalized.Contains("thumb") ||
            normalized.Contains("hand") ||
            normalized.Contains("toe") || normalized.Contains("twist") ||
            normalized.Contains("helper"))
            return false;

        return normalized.Contains("head") || normalized.Contains("neck") ||
            normalized.Contains("spine") || normalized.Contains("chest") ||
            normalized.Contains("torso") || normalized.Contains("hips") ||
            normalized.Contains("pelvis") || normalized.Contains("shoulder") ||
            normalized.Contains("clavicle") || normalized.Contains("arm") ||
            normalized.Contains("elbow") ||
            normalized.Contains("thigh") || normalized.Contains("leg") ||
            normalized.Contains("calf") || normalized.Contains("shin") ||
            normalized.Contains("knee") || normalized.Contains("foot");
    }

}
