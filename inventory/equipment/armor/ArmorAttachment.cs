using Godot;

[Tool, GlobalClass]
public partial class ArmorAttachment : Resource
{
    private MixamoBone _bone;
    private PackedScene _scene;
    private Vector3 _equipPosition;
    private Vector3 _equipRotationDegrees;
    private Vector3 _equipScale = Vector3.One;

    [Export] public MixamoBone Bone
    {
        get => _bone;
        set
        {
            if (_bone == value)
                return;
            _bone = value;
            EmitChanged();
        }
    }

    [Export] public PackedScene Scene
    {
        get => _scene;
        set
        {
            if (_scene == value)
                return;
            _scene = value;
            EmitChanged();
        }
    }

    [Export] public Vector3 EquipPosition
    {
        get => _equipPosition;
        set
        {
            if (_equipPosition == value)
                return;
            _equipPosition = value;
            EmitChanged();
        }
    }

    [Export] public Vector3 EquipRotationDegrees
    {
        get => _equipRotationDegrees;
        set
        {
            if (_equipRotationDegrees == value)
                return;
            _equipRotationDegrees = value;
            EmitChanged();
        }
    }

    [Export] public Vector3 EquipScale
    {
        get => _equipScale;
        set
        {
            if (_equipScale == value)
                return;
            _equipScale = value;
            EmitChanged();
        }
    }
}
