using Godot;

public partial class WeaponScene : Node3D
{
    [Export] public PackedScene ModelScene { get; set; }

    public override void _Ready()
    {
        if (ModelScene == null)
        {
            GD.PushError($"WeaponScene '{Name}' requires a weapon model scene.");
            return;
        }

        AddChild(ModelScene.Instantiate<Node3D>());
    }
}
