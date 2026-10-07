using Godot;

public partial class PlayerInputComponent : Node
{
    public Vector2 GetMovementInput()
    {
        return Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
    }

    public bool IsJumpPressed()
    {
        return Input.IsActionJustPressed("ui_accept");
    }
}