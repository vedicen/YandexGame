using Godot;

public partial class Player : AnimatedEntity
{
    public InventoryComponent Inventory { get; private set; }
    public PlayerStateMachine States { get; private set; }
    public InventoryUI InventoryUI { get; private set; }
    private Chest _openChest;
    public override void _EnterTree()
    {
        if (int.TryParse(Name, out int peer)) SetMultiplayerAuthority(peer, true);
    }
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

    private Button interact_key;
    private interact_obj current_interact_obj;
    private float _cameraRotationX = 0f;
    

    public override void _Ready()
    {
        base._Ready();
        
        playerInputComponent ??= GetNodeOrNull<PlayerInputComponent>("PlayerInputComponent");
        Inventory = GetNode<InventoryComponent>("InventoryComponent");
        States = new PlayerStateMachine { Name = "States" };
        AddChild(States);
        interact_key ??= GetNodeOrNull<Button>("UI/Ineract_Key");

        interact_key.Pressed += pressed_interact_key;
        interact_key.Hide();
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
            InventoryUI = new InventoryUI { Player = this };
            AddChild(InventoryUI);
            Callable.From(() => InventoryService.Instance?.RequestSnapshot()).CallDeferred();
        }
        else
        {
            _camera.Current = false;
            SetPhysicsProcess(false);
            SetProcess(false);
            GetNode<Control>("UI").Hide();
        }
    }

    public override void _Process(double delta)
    {
        if (playerInputComponent == null || !playerInputComponent.HasInputAuthority)
            return;

        Vector2 mouseMotion = playerInputComponent.ConsumeLookMotion();
        if (!States.CanLook) return;
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
        if (!States.CanMove)
        {
            Velocity = new Vector3(0, IsOnFloor() ? 0 : velocity.Y + GetGravity().Y * (float)delta, 0);
            MoveAndSlide();
            return;
        }

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

        States.UpdateMotion(IsOnFloor(), direction != Vector3.Zero, crouching);

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

        if (States.CanAttack && playerInputComponent.Attack1Pressed)
            PlayAttack1();
        else if (States.CanAttack && playerInputComponent.Attack2Pressed)
            PlayAttack2();
    }
    public void interact_show(interact_obj _interact_obj)
    {

        if (playerInputComponent?.HasInputAuthority != true) return;
        current_interact_obj = _interact_obj;
        GD.Print("Показал кнопку");
        interact_key.Show();
    }
    public void interact_hide(interact_obj source = null)
    {
        if (source != null && current_interact_obj != source) return;
        current_interact_obj = null;
        GD.Print("Убрал  кнопку");
        interact_key.Hide();
    }
    private void pressed_interact_key()
    {

        if (!States.CanMove || !GodotObject.IsInstanceValid(current_interact_obj)) return;
        current_interact_obj.interact(this);
        interact_key.Hide();
    }

    public override void _Input(InputEvent ev)
    {
        if (playerInputComponent?.HasInputAuthority != true || ev is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.PhysicalKeycode == Key.Tab || key.Keycode == Key.Escape && !States.CanMove)
        {
            if (States.CanMove) { States.Transition(PlayerStateMachine.State.Inventory); InventoryUI.Open(null); }
            else CloseInventory();
            GetViewport().SetInputAsHandled();
        }
        else if (key.PhysicalKeycode == Key.E && States.CanMove)
        {
            pressed_interact_key();
            GetViewport().SetInputAsHandled();
        }
    }
    public void SetChest(Chest chest)
    {
        if (_openChest == chest) return;
        _openChest = chest;
        if (chest != null)
        {
            States.Transition(PlayerStateMachine.State.Chest);
            InventoryUI?.Open(chest);
        }
        else if (States.Current == PlayerStateMachine.State.Chest)
        {
            States.Transition(PlayerStateMachine.State.Locomotion);
            InventoryUI?.Close();
        }
    }
    public void CloseInventory()
    {
        InventoryService.Instance?.Send("close", this);
        _openChest = null;
        States.Transition(PlayerStateMachine.State.Locomotion);
        InventoryUI?.Close();
    }


}
