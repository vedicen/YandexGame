using Godot;

public partial class AnimatedEntity : Entity
{
	private void OnEquipmentChanged()
	{
		Stack weaponStack = _equipmentComponent?.Valid(0) == true
			? _equipmentComponent.Slots[0]
			: null;
		if (weaponStack == null)
		{
			if (EquippedHandItem != null)
			{
				EnsureEquipmentComponent().EquipWeapon(null);
				RefreshWeaponAttackAnimations();
			}
			return;
		}

		if (weaponStack.Item is not HandItem handItem)
		{
			GD.PushError($"AnimatedEntity '{Name}' equipment slot only accepts HandItem items.");
			return;
		}

		if (ReferenceEquals(EquippedHandItem, handItem) ||
			(!string.IsNullOrEmpty(EquippedHandItem?.ResourcePath) &&
			 EquippedHandItem.ResourcePath == handItem.ResourcePath))
			return;

		EnsureEquipmentComponent().EquipHandItem(handItem);
		RefreshWeaponAttackAnimations();
	}

	private static bool IsSameWeaponResource(Weapon current, Weapon next) =>
		ReferenceEquals(current, next) ||
		(current != null && next != null &&
			!string.IsNullOrEmpty(current.ResourcePath) &&
			current.ResourcePath == next.ResourcePath);

	private void RefreshWeaponAttackAnimations()
	{
		if (_runtimeAnimationLibrary == null)
			return;

		MeleeWeapon meleeWeapon = EquippedWeapon as MeleeWeapon;
		ReplaceWeaponAttackAnimation("Attack1", Attack1AnimationScene, meleeWeapon?.Attack1AnimationScene);
		ReplaceWeaponAttackAnimation("Attack2", Attack2AnimationScene, meleeWeapon?.Attack2AnimationScene);
		AddRangedWeaponAnimations(_runtimeAnimationLibrary, EquippedWeapon as RangedWeapon);
		if (_runtimeAnimationLibrary.HasAnimation("ItemAction"))
			_runtimeAnimationLibrary.RemoveAnimation("ItemAction");
		AddUpperBodyAnimation(_runtimeAnimationLibrary, EquippedHandItem?.ActionAnimationScene, "ItemAction");

		if (_animationTree != null)
		{
			BuildAnimationTree();
			ApplyAnimation(_replicatedAnimation);
			if (_replicatedBowState != "Idle")
				ApplyBowState(_replicatedBowState);
		}
	}

	public bool HasProjectileInInventory(WeaponProjectile requiredProjectile)
	{
		if (requiredProjectile == null || Inventory?.Slots == null)
			return false;

		foreach (Stack stack in Inventory.Slots)
			if (stack?.Item is WeaponProjectile projectile &&
				(projectile == requiredProjectile || projectile.ResourcePath == requiredProjectile.ResourcePath) &&
				stack.ItemCount > 0)
				return true;

		return false;
	}

	public bool TryConsumeProjectileFromInventory(WeaponProjectile requiredProjectile)
	{
		if (requiredProjectile == null || Inventory?.Slots == null)
			return false;

		for (int i = 0; i < Inventory.Slots.Count; i++)
		{
			Stack stack = Inventory.Slots[i];
			if (stack?.Item is not WeaponProjectile projectile ||
				(projectile != requiredProjectile && projectile.ResourcePath != requiredProjectile.ResourcePath) ||
				stack.ItemCount <= 0)
				continue;

			stack.ItemCount--;
			if (stack.ItemCount == 0)
				Inventory.Slots[i] = null;
			Inventory.NotifyChanged();
			return true;
		}

		return false;
	}

	public bool TryFireBowAt(Vector3 targetPosition, float chargeTime, Vector3? flightDirection = null)
	{
		if (Multiplayer.HasMultiplayerPeer() && !Multiplayer.IsServer())
			return false;

		if (EquippedWeapon is not RangedWeapon bow || !bow.HasValidConfiguration)
		{
			GD.PushError($"AnimatedEntity '{Name}' cannot fire: no valid ranged weapon is equipped.");
			return false;
		}

		if (!float.IsFinite(chargeTime) || chargeTime < bow.MinChargeTime)
		{
			GD.PushWarning($"AnimatedEntity '{Name}' cannot fire: charge time {chargeTime} is below the weapon minimum.");
			return false;
		}

		Vector3 originDirection = (targetPosition - GlobalPosition).Normalized();
		WeaponScene bowScene = EquippedWeaponScene;
		Vector3 origin = bowScene != null
			? bowScene.GlobalPosition + originDirection * 0.25f
			: GlobalPosition + Vector3.Up * 1.35f + originDirection * 0.8f;
		Vector3 direction = flightDirection ?? targetPosition - origin;
		if (direction.LengthSquared() <= 0.0001f)
		{
			GD.PushError($"AnimatedEntity '{Name}' cannot fire: target position is too close to the projectile origin.");
			return false;
		}

		if (!TryConsumeProjectileFromInventory(bow.RequiredProjectile))
			return false;

		float clampedCharge = Mathf.Clamp(chargeTime, bow.MinChargeTime, bow.MaxChargeTime);
		float chargePercent = Mathf.Clamp(
			(clampedCharge - bow.MinChargeTime) / (bow.MaxChargeTime - bow.MinChargeTime),
			0.0f,
			1.0f);
		float damage = bow.Damage * (0.5f + 0.5f * chargePercent);
		string projectileName = $"{Name}_Arrow_{Time.GetTicksMsec()}";

		BroadcastBowAnimationState("Shooting");
		InventoryService.Instance?.Publish();
		if (Multiplayer.HasMultiplayerPeer())
		{
			Rpc(nameof(SpawnNpcBowProjectile), projectileName, bow.ResourcePath,
				bow.RequiredProjectile.ResourcePath, origin, direction.Normalized(), damage);
			return true;
		}

		WeaponProjectileScene instance = bow.SpawnProjectile(
			this, GetTree().CurrentScene, projectileName, bow.RequiredProjectile,
			origin, direction.Normalized(), damage);
		if (instance == null)
		{
			GD.PushError($"AnimatedEntity '{Name}' failed to spawn projectile '{projectileName}'.");
			return false;
		}

		return true;
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SpawnNpcBowProjectile(
		string projectileName,
		string weaponResourcePath,
		string projectileResourcePath,
		Vector3 origin,
		Vector3 direction,
		float damage)
	{
		if (Multiplayer.HasMultiplayerPeer())
		{
			long sender = Multiplayer.GetRemoteSenderId();
			if (sender != 0 && sender != 1)
				return;
		}

		RangedWeapon bow = GD.Load<RangedWeapon>(weaponResourcePath);
		WeaponProjectile projectile = GD.Load<WeaponProjectile>(projectileResourcePath);
		if (bow == null || projectile == null)
		{
			GD.PushError($"AnimatedEntity '{Name}' failed to load projectile resources for '{projectileName}'.");
			return;
		}

		WeaponProjectileScene instance = bow.SpawnProjectile(
			this, GetTree().CurrentScene, projectileName, projectile, origin, direction, damage);
		if (instance == null)
			GD.PushError($"AnimatedEntity '{Name}' failed to spawn replicated projectile '{projectileName}'.");
	}

	public bool TryEquipWeaponFromInventory(int inventorySlot)
	{
		return Inventory?.Valid(inventorySlot) == true &&
			Inventory.Slots[inventorySlot]?.Item is Weapon &&
			TryEquipHandItemFromInventory(inventorySlot);
	}

	public bool TryEquipHandItemFromInventory(int inventorySlot)
	{
		if (Inventory == null || _equipmentComponent?.Valid(0) != true ||
			!Inventory.Valid(inventorySlot) || Inventory.Slots[inventorySlot]?.Item is not HandItem handItem)
			return false;

		PackedScene model = handItem.Model;
		if (model == null)
		{
			GD.PushError($"AnimatedEntity '{Name}' cannot equip hand item '{handItem.DisplayName}' without a model.");
			return false;
		}

		return Inventory.Transfer(inventorySlot, _equipmentComponent, 0);
	}

	public bool TryStowEquippedRangedWeaponWithoutAmmo()
	{
		if (EquippedWeapon is not RangedWeapon bow ||
			HasProjectileInInventory(bow.RequiredProjectile) ||
			Inventory == null || _equipmentComponent?.Valid(0) != true ||
			!Inventory.CanAdd(bow, 1))
			return false;

		if (Inventory.Add(bow, 1) != 0)
			return false;
		_equipmentComponent.Slots[0] = null;
		_equipmentComponent.NotifyChanged();
		return true;
	}

	public void TryAutoEquipRangedWeaponWithAmmo()
	{
		if (Inventory?.Slots == null)
			return;

		Weapon heldWeapon = EquippedWeapon;

		for (int i = 0; i < Inventory.Slots.Count; i++)
		{
			if (Inventory.Slots[i]?.Item is not RangedWeapon bow ||
				bow.Model == null ||
				!HasProjectileInInventory(bow.RequiredProjectile) ||
				(heldWeapon != null && bow.Rarity <= heldWeapon.Rarity))
				continue;

			if (TryEquipWeaponFromInventory(i))
				return;
		}
	}

	public bool IsWeaponUpgrade(Weapon weapon) =>
		weapon != null && (EquippedWeapon == null || weapon.Rarity > EquippedWeapon.Rarity);

	private EquipmentComponent EnsureEquipmentComponent()
	{
		_equipmentComponent ??= GetNodeOrNull<EquipmentComponent>("EquipmentComponent");
		if (_equipmentComponent != null)
			return _equipmentComponent;

		_equipmentComponent = new EquipmentComponent { Name = "EquipmentComponent" };
		AddChild(_equipmentComponent);
		return _equipmentComponent;
	}

	private void EquipDefaultFootwear()
	{
		if (_equipmentComponent == null)
		{
			GD.PushWarning("AnimatedEntity: EquipmentComponent child not found; default footwear was not equipped.");
			return;
		}

		PackedScene footwearScene = GD.Load<PackedScene>("res://s.tscn");
		if (footwearScene == null)
		{
			GD.PushError("AnimatedEntity: Could not load footwear scene at res://s.tscn.");
			return;
		}

		GD.Print($"AnimatedEntity: Requesting default footwear for {Name}.");
		_equipmentComponent.Equip(EquipmentComponent.Slot.Feet, footwearScene);
	}

}
