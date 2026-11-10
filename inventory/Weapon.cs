using Godot;

public abstract partial class Weapon : HandItem
{
    [Export(PropertyHint.Range, "0,10000,0.1")] public float Damage { get; set; } = 10.0f;
}
