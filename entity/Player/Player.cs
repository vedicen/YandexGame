using Godot;
using System;
using System.Collections.Generic;

public partial class Player : AnimatedEntity
{
    public InventoryComponent Inventory { get; private set; }
    public InventoryComponent EquipmentInventory { get; private set; }
    public PlayerStateMachine States { get; private set; }
    public InventoryUI InventoryUI { get; private set; }
    public EquipmentComponent Equipment { get; private set; }
    [Export] public PackedScene InventoryUIScene { get; set; }
    
    private Chest _openChest;
    private TradingStand _tradingStand;
    private interact_obj _inputLockSource;
    private Node _tradeDialog;
    private bool _isChargingBow;
    private RangedWeapon _chargingBow;
    private float _bowChargeTime;
    private int _nextProjectileId;
    private bool _lastPhysicsHadRangedWeapon;
    [Export(PropertyHint.Range, "0,1000000")]
    public int StartingMoney { get; set; } = 500;
    public int Money { get; private set; }
    public event Action<int> MoneyChanged;
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
    private float _cameraRotationY;

    private VBoxContainer interact_key_list;
    private readonly List<interact_obj> _nearbyInteractables = new();
    private readonly List<Button> _interactButtons = new();
    private int _selectedInteractIndex;
    private interact_obj current_interact_obj;
    private float _cameraRotationX = 0f;
    

    public override void _Ready()
    {
        base._Ready();
        
        playerInputComponent ??= GetNodeOrNull<PlayerInputComponent>("PlayerInputComponent");
        EnsureEquipmentComponent();
        Inventory = GetNode<InventoryComponent>("InventoryComponent");
        EquipmentInventory = GetNode<InventoryComponent>("EquipmentInventory");
        EquipmentInventory.Changed += OnEquipmentInventoryChanged;
        OnEquipmentInventoryChanged();
        GD.Print($"[BowDebug] Player ready: player={Name}, inputAuthority={playerInputComponent?.HasInputAuthority}, " +
            $"inputEnabled={playerInputComponent?.IsInputEnabled}, inventorySlots={Inventory?.Slots?.Count ?? 0}, " +
            $"equipmentSlots={EquipmentInventory?.Slots?.Count ?? 0}.");
        Money = StartingMoney;
        States = new PlayerStateMachine { Name = "States" };
        AddChild(States);
        interact_key_list ??= GetNodeOrNull<VBoxContainer>("UI/InteractKeys");
        if (interact_key_list == null)
            GD.PushError("Player requires an InteractKeys container under UI.");
        else
            interact_key_list.Hide();
        if (playerInputComponent == null)
        {
            GD.PushError("Player requires a PlayerInputComponent child.");
            SetPhysicsProcess(false);
            SetProcess(false);
            return;
        }

        AddToGroup("players");
        _cameraPivot = GetNode<Node3D>("CameraPivot");
        _cameraRotationY = _cameraPivot.Rotation.Y;
        _camera = GetNode<Camera3D>("CameraPivot/SpringArm3D/Camera3D");

        if (playerInputComponent.HasInputAuthority)
        {
            _camera.Current = true;
            if (InventoryUIScene == null)
            {
                GD.PushError("Player requires an InventoryUIScene.");
                return;
            }

            InventoryUI = InventoryUIScene.Instantiate<InventoryUI>();
            InventoryUI.Player = this;
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
        base._Process(delta);

        if (playerInputComponent == null || !playerInputComponent.HasInputAuthority)
            return;

        Vector2 mouseMotion = playerInputComponent.ConsumeLookMotion();
        if (!States.CanLook) return;
        if (playerInputComponent.IsMouseCaptured && mouseMotion != Vector2.Zero)
        {
            _cameraRotationY = Mathf.Wrap(
                _cameraRotationY - mouseMotion.X * MouseSensitivity,
                -Mathf.Pi,
                Mathf.Pi);

            _cameraRotationX -= mouseMotion.Y * MouseSensitivity;
            _cameraRotationX = Mathf.Clamp(_cameraRotationX, Mathf.DegToRad(MinPitch), Mathf.DegToRad(MaxPitch));

            Vector3 pivotRotation = _cameraPivot.Rotation;
            pivotRotation.Y = _cameraRotationY;
            pivotRotation.X = _cameraRotationX;
            _cameraPivot.Rotation = pivotRotation;
        }

        ReplicatedBowAimPitch = Mathf.Clamp(
            _cameraRotationX,
            -Mathf.DegToRad(45.0f),
            Mathf.DegToRad(45.0f));

    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(EquipmentInventory))
            EquipmentInventory.Changed -= OnEquipmentInventoryChanged;
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);

        if (playerInputComponent == null || !playerInputComponent.HasInputAuthority)
            return;

        Vector3 velocity = Velocity;


        if (playerInputComponent.JumpPressed && IsOnFloor())
            velocity.Y = JumpVelocity;

        Vector2 inputDir = playerInputComponent.Movement;
        Vector3 localDirection = new Basis(Vector3.Up, _cameraRotationY) * new Vector3(inputDir.X, 0, inputDir.Y);
        localDirection.Y = 0;
        localDirection = localDirection.Normalized();
        Vector3 direction = (Transform.Basis * localDirection).Normalized();
        if (localDirection != Vector3.Zero && !_isChargingBow)
            FaceMoveDirection(localDirection);
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

        RangedWeapon bow = Equipment?.EquippedWeapon as RangedWeapon;
        if ((bow != null) != _lastPhysicsHadRangedWeapon)
        {
            _lastPhysicsHadRangedWeapon = bow != null;
            GD.Print($"[BowDebug] Ranged weapon input path {(bow != null ? "enabled" : "disabled")}: " +
                $"player={Name}, equippedType={Equipment?.EquippedWeapon?.GetType().Name ?? "<none>"}, " +
                $"equippedName={Equipment?.EquippedWeapon?.DisplayName ?? "<none>"}.");
        }

        if (bow != null)
        {
            ProcessBowInput(bow, (float)delta);
            if (_isChargingBow)
                FaceMoveDirection(new Basis(Vector3.Up, _cameraRotationY) * Vector3.Forward);
        }
        else
        {
            CancelBowCharge();
            playerInputComponent.ClearAttack1Events();
            if (States.CanAttack && playerInputComponent.Attack1Pressed)
                PlayAttack1();
            else if (States.CanAttack && playerInputComponent.Attack2Pressed)
                PlayAttack2();
        }
    }

    private void ProcessBowInput(RangedWeapon bow, float delta)
    {
        if (!States.CanAttack)
        {
            if (playerInputComponent.HasPendingAttack1Press ||
                playerInputComponent.HasPendingAttack1Release)
                LogBowDebug($"Dropping attack input: player={Name}, state={States.Current}, " +
                    $"canAttack={States.CanAttack}.");
            CancelBowCharge();
            playerInputComponent.ClearAttack1Events();
            return;
        }

        if (_isChargingBow && _chargingBow != bow)
            CancelBowCharge();

        bool attackPressed = playerInputComponent.ConsumeAttack1Pressed();
        if (!_isChargingBow && attackPressed)
        {
            LogBowDebug($"Processing attack1 press: player={Name}, weapon={bow.DisplayName}, " +
                $"requiredProjectile={bow.RequiredProjectile?.ResourcePath ?? "<null>"}, " +
                $"chargeRange={bow.MinChargeTime:0.###}-{bow.MaxChargeTime:0.###}, " +
                $"state={ReplicatedBowState}.");
            if (!bow.HasValidConfiguration)
            {
                GD.PushError($"[BowDebug] Equipped ranged weapon '{bow.DisplayName}' has invalid configuration: " +
                    $"projectile={bow.RequiredProjectile?.ResourcePath ?? "<null>"}, " +
                    $"projectileScene={bow.ProjectileScene?.ResourcePath ?? "<null>"}, " +
                    $"speed={bow.ProjectileSpeed}, min={bow.MinChargeTime}, max={bow.MaxChargeTime}.");
                playerInputComponent.ClearAttack1Events();
                return;
            }

            if (!HasProjectile(bow.RequiredProjectile))
            {
                GD.PushWarning($"[BowDebug] Required projectile not found: player={Name}, " +
                    $"required={bow.RequiredProjectile.ResourcePath}, inventory={DescribeInventory()}.");
                playerInputComponent.ClearAttack1Events();
                return;
            }

            _isChargingBow = true;
            _chargingBow = bow;
            _bowChargeTime = 0;
            LogBowDebug($"Bow charge started: player={Name}, bow={bow.DisplayName}.");
            BroadcastBowAnimationState("Pulling");
        }

        if (!_isChargingBow || _chargingBow != bow)
            return;

        _bowChargeTime += delta;
        if (_bowChargeTime >= bow.MaxChargeTime && ReplicatedBowState != "Pulled")
        {
            LogBowDebug($"Bow reached max charge: player={Name}, time={_bowChargeTime:0.###}.");
            BroadcastBowAnimationState("Pulled");
        }

        bool attackReleased = playerInputComponent.ConsumeAttack1Released();
        if (!attackReleased && !_isChargingBow)
            return;

        if (!attackReleased && playerInputComponent.Attack1Held)
            return;

        float chargeTime = _bowChargeTime;
        _isChargingBow = false;
        _chargingBow = null;
        _bowChargeTime = 0;
        if (chargeTime < bow.MinChargeTime)
        {
            GD.PushWarning($"[BowDebug] Bow shot cancelled: charge={chargeTime:0.###}, " +
                $"minimum={bow.MinChargeTime:0.###}.");
            BroadcastBowAnimationState("Idle");
            return;
        }

        LogBowDebug($"Bow shot released: player={Name}, charge={chargeTime:0.###}, " +
            $"state={ReplicatedBowState}.");
        BroadcastBowAnimationState("Shooting");
        Vector3 direction = GetBowShotDirection();
        WeaponScene bowScene = Equipment?.GetEquippedWeaponScene();
        if (bowScene == null)
            GD.PushWarning($"[BowDebug] Bow model node is unavailable for {Name}; using player-based projectile origin.");
        Vector3 origin = bowScene != null
            ? bowScene.GlobalPosition + direction * 0.25f
            : GlobalPosition + Vector3.Up * 1.35f + direction * 0.8f;
        RequestBowShot(chargeTime, origin, direction);
    }

    private Vector3 GetBowShotDirection()
    {
        Vector3 cameraDirection = -_camera.GlobalTransform.Basis.Z.Normalized();
        Vector3 horizontalDirection = new(cameraDirection.X, 0, cameraDirection.Z);
        float pitch = Mathf.Clamp(
            Mathf.Atan2(cameraDirection.Y, horizontalDirection.Length()),
            -Mathf.DegToRad(45.0f),
            Mathf.DegToRad(45.0f));

        return horizontalDirection.Normalized() * Mathf.Cos(pitch) +
            Vector3.Up * Mathf.Sin(pitch);
    }

    private void CancelBowCharge()
    {
        if (!_isChargingBow)
            return;

        _isChargingBow = false;
        _chargingBow = null;
        _bowChargeTime = 0;
        BroadcastBowAnimationState("Idle");
    }

    private bool HasProjectile(WeaponProjectile requiredProjectile)
    {
        if (requiredProjectile == null || Inventory == null)
            return false;

        foreach (Stack stack in Inventory.Slots)
            if (stack?.Item is WeaponProjectile projectile &&
                (projectile == requiredProjectile ||
                 projectile.ResourcePath == requiredProjectile.ResourcePath) &&
                stack.ItemCount > 0)
                return true;

        return false;
    }

    private string DescribeInventory()
    {
        if (Inventory?.Slots == null)
            return "<inventory unavailable>";

        var contents = new List<string>();
        foreach (Stack stack in Inventory.Slots)
            if (stack?.Item != null)
                contents.Add($"{stack.Item.GetType().Name}:{stack.Item.ResourcePath}x{stack.ItemCount}");
        return contents.Count == 0 ? "<empty>" : string.Join(", ", contents);
    }

    private void RequestBowShot(float chargeTime, Vector3 origin, Vector3 direction)
    {
        if (!Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer())
        {
            HandleBowShotRequest(chargeTime, origin, direction, 0);
            return;
        }

        RpcId(1, nameof(ReceiveBowShotRequest), chargeTime, origin, direction);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveBowShotRequest(float chargeTime, Vector3 origin, Vector3 direction)
    {
        if (!Multiplayer.IsServer())
            return;

        long sender = Multiplayer.GetRemoteSenderId();
        if (sender != GetMultiplayerAuthority())
            return;

        HandleBowShotRequest(chargeTime, origin, direction, sender);
    }

    private void HandleBowShotRequest(float chargeTime, Vector3 origin, Vector3 direction, long sender)
    {
        RangedWeapon bow = Equipment?.EquippedWeapon as RangedWeapon;
        string rejectionReason = null;
        if (bow == null)
            rejectionReason = "no ranged weapon is equipped";
        else if (!bow.HasValidConfiguration)
            rejectionReason = "ranged weapon configuration is invalid";
        else if (!float.IsFinite(chargeTime) || chargeTime < bow.MinChargeTime)
            rejectionReason = $"charge time {chargeTime:0.###} is below minimum {bow.MinChargeTime:0.###}";
        else if (!IsFinite(origin) || !IsFinite(direction) || direction.LengthSquared() < 0.0001f)
            rejectionReason = "projectile origin or direction is invalid";
        else if (origin.DistanceTo(GlobalPosition) > 5.0f)
            rejectionReason = "projectile origin is too far from the player";
        else if (!HasProjectile(bow.RequiredProjectile))
            rejectionReason = $"required projectile '{bow.RequiredProjectile?.ResourcePath ?? "<null>"}' is not in inventory";

        if (rejectionReason != null)
        {
            GD.PushWarning($"[BowDebug] Rejected bow shot: player={Name}, sender={sender}, reason={rejectionReason}.");
            RejectBowShot(sender);
            return;
        }

        if (!TryConsumeProjectile(bow.RequiredProjectile))
        {
            GD.PushWarning($"[BowDebug] Could not consume required projectile for player={Name}.");
            RejectBowShot(sender);
            return;
        }

        float clampedCharge = Mathf.Clamp(chargeTime, bow.MinChargeTime, bow.MaxChargeTime);
        float damage;
        if (clampedCharge >= bow.MaxChargeTime)
        {
            damage = bow.Damage;
        }
        else
        {
            float chargePercent = Mathf.Clamp(
                (clampedCharge - bow.MinChargeTime) /
                (bow.MaxChargeTime - bow.MinChargeTime),
                0,
                1);
            damage = bow.Damage * 0.5f + bow.Damage * 0.5f * chargePercent;
        }

        InventoryService.Instance?.Publish();
        string projectileName = $"{Name}_Arrow_{++_nextProjectileId}";
        Vector3 flightDirection = direction.Normalized();
        if (Multiplayer.HasMultiplayerPeer())
        {
            Rpc(nameof(SpawnBowProjectile), projectileName, bow.ResourcePath,
                bow.RequiredProjectile.ResourcePath, origin, flightDirection, damage);
        }
        else
        {
            WeaponProjectileScene instance = bow.SpawnProjectile(this, GetTree().CurrentScene, projectileName,
                bow.RequiredProjectile, origin, flightDirection, damage);
            if (instance == null)
                GD.PushError($"[BowDebug] Projectile instance was not created: player={Name}, name={projectileName}.");
            else
                GD.Print($"[BowDebug] Projectile spawned: player={Name}, name={projectileName}, position={instance.GlobalPosition}.");
        }
    }

    private bool TryConsumeProjectile(WeaponProjectile requiredProjectile)
    {
        if (Inventory == null || requiredProjectile == null)
            return false;

        for (int i = 0; i < Inventory.Slots.Count; i++)
        {
            Stack stack = Inventory.Slots[i];
            if (stack?.Item is not WeaponProjectile projectile ||
                (projectile != requiredProjectile &&
                 projectile.ResourcePath != requiredProjectile.ResourcePath) ||
                stack.ItemCount <= 0)
                continue;

            stack.ItemCount--;
            if (stack.ItemCount == 0)
                Inventory.Slots[i] = null;
            Inventory.NotifyChanged();
            return true;
        }

        return false;
    }

    private void RejectBowShot(long peerId)
    {
        if (Multiplayer.HasMultiplayerPeer() && peerId > 1)
            RpcId(peerId, nameof(ReceiveBowShotRejected));
        else
            BroadcastBowAnimationState("Idle");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveBowShotRejected()
    {
        if (Multiplayer.HasMultiplayerPeer() && Multiplayer.GetRemoteSenderId() != 1)
            return;

        _isChargingBow = false;
        _chargingBow = null;
        _bowChargeTime = 0;
        BroadcastBowAnimationState("Idle");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SpawnBowProjectile(
        string projectileName,
        string weaponResourcePath,
        string projectileResourcePath,
        Vector3 origin,
        Vector3 direction,
        float damage)
    {
        if (Multiplayer.HasMultiplayerPeer())
        {
            long sender = Multiplayer.GetRemoteSenderId();
            if (sender != 0 && sender != 1)
                return;
        }

        RangedWeapon bow = GD.Load<RangedWeapon>(weaponResourcePath);
        WeaponProjectile projectile = GD.Load<WeaponProjectile>(projectileResourcePath);
        if (bow == null || bow.ProjectileScene == null || projectile?.Model == null)
        {
            GD.PushError($"[BowDebug] Projectile RPC resource load failed: player={Name}, " +
                $"weaponPath={weaponResourcePath}, loadedWeapon={bow != null}, " +
                $"scene={bow?.ProjectileScene?.ResourcePath ?? "<null>"}, " +
                $"projectilePath={projectileResourcePath}, loadedProjectile={projectile != null}, " +
                $"model={projectile?.Model?.ResourcePath ?? "<null>"}, " +
                $"currentEquipped={Equipment?.EquippedWeapon?.ResourcePath ?? "<null>"}, " +
                $"parent={GetTree().CurrentScene?.GetPath().ToString() ?? "<null>"}.");
            return;
        }

        GD.Print($"[BowDebug] Spawning projectile from resource paths: player={Name}, " +
            $"weapon={bow.ResourcePath}, scene={bow.ProjectileScene.ResourcePath}, " +
            $"projectile={projectile.ResourcePath}, model={projectile.Model.ResourcePath}.");
        WeaponProjectileScene instance = bow.SpawnProjectile(this, GetTree().CurrentScene, projectileName,
            projectile, origin, direction, damage);
        if (instance == null)
            GD.PushError($"[BowDebug] Projectile instance was not created: player={Name}, name={projectileName}.");
        else
            GD.Print($"[BowDebug] Projectile spawned: player={Name}, name={projectileName}, position={instance.GlobalPosition}.");
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    public void interact_show(interact_obj _interact_obj)
    {
        if (playerInputComponent?.HasInputAuthority != true ||
            !GodotObject.IsInstanceValid(_interact_obj) ||
            _nearbyInteractables.Contains(_interact_obj))
            return;

        _nearbyInteractables.Add(_interact_obj);
        _interactButtons.Add(null);
        if (_nearbyInteractables.Count == 1)
            _selectedInteractIndex = 0;
        RefreshInteractButtons();
    }

    public void interact_hide(interact_obj source = null)
    {
        if (source == null)
        {
            foreach (Button button in _interactButtons)
            {
                if (!GodotObject.IsInstanceValid(button)) continue;
                interact_key_list?.RemoveChild(button);
                button.QueueFree();
            }
            _nearbyInteractables.Clear();
            current_interact_obj = null;
            _selectedInteractIndex = 0;
            _interactButtons.Clear();
            RefreshInteractButtons();
            return;
        }

        int index = _nearbyInteractables.IndexOf(source);
        if (index < 0) return;

        if (index < _interactButtons.Count)
        {
            Button button = _interactButtons[index];
            _interactButtons.RemoveAt(index);
            if (GodotObject.IsInstanceValid(button))
            {
                interact_key_list?.RemoveChild(button);
                button.QueueFree();
            }
        }
        _nearbyInteractables.RemoveAt(index);
        if (_nearbyInteractables.Count == 0)
            _selectedInteractIndex = 0;
        else
        {
            if (index < _selectedInteractIndex)
                _selectedInteractIndex--;
            _selectedInteractIndex = Mathf.Clamp(_selectedInteractIndex, 0, _nearbyInteractables.Count - 1);
        }
        RefreshInteractButtons();
    }

    private void RefreshInteractButtons()
    {
        if (interact_key_list == null) return;

        while (_interactButtons.Count < _nearbyInteractables.Count)
            _interactButtons.Add(null);

        for (int i = 0; i < _interactButtons.Count; i++)
        {
            Button button = _interactButtons[i];
            if (button != null && !GodotObject.IsInstanceValid(button))
                button = null;

            if (button == null)
            {
                button = new Button();
                Button createdButton = button;
                button.Pressed += () => OnInteractButtonPressed(createdButton);
                _interactButtons[i] = button;
                interact_key_list.AddChild(button);
            }

            interact_obj interactable = _nearbyInteractables[i];
            button.Text = $"{interactable.interaction_name}  [E]";
            bool isSelected = i == _selectedInteractIndex;
            button.SelfModulate = isSelected
                ? new Color(1.0f, 0.82f, 0.2f)
                : Colors.White;
            button.Show();
        }

        for (int i = _interactButtons.Count - 1; i >= _nearbyInteractables.Count; i--)
        {
            Button button = _interactButtons[i];
            _interactButtons.RemoveAt(i);
            if (GodotObject.IsInstanceValid(button))
            {
                interact_key_list.RemoveChild(button);
                button.QueueFree();
            }
        }

        current_interact_obj = _nearbyInteractables.Count > 0
            ? _nearbyInteractables[_selectedInteractIndex]
            : null;
        interact_key_list.Visible = _nearbyInteractables.Count > 0;
    }

    private void OnInteractButtonPressed(Button button)
    {
        int index = _interactButtons.IndexOf(button);
        if (index < 0) return;
        if (index != _selectedInteractIndex)
        {
            SelectInteractable(index);
            return;
        }

        pressed_interact_key();
    }

    private void SelectInteractable(int index)
    {
        if (index < 0 || index >= _nearbyInteractables.Count)
            return;
        _selectedInteractIndex = index;
        current_interact_obj = _nearbyInteractables[index];
        RefreshInteractButtons();
    }

    private void CycleInteractable(int direction)
    {
        if (_nearbyInteractables.Count < 2)
            return;
        _selectedInteractIndex = (_selectedInteractIndex + direction + _nearbyInteractables.Count)
            % _nearbyInteractables.Count;
        current_interact_obj = _nearbyInteractables[_selectedInteractIndex];
        RefreshInteractButtons();
    }
    private void pressed_interact_key()
    {

        if (!States.CanMove || !GodotObject.IsInstanceValid(current_interact_obj)) return;
        if (current_interact_obj.disable_input_on_interact && !BeginInteraction(current_interact_obj)) return;
        current_interact_obj.interact(this);
    }

    public override void _Input(InputEvent ev)
    {
        if (playerInputComponent?.HasInputAuthority != true) return;
        if (States.Current == PlayerStateMachine.State.Trading) return;

        if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp })
        {
            if (States.CanMove && playerInputComponent.IsInputEnabled && _nearbyInteractables.Count > 1)
            {
                CycleInteractable(-1);
                GetViewport().SetInputAsHandled();
            }
            return;
        }
        if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown })
        {
            if (States.CanMove && playerInputComponent.IsInputEnabled && _nearbyInteractables.Count > 1)
            {
                CycleInteractable(1);
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (ev is not InputEventKey { Pressed: true, Echo: false } key) return;
        bool closeOpenInventory =
            key.PhysicalKeycode == Key.Tab ||
            key.Keycode == Key.Escape && !States.CanMove;
        if (!playerInputComponent.IsInputEnabled)
        {
            if (closeOpenInventory &&
                States.Current is PlayerStateMachine.State.Chest or PlayerStateMachine.State.Inventory)
            {
                CloseInventory();
                GetViewport().SetInputAsHandled();
            }
            return;
        }

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
        Chest previousChest = _openChest;
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
            if (_inputLockSource == previousChest)
                EndInteraction(previousChest);
        }
    }

    public bool BeginTrading(TradingStand stand)
    {
        if (stand == null || !States.CanMove || _tradingStand != null)
            return false;

        _tradingStand = stand;
        if (States.Transition(PlayerStateMachine.State.Trading))
            return true;

        _tradingStand = null;
        return false;
    }

    public bool BeginInteraction(interact_obj source)
    {
        if (source == null || !GodotObject.IsInstanceValid(source) ||
            playerInputComponent?.HasInputAuthority != true || _inputLockSource != null)
            return false;

        _inputLockSource = source;
        playerInputComponent.SetInputEnabled(false);
        return true;
    }

    public void EndInteraction(interact_obj source)
    {
        if (_inputLockSource != source)
            return;

        _inputLockSource = null;
        playerInputComponent?.SetInputEnabled(true);
    }

    public void SetTradeDialog(Node dialog)
    {
        _tradeDialog = dialog;
    }

    public void ShowTradeOffer(int id, int price, string itemName)
    {
        if (GodotObject.IsInstanceValid(_tradeDialog))
            _tradeDialog.Call("show_trade_offer", id, price, itemName);
    }

    public void ShowTradeResult(string message, bool closeOffer)
    {
        if (GodotObject.IsInstanceValid(_tradeDialog))
            _tradeDialog.Call("show_trade_result", message, closeOffer);
    }

    public void SetMoney(int amount)
    {
        if (Money == amount) return;
        Money = amount;
        MoneyChanged?.Invoke(Money);
    }

    private void OnEquipmentInventoryChanged()
    {
        Stack weaponStack = EquipmentInventory?.Valid(0) == true
            ? EquipmentInventory.Slots[0]
            : null;
        GD.Print($"[Combat] Equipment slot changed: player={Name}, " +
            $"item={weaponStack?.Item?.DisplayName ?? "<empty>"}, count={weaponStack?.ItemCount ?? 0}.");
        if (weaponStack == null)
        {
            EnsureEquipmentComponent().EquipWeapon(null);
            return;
        }

        if (weaponStack.Item is not Weapon weapon)
        {
            GD.PushError($"Player '{Name}' weapon slot only accepts Weapon items.");
            return;
        }

        GD.Print($"[BowDebug] Equipment inventory resolved: player={Name}, " +
            $"weaponType={weapon.GetType().Name}, weaponPath={weapon.ResourcePath}.");
        GD.Print($"[Combat] Equipping weapon from slot: player={Name}, weapon={weapon.DisplayName}, " +
            $"model={(weapon.WeaponModel?.ResourcePath ?? "<missing>")}.");
        EnsureEquipmentComponent().EquipWeapon(weapon);
    }

    private EquipmentComponent EnsureEquipmentComponent()
    {
        if (GodotObject.IsInstanceValid(Equipment))
            return Equipment;

        Equipment = GetNodeOrNull<EquipmentComponent>("EquipmentComponent") ??
            FindChild("EquipmentComponent", true, false) as EquipmentComponent;
        if (Equipment != null)
            return Equipment;

        GD.PushWarning($"Player '{Name}' scene has no EquipmentComponent; adding one at runtime.");
        Equipment = new EquipmentComponent { Name = "EquipmentComponent" };
        AddChild(Equipment);
        return Equipment;
    }

    public void EndTrading(TradingStand stand)
    {
        if (_tradingStand != stand)
            return;

        _tradingStand = null;
        _tradeDialog = null;
        if (States.Current == PlayerStateMachine.State.Trading)
            States.Transition(PlayerStateMachine.State.Locomotion);
    }

    public void CloseInventory()
    {
        InventoryService.Instance?.Send("close", this);
        Chest previousChest = _openChest;
        _openChest = null;
        if (States.Current is PlayerStateMachine.State.Chest or PlayerStateMachine.State.Inventory)
            States.Transition(PlayerStateMachine.State.Locomotion);
        InventoryUI?.Close();
        if (_inputLockSource == previousChest && previousChest != null)
            EndInteraction(previousChest);
    }


}
