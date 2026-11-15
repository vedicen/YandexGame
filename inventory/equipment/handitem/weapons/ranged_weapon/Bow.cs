using Godot;

[GlobalClass]
public partial class Bow : RangedWeapon
{
    private const string HeldProjectileNodeName = "HeldProjectile";

    [ExportGroup("Held Projectile")]
    [Export] public Vector3 HeldProjectilePosition { get; set; } = Vector3.Zero;
    [Export] public Vector3 HeldProjectileRotationDegrees { get; set; } = Vector3.Zero;
    [Export] public Vector3 HeldProjectileScale { get; set; } = Vector3.One;

    public void UpdateHeldProjectile(AnimatedEntity owner, string bowState)
    {
        Node3D rightHand = owner?.Equipment?.GetRightHandAttachment();
        if (rightHand == null)
        {
            GD.PushError($"Bow '{DisplayName}' cannot update its held projectile: right-hand attachment is unavailable.");
            return;
        }

        Node3D heldProjectile = rightHand.GetNodeOrNull<Node3D>(HeldProjectileNodeName);
        if (bowState is not ("Pulling" or "Pulled"))
        {
            if (heldProjectile != null)
            {
                heldProjectile.Visible = false;
                rightHand.RemoveChild(heldProjectile);
                heldProjectile.QueueFree();
            }
            return;
        }

        if (heldProjectile != null)
            return;

        if (RequiredProjectile?.Model == null)
        {
            GD.PushError($"Bow '{DisplayName}' cannot show its held projectile: projectile model is not configured.");
            return;
        }

        Node projectileNode = RequiredProjectile.Model.Instantiate();
        if (projectileNode is not Node3D projectileModel)
        {
            GD.PushError(
                $"Bow '{DisplayName}' cannot show its held projectile: model root must be Node3D, " +
                $"got '{projectileNode?.GetType().Name ?? "<null>"}'.");
            projectileNode?.QueueFree();
            return;
        }

        projectileModel.Name = HeldProjectileNodeName;
        projectileModel.Position = HeldProjectilePosition;
        projectileModel.RotationDegrees = HeldProjectileRotationDegrees;
        projectileModel.Scale = HeldProjectileScale;
        rightHand.AddChild(projectileModel);
    }
}
