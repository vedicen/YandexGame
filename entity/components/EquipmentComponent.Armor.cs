using Godot;
using System;
using System.Collections.Generic;

public partial class EquipmentComponent
{
    [Export] public int ArmorSlotCount { get; set; } = 3;

    private readonly Dictionary<ArmorSlot, Armor> _pendingArmor = new();
    private readonly Dictionary<ArmorSlot, Armor> _equippedArmor = new();
    private readonly Dictionary<ArmorSlot, List<Node3D>> _armorNodes = new();
    private readonly Dictionary<ArmorSlot, List<(ArmorAttachment Attachment, Action Handler)>> _armorAttachmentHandlers = new();
    private readonly Dictionary<MixamoBone, BoneAttachment3D> _armorBones = new();

    public override int ItemSlotEnd =>
        Math.Max(0, (Slots?.Count ?? 0) - Math.Max(0, ArmorSlotCount));

    public bool IsArmorSlot(int index) => TryGetArmorSlot(index, out _);

    public bool TryGetArmorSlot(int index, out ArmorSlot slot)
    {
        slot = default;
        if (ArmorSlotCount <= 0 || Slots == null)
            return false;

        int start = ItemSlotEnd;
        if (index < start || index >= start + ArmorSlotCount || index >= Slots.Count)
            return false;

        slot = (ArmorSlot)(index - start);
        return Enum.IsDefined(slot);
    }

    public int GetArmorSlotIndex(ArmorSlot slot) => ItemSlotEnd + (int)slot;

    public bool AcceptsItem(int index, Item item)
    {
        if (item == null)
            return true;
        if (index == 0)
            return item is HandItem;
        if (TryGetArmorSlot(index, out ArmorSlot slot))
            return item is Armor armor && armor.Kind == slot;
        return index >= FirstItemSlot && index < ItemSlotEnd;
    }

    public void EquipArmor(Armor armor)
    {
        if (armor == null)
            return;

        if (_equippedArmor.TryGetValue(armor.Kind, out Armor current) && IsSameArmor(current, armor))
            return;

        if (!Initialize())
        {
            _pendingArmor[armor.Kind] = armor;
            Callable.From(InitializePendingEquipment).CallDeferred();
            return;
        }

        EquipArmorNow(armor);
    }

    public void ClearArmor(ArmorSlot slot)
    {
        _pendingArmor.Remove(slot);
        if (_armorAttachmentHandlers.TryGetValue(slot, out List<(ArmorAttachment Attachment, Action Handler)> handlers))
        {
            foreach ((ArmorAttachment attachment, Action handler) in handlers)
                attachment.Changed -= handler;
            _armorAttachmentHandlers.Remove(slot);
        }

        if (_armorNodes.TryGetValue(slot, out List<Node3D> nodes))
        {
            foreach (Node3D visual in nodes)
            {
                if (!GodotObject.IsInstanceValid(visual))
                    continue;

                visual.GetParent()?.RemoveChild(visual);
                visual.QueueFree();
            }

            _armorNodes.Remove(slot);
        }

        _equippedArmor.Remove(slot);
        RemoveUnusedArmorBones();
    }

    private void ApplyPendingArmor()
    {
        if (_pendingArmor.Count == 0)
            return;

        ArmorSlot[] slots = new ArmorSlot[_pendingArmor.Count];
        _pendingArmor.Keys.CopyTo(slots, 0);
        foreach (ArmorSlot slot in slots)
        {
            if (!_pendingArmor.Remove(slot, out Armor armor))
                continue;
            EquipArmorNow(armor);
        }
    }

    private void EquipArmorNow(Armor armor)
    {
        ClearArmor(armor.Kind);
        var created = new List<Node3D>();
        if (armor.Attachments != null)
        {
            foreach (ArmorAttachment piece in armor.Attachments)
            {
                if (piece?.Scene == null)
                {
                    GD.PushError($"EquipmentComponent: armor '{armor.DisplayName}' has an attachment without a scene.");
                    continue;
                }

                BoneAttachment3D attachment = GetOrCreateArmorBone(piece.Bone);
                if (attachment == null)
                    continue;

                Node instance = piece.Scene.Instantiate();
                if (instance is not Node3D visual)
                {
                    GD.PushError($"EquipmentComponent: armor scene '{piece.Scene.ResourcePath}' root must be Node3D.");
                    instance?.QueueFree();
                    continue;
                }

                attachment.AddChild(visual);
                visual.Name = $"Armor_{armor.Kind}_{piece.Bone}";
                visual.Position = piece.EquipPosition;
                visual.RotationDegrees = piece.EquipRotationDegrees;
                visual.Scale = piece.EquipScale;
                created.Add(visual);
            }
        }

        _armorNodes[armor.Kind] = created;
        _equippedArmor[armor.Kind] = armor;
        var handlers = new List<(ArmorAttachment Attachment, Action Handler)>();
        if (armor.Attachments != null)
        {
            foreach (ArmorAttachment piece in armor.Attachments)
            {
                if (piece == null)
                    continue;

                ArmorSlot slot = armor.Kind;
                Action handler = () => RefreshEquippedArmor(slot, armor);
                piece.Changed += handler;
                handlers.Add((piece, handler));
            }
        }
        _armorAttachmentHandlers[armor.Kind] = handlers;
    }

    private void RefreshEquippedArmor(ArmorSlot slot, Armor armor)
    {
        if (_equippedArmor.TryGetValue(slot, out Armor equipped) && ReferenceEquals(equipped, armor))
            EquipArmorNow(armor);
    }

    private BoneAttachment3D GetOrCreateArmorBone(MixamoBone bone)
    {
        if (_armorBones.TryGetValue(bone, out BoneAttachment3D existing) &&
            GodotObject.IsInstanceValid(existing))
            return existing;

        if (!TryResolveBoneName(bone, out string boneName))
        {
            GD.PushError($"EquipmentComponent: bone '{bone.ToMixamoName()}' was not found; armor piece was skipped.");
            return null;
        }

        var attachment = new BoneAttachment3D { Name = $"ArmorBone_{bone}" };
        _skeleton.AddChild(attachment);
        attachment.BoneName = boneName;
        _armorBones[bone] = attachment;
        return attachment;
    }

    private bool TryResolveBoneName(MixamoBone bone, out string resolved)
    {
        resolved = string.Empty;
        if (_skeleton == null)
            return false;

        string fullName = bone.ToMixamoName();
        string shortName = fullName[(fullName.LastIndexOf(':') + 1)..];
        string normalizedFull = NormalizeBoneName(fullName);
        string normalizedShort = NormalizeBoneName(shortName);
        for (int i = 0; i < _skeleton.GetBoneCount(); i++)
        {
            string actual = _skeleton.GetBoneName(i).ToString();
            if (actual.Equals(fullName, StringComparison.OrdinalIgnoreCase) ||
                actual.Equals(shortName, StringComparison.OrdinalIgnoreCase) ||
                NormalizeBoneName(actual).Equals(normalizedFull, StringComparison.OrdinalIgnoreCase) ||
                NormalizeBoneName(actual).Equals(normalizedShort, StringComparison.OrdinalIgnoreCase))
            {
                resolved = actual;
                return true;
            }
        }

        return false;
    }

    private void RemoveUnusedArmorBones()
    {
        var used = new HashSet<BoneAttachment3D>();
        foreach (List<Node3D> nodes in _armorNodes.Values)
        {
            foreach (Node3D visual in nodes)
            {
                if (GodotObject.IsInstanceValid(visual) && visual.GetParent() is BoneAttachment3D parent)
                    used.Add(parent);
            }
        }

        var unused = new List<MixamoBone>();
        foreach (KeyValuePair<MixamoBone, BoneAttachment3D> pair in _armorBones)
        {
            if (used.Contains(pair.Value))
                continue;

            if (GodotObject.IsInstanceValid(pair.Value))
            {
                pair.Value.GetParent()?.RemoveChild(pair.Value);
                pair.Value.QueueFree();
            }

            unused.Add(pair.Key);
        }

        foreach (MixamoBone bone in unused)
            _armorBones.Remove(bone);
    }

    private static bool IsSameArmor(Armor current, Armor next) =>
        ReferenceEquals(current, next) ||
        (current != null && next != null &&
            !string.IsNullOrEmpty(current.ResourcePath) &&
            current.ResourcePath == next.ResourcePath);

    private static string NormalizeBoneName(string value) =>
        value.Replace("_", "").Replace(".", "").Replace("-", "").Replace(" ", "").Replace(":", "");
}
