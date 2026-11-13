using Godot;
using System;

/// <summary>Skeleton-specific dismemberment rules. Bone names come from the model's Skeleton3D.</summary>
public partial class EnemySkeleton : AnimatedEnemy
{
	[ExportGroup("Dismemberment Parts")]
	[Export] public PackedScene PartOfBodyArm { get; set; }
	[Export] public PackedScene PartOfBodyJaw { get; set; }
	[Export] public PackedScene PartOfBodySkull { get; set; }

	[ExportGroup("Dismemberment Chances")]
	[Export(PropertyHint.Range, "0,1,0.01")] public float ArmDetachChance { get; set; } = 0.30f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float JawDetachChance { get; set; } = 0.70f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float SkullDetachChance { get; set; } = 0.60f;

	private enum HeadStage { JawAttached, JawDetached, SkullDetached }
	private HeadStage _headStage;
	private bool _leftArmDetached;
	private bool _rightArmDetached;
	private long _lastHeadAttackId = -1;
	private long _lastLeftArmAttackId = -1;
	private long _lastRightArmAttackId = -1;
	private Node3D _modelRoot;
	private readonly RandomNumberGenerator _random = new();

	public override void _Ready()
	{
		base._Ready();
		_modelRoot = GetNodeOrNull<Node3D>("Model");
		_random.Randomize();
	}

	public override void OnBoneHit(string boneName, long attackId, Vector3 knockbackDirection)
	{
		if (string.IsNullOrWhiteSpace(boneName)) return;

		if (TryGetArmSide(boneName, out bool isLeft))
		{
			if (isLeft)
			{
				if (_lastLeftArmAttackId == attackId) return;
				_lastLeftArmAttackId = attackId;
				if (!_leftArmDetached && RollChance(ArmDetachChance))
				{
					_leftArmDetached = Detach("left_arm", PartOfBodyArm, knockbackDirection, mirror: false);
					if (_leftArmDetached)
					{
						DisableArmHitboxes(left: true);
						NotifyHandAvailabilityChanged();
					}
				}
			}
			else
			{
				if (_lastRightArmAttackId == attackId) return;
				_lastRightArmAttackId = attackId;
				if (!_rightArmDetached && RollChance(ArmDetachChance))
				{
					_rightArmDetached = Detach("right_arm", PartOfBodyArm, knockbackDirection, mirror: true);
					if (_rightArmDetached)
					{
						DisableArmHitboxes(left: false);
						NotifyHandAvailabilityChanged();
					}
				}
			}
			return;
		}

		if (_lastHeadAttackId == attackId) return;
		_lastHeadAttackId = attackId;
		switch (_headStage)
		{
			case HeadStage.JawAttached:
				if (RollChance(JawDetachChance) && Detach("teeth", PartOfBodyJaw, knockbackDirection))
					_headStage = HeadStage.JawDetached;
				break;
			case HeadStage.JawDetached:
				if (RollChance(SkullDetachChance) && Detach("skull", PartOfBodySkull, knockbackDirection))
					_headStage = HeadStage.SkullDetached;
				break;
		}
	}

	public override bool CanUseHandItem(HandItem item)
	{
		if (item == null)
			return false;

		return item.RequiredHand switch
		{
			HandItem.HandRequirement.BothAttachToLeft or HandItem.HandRequirement.BothAttachToRight =>
				!_leftArmDetached && !_rightArmDetached,
			HandItem.HandRequirement.Left => !_leftArmDetached,
			_ => !_rightArmDetached
		};
	}

	public override bool TryEquipWeaponFromInventory(int inventorySlot)
	{
		if (Inventory?.Slots == null || !Inventory.Valid(inventorySlot) ||
			inventorySlot < Inventory.FirstItemSlot || Inventory.Slots[inventorySlot]?.Item is not Weapon requestedWeapon)
			return false;

		bool equippedWeaponIsUsable = CanUseWeaponWithAvailableHands(EquippedWeapon);
		Rarity bestRarity = equippedWeaponIsUsable ? EquippedWeapon.Rarity : (Rarity)(-1);
		int bestSlot = -1;
		for (int i = Inventory.FirstItemSlot; i < Inventory.Slots.Count; i++)
		{
			if (Inventory.Slots[i]?.Item is not Weapon candidate ||
				!CanUseWeaponWithAvailableHands(candidate) ||
				(equippedWeaponIsUsable && candidate.Rarity <= EquippedWeapon.Rarity) ||
				candidate.Rarity <= bestRarity)
				continue;

			bestSlot = i;
			bestRarity = candidate.Rarity;
		}

		if (bestSlot >= 0)
			return base.TryEquipWeaponFromInventory(bestSlot);

		// EnemyInventoryManager treats a successful equip request as permission to consume a pickup.
		// Keep a weapon the skeleton cannot hold in its inventory instead of rejecting the pickup.
		return !CanUseHandItem(requestedWeapon);
	}

	private bool CanUseWeaponWithAvailableHands(Weapon weapon)
	{
		if (weapon?.Model == null || !CanUseHandItem(weapon))
			return false;

		if (weapon is MeleeWeapon)
			return Skills?.Contains(TaskData.Skills.MeleeCombat) == true;

		if (weapon is RangedWeapon ranged)
			return Skills?.Contains(TaskData.Skills.RangedCombat) == true &&
				HasProjectileInInventory(ranged.RequiredProjectile);

		return false;
	}

	private void NotifyHandAvailabilityChanged()
	{
		if (EquippedHandItem != null && !CanUseHandItem(EquippedHandItem))
		{
			Equipment?.EquipWeapon(null);
			RefreshWeaponAttackAnimations();
			BroadcastBowAnimationState("Idle");
		}

		Inventory?.NotifyChanged();
	}

	private bool RollChance(float chance)
	{
		if (chance <= 0f) return false;
		if (chance >= 1f) return true;
		return _random.Randf() < chance;
	}

	private bool Detach(string meshName, PackedScene partScene, Vector3 direction, bool mirror = false)
	{
		if (partScene == null)
		{
			GD.PushWarning($"{Name}: no part-of-body scene assigned for {meshName}.");
			return false;
		}

		MeshInstance3D sourceMesh = FindMesh(_modelRoot, meshName);
		if (sourceMesh == null)
		{
			GD.PushWarning($"{Name}: mesh '{meshName}' was not found in the model.");
			return false;
		}

		Vector3 spawnPosition = sourceMesh.GlobalPosition;
		sourceMesh.Visible = false;

		Node3D detachedPart = partScene.Instantiate<Node3D>();
		Node parent = GetTree().CurrentScene ?? GetParent();
		parent.AddChild(detachedPart);
		detachedPart.GlobalPosition = spawnPosition;
		if (mirror) detachedPart.Scale = new Vector3(-detachedPart.Scale.X, detachedPart.Scale.Y, detachedPart.Scale.Z);

		direction.Y = 0;
		direction = direction.LengthSquared() < 0.0001f ? GlobalBasis.Z : direction.Normalized();
		const float travelDistance = 2.2f;
		const float arcHeight = 0.9f;
		const float landingRise = 0.15f;
		Basis initialBasis = detachedPart.GlobalBasis;
		Vector3 rotationAxis = direction.Cross(Vector3.Up).Normalized();
		Tween flight = detachedPart.CreateTween();
		flight.TweenMethod(Callable.From<float>(progress =>
		{
			float verticalOffset = arcHeight * Mathf.Sin(Mathf.Pi * progress) + landingRise * progress;
			float dy = arcHeight * Mathf.Pi * Mathf.Cos(Mathf.Pi * progress) + landingRise;
			detachedPart.GlobalPosition = spawnPosition + direction * (travelDistance * progress) + Vector3.Up * verticalOffset;

			// Pitch the part to match the trajectory slope: angle = atan(dy / dx).
			float dx = travelDistance;
			float angle = Mathf.Atan2(dy, dx);
			detachedPart.GlobalBasis = new Basis(rotationAxis, angle) * initialBasis;
		}), 0f, 1f, 0.75);
		return true;
	}

	private static MeshInstance3D FindMesh(Node node, string exactName)
	{
		if (node == null) return null;
		if (node is MeshInstance3D mesh && mesh.Name.ToString().Equals(exactName, StringComparison.OrdinalIgnoreCase)) return mesh;
		foreach (Node child in node.GetChildren())
		{
			MeshInstance3D match = FindMesh(child, exactName);
			if (match != null) return match;
		}
		return null;
	}

	private void DisableArmHitboxes(bool left)
	{
		DisableArmHitboxesRecursive(_modelRoot, left);
	}

	private static void DisableArmHitboxesRecursive(Node node, bool left)
	{
		if (node == null) return;
		if (node is BoneHitbox hitbox && TryGetArmSide(hitbox.BoneName, out bool hitboxIsLeft) && hitboxIsLeft == left)
			hitbox.DisableHitbox();

		foreach (Node child in node.GetChildren())
			DisableArmHitboxesRecursive(child, left);
	}

	private static bool TryGetArmSide(string boneName, out bool isLeft)
	{
		string name = boneName.ToLowerInvariant();
		bool isArm = name.Contains("arm");
		isLeft = name.Contains("left") || name.Contains("_l") || name.EndsWith(".l");
		bool isRight = name.Contains("right") || name.Contains("_r") || name.EndsWith(".r");
		return isArm && (isLeft || isRight);
	}
}
