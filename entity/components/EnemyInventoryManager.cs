using Godot;

public partial class EnemyInventoryManager : Node
{
    private AnimatedEnemy _owner;
    private bool _initialized;
    private bool _reevaluationQueued;

    public override void _Ready()
    {
        _owner = GetParent() as AnimatedEnemy;
        if (_owner == null)
        {
            GD.PushError($"EnemyInventoryManager '{GetPath()}' must be a child of AnimatedEnemy.");
            return;
        }

        Callable.From(Initialize).CallDeferred();
    }

    public override void _ExitTree()
    {
        if (!_initialized)
            return;

        _owner.Inventory.Changed -= QueueReevaluation;
        _owner.Equipment.Changed -= QueueReevaluation;
    }

    private void Initialize()
    {
        if (_owner.Inventory == null || _owner.Equipment == null)
        {
            GD.PushError($"EnemyInventoryManager '{GetPath()}' requires the owner's inventory and equipment.");
            return;
        }

        _owner.Inventory.Changed += QueueReevaluation;
        _owner.Equipment.Changed += QueueReevaluation;
        _initialized = true;
        ReevaluateEquipment();
    }

    public bool TryHandleWeaponPickup(PickUpObj pickup)
    {
        if (!_initialized || pickup?.Slot?.Item is not Weapon weapon || pickup.Slot.ItemCount <= 0)
            return false;

        InventoryComponent inventory = _owner.Inventory;
        Weapon equipped = _owner.EquippedWeapon;
        bool canEquip = CanUseWeapon(weapon);
        bool shouldEquip = canEquip &&
            (equipped == null || !CanUseWeapon(equipped) || weapon.Rarity > equipped.Rarity);

        int freeSlot = FindFreeInventorySlot(inventory);
        if (shouldEquip && freeSlot >= 0)
        {
            inventory.Slots[freeSlot] = new Stack { Item = weapon, ItemCount = 1 };
            inventory.NotifyChanged();

            if (!_owner.TryEquipWeaponFromInventory(freeSlot))
            {
                inventory.Slots[freeSlot] = null;
                inventory.NotifyChanged();
                GD.PushError($"EnemyInventoryManager: failed to equip picked-up weapon '{weapon.DisplayName}' for '{_owner.Name}'.");
                return false;
            }

            pickup.Slot.ItemCount--;
            return true;
        }

        if (freeSlot < 0)
            return false;

        inventory.Slots[freeSlot] = new Stack { Item = weapon, ItemCount = 1 };
        inventory.NotifyChanged();
        pickup.Slot.ItemCount--;
        return true;
    }

    private void QueueReevaluation()
    {
        if (!_initialized || _reevaluationQueued)
            return;

        _reevaluationQueued = true;
        Callable.From(ReevaluateEquipment).CallDeferred();
    }

    private void ReevaluateEquipment()
    {
        _reevaluationQueued = false;
        if (!_initialized || !GodotObject.IsInstanceValid(_owner) || _owner.IsQueuedForDeletion())
            return;

        Weapon equipped = _owner.EquippedWeapon;
        if (!HasCombatSkill())
        {
            if (equipped != null)
                TryStowEquippedWeapon();
            return;
        }

        bool equippedIsUsable = CanUseWeapon(equipped);
        int bestSlot = -1;
        Rarity bestRarity = equippedIsUsable ? equipped.Rarity : (Rarity)(-1);

        for (int i = 0; i < _owner.Inventory.Slots.Count; i++)
        {
            if (_owner.Inventory.Slots[i]?.Item is not Weapon candidate ||
                !CanUseWeapon(candidate) ||
                (equippedIsUsable && candidate.Rarity <= equipped.Rarity) ||
                candidate.Rarity <= bestRarity)
                continue;

            bestSlot = i;
            bestRarity = candidate.Rarity;
        }

        if (bestSlot >= 0)
        {
            if (!_owner.TryEquipWeaponFromInventory(bestSlot))
                GD.PushError($"EnemyInventoryManager: failed to equip inventory weapon for '{_owner.Name}'.");
            return;
        }

        if (equipped != null && !equippedIsUsable)
            TryStowEquippedWeapon();
    }

    private bool CanUseWeapon(Weapon weapon)
    {
        if (weapon?.WeaponModel == null)
            return false;

        if (weapon is MeleeWeapon)
            return HasSkill(TaskData.Skills.MeleeCombat);

        if (weapon is RangedWeapon ranged)
            return HasSkill(TaskData.Skills.RangedCombat) &&
                _owner.HasProjectileInInventory(ranged.RequiredProjectile);

        return false;
    }

    private bool HasCombatSkill() =>
        HasSkill(TaskData.Skills.MeleeCombat) || HasSkill(TaskData.Skills.RangedCombat);

    private bool HasSkill(TaskData.Skills skill)
    {
        if (_owner.Skills == null)
            return false;

        foreach (TaskData.Skills ownerSkill in _owner.Skills)
            if (ownerSkill == skill)
                return true;

        return false;
    }

    private bool TryStowEquippedWeapon()
    {
        InventoryComponent inventory = _owner.Inventory;
        EquipmentComponent equipment = _owner.Equipment;
        if (equipment?.Valid(0) != true || equipment.Slots[0]?.Item is not Weapon weapon)
            return false;

        int freeSlot = FindFreeInventorySlot(inventory);
        if (freeSlot < 0)
            return false;

        inventory.Slots[freeSlot] = new Stack { Item = weapon, ItemCount = equipment.Slots[0].ItemCount };
        equipment.Slots[0] = null;
        inventory.NotifyChanged();
        equipment.NotifyChanged();
        return true;
    }

    private static int FindFreeInventorySlot(InventoryComponent inventory)
    {
        if (inventory?.Slots == null)
            return -1;

        for (int i = 0; i < inventory.Slots.Count; i++)
            if (inventory.Slots[i]?.Item == null)
                return i;

        return -1;
    }
}
