using Godot;

public partial class AnimatedEntity : Entity
{
	private bool AddUpperBodyAnimation(
		AnimationLibrary library,
		PackedScene sourceScene,
		string targetName,
		bool loop = false,
		bool turnBowBodyRight = false,
		string crouchedTargetName = null)
	{
		if (sourceScene == null)
			return false;

		Node sourceRoot = sourceScene.Instantiate();
		AnimationPlayer sourcePlayer = FindAnimationPlayer(sourceRoot);
		if (sourcePlayer == null)
		{
			GD.PushWarning($"Animation source for '{targetName}' has no AnimationPlayer.");
			sourceRoot.Free();
			return false;
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
				return false;
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
				return false;
			}

			if (!hasUpperBodyTrack)
			{
				sourceRoot.Free();
				GD.PushWarning($"Animation source for '{targetName}' has no recognizable upper-body bone tracks.");
				return false;
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
					return false;
				}
			}

			library.AddAnimation(targetName, attackAnimation);
			if (crouchedTargetName != null)
				library.AddAnimation(crouchedTargetName, crouchedAnimation);
			sourceRoot.Free();
			return true;
		}

		GD.PushWarning($"Animation source for '{targetName}' has no playable animation.");
		sourceRoot.Free();
		return false;
	}

	private void AddWeaponAttackAnimation(
		AnimationLibrary library,
		PackedScene entityAnimation,
		string targetName,
		PackedScene weaponAnimation)
	{
		if (weaponAnimation != null &&
			AddUpperBodyAnimation(library, weaponAnimation, targetName))
			return;

		AddUpperBodyAnimation(library, entityAnimation, targetName);
	}

	private void BuildAnimationTree()
	{
		if (_animationTree != null)
		{
			_animationTree.Active = false;
			_modelRoot.RemoveChild(_animationTree);
			_animationTree.Free();
			_animationTree = null;
		}

		var blendTree = new AnimationNodeBlendTree();
		var locomotion = new AnimationNodeTransition
		{
			XfadeTime = (float)BlendDuration
		};
		blendTree.AddNode("Locomotion", locomotion);
		var animationNames = new[]
		{
			"Idle", "Walk", "Run", "Jumping", "RunWithSword", "CrouchedIdle", "CrouchedWalk"
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

		blendTree.AddNode("OneShotClip", attackClip);

		_oneShotNode = new AnimationNodeOneShot
		{
			FadeInTime = (float)BlendDuration,
			FadeOutTime = (float)BlendDuration,
			BreakLoopAtEnd = true,
			FilterEnabled = true
		};
		blendTree.AddNode("OneShot", _oneShotNode);
		blendTree.ConnectNode("OneShot", 0, "Locomotion");
		blendTree.ConnectNode("OneShot", 1, "OneShotClip");
		AddUpperBodyFilter(_oneShotNode, "Attack1");
		AddUpperBodyFilter(_oneShotNode, "Attack2");
		AddUpperBodyFilter(_oneShotNode, "ItemAction");

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

	private AnimationPlayer _animationPlayer;
	private AnimationTree _animationTree;
	private Node3D _modelRoot;
	private AnimationLibrary _runtimeAnimationLibrary;
	private AnimationNodeOneShot _oneShotNode;
	private string[] _availableAnimationNames = System.Array.Empty<string>();
	private string _replicatedAnimation = "Idle";
	private string _replicatedAttack = "";

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
				if (!string.IsNullOrEmpty(value))
					ApplyOneShot(value);
		}
	}


	private void AddRangedWeaponAnimations(AnimationLibrary library, RangedWeapon rangedWeapon)
	{
		var obsoleteAnimations = new System.Collections.Generic.List<StringName>();
		foreach (StringName animationName in library.GetAnimationList())
		{
			string name = animationName.ToString();
			if (name is "BowPull" or "BowPullCrouched" or
				"BowHold" or "BowHoldCrouched" or
				"BowShoot" or "BowShootCrouched" ||
				name.StartsWith("BowAim_", System.StringComparison.Ordinal))
				obsoleteAnimations.Add(animationName);
		}
		foreach (StringName animationName in obsoleteAnimations)
			library.RemoveAnimation(animationName);

		if (rangedWeapon == null)
			return;

		AddUpperBodyAnimation(library, rangedWeapon.BowPullAnimationScene, "BowPull",
			turnBowBodyRight: true, crouchedTargetName: "BowPullCrouched");
		AddUpperBodyAnimation(library, rangedWeapon.BowHoldAnimationScene, "BowHold", true,
			true, "BowHoldCrouched");
		AddUpperBodyAnimation(library, rangedWeapon.BowShootAnimationScene, "BowShoot",
			turnBowBodyRight: true, crouchedTargetName: "BowShootCrouched");
	}

	private void ReplaceWeaponAttackAnimation(
		string targetName,
		PackedScene entityAnimation,
		PackedScene weaponAnimation)
	{
		if (_runtimeAnimationLibrary.HasAnimation(targetName))
			_runtimeAnimationLibrary.RemoveAnimation(targetName);

		AddWeaponAttackAnimation(
			_runtimeAnimationLibrary,
			entityAnimation,
			targetName,
			weaponAnimation);
	}


	public void PlayIdle() => SetAnimationState("Idle");
	public void PlayWalk() => SetAnimationState("Walk");
	public void PlayRun() => SetAnimationState("Run");
	public void PlayJumping() => SetAnimationState("Jumping");
	public void PlayRunWithSword() => SetAnimationState("RunWithSword");
	/// <summary>
	/// Plays a configured one-shot animation from the currently equipped item.
	/// </summary>
	public void PlayOneShot(string animationName)
	{
		if ((animationName is "Attack1" or "Attack2") && EquippedHandItem?.ActionAnimationScene != null)
			animationName = "ItemAction";

		GD.Print($"[Animation] One-shot requested: entity={Name}, animation={animationName}.");
		if (string.IsNullOrWhiteSpace(animationName) || _animationPlayer == null ||
			!_animationPlayer.HasAnimation(RuntimeLibraryPrefix + animationName))
		{
			GD.PushWarning($"No one-shot animation '{animationName}' is configured for {Name}.");
			return;
		}
		if (Multiplayer.HasMultiplayerPeer() && GetMultiplayerAuthority() != Multiplayer.GetUniqueId())
			return;

		if (Multiplayer.HasMultiplayerPeer())
			Rpc(nameof(ReceiveOneShotAnimation), animationName);
		else
			ReceiveOneShotAnimation(animationName);
	}
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceiveOneShotAnimation(string animationName)
	{
		if (Multiplayer.HasMultiplayerPeer() && Multiplayer.GetRemoteSenderId() != 0 &&
			Multiplayer.GetRemoteSenderId() != GetMultiplayerAuthority())
		{
			GD.PushWarning($"Rejected one-shot animation '{animationName}' from peer {Multiplayer.GetRemoteSenderId()}.");
			return;
		}
		if (_animationTree == null || _animationPlayer == null ||
			!_animationPlayer.HasAnimation(RuntimeLibraryPrefix + animationName) ||
			animationName is not ("Attack1" or "Attack2" or "ItemAction"))
			return;

		bool isAttack = animationName is "Attack1" or "Attack2";
		_replicatedAttack = isAttack ? animationName : "";
		ApplyOneShot(animationName);
	}
	public void PlayRunForEquippedWeapon()
	{
		if (EquippedWeapon is MeleeWeapon && _animationPlayer?.HasAnimation(RuntimeLibraryPrefix + "RunWithSword") == true)
			PlayRunWithSword();
		else
			PlayRun();
	}
	public void PlayCrouchedIdle() => SetAnimationState(
		_animationPlayer?.HasAnimation(RuntimeLibraryPrefix + "CrouchedIdle") == true
			? "CrouchedIdle"
			: "Idle");
	public void PlayCrouchedWalk() => SetAnimationState(
		_animationPlayer?.HasAnimation(RuntimeLibraryPrefix + "CrouchedWalk") == true
			? "CrouchedWalk"
			: "Walk");
	public void PlayAttack1() => PlayOneShot("Attack1");
	public void PlayAttack2() => PlayOneShot("Attack2");


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

			Animation animation = sourcePlayer.GetAnimation(sourceName);
			if (animation != null)
				library.AddAnimation(targetName, animation);
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

	private void ApplyOneShot(string animationName)
	{
		if (_animationTree == null || string.IsNullOrWhiteSpace(animationName))
			return;

		StringName qualifiedName = RuntimeLibraryPrefix + animationName;
		if (!_animationPlayer.HasAnimation(qualifiedName))
			return;

		_animationTree.Set("parameters/OneShotClip/animation", qualifiedName);
		_animationTree.Set("parameters/OneShot/request", (int)AnimationNodeOneShot.OneShotRequest.Abort);
		_animationTree.Set("parameters/OneShot/request", (int)AnimationNodeOneShot.OneShotRequest.Fire);
		if (animationName is "Attack1" or "Attack2")
		{
			WeaponDamageZone damageZone = GetWeaponDamageZone();
			GD.Print($"[Combat] Applying attack: entity={Name}, attack={animationName}, " +
				$"authority={GetMultiplayerAuthority()}, localPeer={(Multiplayer.HasMultiplayerPeer() ? Multiplayer.GetUniqueId() : 1)}, " +
				$"damageZone={damageZone?.GetPath().ToString() ?? "<missing>"}, animationTreeReady=True.");
			if (damageZone == null)
				GD.PushWarning($"[Combat] Cannot start attack: entity '{GetPath()}' has no WeaponDamageZone under it.");
			else
			{
				WeaponScene weaponScene = EquippedWeaponScene;
				if (weaponScene != null)
					weaponScene.BeginSwing();
				else
					damageZone.BeginSwing();
			}
		}
	}

}
