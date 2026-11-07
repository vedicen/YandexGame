using Godot;
using System.Collections.Generic;

public partial class EnemyAIComponent : Node
{
	[Export] public float Speed { get; set; } = 3.0f;
	[Export] public float StopDistance { get; set; } = 1.5f;
	[Export] public float TurnSpeed { get; set; } = 8.0f;
	[Export] public Area3D DetectionArea { get; set; }

	private AnimatedEntity _enemy;
	private readonly List<Node3D> _targetsInRange = new();

	public override void _Ready()
	{
		_enemy = GetParent() as AnimatedEntity;
		if (_enemy == null)
		{
			GD.PushError("EnemyAIComponent must be a child of an AnimatedEntity.");
			SetPhysicsProcess(false);
			return;
		}

		DetectionArea ??= GetNodeOrNull<Area3D>("../DetectionArea");
		if (DetectionArea == null)
		{
			GD.PushError($"EnemyAIComponent '{GetPath()}' requires a DetectionArea.");
			SetPhysicsProcess(false);
			return;
		}

		DetectionArea.BodyEntered += OnBodyEntered;
		DetectionArea.BodyExited += OnBodyExited;
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
		if (_enemy == null || !HasAuthority())
			return;

		Node3D target = FindNearestTargetInRange();
		Vector3 velocity = _enemy.Velocity;

		if (!_enemy.IsOnFloor())
			velocity += _enemy.GetGravity() * (float)delta;

		if (target == null)
		{
			StopHorizontalMovement(ref velocity, (float)delta);
			_enemy.PlayIdle();
		}
		else
		{
			Vector3 offset = target.GlobalPosition - _enemy.GlobalPosition;
			offset.Y = 0;

			if (offset.Length() <= StopDistance)
			{
				StopHorizontalMovement(ref velocity, (float)delta);
				_enemy.PlayIdle();
			}
			else
			{
				Vector3 direction = offset.Normalized();
				velocity.X = direction.X * Speed;
				velocity.Z = direction.Z * Speed;

				float targetRotation = Mathf.Atan2(-direction.X, -direction.Z);
				Vector3 rotation = _enemy.Rotation;
				rotation.Y = Mathf.LerpAngle(rotation.Y, targetRotation, TurnSpeed * (float)delta);
				_enemy.Rotation = rotation;
				_enemy.PlayRun();
			}
		}

		_enemy.Velocity = velocity;
		_enemy.MoveAndSlide();
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
