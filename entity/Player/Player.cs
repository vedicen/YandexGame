using Godot;

public partial class Player : AnimatedEntity
{
    [ExportGroup("Movement")]

    [Export] public PlayerInputComponent playerInputComponent;
    [Export] public float Speed { get; set; } = 5.0f;
    [Export] public float JumpVelocity { get; set; } = 4.5f;
    [Export] public float CrouchSpeedMultiplier { get; set; } = 0.5f;

    [ExportGroup("Camera Sensitivity")]
    [Export] public float MouseSensitivity { get; set; } = 0.003f;
    [Export] public float MinPitch { get; set; } = -80.0f; // Ограничение взгляда вниз (в градусах)
    [Export] public float MaxPitch { get; set; } = 60.0f;  // Ограничение взгляда вверх (в градусах)

    private Node3D _cameraPivot;
    private Camera3D _camera;

    private float _cameraRotationX = 0f;

    public override void _Ready()
    {
        base._Ready();

        playerInputComponent ??= GetNodeOrNull<PlayerInputComponent>("PlayerInputComponent");
        if (playerInputComponent == null)
        {
            GD.PushError("Player requires a PlayerInputComponent child.");
            SetPhysicsProcess(false);
            SetProcess(false);
            return;
        }

        AddToGroup("players");
        _cameraPivot = GetNode<Node3D>("CameraPivot");
        _camera = GetNode<Camera3D>("CameraPivot/SpringArm3D/Camera3D");

        if (playerInputComponent.HasInputAuthority)
        {
            _camera.Current = true;
        }
        else
        {
            _camera.Current = false;
            SetPhysicsProcess(false);
            SetProcess(false);
        }
    }

    public override void _Process(double delta)
    {
        if (playerInputComponent == null || !playerInputComponent.HasInputAuthority)
            return;

        Vector2 mouseMotion = playerInputComponent.ConsumeLookMotion();
        if (playerInputComponent.IsMouseCaptured && mouseMotion != Vector2.Zero)
        {
            RotateY(-mouseMotion.X * MouseSensitivity);

            _cameraRotationX -= mouseMotion.Y * MouseSensitivity;
            _cameraRotationX = Mathf.Clamp(_cameraRotationX, Mathf.DegToRad(MinPitch), Mathf.DegToRad(MaxPitch));

            Vector3 pivotRotation = _cameraPivot.Rotation;
            pivotRotation.X = _cameraRotationX;
            _cameraPivot.Rotation = pivotRotation;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (playerInputComponent == null || !playerInputComponent.HasInputAuthority)
            return;

        Vector3 velocity = Velocity;

        if (!IsOnFloor())
            velocity += GetGravity() * (float)delta;

        if (playerInputComponent.JumpPressed && IsOnFloor())
            velocity.Y = JumpVelocity;

        Vector2 inputDir = playerInputComponent.Movement;
        Vector3 direction = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();
        bool crouching = playerInputComponent.Crouching;
        float movementSpeed = Speed * (crouching ? CrouchSpeedMultiplier : 1.0f);

        if (direction != Vector3.Zero)
        {
            velocity.X = direction.X * movementSpeed;
            velocity.Z = direction.Z * movementSpeed;
        }
        else
        {
            velocity.X = Mathf.MoveToward(Velocity.X, 0, Speed);
            velocity.Z = Mathf.MoveToward(Velocity.Z, 0, Speed);
        }

        Velocity = velocity;
        MoveAndSlide();

        if (crouching)
        {
            if (direction == Vector3.Zero)
                PlayCrouchedIdle();
            else
                PlayCrouchedWalk();
        }
        else if (direction == Vector3.Zero)
        {
            PlayIdle();
        }
        else
        {
            PlayWalk();
        }

        if (playerInputComponent.Attack1Pressed)
            PlayAttack1();
        else if (playerInputComponent.Attack2Pressed)
            PlayAttack2();
    }
}