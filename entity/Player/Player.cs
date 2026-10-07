using Godot;

public partial class Player : AnimatedEntity
{
    [ExportGroup("Movement")]
    [Export] public float Speed { get; set; } = 5.0f;
    [Export] public float JumpVelocity { get; set; } = 4.5f;

    [ExportGroup("Camera Sensitivity")]
    [Export] public float MouseSensitivity { get; set; } = 0.003f;
    [Export] public float MinPitch { get; set; } = -80.0f; // Ограничение взгляда вниз (в градусах)
    [Export] public float MaxPitch { get; set; } = 60.0f;  // Ограничение взгляда вверх (в градусах)

    private Node3D _cameraPivot;
    private SpringArm3D _springArm;
    private Camera3D _camera;

    // Накопленные углы поворота камеры
    private float _cameraRotationX = 0f;

    public override void _Ready()
    {
        base._Ready();

        _cameraPivot = GetNode<Node3D>("CameraPivot");
        _camera = GetNode<Camera3D>("CameraPivot/SpringArm3D/Camera3D");

        // Проверяем, принадлежит ли этот узел текущему клиенту
        if (GetMultiplayerAuthority() == Multiplayer.GetUniqueId())
        {
            _camera.Current = true;
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        else
        {
            // Для чужих игроков выключаем камеру и обработку физического ввода
            _camera.Current = false;
            SetPhysicsProcess(false);
            SetProcessUnhandledInput(false);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Обрабатываем поворот мыши только на локальном клиенте
        if (GetMultiplayerAuthority() != Multiplayer.GetUniqueId())
            return;

        // При нажатии Escape освобождаем/захватываем курсор
        if (@event.IsActionPressed("ui_cancel"))
        {
            if (Input.MouseMode == Input.MouseModeEnum.Captured)
                Input.MouseMode = Input.MouseModeEnum.Visible;
            else
                Input.MouseMode = Input.MouseModeEnum.Captured;
        }

        // Поворот камеры мышью
        if (Input.MouseMode == Input.MouseModeEnum.Captured && @event is InputEventMouseMotion mouseMotion)
        {
            // Горизонтальный поворот (поворачиваем весь персонаж или сам Pivot)
            RotateY(-mouseMotion.Relative.X * MouseSensitivity);

            // Вертикальный поворот (поворачиваем только CameraPivot)
            _cameraRotationX -= mouseMotion.Relative.Y * MouseSensitivity;
            _cameraRotationX = Mathf.Clamp(_cameraRotationX, Mathf.DegToRad(MinPitch), Mathf.DegToRad(MaxPitch));

            Vector3 pivotRotation = _cameraPivot.Rotation;
            pivotRotation.X = _cameraRotationX;
            _cameraPivot.Rotation = pivotRotation;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (GetMultiplayerAuthority() != Multiplayer.GetUniqueId())
            return;

        Vector3 velocity = Velocity;

        if (!IsOnFloor())
            velocity += GetGravity() * (float)delta;

        if (Input.IsActionJustPressed("ui_accept") && IsOnFloor())
            velocity.Y = JumpVelocity;

        // Вектор ввода движения
        Vector2 inputDir = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");

        // Движение относительно направления персонажа/камеры
        Vector3 direction = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();

        if (direction != Vector3.Zero)
        {
            velocity.X = direction.X * Speed;
            velocity.Z = direction.Z * Speed;
        }
        else
        {
            velocity.X = Mathf.MoveToward(Velocity.X, 0, Speed);
            velocity.Z = Mathf.MoveToward(Velocity.Z, 0, Speed);
        }

        Velocity = velocity;
        MoveAndSlide();

        if (direction == Vector3.Zero)
            PlayIdle();
        else
            PlayWalk();
    }
}