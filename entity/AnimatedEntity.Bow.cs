using Godot;

public partial class AnimatedEntity : Entity
{
	private const float MaxBowAimPitch = Mathf.Pi / 4.0f;
	[Export] public bool BowDebugLogging { get; set; }
	[Export] public float BowBodyYawDegrees { get; set; } = -60.0f;
	[Export] public float BowCrouchBodyPitchDegrees { get; set; } = 20.0f;

	public void LogBowDebug(string message)
	{
		if (BowDebugLogging)
			GD.Print($"[BowDebug] {message}");
	}

	private string _replicatedBowState = "Idle";
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
			blendTree.ConnectNode("output", 0, "OneShot");
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
		blendTree.ConnectNode("Bow", 0, "OneShot");
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
		if (EquippedWeapon is Bow bow)
			bow.UpdateHeldProjectile(this, state);

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

}
