using Godot;

public partial class AnimatedEntity : Entity
{
	private const string RuntimeLibraryName = "entity_runtime";
	private const string RuntimeLibraryPrefix = RuntimeLibraryName + "/";

	[ExportGroup("Model")]
	[Export] public PackedScene ModelScene { get; set; }

	[ExportGroup("Animation Sources")]
	[Export] public PackedScene IdleAnimationScene { get; set; }
	[Export] public PackedScene WalkAnimationScene { get; set; }
	[Export] public PackedScene RunAnimationScene { get; set; }
	[Export] public PackedScene RunWithSwordAnimationScene { get; set; }
	[Export] public PackedScene Attack1AnimationScene { get; set; }
	[Export] public PackedScene Attack2AnimationScene { get; set; }
	[Export] public PackedScene CrouchedIdleAnimationScene { get; set; }
	[Export] public PackedScene CrouchedWalkAnimationScene { get; set; }
	[Export] public double BlendDuration { get; set; } = 0.2;
	[Export] public float ModelTurnSpeed { get; set; } = 10.0f;
	[Export] public float HealthBarHeight { get; set; } = 2.2f;

	[ExportGroup("Hitboxes")]
	[Export(PropertyHint.Range, "0.01,1.0,0.01")]
	public float HeadHitboxRadius { get; set; } = 0.208f;
	[Export(PropertyHint.Range, "0.01,1.0,0.01")]
	public float FootHitboxRadius { get; set; } = 0.13f;
	[Export(PropertyHint.Range, "0.01,1.0,0.01")]
	public float DefaultHitboxRadius { get; set; } = 0.169f;

	private float _modelBaseRotationY;
	private float _replicatedModelFacing;

	[Export]
	public float ReplicatedModelFacing
	{
		get => _replicatedModelFacing;
		set => _replicatedModelFacing = Mathf.Wrap(value, -Mathf.Pi, Mathf.Pi);
	}

	private EquipmentComponent _equipmentComponent;
	private Skeleton3D _skeleton;
	private CombatStatsComponent _combatStats;
	public CombatStatsComponent CombatStats => _combatStats;
	private ProgressBar _healthBar;

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

	private void AddUpperBodyAnimation(AnimationLibrary library, PackedScene sourceScene, string targetName)
	{
		if (sourceScene == null)
			return;

		Node sourceRoot = sourceScene.Instantiate();
		AnimationPlayer sourcePlayer = FindAnimationPlayer(sourceRoot);
		if (sourcePlayer == null)
		{
			GD.PushWarning($"Animation source for '{targetName}' has no AnimationPlayer.");
			sourceRoot.Free();
			return;
		}

		foreach (StringName sourceName in sourcePlayer.GetAnimationList())
		{
			if (sourceName.ToString().Equals("RESET", System.StringComparison.OrdinalIgnoreCase))
				continue;

			Animation sourceAnimation = sourcePlayer.GetAnimation(sourceName);
			Animation attackAnimation = sourceAnimation.Duplicate(true) as Animation;
			if (attackAnimation == null)
			{
				sourceRoot.Free();
				GD.PushWarning($"Animation source for '{targetName}' could not be duplicated.");
				return;
			}

			attackAnimation.LoopMode = Animation.LoopModeEnum.None;
			bool hasUpperBodyTrack = false;
			for (int track = 0; track < attackAnimation.GetTrackCount(); track++)
			{
				bool isUpperBodyTrack = IsUpperBodyTrack(attackAnimation.TrackGetPath(track).ToString());
				attackAnimation.TrackSetEnabled(track, isUpperBodyTrack);
				hasUpperBodyTrack |= isUpperBodyTrack;
			}

			if (!hasUpperBodyTrack)
			{
				sourceRoot.Free();
				GD.PushWarning($"Animation source for '{targetName}' has no recognizable upper-body bone tracks.");
				return;
			}

			library.AddAnimation(targetName, attackAnimation);
			sourceRoot.Free();
			return;
		}

		GD.PushWarning($"Animation source for '{targetName}' has no playable animation.");
		sourceRoot.Free();
	}

	private void BuildAnimationTree()
	{
		var blendTree = new AnimationNodeBlendTree();
		var locomotion = new AnimationNodeTransition
		{
			XfadeTime = (float)BlendDuration
		};
		blendTree.AddNode("Locomotion", locomotion);
		var animationNames = new[]
		{
			"Idle", "Walk", "Run", "RunWithSword", "CrouchedIdle", "CrouchedWalk"
		};
		var availableAnimations = new System.Collections.Generic.List<string>();

		foreach (string animationName in animationNames)
		{
			StringName qualifiedName = RuntimeLibraryPrefix + animationName;
			if (!_animationPlayer.HasAnimation(qualifiedName))
				continue;

			int transitionIndex = availableAnimations.Count;
			availableAnimations.Add(animationName);
			locomotion.AddInput(animationName);

			string nodeName = $"Animation_{animationName}";
			blendTree.AddNode(nodeName, new AnimationNodeAnimation
			{
				Animation = qualifiedName
			});
			blendTree.ConnectNode("Locomotion", transitionIndex, nodeName);
		}

		if (availableAnimations.Count == 0)
		{
			GD.PushError("No usable locomotion animations were assigned to AnimatedEntity.");
			return;
		}
		_availableAnimationNames = availableAnimations.ToArray();

		var attackClip = new AnimationNodeAnimation();
		if (_animationPlayer.HasAnimation(RuntimeLibraryPrefix + "Attack1"))
			attackClip.Animation = RuntimeLibraryPrefix + "Attack1";
		else if (_animationPlayer.HasAnimation(RuntimeLibraryPrefix + "Attack2"))
			attackClip.Animation = RuntimeLibraryPrefix + "Attack2";

		blendTree.AddNode("AttackClip", attackClip);

		var attack = new AnimationNodeOneShot
		{
			FadeInTime = (float)BlendDuration,
			FadeOutTime = (float)BlendDuration,
			BreakLoopAtEnd = true,
			FilterEnabled = true
		};
		blendTree.AddNode("Attack", attack);
		blendTree.ConnectNode("Attack", 0, "Locomotion");
		blendTree.ConnectNode("Attack", 1, "AttackClip");
		blendTree.ConnectNode("output", 0, "Attack");

		AddUpperBodyFilter(attack, "Attack1");
		AddUpperBodyFilter(attack, "Attack2");

		_animationTree = new AnimationTree
		{
			Name = "AnimationTree",
			TreeRoot = blendTree
		};
		_modelRoot.AddChild(_animationTree);
		_animationTree.AnimPlayer = _animationTree.GetPathTo(_animationPlayer);
		_animationPlayer.Stop();
		_animationTree.Active = true;
	}

	private void AddUpperBodyFilter(AnimationNodeOneShot attackNode, string animationName)
	{
		StringName animationPath = RuntimeLibraryPrefix + animationName;
		if (!_animationPlayer.HasAnimation(animationPath))
			return;

		Animation animation = _animationPlayer.GetAnimation(animationPath);
		for (int track = 0; track < animation.GetTrackCount(); track++)
		{
			if (IsUpperBodyTrack(animation.TrackGetPath(track).ToString()))
				attackNode.SetFilterPath(animation.TrackGetPath(track), true);
		}
	}

	private static bool IsUpperBodyTrack(string trackPath)
	{
		int boneStart = trackPath.LastIndexOf(':');
		if (boneStart < 0)
			return false;

		string boneName = trackPath[(boneStart + 1)..];
		int pathEnd = boneName.IndexOfAny(new[] { '/', ':' });
		if (pathEnd >= 0)
			boneName = boneName[..pathEnd];

		return boneName.Contains("spine", System.StringComparison.OrdinalIgnoreCase) ||
			boneName.Contains("chest", System.StringComparison.OrdinalIgnoreCase) ||
			boneName.Contains("neck", System.StringComparison.OrdinalIgnoreCase) ||
			boneName.Contains("head", System.StringComparison.OrdinalIgnoreCase) ||
			boneName.Contains("shoulder", System.StringComparison.OrdinalIgnoreCase) ||
			boneName.Contains("clavicle", System.StringComparison.OrdinalIgnoreCase) ||
			boneName.Contains("upperarm", System.StringComparison.OrdinalIgnoreCase) ||
			boneName.Contains("forearm", System.StringComparison.OrdinalIgnoreCase) ||
			boneName.Contains("arm", System.StringComparison.OrdinalIgnoreCase) ||
			boneName.Contains("hand", System.StringComparison.OrdinalIgnoreCase);
	}

	private string _replicatedAnimation = "Idle";
	private string _replicatedAttack = "";
	private AnimationPlayer _animationPlayer;
	private AnimationTree _animationTree;
	private Node3D _modelRoot;
	private string[] _availableAnimationNames = System.Array.Empty<string>();
	private bool _attackWasActive;

	[Export]
	public string ReplicatedAttack
	{
		get => _replicatedAttack;
		set
		{
			if (_replicatedAttack == value)
				return;

			_replicatedAttack = value;
			if (IsInsideTree())
				ApplyAttack(value);
		}
	}

	public override void _Ready()
	{
		base._Ready();
		_equipmentComponent = GetNodeOrNull<EquipmentComponent>("EquipmentComponent");
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
		AddAnimation(runtimeLibrary, IdleAnimationScene, "Idle");
		AddAnimation(runtimeLibrary, WalkAnimationScene, "Walk");
		AddAnimation(runtimeLibrary, RunAnimationScene, "Run");
		AddAnimation(runtimeLibrary, RunWithSwordAnimationScene, "RunWithSword");
		AddAnimation(runtimeLibrary, CrouchedIdleAnimationScene, "CrouchedIdle");
		AddAnimation(runtimeLibrary, CrouchedWalkAnimationScene, "CrouchedWalk");
		AddUpperBodyAnimation(runtimeLibrary, Attack1AnimationScene, "Attack1");
		AddUpperBodyAnimation(runtimeLibrary, Attack2AnimationScene, "Attack2");

		BuildAnimationTree();

		ApplyAnimation(_replicatedAnimation);
		ApplyAttack(_replicatedAttack);
	}

	private void CreateHealthBar()
	{
		if (Health == null)
			return;

		var viewport = new SubViewport
		{
			Name = "HealthBarViewport",
			Size = new Vector2I(180, 22),
			TransparentBg = true,
			OwnWorld3D = false,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always
		};
		AddChild(viewport);

		var background = new ColorRect
		{
			Color = new Color(0.08f, 0.08f, 0.08f, 0.85f),
			Position = Vector2.Zero,
			Size = new Vector2(180, 22),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		viewport.AddChild(background);

		_healthBar = new ProgressBar
		{
			Position = new Vector2(2, 2),
			Size = new Vector2(176, 18),
			MinValue = 0,
			MaxValue = Health.MaxHealth,
			ShowPercentage = false,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_healthBar.AddThemeStyleboxOverride("background", new StyleBoxFlat
		{
			BgColor = new Color(0.2f, 0.12f, 0.12f),
			ContentMarginLeft = 0,
			ContentMarginRight = 0,
			ContentMarginTop = 0,
			ContentMarginBottom = 0
		});
		_healthBar.AddThemeStyleboxOverride("fill", new StyleBoxFlat
		{
			BgColor = new Color(0.82f, 0.12f, 0.14f),
			ContentMarginLeft = 0,
			ContentMarginRight = 0,
			ContentMarginTop = 0,
			ContentMarginBottom = 0
		});
		viewport.AddChild(_healthBar);

		var sprite = new Sprite3D
		{
			Name = "HealthBar",
			Texture = viewport.GetTexture(),
			Position = new Vector3(0, HealthBarHeight, 0),
			PixelSize = 0.01f,
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			Shaded = false,
			NoDepthTest = true,
			AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass
		};
		AddChild(sprite);

		Health.HealthChanged += UpdateHealthBar;
		UpdateHealthBar(Health.CurrentHealth, Health.MaxHealth);
	}

	private void UpdateHealthBar(float currentHealth, float maxHealth)
	{
		if (_healthBar == null)
			return;

		_healthBar.MaxValue = maxHealth;
		_healthBar.Value = currentHealth;
	}

	public override void _ExitTree()
	{
		if (Health != null)
		{
			Health.HealthChanged -= UpdateHealthBar;
			Health.HealthChanged -= ReplicateHealth;
		}
	}

	private void ReplicateHealth(float currentHealth, float maxHealth)
	{
		if (Multiplayer.HasMultiplayerPeer() && Multiplayer.IsServer())
			Rpc(nameof(ReceiveReplicatedHealth), currentHealth);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceiveReplicatedHealth(float currentHealth)
	{
		if (Multiplayer.IsServer() || Multiplayer.GetRemoteSenderId() != 1)
		{
			GD.PushWarning($"[Combat] Rejected health update from peer {Multiplayer.GetRemoteSenderId()} for {GetPath()}.");
			return;
		}

		Health?.SetReplicatedHealth(currentHealth);
	}

	public void RequestHealthSync()
	{
		if (!Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer())
			return;

		RpcId(1, nameof(RequestReplicatedHealth));
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestReplicatedHealth()
	{
		if (!Multiplayer.IsServer() || Health == null)
			return;

		long peerId = Multiplayer.GetRemoteSenderId();
		if (peerId > 1)
			RpcId(peerId, nameof(ReceiveReplicatedHealth), Health.CurrentHealth);
	}

	private void GenerateBoneHitboxes()
	{
		_skeleton = FindSkeleton(_modelRoot);
		if (_skeleton == null)
		{
			GD.PushWarning($"AnimatedEntity '{Name}' has no Skeleton3D; bone hitboxes were not created.");
			return;
		}

		for (int i = 0; i < _skeleton.GetBoneCount(); i++)
		{
			string boneName = _skeleton.GetBoneName(i).ToString();
			if (!BoneHitbox.ShouldCreateForBone(boneName))
				continue;

			var attachment = new BoneAttachment3D
			{
				Name = $"HitboxAttachment_{i}",
				BoneName = boneName
			};
			_skeleton.AddChild(attachment);
			var hitbox = new BoneHitbox
			{
				Name = $"Hitbox_{i}"
			};
			hitbox.Initialize(this, boneName, GetBoneHitboxRadius(boneName));
			attachment.AddChild(hitbox);
		}
	}

	private float GetBoneHitboxRadius(string boneName)
	{
		if (boneName.Contains("head", System.StringComparison.OrdinalIgnoreCase))
			return HeadHitboxRadius;
		if (boneName.Contains("foot", System.StringComparison.OrdinalIgnoreCase))
			return FootHitboxRadius;
		return DefaultHitboxRadius;
	}

	private static Skeleton3D FindSkeleton(Node node)
	{
		if (node is Skeleton3D skeleton)
			return skeleton;

		foreach (Node child in node.GetChildren())
		{
			Skeleton3D result = FindSkeleton(child);
			if (result != null)
				return result;
		}

		return null;
	}

	private void EquipDefaultFootwear()
	{
		if (_equipmentComponent == null)
		{
			GD.PushWarning("AnimatedEntity: EquipmentComponent child not found; default footwear was not equipped.");
			return;
		}

		PackedScene footwearScene = GD.Load<PackedScene>("res://s.tscn");
		if (footwearScene == null)
		{
			GD.PushError("AnimatedEntity: Could not load footwear scene at res://s.tscn.");
			return;
		}

		GD.Print($"AnimatedEntity: Requesting default footwear for {Name}.");
		_equipmentComponent.Equip(EquipmentComponent.Slot.Feet, footwearScene);
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

		bool attackActive = _animationTree.Get("parameters/Attack/active").AsBool();
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
		GetWeaponDamageZone()?.EndSwing();
		if (!Multiplayer.HasMultiplayerPeer() || GetMultiplayerAuthority() == Multiplayer.GetUniqueId())
			ReplicatedAttack = "";
		else
			_replicatedAttack = "";
	}

	public void PlayIdle() => SetAnimationState("Idle");
	public void PlayWalk() => SetAnimationState("Walk");
	public void PlayRun() => SetAnimationState("Run");
	public void PlayRunWithSword() => SetAnimationState("RunWithSword");
	public void PlayCrouchedIdle() => SetAnimationState(
		_animationPlayer?.HasAnimation(RuntimeLibraryPrefix + "CrouchedIdle") == true
			? "CrouchedIdle"
			: "Idle");
	public void PlayCrouchedWalk() => SetAnimationState(
		_animationPlayer?.HasAnimation(RuntimeLibraryPrefix + "CrouchedWalk") == true
			? "CrouchedWalk"
			: "Walk");
	public void PlayAttack1() => SetAttackState("Attack1");
	public void PlayAttack2() => SetAttackState("Attack2");

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

	private void SetAttackState(string animationName)
	{
		GD.Print($"[Combat] Attack requested: entity={Name}, attack={animationName}.");
		if (Multiplayer.HasMultiplayerPeer() && GetMultiplayerAuthority() != Multiplayer.GetUniqueId())
			return;

		if (_animationPlayer == null ||
			!_animationPlayer.HasAnimation(RuntimeLibraryPrefix + animationName))
		{
			GD.PushWarning($"No animation source is assigned for '{animationName}'.");
			return;
		}

		ReplicatedAttack = animationName;
	}

	private void AddAnimation(AnimationLibrary library, PackedScene sourceScene, string targetName)
	{
		if (sourceScene == null)
			return;

		Node sourceRoot = sourceScene.Instantiate();
		AnimationPlayer sourcePlayer = FindAnimationPlayer(sourceRoot);
		if (sourcePlayer == null)
		{
			GD.PushWarning($"Animation source for '{targetName}' has no AnimationPlayer.");
			sourceRoot.Free();
			return;
		}

		foreach (StringName sourceName in sourcePlayer.GetAnimationList())
		{
			if (sourceName.ToString().Equals("RESET", System.StringComparison.OrdinalIgnoreCase))
				continue;

			library.AddAnimation(targetName, sourcePlayer.GetAnimation(sourceName));
			sourceRoot.Free();
			return;
		}

		GD.PushWarning($"Animation source for '{targetName}' has no playable animation.");
		sourceRoot.Free();
	}

	private static AnimationPlayer FindAnimationPlayer(Node node)
	{
		if (node is AnimationPlayer animationPlayer)
			return animationPlayer;

		foreach (Node child in node.GetChildren())
		{
			AnimationPlayer result = FindAnimationPlayer(child);
			if (result != null)
				return result;
		}

		return null;
	}

	private void ApplyAnimation(string animationName)
	{
		if (_animationTree == null || string.IsNullOrWhiteSpace(animationName))
			return;

		string targetAnimation = System.Array.IndexOf(_availableAnimationNames, animationName) >= 0
			? animationName
			: _availableAnimationNames[0];
		_animationTree.Set("parameters/Locomotion/transition_request", targetAnimation);
	}

	private void ApplyAttack(string animationName)
	{
		if (_animationTree == null || string.IsNullOrWhiteSpace(animationName))
			return;

		StringName qualifiedName = RuntimeLibraryPrefix + animationName;
		if (!_animationPlayer.HasAnimation(qualifiedName))
			return;

		WeaponDamageZone damageZone = GetWeaponDamageZone();
		GD.Print($"[Combat] Applying attack: entity={Name}, attack={animationName}, " +
			$"authority={GetMultiplayerAuthority()}, localPeer={(Multiplayer.HasMultiplayerPeer() ? Multiplayer.GetUniqueId() : 1)}, " +
			$"damageZone={damageZone?.GetPath().ToString() ?? "<missing>"}, animationTreeReady=True.");
		_animationTree.Set("parameters/AttackClip/animation", qualifiedName);
		_animationTree.Set("parameters/Attack/request", (int)AnimationNodeOneShot.OneShotRequest.Fire);
		if (damageZone == null)
			GD.PushWarning($"[Combat] Cannot start attack: entity '{GetPath()}' has no WeaponDamageZone under it.");
		else
			damageZone.BeginSwing();
	}

	private WeaponDamageZone GetWeaponDamageZone()
	{
		return FindChild("DamageZone", true, false) as WeaponDamageZone;
	}
}