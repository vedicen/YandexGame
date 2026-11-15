using Godot;

public enum ArmorSlot
{
    Helmet,
    Chest,
    Boots
}

public abstract partial class Armor : Equipment
{
    public abstract ArmorSlot Kind { get; }

    [Export] public Godot.Collections.Array<ArmorAttachment> Attachments { get; set; } = new();
}
