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
        Hands
    }

    private Node3D _modelRoot;
    private Skeleton3D _skeleton;

    private readonly Dictionary<Slot, Node3D> _slots = new();
    private readonly Dictionary<Slot, Node> _equipped = new();
    private readonly Dictionary<Slot, PackedScene> _pendingEquipment = new();
    private bool _initialized;

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
            if (_skeleton != null && slot == Slot.Feet && !string.IsNullOrEmpty(footBone))
            {
                var attachment = new BoneAttachment3D
                {
                    Name = $"{slot}Slot",
                    BoneName = footBone
                };
                _skeleton.AddChild(attachment);
                slotNode = attachment;
                GD.Print($"EquipmentComponent: Feet slot attached to skeleton bone '{footBone}'.");
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
    }

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
            GD.Print($"EquipmentComponent: Queued {slot} equipment until slots are ready.");
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
        GD.Print($"EquipmentComponent: Equipped {slot} with {clothingScene.ResourcePath}.");
    }

    public void Unequip(Slot slot)
    {
        if (!_equipped.TryGetValue(slot, out Node clothing))
            return;

        clothing.QueueFree();
        _equipped.Remove(slot);
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