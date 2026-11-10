using Godot;

public partial class PlayerInputComponent : Node
{
    private Vector2 _lookMotion;
    private bool _attack1PressedPending;
    private bool _attack1ReleasedPending;
    public bool IsInputEnabled { get; private set; } = true;

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
        HasInputAuthority && IsInputEnabled ? Input.GetVector("left", "right", "forward", "backward") : Vector2.Zero;

    public bool JumpPressed =>
        HasInputAuthority && IsInputEnabled && Input.IsActionJustPressed("jump");

    public bool Crouching =>
        HasInputAuthority && IsInputEnabled && Input.IsActionPressed("crouch");

    public bool Running =>
        HasInputAuthority && IsInputEnabled && Input.IsActionPressed("run");

    public bool Attack1Pressed =>
        HasInputAuthority && IsInputEnabled && Input.IsActionJustPressed("attack1");

    public bool Attack1Held =>
        HasInputAuthority && IsInputEnabled && Input.IsActionPressed("attack1");

    public bool HasPendingAttack1Press => _attack1PressedPending;
    public bool HasPendingAttack1Release => _attack1ReleasedPending;

    public bool Attack1Released =>
        HasInputAuthority && IsInputEnabled && Input.IsActionJustReleased("attack1");

    public bool ConsumeAttack1Pressed()
    {
        bool pressed = _attack1PressedPending;
        _attack1PressedPending = false;
        return pressed;
    }

    public bool ConsumeAttack1Released()
    {
        bool released = _attack1ReleasedPending;
        _attack1ReleasedPending = false;
        return released;
    }

    public void ClearAttack1Events()
    {
        _attack1PressedPending = false;
        _attack1ReleasedPending = false;
    }

    public bool Attack2Pressed =>
        HasInputAuthority && IsInputEnabled && Input.IsActionJustPressed("attack2");

    public bool IsMouseCaptured =>
        IsInputEnabled && Input.MouseMode == Input.MouseModeEnum.Captured;

    public void SetInputEnabled(bool enabled)
    {
        if (IsInputEnabled == enabled) return;
        IsInputEnabled = enabled;
        _lookMotion = Vector2.Zero;
        ClearAttack1Events();
        if (HasInputAuthority)
            Input.MouseMode = enabled ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
    }

    public override void _Input(InputEvent @event)
    {
        bool pressed = @event.IsActionPressed("attack1") && !@event.IsEcho();
        bool released = @event.IsActionReleased("attack1");
        if (!pressed && !released)
            return;

        bool hasAuthority = HasInputAuthority;
        if (!hasAuthority || !IsInputEnabled)
        {
            if (GetParent() is AnimatedEntity entity)
                entity.LogBowDebug($"Ignored attack1 {(pressed ? "press" : "release")}: " +
                    $"inputAuthority={hasAuthority}, inputEnabled={IsInputEnabled}, node={GetPath()}.");
            return;
        }

        if (pressed)
        {
            _attack1PressedPending = true;
            if (GetParent() is AnimatedEntity entity)
                entity.LogBowDebug($"Captured attack1 press: player={GetParent()?.Name}, " +
                    $"actionMapped={InputMap.HasAction("attack1")}, mouseCaptured={IsMouseCaptured}.");
        }
        else
        {
            _attack1ReleasedPending = true;
            if (GetParent() is AnimatedEntity entity)
                entity.LogBowDebug($"Captured attack1 release: player={GetParent()?.Name}.");
        }
    }

    public override void _Ready()
    {
        if (HasInputAuthority)
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!HasInputAuthority || !IsInputEnabled)
            return;
        if (GetParent() is Player player && player.States?.CanLook == false) return;

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
