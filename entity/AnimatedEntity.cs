using Godot;
using System;

public partial class AnimatedEntity : Entity
{
	private const string RuntimeLibraryName = "entity_runtime";
	private const string RuntimeLibraryPrefix = RuntimeLibraryName + "/";

	[ExportGroup("AI")]
    [Export] public TaskData.Races Race { get; set; } = TaskData.Races.None;
    [Export] public Godot.Collections.Array<TaskData.Skills> Skills { get; set; } = null;

	[ExportGroup("Model")]
	[Export] public PackedScene ModelScene { get; set; }

	[ExportGroup("Equipment")]
	[Export] public Weapon StartingWeapon { get; set; }

	[ExportGroup("Animation Sources")]
	[Export] public PackedScene IdleAnimationScene { get; set; }
	[Export] public PackedScene WalkAnimationScene { get; set; }
	[Export] public PackedScene RunAnimationScene { get; set; }
	[Export] public PackedScene JumpingAnimationScene { get; set; }
	[Export] public PackedScene RunWithSwordAnimationScene { get; set; }
	[Export] public PackedScene Attack1AnimationScene { get; set; }
	[Export] public PackedScene Attack2AnimationScene { get; set; }
	[Export] public PackedScene CrouchedIdleAnimationScene { get; set; }
	[Export] public PackedScene CrouchedWalkAnimationScene { get; set; }
	[Export] public float RunMultiplier { get; set; } = 1.5f;
	[Export] public double BlendDuration { get; set; } = 0.2;
	[Export] public float ModelTurnSpeed { get; set; } = 10.0f;

	private float _modelBaseRotationY;
	private bool _attackWasActive;
	private float _replicatedModelFacing;

	[Export]
	public float ReplicatedModelFacing
	{
		get => _replicatedModelFacing;
		set => _replicatedModelFacing = Mathf.Wrap(value, -Mathf.Pi, Mathf.Pi);
	}

	private EquipmentComponent _equipmentComponent;
	private CombatStatsComponent _combatStats;
	public InventoryComponent Inventory { get; private set; }
	public EquipmentComponent Equipment => _equipmentComponent;
	public CombatStatsComponent CombatStats => _combatStats;
	public Weapon EquippedWeapon => _equipmentComponent?.EquippedWeapon;
	public HandItem EquippedHandItem => _equipmentComponent?.EquippedHandItem;
	public WeaponScene EquippedWeaponScene => _equipmentComponent?.GetEquippedWeaponScene();

	/// <summary>Called after a weapon hit has been applied to one of this entity's bone hitboxes.</summary>
	public virtual void OnBoneHit(string boneName, long attackId, Vector3 knockbackDirection) { }

	[Export]
	
	public string ReplicatedAnimation
	{
		get => _replicatedAnimation;
		set
		{
			if (_replicatedAnimation == value)
				return;

			_replicatedAnimation = value;
			if (IsInsideTree())
				ApplyAnimation(value);
		}
	}

	public override void _Ready()
	{
		base._Ready();
		_equipmentComponent = GetNodeOrNull<EquipmentComponent>("EquipmentComponent");
		Inventory = _equipmentComponent;
		if (_equipmentComponent == null)
		{
			GD.PushError($"AnimatedEntity '{Name}' requires an EquipmentComponent.");
			return;
		}

		_equipmentComponent.Changed += OnEquipmentChanged;
		_combatStats = GetNodeOrNull<CombatStatsComponent>("CombatStatsComponent");
		AddToGroup("animated_entities");
		if (Health != null)
			Health.HealthChanged += ReplicateHealth;
		CreateHealthBar();
		_modelRoot = GetNodeOrNull<Node3D>("Model");
		if (_modelRoot == null)
			return;

		_modelBaseRotationY = _modelRoot.Rotation.Y;
		if (ModelScene == null)
			return;

		_modelRoot.AddChild(ModelScene.Instantiate<Node3D>());
		if (_equipmentComponent.Valid(0) && _equipmentComponent.Slots[0] == null && StartingWeapon != null)
		{
			_equipmentComponent.Slots[0] = new Stack { Item = StartingWeapon, ItemCount = 1 };
			_equipmentComponent.NotifyChanged();
		}
		else
		{
			OnEquipmentChanged();
		}
		GenerateBoneHitboxes();
		EquipDefaultFootwear();
		_animationPlayer = FindAnimationPlayer(_modelRoot);
		if (_animationPlayer == null)
		{
			GD.PrintErr("The assigned model does not contain an AnimationPlayer.");
			return;
		}

		var runtimeLibrary = new AnimationLibrary();
		_animationPlayer.AddAnimationLibrary(RuntimeLibraryName, runtimeLibrary);
		_runtimeAnimationLibrary = runtimeLibrary;
		AddAnimation(runtimeLibrary, IdleAnimationScene, "Idle");
		AddAnimation(runtimeLibrary, WalkAnimationScene, "Walk");
		AddAnimation(runtimeLibrary, RunAnimationScene, "Run");
		AddAnimation(runtimeLibrary, JumpingAnimationScene, "Jumping");
		AddAnimation(runtimeLibrary, RunWithSwordAnimationScene, "RunWithSword");
		AddAnimation(runtimeLibrary, CrouchedIdleAnimationScene, "CrouchedIdle");
		AddAnimation(runtimeLibrary, CrouchedWalkAnimationScene, "CrouchedWalk");
		MeleeWeapon meleeWeapon = EquippedWeapon as MeleeWeapon;
		AddWeaponAttackAnimation(runtimeLibrary, Attack1AnimationScene, "Attack1",
			meleeWeapon?.Attack1AnimationScene);
		AddWeaponAttackAnimation(runtimeLibrary, Attack2AnimationScene, "Attack2",
			meleeWeapon?.Attack2AnimationScene);
		AddUpperBodyAnimation(runtimeLibrary, EquippedHandItem?.ActionAnimationScene, "ItemAction");
		AddRangedWeaponAnimations(runtimeLibrary, EquippedWeapon as RangedWeapon);
		LogBowDebug($"Animation setup: entity={Name}, player={_animationPlayer.GetPath()}, " +
			$"tree={_animationTree?.GetPath().ToString() ?? "<not-built>"}, " +
			$"pull={_animationPlayer.HasAnimation(RuntimeLibraryPrefix + "BowPull")}, " +
			$"hold={_animationPlayer.HasAnimation(RuntimeLibraryPrefix + "BowHold")}, " +
			$"shoot={_animationPlayer.HasAnimation(RuntimeLibraryPrefix + "BowShoot")}.");

		BuildAnimationTree();

		ApplyAnimation(_replicatedAnimation);
		if (!string.IsNullOrEmpty(_replicatedAttack))
			ApplyOneShot(_replicatedAttack);
		ApplyBowState(_replicatedBowState);
	}

	public override void _ExitTree()
	{
		if (GodotObject.IsInstanceValid(_equipmentComponent))
			_equipmentComponent.Changed -= OnEquipmentChanged;
		if (Health != null)
		{
			Health.HealthChanged -= UpdateHealthBar;
			Health.HealthChanged -= ReplicateHealth;
		}
	}

	public override void _Process(double delta)
	{
		if (_modelRoot != null)
		{
			Vector3 modelRotation = _modelRoot.Rotation;
			float targetRotation = _modelBaseRotationY + _replicatedModelFacing;
			modelRotation.Y = Mathf.LerpAngle(modelRotation.Y, targetRotation,
				Mathf.Clamp(ModelTurnSpeed * (float)delta, 0.0f, 1.0f));
			_modelRoot.Rotation = modelRotation;
		}

		if (_animationTree == null)
			return;

		UpdateBow(delta);

		bool attackActive = !string.IsNullOrEmpty(_replicatedAttack) &&
			_animationTree.Get("parameters/OneShot/active").AsBool();
		if (attackActive)
		{
			if (!_attackWasActive)
				GD.Print($"[Combat] Attack animation active: entity={Name}, attack={_replicatedAttack}.");
			_attackWasActive = true;
			return;
		}

		if (!_attackWasActive)
			return;

		_attackWasActive = false;
		GD.Print($"[Combat] Attack animation finished: entity={Name}, attack={_replicatedAttack}.");
		WeaponScene weaponScene = EquippedWeaponScene;
		if (weaponScene != null)
			weaponScene.EndSwing();
		else
			GetWeaponDamageZone()?.EndSwing();
		if (!Multiplayer.HasMultiplayerPeer() || GetMultiplayerAuthority() == Multiplayer.GetUniqueId())
			ReplicatedAttack = "";
		else
			_replicatedAttack = "";
	}

	public void FaceMoveDirection(Vector3 localDirection)
	{
		if (localDirection.LengthSquared() <= 0.0001f)
			return;
		if (Multiplayer.HasMultiplayerPeer() && GetMultiplayerAuthority() != Multiplayer.GetUniqueId())
			return;

		ReplicatedModelFacing = Mathf.Atan2(-localDirection.X, -localDirection.Z);
	}

	private void SetAnimationState(string animationName)
	{
		if (Multiplayer.HasMultiplayerPeer() && GetMultiplayerAuthority() != Multiplayer.GetUniqueId())
			return;

		ReplicatedAnimation = animationName;
	}

	private WeaponDamageZone GetWeaponDamageZone()
	{
		return FindChild("DamageZone", true, false) as WeaponDamageZone;
	}
}
