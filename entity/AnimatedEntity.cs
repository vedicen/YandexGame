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
	[Export] public PackedScene HitAnimationScene { get; set; }
	[Export] public PackedScene Attack1AnimationScene { get; set; }
	[Export] public PackedScene Attack2AnimationScene { get; set; }
	[Export] public PackedScene CrouchedIdleAnimationScene { get; set; }
	[Export] public PackedScene CrouchedWalkAnimationScene { get; set; }
	[Export] public double BlendDuration { get; set; } = 0.2;

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
	private AnimationPlayer _attackAnimationPlayer;
	private Node3D _modelRoot;

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

		_modelRoot = GetNodeOrNull<Node3D>("Model");
		if (_modelRoot == null || ModelScene == null)
			return;

		_modelRoot.AddChild(ModelScene.Instantiate<Node3D>());
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
		AddAnimation(runtimeLibrary, HitAnimationScene, "Hit");
		AddAnimation(runtimeLibrary, CrouchedIdleAnimationScene, "CrouchedIdle");
		AddAnimation(runtimeLibrary, CrouchedWalkAnimationScene, "CrouchedWalk");

		_attackAnimationPlayer = new AnimationPlayer
		{
			Name = "AttackAnimationPlayer"
		};
		_modelRoot.AddChild(_attackAnimationPlayer);
		Node animationRoot = _animationPlayer.GetNodeOrNull(_animationPlayer.RootNode);
		if (animationRoot == null)
		{
			GD.PushError("The model AnimationPlayer has an invalid root node.");
			_attackAnimationPlayer.QueueFree();
			_attackAnimationPlayer = null;
			ApplyAnimation(_replicatedAnimation);
			return;
		}

		_attackAnimationPlayer.RootNode = _attackAnimationPlayer.GetPathTo(animationRoot);
		_attackAnimationPlayer.AnimationFinished += OnAttackAnimationFinished;

		var attackLibrary = new AnimationLibrary();
		_attackAnimationPlayer.AddAnimationLibrary(RuntimeLibraryName, attackLibrary);
		AddUpperBodyAnimation(attackLibrary, Attack1AnimationScene, "Attack1");
		AddUpperBodyAnimation(attackLibrary, Attack2AnimationScene, "Attack2");

		ApplyAnimation(_replicatedAnimation);
		ApplyAttack(_replicatedAttack);
	}

	public void PlayIdle() => SetAnimationState("Idle");
	public void PlayWalk() => SetAnimationState("Walk");
	public void PlayRun() => SetAnimationState("Run");
	public void PlayRunWithSword() => SetAnimationState("RunWithSword");
	public void PlayHit() => SetAnimationState("Hit");
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

	private void SetAnimationState(string animationName)
	{
		if (Multiplayer.HasMultiplayerPeer() && GetMultiplayerAuthority() != Multiplayer.GetUniqueId())
			return;

		ReplicatedAnimation = animationName;
	}

	private void SetAttackState(string animationName)
	{
		if (Multiplayer.HasMultiplayerPeer() && GetMultiplayerAuthority() != Multiplayer.GetUniqueId())
			return;

		if (_attackAnimationPlayer == null ||
			!_attackAnimationPlayer.HasAnimation(RuntimeLibraryPrefix + animationName))
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
		if (_animationPlayer == null || string.IsNullOrWhiteSpace(animationName))
			return;

		StringName qualifiedName = RuntimeLibraryPrefix + animationName;
		if (_animationPlayer.HasAnimation(qualifiedName))
			_animationPlayer.Play(qualifiedName, BlendDuration);
	}

	private void ApplyAttack(string animationName)
	{
		if (_attackAnimationPlayer == null || string.IsNullOrWhiteSpace(animationName))
			return;

		StringName qualifiedName = RuntimeLibraryPrefix + animationName;
		if (_attackAnimationPlayer.HasAnimation(qualifiedName))
			_attackAnimationPlayer.Play(qualifiedName, BlendDuration);
	}

	private void OnAttackAnimationFinished(StringName animationName)
	{
		if (animationName != RuntimeLibraryPrefix + "Attack1" &&
			animationName != RuntimeLibraryPrefix + "Attack2")
			return;

		if (!Multiplayer.HasMultiplayerPeer() || GetMultiplayerAuthority() == Multiplayer.GetUniqueId())
			ReplicatedAttack = "";
		else
			_replicatedAttack = "";
	}
}