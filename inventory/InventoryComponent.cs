using Godot;
using System;

[GlobalClass]
public partial class InventoryComponent : Node
{
    [Export(PropertyHint.Range, "1,100")] public int MaxSlots { get; set; } = 20;
    [Export] public Godot.Collections.Array<Stack> Slots { get; set; } = new();
    public event Action Changed;
    public override void _Ready()
    {
        AddToGroup("inventories");
        var originals = Slots ?? new();
        Slots = new();
        for (int i = 0; i < MaxSlots; i++)
            Slots.Add(i < originals.Count ? originals[i]?.Copy() : null);
    }
    public bool Valid(int index) => Slots != null && index >= 0 && index < Slots.Count;
    public void NotifyChanged() => Changed?.Invoke();
    public int Add(Item item, int count)
    {
        if (item == null || count <= 0) return count;
        Slots ??= new();
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < Slots.Count && count > 0; i++)
            {
                var slot = Slots[i];
                if (pass == 0 && slot?.Item != item || pass == 1 && slot != null) continue;
                int amount = Math.Min(count, Math.Max(1, item.MaxStackSize) - (slot?.ItemCount ?? 0));
                if (amount <= 0) continue;
                Slots[i] ??= new Stack { Item = item, ItemCount = 0 };
                Slots[i].ItemCount += amount;
                count -= amount;
            }
        NotifyChanged();
        return count;
    }
    public bool Transfer(int from, InventoryComponent target, int to)
    {
        if (target == null || !Valid(from) || !target.Valid(to) || this == target && from == to || Slots[from] == null) return false;
        var source = Slots[from];
        var dest = target.Slots[to];
        if (dest?.Item == source.Item)
        {
            int amount = Math.Min(source.ItemCount, source.Item.MaxStackSize - dest.ItemCount);
            if (amount <= 0) return false;
            dest.ItemCount += amount;
            source.ItemCount -= amount;
            if (source.ItemCount == 0) Slots[from] = null;
        }
        else { target.Slots[to] = source; Slots[from] = dest; }
        NotifyChanged();
        if (target != this) target.NotifyChanged();
        return true;
    }
}
