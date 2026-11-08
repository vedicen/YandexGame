using Godot;
using System.Collections.Generic;

public partial class EnemyAIComponent : Node
{
    [Export] public Area3D DetectionArea { get; set; }
    [Export] public float MoveSpeed { get; set; } = 3.5f;
    [Export] public float CollectionRange { get; set; } = 1.25f;
    [Export] public float AttackRange { get; set; } = 1.6f;
    [Export] public double RescanInterval { get; set; } = 0.5;

    private AnimatedEntity _owner;
    private NavigationAgent3D _navigationAgent;
    private double _rescanTimer;
    private double _attackCooldown;
    private AITask _activeTask;

    private readonly List<AITask> _tasks = new();

    public IReadOnlyList<AITask> Tasks => _tasks;

    public override void _Ready()
    {
        _owner = GetParent() as AnimatedEntity;
        if (_owner == null)
        {
            GD.PushError("EnemyAIComponent must be a child of AnimatedEntity.");
            return;
        }

        if (DetectionArea == null)
        {
            GD.PushError("DetectionArea is not assigned.");
            return;
        }

        _navigationAgent = _owner.GetNodeOrNull<NavigationAgent3D>("NavigationAgent3D");
        if (_navigationAgent == null)
            GD.PushWarning($"Enemy '{_owner.Name}' has no NavigationAgent3D; AI will move directly toward tasks.");

        DetectionArea.BodyEntered += OnBodyEntered;
    }

    public override void _ExitTree()
    {
        if (DetectionArea != null && IsInstanceValid(DetectionArea))
            DetectionArea.BodyEntered -= OnBodyEntered;

        ReleaseActiveTask(completed: false);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_owner == null || DetectionArea == null || !IsServerAuthority())
            return;

        _attackCooldown = Mathf.Max(0.0, _attackCooldown - delta);

        _rescanTimer -= delta;
        if (_rescanTimer <= 0.0)
        {
            _rescanTimer = RescanInterval;
            RescanTargets();
        }

        if (!IsTaskTargetValid(_activeTask))
        {
            ReleaseActiveTask(completed: false);
            StopMoving();
            return;
        }

        ProcessActiveTask(delta);
    }

    private void OnBodyEntered(Node3D body)
    {
        if (body is Entity && IsServerAuthority())
            RescanTargets();
    }

    private void RescanTargets()
    {
        AnimatedEntity hostile = FindVisibleHostile();
        if (hostile != null)
        {
            if (_activeTask?.Data.Type != TaskData.TaskTypes.Kill ||
                !ReferenceEquals(_activeTask.Target, hostile))
            {
                ReleaseActiveTask(completed: false);
                AssignKillTask(hostile);
            }

            return;
        }

        if (_activeTask != null)
            return;

        Entity taskTarget = FindAvailableCollectionTask();
        if (taskTarget != null)
            TryAcceptCollectionTask(taskTarget);
    }

    private AnimatedEntity FindVisibleHostile()
    {
        AnimatedEntity closest = null;
        float closestDistanceSquared = float.MaxValue;

        foreach (Node3D body in DetectionArea.GetOverlappingBodies())
        {
            if (body is not AnimatedEntity target ||
                ReferenceEquals(target, _owner) ||
                !IsAlive(target) ||
                _owner.Race == TaskData.Races.None ||
                target.Race == TaskData.Races.None ||
                target.Race == _owner.Race)
                continue;

            float distanceSquared = _owner.GlobalPosition.DistanceSquaredTo(target.GlobalPosition);
            if (distanceSquared < closestDistanceSquared)
            {
                closest = target;
                closestDistanceSquared = distanceSquared;
            }
        }

        return closest;
    }

    private Entity FindAvailableCollectionTask()
    {
        Entity closest = null;
        float closestDistanceSquared = float.MaxValue;

        foreach (Node3D body in DetectionArea.GetOverlappingBodies())
        {
            if (body is not Entity target ||
                ReferenceEquals(target, _owner) ||
                target.CurrentTaskOwner != null)
                continue;

            TaskData data = target.GetTask();
            if (data == null ||
                data.State != TaskData.TaskStates.Required ||
                data.Type != TaskData.TaskTypes.Collect ||
                !HasRequiredRace(data) ||
                !HasRequiredSkills(data))
                continue;

            float distanceSquared = _owner.GlobalPosition.DistanceSquaredTo(target.GlobalPosition);
            if (distanceSquared < closestDistanceSquared)
            {
                closest = target;
                closestDistanceSquared = distanceSquared;
            }
        }

        return closest;
    }

    private void TryAcceptCollectionTask(Entity target)
    {
        if (!IsInstanceValid(target) || target.CurrentTaskOwner != null)
            return;

        TaskData data = target.GetTask();
        if (data == null ||
            data.State != TaskData.TaskStates.Required ||
            data.Type != TaskData.TaskTypes.Collect ||
            !HasRequiredRace(data) ||
            !HasRequiredSkills(data))
            return;

        target.CurrentTaskOwner = _owner;
        data.State = TaskData.TaskStates.InProgress;
        SetActiveTask(new AITask(target, data));
        GD.Print($"{_owner.Name} accepted collection task from {target.Name}");
    }

    private void AssignKillTask(AnimatedEntity target)
    {
        var data = new TaskData
        {
            Type = TaskData.TaskTypes.Kill,
            State = TaskData.TaskStates.InProgress
        };

        SetActiveTask(new AITask(target, data));
        GD.Print($"{_owner.Name} assigned kill task for {target.Name}");
    }

    private void SetActiveTask(AITask task)
    {
        _activeTask = task;
        _tasks.Clear();
        _tasks.Add(task);
        if (_navigationAgent != null)
            _navigationAgent.TargetPosition = task.Target.GlobalPosition;
        UpdateNavigationTarget();
    }

    private void ReleaseActiveTask(bool completed)
    {
        if (_activeTask == null)
            return;

        Entity target = _activeTask.Target;
        TaskData data = _activeTask.Data;
        bool isCollection = data.Type == TaskData.TaskTypes.Collect;

        if (IsInstanceValid(target) && isCollection)
        {
            if (ReferenceEquals(target.CurrentTaskOwner, _owner))
                target.CurrentTaskOwner = null;

            data.State = completed
                ? TaskData.TaskStates.Completed
                : TaskData.TaskStates.Required;
        }

        _activeTask = null;
        _tasks.Clear();
    }

    private void ProcessActiveTask(double delta)
    {
        Entity target = _activeTask.Target;
        Vector3 offset = target.GlobalPosition - _owner.GlobalPosition;
        Vector3 horizontalOffset = new(offset.X, 0.0f, offset.Z);
        float range = _activeTask.Data.Type == TaskData.TaskTypes.Kill
            ? AttackRange
            : CollectionRange;

        if (horizontalOffset.Length() <= range)
        {
            StopMoving();
            _owner.FaceMoveDirection(horizontalOffset);

            if (_activeTask.Data.Type == TaskData.TaskTypes.Kill)
                TryAttack(target);
            else
                CompleteCollectionTask(target);

            return;
        }

        UpdateNavigationTarget();
        Vector3 nextPosition = _navigationAgent?.GetNextPathPosition() ?? target.GlobalPosition;
        Vector3 direction = nextPosition - _owner.GlobalPosition;
        direction.Y = 0.0f;
        if (direction.LengthSquared() < 0.001f)
        {
            direction = horizontalOffset;
        }

        direction = direction.Normalized();
        _owner.Velocity = new Vector3(
            direction.X * MoveSpeed,
            _owner.Velocity.Y,
            direction.Z * MoveSpeed);
        _owner.FaceMoveDirection(direction);

        if (_owner.EquippedWeapon is MeleeWeapon)
            _owner.PlayRunWithSword();
        else
            _owner.PlayRun();
    }

    private void TryAttack(Entity target)
    {
        if (_owner.EquippedWeapon is not MeleeWeapon ||
            !string.IsNullOrEmpty(_owner.ReplicatedAttack) ||
            _attackCooldown > 0.0 ||
            target is not AnimatedEntity)
            return;

        _owner.PlayAttack1();
        _attackCooldown = 1.0;
    }

    private void CompleteCollectionTask(Entity target)
    {
        ReleaseActiveTask(completed: true);
        target.QueueFree();
        GD.Print($"{_owner.Name} collected {target.Name}");
    }

    private void UpdateNavigationTarget()
    {
        if (_navigationAgent != null &&
            IsTaskTargetValid(_activeTask) &&
            _navigationAgent.TargetPosition.DistanceSquaredTo(_activeTask.Target.GlobalPosition) > 0.25f)
        {
            _navigationAgent.TargetPosition = _activeTask.Target.GlobalPosition;
        }
    }

    private bool IsTaskTargetValid(AITask task)
    {
        if (task == null || !IsInstanceValid(task.Target) || task.Target.IsQueuedForDeletion())
            return false;

        if (task.Data.Type == TaskData.TaskTypes.Kill)
            return task.Target is AnimatedEntity && IsAlive(task.Target);

        return task.Data.Type == TaskData.TaskTypes.Collect &&
            task.Data.State == TaskData.TaskStates.InProgress;
    }

    private static bool IsAlive(Entity entity) =>
        entity.Health == null || entity.Health.CurrentHealth > 0.0f;

    private void StopMoving()
    {
        _owner.Velocity = new Vector3(0.0f, _owner.Velocity.Y, 0.0f);
        _owner.PlayIdle();
    }

    private bool HasRequiredRace(TaskData data) =>
        data.RequiredRace == TaskData.Races.None || _owner.Race == data.RequiredRace;

    private bool HasRequiredSkills(TaskData data)
    {
        if (data.RequiredSkills == null || data.RequiredSkills.Count == 0)
            return true;

        if (_owner.Skills == null)
            return false;

        foreach (TaskData.Skills skill in data.RequiredSkills)
        {
            if (!_owner.Skills.Contains(skill))
                return false;
        }

        return true;
    }

    private bool IsServerAuthority() =>
        !Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer();
}
