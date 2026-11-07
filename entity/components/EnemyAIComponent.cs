using Godot;
using System.Collections.Generic;

public partial class EnemyAIComponent : Node
{
	[Export] public float Speed { get; set; } = 3.0f;
	[Export] public float StopDistance { get; set; } = 0.4f;
	[Export] public float MeleeAttackRange { get; set; } = 1.2f;
	[Export] public float MeleeAttackCooldown { get; set; } = 1.2f;
	[Export] public float RangedMinimumDistance { get; set; } = 10.0f;
	[Export] public float RangedAttackRange { get; set; } = 20.0f;
	[Export] public float RangedChargeTime { get; set; } = 0.8f;
	[Export] public float RangedAttackCooldown { get; set; } = 2.0f;
	[Export] public float RangedShotRecoveryTime { get; set; } = 0.8f;
	[Export] public float RangedAimLowerOffset { get; set; } = 1.5f;
	[Export] public float TurnSpeed { get; set; } = 8.0f;
	[Export] public Area3D DetectionArea { get; set; }
	[Export] public bool DebugLogging { get; set; }

	private AnimatedEntity _enemy;
	private NavigationAgent3D _navigationAgent;
	private readonly List<Node3D> _targetsInRange = new();
	private float _meleeAttackCooldown;
	private float _rangedAttackCooldown;
	private float _rangedChargeElapsed;
	private float _rangedChargeDuration;
	private float _rangedShotRecoveryRemaining;
	private int _nextProjectileId;
	private float _locomotionChangeCooldown;
	private string _lastLocomotionAnimation = "";
	private Vector3 _lastNavigationTarget;
	private bool _hasNavigationTarget;
	private float _debugTimer;
	private bool _loggedNoAuthority;

	public override void _Ready()
	{
		_enemy = GetParent() as AnimatedEntity;
		if (_enemy == null)
		{
			GD.PushError("EnemyAIComponent must be a child of an AnimatedEntity.");
			SetPhysicsProcess(false);
			return;
		}

		_enemy.SetPhysicsProcess(false);

		if (Speed <= 0f)
			GD.PushWarning($"[EnemyAI] {_enemy.Name}: Speed = {Speed}. Враг не сможет двигаться (проверь значение в инспекторе).");

		DetectionArea ??= GetNodeOrNull<Area3D>("../DetectionArea");
		if (DetectionArea == null)
		{
			GD.PushError($"EnemyAIComponent '{GetPath()}' requires a DetectionArea.");
			SetPhysicsProcess(false);
			return;
		}

		DetectionArea.BodyEntered += OnBodyEntered;
		DetectionArea.BodyExited += OnBodyExited;
		_navigationAgent = GetNodeOrNull<NavigationAgent3D>("../NavigationAgent3D");
		if (_navigationAgent != null)
		{
			_navigationAgent.PathDesiredDistance = 0.5f;
			_navigationAgent.TargetDesiredDistance = StopDistance;
		}
	}

	public override void _ExitTree()
	{
		if (GodotObject.IsInstanceValid(DetectionArea))
		{
			DetectionArea.BodyEntered -= OnBodyEntered;
			DetectionArea.BodyExited -= OnBodyExited;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_enemy == null)
			return;

		if (!HasAuthority())
		{
			if (DebugLogging && !_loggedNoAuthority)
			{
				_loggedNoAuthority = true;
				GD.Print($"[EnemyAI] {_enemy.Name}: нет авторитета на этом пире " +
					$"(server={Multiplayer.IsServer()}, authority={_enemy.GetMultiplayerAuthority()}, " +
					$"local={Multiplayer.GetUniqueId()}). AI здесь не работает.");
			}
			return;
		}

		_meleeAttackCooldown = Mathf.Max(0, _meleeAttackCooldown - (float)delta);
		_rangedAttackCooldown = Mathf.Max(0, _rangedAttackCooldown - (float)delta);
		_locomotionChangeCooldown = Mathf.Max(0, _locomotionChangeCooldown - (float)delta);
		Node3D target = FindNearestTargetInRange();
		bool hasMeleeWeapon = _enemy.EquippedWeapon is MeleeWeapon;
		RangedWeapon rangedWeapon = _enemy.EquippedWeapon as RangedWeapon;
		Vector3 velocity = _enemy.Velocity;

		if (!_enemy.IsOnFloor())
			velocity += _enemy.GetGravity() * (float)delta;

		float distance = -1f;
		bool inMeleeRange = false;
		bool inRangedAttackRange = false;
		bool shouldStop = true;
		string navInfo = "n/a";

		if (target == null)
		{
			StopHorizontalMovement(ref velocity, (float)delta);
			CancelRangedCharge();
			UpdateLocomotionAnimation("Idle");
		}
		else
		{
			Vector3 offset = target.GlobalPosition - _enemy.GlobalPosition;
			offset.Y = 0;
			distance = offset.Length();
			inMeleeRange = hasMeleeWeapon && distance <= MeleeAttackRange;
			inRangedAttackRange = rangedWeapon != null &&
				distance >= RangedMinimumDistance &&
				distance <= RangedAttackRange;
			shouldStop = distance <= StopDistance || inMeleeRange;

			if (rangedWeapon != null)
			{
				if (distance < RangedMinimumDistance)
				{
					Vector3 retreatDirection = -offset.Normalized();
					velocity.X = retreatDirection.X * Speed;
					velocity.Z = retreatDirection.Z * Speed;
					shouldStop = false;
					CancelRangedCharge();
				}
				else if (distance > RangedAttackRange)
				{
					Vector3 direction = GetMovementDirection(target, offset, out navInfo);
					velocity.X = direction.X * Speed;
					velocity.Z = direction.Z * Speed;
					shouldStop = false;
					CancelRangedCharge();
				}
				else
				{
					StopHorizontalMovement(ref velocity, (float)delta);
					shouldStop = true;
					ProcessRangedAttack(rangedWeapon, target, (float)delta);
				}
			}
			else if (shouldStop)
			{
				StopHorizontalMovement(ref velocity, (float)delta);
			}
			else
			{
				Vector3 direction = GetMovementDirection(target, offset, out navInfo);
				velocity.X = direction.X * Speed;
				velocity.Z = direction.Z * Speed;
			}

			if (distance > 0.001f)
			{
				Vector3 direction = offset.Normalized();
				float targetRotation = Mathf.Atan2(-direction.X, -direction.Z);
				Vector3 rotation = _enemy.Rotation;
				rotation.Y = Mathf.LerpAngle(rotation.Y, targetRotation,
					Mathf.Clamp(TurnSpeed * (float)delta, 0, 1));
				_enemy.Rotation = rotation;
			}

			if (rangedWeapon == null && inMeleeRange)
			{
				if (string.IsNullOrEmpty(_enemy.ReplicatedAttack) && _meleeAttackCooldown <= 0)
				{
					_enemy.PlayAttack1();
					_meleeAttackCooldown = MeleeAttackCooldown;
				}
			}

			UpdateLocomotionAnimation(shouldStop ? "Idle" : "Run");
		}

		Vector3 positionBefore = _enemy.GlobalPosition;
		_enemy.Velocity = velocity;
		_enemy.MoveAndSlide();

		if (DebugLogging)
		{
			_debugTimer -= (float)delta;
			if (_debugTimer <= 0f)
			{
				_debugTimer = 0.5f;
				float moved = positionBefore.DistanceTo(_enemy.GlobalPosition);
				string collisions = "none";
				int collisionCount = _enemy.GetSlideCollisionCount();
				if (collisionCount > 0)
				{
					var parts = new List<string>();
					for (int i = 0; i < collisionCount; i++)
					{
						KinematicCollision3D collision = _enemy.GetSlideCollision(i);
						Node collider = collision.GetCollider() as Node;
						parts.Add($"{(collider != null ? collider.GetPath().ToString() : "?")} " +
							$"(layer={(collider is CollisionObject3D co ? co.CollisionLayer : 0)}, normal={collision.GetNormal()})");
					}
					collisions = string.Join("; ", parts);
				}
				GD.Print($"[EnemyAI] {_enemy.Name}: target={(target != null ? target.Name.ToString() : "<none>")}, " +
					$"dist={distance:F2}, melee={hasMeleeWeapon}, ranged={rangedWeapon != null}, " +
					$"inMeleeRange={inMeleeRange}, inRangedRange={inRangedAttackRange}, shouldStop={shouldStop}, " +
					$"Speed={Speed}, vel={_enemy.Velocity}, moved={moved:F4}, onFloor={_enemy.IsOnFloor()}, " +
					$"pos={_enemy.GlobalPosition}, nav=[{navInfo}], " +
					$"layer={_enemy.CollisionLayer}, mask={_enemy.CollisionMask}, collisions=[{collisions}].");
			}
		}
	}

	private void ProcessRangedAttack(RangedWeapon weapon, Node3D target, float delta)
	{
		if (!weapon.HasValidConfiguration)
		{
			GD.PushWarning($"[EnemyAI] {_enemy.Name}: ranged weapon '{weapon.DisplayName}' is misconfigured.");
			CancelRangedCharge();
			return;
		}

		if (_enemy.ReplicatedBowState == "Shooting")
		{
			_rangedShotRecoveryRemaining -= delta;
			if (_rangedShotRecoveryRemaining <= 0)
				_enemy.BroadcastBowAnimationState("Idle");
			return;
		}

		if (_enemy.ReplicatedBowState == "Idle")
		{
			if (_rangedAttackCooldown > 0)
				return;

			_rangedChargeDuration = Mathf.Clamp(
				Mathf.Max(RangedChargeTime, weapon.MinChargeTime),
				weapon.MinChargeTime,
				weapon.MaxChargeTime);
			_rangedChargeElapsed = 0;
			_enemy.BroadcastBowAnimationState("Pulling", _rangedChargeDuration);
			return;
		}

		if (_enemy.ReplicatedBowState is not ("Pulling" or "Pulled"))
			return;

		_rangedChargeElapsed += delta;
		if (_enemy.ReplicatedBowState == "Pulling" &&
			_rangedChargeElapsed >= _rangedChargeDuration * 0.85f)
			_enemy.BroadcastBowAnimationState("Pulled");

		if (_rangedChargeElapsed < _rangedChargeDuration)
			return;

		Vector3 origin = _enemy.EquippedWeaponScene != null
			? _enemy.EquippedWeaponScene.GlobalPosition + Vector3.Up * 0.1f
			: _enemy.GlobalPosition + Vector3.Up * 1.35f;
		Vector3 targetPosition = target.GlobalPosition + Vector3.Up * (1.0f - RangedAimLowerOffset);
		Vector3 direction = (targetPosition - origin).Normalized();
		float pitch = Mathf.Atan2(direction.Y, new Vector2(direction.X, direction.Z).Length());
		_enemy.ReplicatedBowAimPitch = pitch;
		_enemy.BroadcastBowAnimationState("Shooting");
		SpawnRangedProjectile(weapon, origin, direction, GetRangedDamage(weapon, _rangedChargeDuration));
		_rangedAttackCooldown = Mathf.Max(0, RangedAttackCooldown);
		_rangedShotRecoveryRemaining = Mathf.Max(0.1f, RangedShotRecoveryTime);
		_rangedChargeElapsed = 0;
		_rangedChargeDuration = 0;
	}

	private void UpdateLocomotionAnimation(string animation)
	{
		if (_lastLocomotionAnimation == animation || _locomotionChangeCooldown > 0)
			return;

		if (animation == "Run")
			_enemy.PlayRun();
		else
			_enemy.PlayIdle();

		_lastLocomotionAnimation = animation;
		_locomotionChangeCooldown = 1.0f;
	}

	private float GetRangedDamage(RangedWeapon weapon, float chargeTime)
	{
		float charge = Mathf.Clamp(chargeTime, weapon.MinChargeTime, weapon.MaxChargeTime);
		if (charge >= weapon.MaxChargeTime)
			return weapon.Damage;

		float chargePercent = Mathf.Clamp(
			(charge - weapon.MinChargeTime) / (weapon.MaxChargeTime - weapon.MinChargeTime),
			0,
			1);
		return weapon.Damage * (0.5f + 0.5f * chargePercent);
	}

	private void SpawnRangedProjectile(
		RangedWeapon weapon,
		Vector3 origin,
		Vector3 direction,
		float damage)
	{
		string projectileName = $"{_enemy.Name}_Arrow_{++_nextProjectileId}";
		if (Multiplayer.HasMultiplayerPeer())
		{
			Rpc(nameof(SpawnEnemyProjectile), projectileName, weapon.ResourcePath,
				weapon.RequiredProjectile.ResourcePath, origin, direction, damage);
			return;
		}

		WeaponProjectileScene projectile = weapon.SpawnProjectile(
			_enemy, _enemy.GetTree().CurrentScene, projectileName,
			weapon.RequiredProjectile, origin, direction, damage);
		if (projectile == null)
			GD.PushError($"[EnemyAI] {_enemy.Name}: failed to spawn ranged projectile.");
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SpawnEnemyProjectile(
		string projectileName,
		string weaponPath,
		string projectilePath,
		Vector3 origin,
		Vector3 direction,
		float damage)
	{
		if (Multiplayer.HasMultiplayerPeer() &&
			Multiplayer.GetRemoteSenderId() != 0 &&
			Multiplayer.GetRemoteSenderId() != 1)
			return;

		RangedWeapon weapon = GD.Load<RangedWeapon>(weaponPath);
		WeaponProjectile projectileResource = GD.Load<WeaponProjectile>(projectilePath);
		if (weapon == null || projectileResource == null)
		{
			GD.PushError($"[EnemyAI] {_enemy.Name}: failed to load projectile resources " +
				$"(weapon='{weaponPath}', projectile='{projectilePath}').");
			return;
		}

		WeaponProjectileScene projectile = weapon.SpawnProjectile(
			_enemy, _enemy.GetTree().CurrentScene, projectileName,
			projectileResource, origin, direction, damage);
		if (projectile == null)
			GD.PushError($"[EnemyAI] {_enemy.Name}: failed to spawn replicated projectile '{projectileName}'.");
	}

	private void CancelRangedCharge()
	{
		if (_enemy.ReplicatedBowState is not ("Pulling" or "Pulled"))
			return;

		_rangedChargeElapsed = 0;
		_rangedChargeDuration = 0;
		_enemy.BroadcastBowAnimationState("Idle");
	}

	private Vector3 GetMovementDirection(Node3D target, Vector3 directOffset, out string info)
	{
		Vector3 direct = directOffset.Normalized();

		if (_navigationAgent == null)
		{
			info = "no agent, direct";
			return direct;
		}

		Vector3 targetPosition = target.GlobalPosition;
		if (!_hasNavigationTarget ||
			_lastNavigationTarget.DistanceSquaredTo(targetPosition) > 0.0625f)
		{
			_navigationAgent.TargetPosition = targetPosition;
			_lastNavigationTarget = targetPosition;
			_hasNavigationTarget = true;
		}

		if (NavigationServer3D.MapGetIterationId(_navigationAgent.GetNavigationMap()) == 0)
		{
			info = "map not ready, direct";
			return direct;
		}

		Vector3 nextPosition = _navigationAgent.GetNextPathPosition();
		int pathLength = _navigationAgent.GetCurrentNavigationPath().Length;
		bool reachable = _navigationAgent.IsTargetReachable();
		info = $"pathLen={pathLength}, reachable={reachable}, finished={_navigationAgent.IsNavigationFinished()}";

		// Нет пути или цель недостижима по навмешу - идём напрямую, чтобы враг не стоял на месте.
		if (pathLength <= 1 || !reachable)
		{
			info += ", direct";
			return direct;
		}

		Vector3 pathOffset = nextPosition - _enemy.GlobalPosition;
		pathOffset.Y = 0;
		if (pathOffset.LengthSquared() > 0.0001f)
			return pathOffset.Normalized();

		info += ", direct";
		return direct;
	}

	private void OnBodyEntered(Node3D body)
	{
		if (body is Player && !_targetsInRange.Contains(body))
			_targetsInRange.Add(body);
	}

	private void OnBodyExited(Node3D body)
	{
		_targetsInRange.Remove(body);
	}

	private Node3D FindNearestTargetInRange()
	{
		Node3D nearestTarget = null;
		float nearestDistanceSquared = float.MaxValue;

		for (int i = _targetsInRange.Count - 1; i >= 0; i--)
		{
			Node3D candidate = _targetsInRange[i];
			if (!GodotObject.IsInstanceValid(candidate) || !candidate.IsInsideTree())
			{
				_targetsInRange.RemoveAt(i);
				continue;
			}

			float distanceSquared = _enemy.GlobalPosition.DistanceSquaredTo(candidate.GlobalPosition);
			if (distanceSquared >= nearestDistanceSquared)
				continue;

			nearestDistanceSquared = distanceSquared;
			nearestTarget = candidate;
		}

		return nearestTarget;
	}

	private void StopHorizontalMovement(ref Vector3 velocity, float delta)
	{
		velocity.X = Mathf.MoveToward(velocity.X, 0, Speed * delta);
		velocity.Z = Mathf.MoveToward(velocity.Z, 0, Speed * delta);
	}

	private bool HasAuthority()
	{
		return !Multiplayer.HasMultiplayerPeer() ||
			(Multiplayer.IsServer() && _enemy.GetMultiplayerAuthority() == Multiplayer.GetUniqueId());
	}
}
