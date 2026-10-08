using Godot;

public partial class PlayerInputComponent : Node
{
    public Vector2 GetMovementInput()
    {
        return Input.GetVector("left", "right", "forward", "backward");
    }

    public bool IsJumpPressed()
    {
        return Input.IsActionJustPressed("jump");
    }

     public bool IsCrouchPressed()
    {
        return Input.IsActionPressed("crouch");
    }
     public bool IsAttack1Pressed()
    {
        return Input.IsActionJustPressed("attack1");
    }
     public bool IsAttack2Pressed()
    {
        return Input.IsActionJustPressed("attack2");
    }
}