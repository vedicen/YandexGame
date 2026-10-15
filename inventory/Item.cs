using Godot;

public enum Rarity { Common, Rare, Epic, Mythic, Legendary }

[GlobalClass]
public partial class Item : Resource
{
    [Export] public string DisplayName { get; set; } = "Предмет";
    [Export] public PackedScene Model { get; set; }
    [Export(PropertyHint.Range, "1,999")] public int MaxStackSize { get; set; } = 20;
    [Export] public Rarity Rarity { get; set; }
    public Color LightColor => Rarity switch {
        Rarity.Rare => new Color("499fff"), Rarity.Epic => new Color("b55cff"),
        Rarity.Mythic => new Color("ff4477"), Rarity.Legendary => new Color("ffbc39"),
        _ => new Color("c4d1dc") };
    public float LightEnergy => 0.7f + (int)Rarity * 0.65f;
}
