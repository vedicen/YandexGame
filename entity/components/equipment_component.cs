using Godot;
using System.Collections.Generic;

public partial class EquipmentComponent : Node
{
    public enum Slot
    {
        Head,
        Chest,
        Legs,
        Feet,
        Hands,
        Weapon,
        RangedWeapon
    }

    private Node3D _modelRoot;
    private Skeleton3D _skeleton;

    private readonly Dictionary<Slot, Node3D> _slots = new();
    private readonly Dictionary<Slot, Node> _equipped = new();
    private readonly Dictionary<Slot, PackedScene> _pendingEquipment = new();
    private bool _initialized;
    public Weapon EquippedWeapon { get; private set; }

    public override void _Ready()
    {
        Callable.From(InitializePendingEquipment).CallDeferred();
    }

    private bool Initialize()
    {
        if (_initialized)
            return true;

        _modelRoot = GetNodeOrNull<Node3D>("../Model");

        if (_modelRoot == null)
            return false;

        _skeleton = FindSkeleton(_modelRoot);

        foreach (Slot slot in System.Enum.GetValues(typeof(Slot)))
        {
            Node3D slotNode;
            string footBone = _skeleton == null ? string.Empty : FindFootBone();
            string handBone = _skeleton == null
                ? string.Empty
                : slot switch
                {
                    Slot.Weapon => FindRightHandBone(),
                    Slot.RangedWeapon => FindLeftHandBone(),
                    _ => string.Empty
                };
            if (_skeleton != null && slot == Slot.Feet && !string.IsNullOrEmpty(footBone))
            {
                var attachment = new BoneAttachment3D
                {
                    Name = $"{slot}Slot",
                    BoneName = footBone
                };
                _skeleton.AddChild(attachment);
                slotNode = attachment;
                
            }
            else if (_skeleton != null &&
                slot is Slot.Weapon or Slot.RangedWeapon &&
                !string.IsNullOrEmpty(handBone))
            {
                var attachment = new BoneAttachment3D
                {
                    Name = $"{slot}Slot",
                    BoneName = handBone
                };
                _skeleton.AddChild(attachment);
                slotNode = attachment;
                
            }
            else if (slot is Slot.Weapon or Slot.RangedWeapon)
            {
                if (slot == Slot.RangedWeapon)
                    GD.PushError("EquipmentComponent: Could not find a left-hand bone; ranged weapon slot is unavailable.");
                else
                    GD.PushError("EquipmentComponent: Could not find a right-hand bone; weapon slot is unavailable.");
                continue;
            }
            else
            {
                slotNode = new Node3D { Name = $"{slot}Slot" };
                _modelRoot.AddChild(slotNode);
                if (slot == Slot.Feet && _skeleton != null)
                    GD.PushWarning("EquipmentComponent: No foot bone found; Feet slot is attached to the model root.");
            }

            _slots[slot] = slotNode;
        }

        _initialized = true;
        return true;
    }

    private string FindFootBone()
    {
        string fallback = string.Empty;
        for (int i = 0; i < _skeleton.GetBoneCount(); i++)
        {
            string boneName = _skeleton.GetBoneName(i).ToString();
            string normalized = boneName.Replace("_", "").Replace(".", "").Replace("-", "").Replace(" ", "");
            if (!normalized.Contains("foot", System.StringComparison.OrdinalIgnoreCase))
                continue;

            if (normalized.Contains("right", System.StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("rfoot", System.StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("footr", System.StringComparison.OrdinalIgnoreCase))
                return boneName;

            if (string.IsNullOrEmpty(fallback))
                fallback = boneName;
        }

        return fallback;
    }

    private string FindRightHandBone()
    {
        for (int i = 0; i < _skeleton.GetBoneCount(); i++)
        {
            string boneName = _skeleton.GetBoneName(i).ToString();
            string normalized = boneName.Replace("_", "").Replace(".", "")
                .Replace("-", "").Replace(" ", "").Replace(":", "");
            if ((normalized.Contains("right", System.StringComparison.OrdinalIgnoreCase) &&
                 normalized.Contains("hand", System.StringComparison.OrdinalIgnoreCase)) ||
                normalized.Contains("righthand", System.StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("handr", System.StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("rhand", System.StringComparison.OrdinalIgnoreCase))
                return boneName;
        }

        return string.Empty;
    }

    private string FindLeftHandBone()
    {
        for (int i = 0; i < _skeleton.GetBoneCount(); i++)
        {
            string boneName = _skeleton.GetBoneName(i).ToString();
            string normalized = boneName.Replace("_", "").Replace(".", "")
                .Replace("-", "").Replace(" ", "").Replace(":", "");
            if ((normalized.Contains("left", System.StringComparison.OrdinalIgnoreCase) &&
                 normalized.Contains("hand", System.StringComparison.OrdinalIgnoreCase)) ||
                normalized.Contains("lefthand", System.StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("handl", System.StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("lhand", System.StringComparison.OrdinalIgnoreCase))
                return boneName;
        }

        return string.Empty;
    }

    private void InitializePendingEquipment()
    {
        if (!Initialize())
        {
            if (_pendingEquipment.Count > 0)
            {
                GD.PushWarning("EquipmentComponent: Model or Skeleton3D is not ready yet; queued equipment will be applied when Equip is called again.");
            }
            return;
        }

        ApplyPendingEquipment();
    }

    private void ApplyPendingEquipment()
    {
        foreach (var entry in _pendingEquipment)
            EquipNow(entry.Key, entry.Value);
        _pendingEquipment.Clear();
        if (_pendingWeapon != null)
        {
            Weapon weapon = _pendingWeapon;
            _pendingWeapon = null;
            EquipWeaponNow(weapon);
        }
    }

    private Weapon _pendingWeapon;

    public void Equip(Slot slot, PackedScene clothingScene)
    {
        if (clothingScene == null)
        {
            GD.PushError($"EquipmentComponent: No scene provided for {slot}.");
            return;
        }

        if (!Initialize())
        {
            _pendingEquipment[slot] = clothingScene;
            
            Callable.From(InitializePendingEquipment).CallDeferred();
            return;
        }

        ApplyPendingEquipment();
        EquipNow(slot, clothingScene);
    }

    private void EquipNow(Slot slot, PackedScene clothingScene)
    {
        if (!_slots.TryGetValue(slot, out Node3D slotNode))
        {
            GD.PushError($"EquipmentComponent: Slot {slot} is not ready; check that the model root exists.");
            return;
        }

        Node clothing = clothingScene.Instantiate();
        if (clothing == null)
        {
            GD.PushError($"EquipmentComponent: Could not instantiate equipment scene for {slot}.");
            return;
        }

        slotNode.AddChild(clothing);
        if (_skeleton != null)
            BindMeshes(clothing);

        Unequip(slot);
        _equipped[slot] = clothing;
        
    }

    public void Unequip(Slot slot)
    {
        if (!_equipped.TryGetValue(slot, out Node clothing))
            return;

        clothing.QueueFree();
        _equipped.Remove(slot);
    }

    public void EquipWeapon(Weapon weapon)
    {
        if (weapon == null)
        {
            
            Unequip(Slot.Weapon);
            Unequip(Slot.RangedWeapon);
            EquippedWeapon = null;
            _pendingWeapon = null;
            return;
        }

        
        if (weapon.WeaponModel == null)
        {
            GD.PushError($"EquipmentComponent: Weapon '{weapon.DisplayName}' has no WeaponModel.");
            return;
        }

        EquippedWeapon = weapon;
        if (!Initialize())
        {
            _pendingWeapon = weapon;
            Callable.From(InitializePendingEquipment).CallDeferred();
            return;
        }

        ApplyPendingEquipment();
        EquipWeaponNow(weapon);
    }

    private void EquipWeaponNow(Weapon weapon)
    {
        Slot weaponSlot = weapon is RangedWeapon ? Slot.RangedWeapon : Slot.Weapon;
        if (!_slots.TryGetValue(weaponSlot, out Node3D slotNode) || slotNode == null)
        {
            GD.PushError($"EquipmentComponent: {weaponSlot} is unavailable; check the corresponding hand bone.");
            return;
        }

        PackedScene weaponScene = GD.Load<PackedScene>("res://inventory/weapon_scene.tscn");
        if (weaponScene == null)
        {
            GD.PushError("EquipmentComponent: Could not load res://inventory/weapon_scene.tscn.");
            return;
        }

        WeaponScene instance = weaponScene.Instantiate<WeaponScene>();
        instance.ModelScene = weapon.WeaponModel;
        instance.Position = weapon.EquipPosition;
        instance.RotationDegrees = weapon.EquipRotationDegrees;
        instance.Scale = weapon.EquipScale;
        instance.ConfigureCombat(GetParent() as AnimatedEntity, weapon);
        Unequip(Slot.Weapon);
        Unequip(Slot.RangedWeapon);
        slotNode.AddChild(instance);
        _equipped[weaponSlot] = instance;
        EquippedWeapon = weapon;
       
    }

    public WeaponScene GetEquippedWeaponScene()
    {
        Slot weaponSlot = EquippedWeapon is RangedWeapon ? Slot.RangedWeapon : Slot.Weapon;
        if (_equipped.TryGetValue(weaponSlot, out Node equipped) &&
            GodotObject.IsInstanceValid(equipped) &&
            !equipped.IsQueuedForDeletion() &&
            equipped is WeaponScene weaponScene)
            return weaponScene;

        foreach (Slot slot in new[] { Slot.Weapon, Slot.RangedWeapon })
        {
            if (!_slots.TryGetValue(slot, out Node3D slotNode) ||
                !GodotObject.IsInstanceValid(slotNode))
                continue;

            foreach (Node child in slotNode.GetChildren())
            {
                if (child is WeaponScene attachedWeapon && !child.IsQueuedForDeletion())
                {
                    _equipped[slot] = attachedWeapon;
                    GD.PushWarning($"[Combat] Recovered equipped weapon reference from slot: {attachedWeapon.GetPath()}.");
                    return attachedWeapon;
                }
            }
        }

        return null;
    }

    private void BindMeshes(Node node)
    {
        if (node is MeshInstance3D mesh)
            mesh.Skeleton = mesh.GetPathTo(_skeleton);

        foreach (Node child in node.GetChildren())
            BindMeshes(child);
    }

    private static Skeleton3D FindSkeleton(Node node)
    {
        if (node is Skeleton3D skeleton)
            return skeleton;

        foreach (Node child in node.GetChildren())
        {
            Skeleton3D result = FindSkeleton(child);

            if (result != null)
                return result;
        }

        return null;
    }
}