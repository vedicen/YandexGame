using Godot;

public partial class AnimatedEntity : Entity
{
	private const string RuntimeLibraryName = "entity_runtime";
	private const string RuntimeLibraryPrefix = RuntimeLibraryName + "/";
	private static readonly float MaxBowAimPitch = Mathf.DegToRad(45.0f);

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
	[Export] public PackedScene BowPullAnimationScene { get; set; }
	[Export] public PackedScene BowHoldAnimationScene { get; set; }
	[Export] public PackedScene BowShootAnimationScene { get; set; }
	[Export] public float RunMultiplier { get; set; } = 1.5f;
	[Export] public double BlendDuration { get; set; } = 0.2;
	[Export] public float ModelTurnSpeed { get; set; } = 10.0f;
	[Export] public float HealthBarHeight { get; set; } = 2.2f;
	[Export] public bool BowDebugLogging { get; set; }
	[Export] public float BowBodyYawDegrees { get; set; } = -60.0f;
	[Export] public float BowCrouchBodyPitchDegrees { get; set; } = 20.0f;

	public void LogBowDebug(string message)
	{
		if (BowDebugLogging)
			GD.Print($"[BowDebug] {message}");
	}

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

	private void AddUpperBodyAnimation(
		AnimationLibrary library,
		PackedScene sourceScene,
		string targetName,
		bool loop = false,
		bool turnBowBodyRight = false,
		string crouchedTargetName = null)
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

			attackAnimation.LoopMode = loop
				? Animation.LoopModeEnum.Linear
				: Animation.LoopModeEnum.None;
			bool hasUpperBodyTrack = false;
			for (int track = 0; track < attackAnimation.GetTrackCount(); track++)
			{
				bool isUpperBodyTrack = IsUpperBodyTrack(attackAnimation.TrackGetPath(track).ToString());
				attackAnimation.TrackSetEnabled(track, isUpperBodyTrack);
				hasUpperBodyTrack |= isUpperBodyTrack;
			}

			if (turnBowBodyRight && !RotateFirstUpperBodyBoneRight(attackAnimation, targetName))
			{
				sourceRoot.Free();
				GD.PushWarning($"Bow animation '{targetName}' has no upper-body rotation track to turn.");
				return;
			}

			if (!hasUpperBodyTrack)
			{
				sourceRoot.Free();
				GD.PushWarning($"Animation source for '{targetName}' has no recognizable upper-body bone tracks.");
				return;
			}

			Animation crouchedAnimation = null;
			if (crouchedTargetName != null)
			{
				crouchedAnimation = attackAnimation.Duplicate(true) as Animation;
				if (crouchedAnimation == null ||
					!RaiseFirstUpperBodyBone(crouchedAnimation, crouchedTargetName))
				{
					sourceRoot.Free();
					GD.PushWarning($"Bow animation '{targetName}' has no upper-body rotation track to raise.");
					return;
				}
			}

			library.AddAnimation(targetName, attackAnimation);
			if (crouchedTargetName != null)
				library.AddAnimation(crouchedTargetName, crouchedAnimation);
			sourceRoot.Free();
			return;
		}

		GD.PushWarning($"Animation source for '{targetName}' has no playable animation.");
		sourceRoot.Free();
	}

	private bool RotateFirstUpperBodyBoneRight(Animation animation, string animationName)
	{
		float yaw = Mathf.DegToRad(BowBodyYawDegrees);
		Quaternion rightTurn = new(Vector3.Up, yaw);

		for (int track = 0; track < animation.GetTrackCount(); track++)
		{
			if (animation.TrackGetType(track) != Animation.TrackType.Rotation3D ||
				!IsUpperBodyTrack(animation.TrackGetPath(track).ToString()))
				continue;

			int keyCount = animation.TrackGetKeyCount(track);
			for (int key = 0; key < keyCount; key++)
			{
				Quaternion rotation = animation.TrackGetKeyValue(track, key).AsQuaternion();
				animation.TrackSetKeyValue(track, key, rightTurn * rotation);
			}

			LogBowDebug($"Turned bow torso bone: animation={animationName}, " +
				$"track={animation.TrackGetPath(track)}, yaw={yaw:0.###} rad, keys={keyCount}.");
			return keyCount > 0;
		}

		return false;
	}

	private bool RaiseFirstUpperBodyBone(Animation animation, string animationName)
	{
		Quaternion raise = new(Vector3.Right, Mathf.DegToRad(BowCrouchBodyPitchDegrees));
		int selectedTrack = -1;
		int selectedPriority = -1;

		for (int track = 0; track < animation.GetTrackCount(); track++)
		{
			if (animation.TrackGetType(track) != Animation.TrackType.Rotation3D ||
				!IsUpperBodyTrack(animation.TrackGetPath(track).ToString()))
				continue;

			int priority = GetUpperBodyBoneRaisePriority(animation.TrackGetPath(track).ToString());
			if (priority <= selectedPriority)
				continue;

			selectedTrack = track;
			selectedPriority = priority;
		}

		if (selectedTrack < 0)
			return false;

		int keyCount = animation.TrackGetKeyCount(selectedTrack);
		for (int key = 0; key < keyCount; key++)
		{
			Quaternion rotation = animation.TrackGetKeyValue(selectedTrack, key).AsQuaternion();
			animation.TrackSetKeyValue(selectedTrack, key, raise * rotation);
		}

		LogBowDebug($"Raised bow crouch bone: animation={animationName}, " +
			$"track={animation.TrackGetPath(selectedTrack)}, pitch={BowCrouchBodyPitchDegrees:0.###} degrees, keys={keyCount}.");
		return keyCount > 0;
	}

	private static int GetUpperBodyBoneRaisePriority(string trackPath)
	{
		int separator = trackPath.LastIndexOf(':');
		string boneName = separator >= 0 ? trackPath[(separator + 1)..] : trackPath;
		boneName = boneName.Replace("_", "").Replace("-", "").Replace(".", "")
			.Replace(" ", "").ToLowerInvariant();

		if (boneName.Contains("upperchest", System.StringComparison.Ordinal))
			return 400;
		if (boneName.Contains("chest", System.StringComparison.Ordinal))
			return 300;
		if (boneName.Contains("spine", System.StringComparison.Ordinal))
		{
			int suffixStart = boneName.IndexOf("spine", System.StringComparison.Ordinal) + "spine".Length;
			string suffix = boneName[suffixStart..];
			int end = 0;
			while (end < suffix.Length && char.IsDigit(suffix[end]))
				end++;
			return 100 + (int.TryParse(suffix[..end], out int spineIndex) ? spineIndex : 0);
		}

		return 0;
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

		AddUpperBodyFilter(attack, "Attack1");
		AddUpperBodyFilter(attack, "Attack2");

		BuildBowNodes(blendTree);

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

	private void BuildBowNodes(AnimationNodeBlendTree blendTree)
	{
		_bowAvailable = false;
		_bowStates.Clear();
		_bowCrouchStates.Clear();
		_bowAimNodes.Clear();
		_bowBlend = 0f;
		_bowCrouchBlend = 0f;
		_bowStateTime = 0f;
		_bowPullLength = 0f;
		_bowShootLength = 0f;

		var bowAnimations = new[]
		{
			("Pulling", "BowPull", "BowPullCrouched"),
			("Pulled", "BowHold", "BowHoldCrouched"),
			("Shooting", "BowShoot", "BowShootCrouched")
		};

		bool anyBowAnimation = false;
		foreach ((string _, string animationName, string _) in bowAnimations)
			anyBowAnimation |= _animationPlayer.HasAnimation(RuntimeLibraryPrefix + animationName);

		if (!anyBowAnimation)
		{
			blendTree.ConnectNode("output", 0, "Attack");
			return;
		}

		// Выбор состояния лука: Pulling / Pulled / Shooting.
		var bowTransition = new AnimationNodeTransition
		{
			XfadeTime = (float)BlendDuration * 0.5f
		};
		blendTree.AddNode("BowState", bowTransition);

		foreach ((string state, string animationName, string crouchedAnimationName) in bowAnimations)
		{
			StringName animationPath = RuntimeLibraryPrefix + animationName;
			if (!_animationPlayer.HasAnimation(animationPath))
				continue;

			Animation animation = _animationPlayer.GetAnimation(animationPath);
			int inputIndex = _bowStates.Count;
			_bowStates.Add(state);
			bowTransition.AddInput(state);

			string stateOutput = AddBowAimBlendSpace(blendTree, state, animationPath, false);
			StringName crouchedAnimationPath = RuntimeLibraryPrefix + crouchedAnimationName;
			if (_animationPlayer.HasAnimation(crouchedAnimationPath))
			{
				string crouchedOutput = AddBowAimBlendSpace(
					blendTree, state, crouchedAnimationPath, true);
				string variantBlendName = $"BowVariant_{state}";
				var variantBlend = new AnimationNodeBlend2 { Sync = true };
				blendTree.AddNode(variantBlendName, variantBlend);
				blendTree.ConnectNode(variantBlendName, 0, stateOutput);
				blendTree.ConnectNode(variantBlendName, 1, crouchedOutput);
				_bowCrouchStates.Add(state);
				stateOutput = variantBlendName;
			}

			if (state == "Pulling")
			{
				// Seek нужен, чтобы натяжение гарантированно начиналось с 0,
				// даже если transition уже стоит на "Pulling" (отмена -> новое натяжение).
				// TimeScale растягивает анимацию натяжения ровно на время натяжения оружия.
				_bowPullLength = (float)animation.Length;
				blendTree.AddNode("BowPullSeek", new AnimationNodeTimeSeek());
				blendTree.AddNode("BowPullScale", new AnimationNodeTimeScale());
				blendTree.ConnectNode("BowPullSeek", 0, stateOutput);
				blendTree.ConnectNode("BowPullScale", 0, "BowPullSeek");
				blendTree.ConnectNode("BowState", inputIndex, "BowPullScale");
			}
			else
			{
				if (state == "Shooting")
					_bowShootLength = (float)animation.Length;
				blendTree.ConnectNode("BowState", inputIndex, stateOutput);
			}
		}

		// Blend2 с фильтром верхней части тела: вход 0 - обычная анимация (ходьба/атака),
		// вход 1 - лук. blend_amount плавно меняется из кода (см. UpdateBow),
		// поэтому оверлей не может "самопроизвольно" закончиться, как OneShot.
		var bow = new AnimationNodeBlend2
		{
			FilterEnabled = true,
			Sync = true
		};
		blendTree.AddNode("Bow", bow);
		blendTree.ConnectNode("Bow", 0, "Attack");
		blendTree.ConnectNode("Bow", 1, "BowState");
		blendTree.ConnectNode("output", 0, "Bow");
		AddUpperBodyFilter(bow, "BowPull");
		AddUpperBodyFilter(bow, "BowPullCrouched");
		AddUpperBodyFilter(bow, "BowHold");
		AddUpperBodyFilter(bow, "BowHoldCrouched");
		AddUpperBodyFilter(bow, "BowShoot");
		AddUpperBodyFilter(bow, "BowShootCrouched");
		_bowAvailable = true;
		ApplyBowAimPitch();
	}

	private string AddBowAimBlendSpace(
		AnimationNodeBlendTree blendTree,
		string state,
		StringName sourcePath,
		bool crouched)
	{
		string variant = crouched ? "Crouched" : "Standing";
		string nodeName = $"BowAim_{state}_{variant}";
		string animationPrefix = $"BowAim_{state}_{variant}";
		Animation source = _animationPlayer.GetAnimation(sourcePath);
		AnimationLibrary library = _animationPlayer.GetAnimationLibrary(RuntimeLibraryName);
		var blendSpace = new AnimationNodeBlendSpace1D
		{
			MinSpace = -MaxBowAimPitch,
			MaxSpace = MaxBowAimPitch,
			BlendMode = AnimationNodeBlendSpace1D.BlendModeEnum.Interpolated
		};

		AddBowAimBlendPoint(blendSpace, library, source, animationPrefix, "Down", -MaxBowAimPitch);
		AddBowAimBlendPoint(blendSpace, library, source, animationPrefix, "Level", 0.0f);
		AddBowAimBlendPoint(blendSpace, library, source, animationPrefix, "Up", MaxBowAimPitch);
		blendTree.AddNode(nodeName, blendSpace);
		_bowAimNodes.Add(nodeName);
		return nodeName;
	}

	private void AddBowAimBlendPoint(
		AnimationNodeBlendSpace1D blendSpace,
		AnimationLibrary library,
		Animation source,
		string animationPrefix,
		string suffix,
		float pitch)
	{
		string animationName = $"{animationPrefix}_{suffix}";
		Animation aimAnimation = source.Duplicate(true) as Animation;
		if (aimAnimation == null)
		{
			GD.PushError($"Could not duplicate bow animation for aim pitch: {animationName}.");
			return;
		}

		ApplyPitchToUpperBodyBone(aimAnimation, pitch, animationName);
		library.AddAnimation(animationName, aimAnimation);
		blendSpace.AddBlendPoint(
			new AnimationNodeAnimation { Animation = RuntimeLibraryPrefix + animationName },
			pitch);
	}

	private void ApplyPitchToUpperBodyBone(Animation animation, float pitch, string animationName)
	{
		int selectedTrack = -1;
		int selectedPriority = -1;
		for (int track = 0; track < animation.GetTrackCount(); track++)
		{
			if (animation.TrackGetType(track) != Animation.TrackType.Rotation3D ||
				!IsUpperBodyTrack(animation.TrackGetPath(track).ToString()))
				continue;

			int priority = GetUpperBodyBoneRaisePriority(animation.TrackGetPath(track).ToString());
			if (priority <= selectedPriority)
				continue;

			selectedTrack = track;
			selectedPriority = priority;
		}

		if (selectedTrack < 0)
		{
			GD.PushWarning($"Bow aim animation '{animationName}' has no upper-body rotation track.");
			return;
		}

		Quaternion pitchRotation = new(Vector3.Forward, pitch);
		for (int key = 0; key < animation.TrackGetKeyCount(selectedTrack); key++)
		{
			Quaternion rotation = animation.TrackGetKeyValue(selectedTrack, key).AsQuaternion();
			animation.TrackSetKeyValue(selectedTrack, key, rotation * pitchRotation);
		}
	}

	private void AddUpperBodyFilter(AnimationNode filteredNode, string animationName)
	{
		StringName animationPath = RuntimeLibraryPrefix + animationName;
		if (!_animationPlayer.HasAnimation(animationPath))
			return;

		Animation animation = _animationPlayer.GetAnimation(animationPath);
		for (int track = 0; track < animation.GetTrackCount(); track++)
		{
			if (IsUpperBodyTrack(animation.TrackGetPath(track).ToString()))
				filteredNode.SetFilterPath(animation.TrackGetPath(track), true);
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
	private string _replicatedBowState = "Idle";
	private AnimationPlayer _animationPlayer;
	private AnimationTree _animationTree;
	private Node3D _modelRoot;
	private string[] _availableAnimationNames = System.Array.Empty<string>();
	private bool _attackWasActive;
	private bool _bowAvailable;
	private readonly System.Collections.Generic.List<string> _bowStates = new();
	private readonly System.Collections.Generic.List<string> _bowCrouchStates = new();
	private readonly System.Collections.Generic.List<string> _bowAimNodes = new();
	private float _bowBlend;
	private float _bowCrouchBlend;
	private float _replicatedBowAimPitch;
	private float _bowStateTime;
	private float _bowPullLength;
	private float _bowShootLength;
	private float _replicatedBowPullDuration;

	[Export]
	public string ReplicatedBowState
	{
		get => _replicatedBowState;
		set
		{
			if (_replicatedBowState == value)
				return;

			_replicatedBowState = value;
			if (IsInsideTree())
				ApplyBowState(value);
		}
	}

	/// <summary>Время натяжения (сек), реплицируется через RPC вместе с состоянием "Pulling".</summary>
	public float ReplicatedBowPullDuration
	{
		get => _replicatedBowPullDuration;
		set
		{
			_replicatedBowPullDuration = value;
			UpdateBowPullSpeed();
		}
	}

	[Export]
	public float ReplicatedBowAimPitch
	{
		get => _replicatedBowAimPitch;
		set
		{
			_replicatedBowAimPitch = Mathf.Clamp(value, -MaxBowAimPitch, MaxBowAimPitch);
			ApplyBowAimPitch();
		}
	}

	private bool IsBowAuthority =>
		!Multiplayer.HasMultiplayerPeer() || GetMultiplayerAuthority() == Multiplayer.GetUniqueId();

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
		AddUpperBodyAnimation(runtimeLibrary, BowPullAnimationScene, "BowPull",
			turnBowBodyRight: true, crouchedTargetName: "BowPullCrouched");
		AddUpperBodyAnimation(runtimeLibrary, BowHoldAnimationScene, "BowHold", true,
			true, "BowHoldCrouched");
		AddUpperBodyAnimation(runtimeLibrary, BowShootAnimationScene, "BowShoot",
			turnBowBodyRight: true, crouchedTargetName: "BowShootCrouched");
		LogBowDebug($"Animation setup: entity={Name}, player={_animationPlayer.GetPath()}, " +
			$"tree={_animationTree?.GetPath().ToString() ?? "<not-built>"}, " +
			$"pull={_animationPlayer.HasAnimation(RuntimeLibraryPrefix + "BowPull")}, " +
			$"hold={_animationPlayer.HasAnimation(RuntimeLibraryPrefix + "BowHold")}, " +
			$"shoot={_animationPlayer.HasAnimation(RuntimeLibraryPrefix + "BowShoot")}.");

		BuildAnimationTree();

		ApplyAnimation(_replicatedAnimation);
		ApplyAttack(_replicatedAttack);
		ApplyBowState(_replicatedBowState);
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

		UpdateBow(delta);

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

	private void UpdateBow(double delta)
	{
		if (!_bowAvailable || _animationTree == null)
			return;

		bool idle = _replicatedBowState == "Idle";
		if (!idle)
			_bowStateTime += (float)delta;

		// Анимация выстрела доиграна - все пиры плавно убирают лук локально,
		// а авторитет переводит состояние в Idle (и реплицирует его).
		bool shootFinished = _replicatedBowState == "Shooting" && _bowStateTime >= _bowShootLength;
		if (shootFinished && IsBowAuthority)
			BroadcastBowAnimationState("Idle");

		float target = (idle || shootFinished) ? 0f : 1f;
		float step = BlendDuration > 0.0 ? (float)(delta / BlendDuration) : 1f;
		float crouchTarget =
			_replicatedAnimation == "CrouchedIdle" || _replicatedAnimation == "CrouchedWalk"
				? 1f
				: 0f;
		float crouchBlend = Mathf.MoveToward(_bowCrouchBlend, crouchTarget, step);
		if (!Mathf.IsEqualApprox(crouchBlend, _bowCrouchBlend))
		{
			_bowCrouchBlend = crouchBlend;
			foreach (string state in _bowCrouchStates)
				_animationTree.Set($"parameters/BowVariant_{state}/blend_amount", _bowCrouchBlend);
		}

		float blend = Mathf.MoveToward(_bowBlend, target, step);
		if (!Mathf.IsEqualApprox(blend, _bowBlend))
		{
			_bowBlend = blend;
			_animationTree.Set("parameters/Bow/blend_amount", _bowBlend);
		}
	}

	private void ApplyBowAimPitch()
	{
		if (_animationTree == null || !_bowAvailable)
			return;

		foreach (string nodeName in _bowAimNodes)
			_animationTree.Set($"parameters/{nodeName}/blend_position", -_replicatedBowAimPitch);
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

	private float GetBowPullDuration()
	{
		float duration = _replicatedBowPullDuration;
		if (duration <= 0f && _equipmentComponent?.EquippedWeapon is RangedWeapon bow)
			duration = (float)bow.MaxChargeTime;
		return duration;
	}

	private void UpdateBowPullSpeed()
	{
		if (!_bowAvailable || _animationTree == null || _bowPullLength <= 0f)
			return;

		float duration = GetBowPullDuration();
		float speed = duration > 0f ? _bowPullLength / duration : 1.0f;
		_animationTree.Set("parameters/BowPullScale/scale", speed);
	}

	private void ApplyBowState(string state)
	{
		if (_animationPlayer == null || _animationTree == null)
		{
			GD.PushWarning($"[BowDebug] Cannot apply bow state '{state}' to {Name}: " +
				$"animationPlayer={_animationPlayer != null}, animationTree={_animationTree != null}.");
			return;
		}
		if (!_bowAvailable)
			return;

		LogBowDebug($"Applying bow state: entity={Name}, state={state}, " +
			$"authority={GetMultiplayerAuthority()}, localPeer=" +
			$"{(Multiplayer.HasMultiplayerPeer() ? Multiplayer.GetUniqueId() : 1)}.");

		_bowStateTime = 0f;
		if (state == "Idle")
			return; // плавное убирание лука делает UpdateBow

		if (!_bowStates.Contains(state))
		{
			GD.PushWarning($"[BowDebug] No animation source is assigned for bow state '{state}' on {Name}.");
			return;
		}

		_animationTree.Active = true;
		if (state == "Pulling")
		{
			UpdateBowPullSpeed();
			_animationTree.Set("parameters/BowPullSeek/seek_request", 0.0);
		}
		_animationTree.Set("parameters/BowState/transition_request", state);
	}

	public void BroadcastBowAnimationState(string state, float pullDuration = 0f)
	{
		if (_replicatedBowState == state)
			return;

		LogBowDebug($"Broadcasting bow state: entity={Name}, state={state}, " +
			$"authority={GetMultiplayerAuthority()}, localPeer=" +
			$"{(Multiplayer.HasMultiplayerPeer() ? Multiplayer.GetUniqueId() : 1)}, " +
			$"network={Multiplayer.HasMultiplayerPeer()}.");

		if (state == "Pulling")
			ReplicatedBowPullDuration = pullDuration > 0f ? pullDuration : GetBowPullDuration();
		ReplicatedBowState = state;
		if (!Multiplayer.HasMultiplayerPeer())
			return;

		Rpc(nameof(ReceiveBowAnimationState), state, _replicatedBowPullDuration);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceiveBowAnimationState(string state, float pullDuration)
	{
		if (Multiplayer.HasMultiplayerPeer() &&
			Multiplayer.GetRemoteSenderId() != 0 &&
			Multiplayer.GetRemoteSenderId() != GetMultiplayerAuthority())
		{
			GD.PushWarning($"[BowDebug] Rejected bow state RPC: entity={Name}, state={state}, " +
				$"sender={Multiplayer.GetRemoteSenderId()}, authority={GetMultiplayerAuthority()}.");
			return;
		}

		if (state is not ("Idle" or "Pulling" or "Pulled" or "Shooting"))
		{
			GD.PushWarning($"Rejected unknown bow animation state '{state}'.");
			return;
		}

		LogBowDebug($"Accepted bow state RPC: entity={Name}, state={state}, " +
			$"sender={Multiplayer.GetRemoteSenderId()}.");
		if (state == "Pulling" && pullDuration > 0f)
			ReplicatedBowPullDuration = pullDuration;
		ReplicatedBowState = state;
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