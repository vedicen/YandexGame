using Godot;

public partial class PlayerInputComponent : Node
{
    private Vector2 _lookMotion;

    public bool HasInputAuthority
    {
        get
        {
            Node parent = GetParent();
            return parent != null &&
                (!Multiplayer.HasMultiplayerPeer() ||
                parent.GetMultiplayerAuthority() == Multiplayer.GetUniqueId());
        }
    }

    public Vector2 Movement =>
        HasInputAuthority ? Input.GetVector("left", "right", "forward", "backward") : Vector2.Zero;

    public bool JumpPressed =>
        HasInputAuthority && Input.IsActionJustPressed("jump");

    public bool Crouching =>
        HasInputAuthority && Input.IsActionPressed("crouch");

    public bool Attack1Pressed =>
        HasInputAuthority && Input.IsActionJustPressed("attack1");

    public bool Attack2Pressed =>
        HasInputAuthority && Input.IsActionJustPressed("attack2");

    public bool IsMouseCaptured =>
        Input.MouseMode == Input.MouseModeEnum.Captured;

    public override void _Ready()
    {
        if (HasInputAuthority)
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!HasInputAuthority)
            return;

        if (@event.IsActionPressed("ui_cancel"))
        {
            Input.MouseMode = IsMouseCaptured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (IsMouseCaptured && @event is InputEventMouseMotion mouseMotion)
            _lookMotion += mouseMotion.Relative;
    }

    public Vector2 ConsumeLookMotion()
    {
        Vector2 lookMotion = _lookMotion;
        _lookMotion = Vector2.Zero;
        return lookMotion;
    }
}