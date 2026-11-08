using Godot;
using System.Collections.Generic;

public partial class EnemyAIComponent : Node
{
    [Export] public Area3D DetectionArea { get; set; }
    [Export] public float MoveSpeed { get; set; } = 3.5f;
    [Export] public float CollectionRange { get; set; } = 1.25f;
    [Export] public float AttackRange { get; set; } = 1.6f;
    // Сколько мс игнорировать предмет после неудачного подбора
    [Export] public int PickupRetryDelayMs { get; set; } = 5000;
    [Export] public double RescanInterval { get; set; } = 0.5;

    // Приоритеты задач: чем больше число, тем важнее.
    // Новая задача прерывает текущую, только если её приоритет СТРОГО выше.
    [ExportGroup("Priorities")]
    [Export] public int KillPriority { get; set; } = 100;
    [Export] public int CollectPriority { get; set; } = 10;
    [Export] public int LootPriority { get; set; } = 20;
    [Export] public int RepairPriority { get; set; } = 5;

    private struct Candidate
    {
        public Entity Target;
        public TaskData.TaskTypes Type;
        public int Priority;
        public float DistanceSquared;
    }

    private AnimatedEntity _owner;
    private NavigationAgent3D _navigationAgent;
    private double _rescanTimer;
    private double _attackCooldown;
    private AITask _activeTask;

    private readonly List<AITask> _tasks = new();
    private readonly Dictionary<Entity, ulong> _ignoredUntilMsec = new();

    public IReadOnlyList<AITask> Tasks => _tasks;

    public int GetPriority(TaskData.TaskTypes type) => type switch
    {
        TaskData.TaskTypes.Kill => KillPriority,
        TaskData.TaskTypes.Collect => CollectPriority,
        TaskData.TaskTypes.Loot => LootPriority,
        TaskData.TaskTypes.Repair => RepairPriority,
        _ => int.MinValue
    };

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
        DetectionArea.BodyExited += OnBodyExited;
    }

    public override void _ExitTree()
    {
        if (DetectionArea != null && IsInstanceValid(DetectionArea))
        {
            DetectionArea.BodyEntered -= OnBodyEntered;
            DetectionArea.BodyExited -= OnBodyExited;
        }

        ReleaseActiveTask(completed: false);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_owner == null || DetectionArea == null || !IsServerAuthority())
            return;

        // Мёртвый враг ничего не делает
        if (!IsAlive(_owner))
        {
            ReleaseActiveTask(completed: false);
            return;
        }

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
        if (body is Entity && IsServerAuthority() && IsAlive(_owner))
            RescanTargets();
    }

    private void OnBodyExited(Node3D body)
    {
        if (body is not AnimatedEntity target ||
            !IsServerAuthority() ||
            _activeTask?.Data.Type != TaskData.TaskTypes.Kill ||
            !ReferenceEquals(_activeTask.Target, target))
            return;

        ReleaseActiveTask(completed: false);
        StopMoving();
    }

    private void RescanTargets()
    {
        // Собираем кандидатов всех видов задач
        Candidate? best = null;

        AnimatedEntity hostile = FindVisibleHostile();
        if (hostile != null)
        {
            best = Better(best, new Candidate
            {
                Target = hostile,
                Type = TaskData.TaskTypes.Kill,
                Priority = KillPriority,
                DistanceSquared = _owner.GlobalPosition.DistanceSquaredTo(hostile.GlobalPosition)
            });
        }

        Entity collectTarget = FindAvailableCollectionTask();
        if (collectTarget != null)
        {
            best = Better(best, new Candidate
            {
                Target = collectTarget,
                Type = TaskData.TaskTypes.Collect,
                Priority = CollectPriority,
                DistanceSquared = _owner.GlobalPosition.DistanceSquaredTo(collectTarget.GlobalPosition)
            });
        }

        Chest lootTarget = FindAvailableLootTask();
        if (lootTarget != null)
        {
            best = Better(best, new Candidate
            {
                Target = lootTarget,
                Type = TaskData.TaskTypes.Loot,
                Priority = LootPriority,
                DistanceSquared = _owner.GlobalPosition.DistanceSquaredTo(lootTarget.GlobalPosition)
            });
        }

        if (best == null)
            return;

        Candidate candidate = best.Value;

        if (_activeTask != null)
        {
            int activePriority = GetPriority(_activeTask.Data.Type);

            // Прерываем текущую задачу только более важной
            if (candidate.Priority <= activePriority)
                return;

            ReleaseActiveTask(completed: false);
        }

        switch (candidate.Type)
        {
            case TaskData.TaskTypes.Kill:
                AssignKillTask((AnimatedEntity)candidate.Target);
                break;
            case TaskData.TaskTypes.Collect:
                TryAcceptCollectionTask(candidate.Target);
                break;
            case TaskData.TaskTypes.Loot:
                TryAcceptLootTask(candidate.Target as Chest);
                break;
        }
    }

    private static Candidate? Better(Candidate? current, Candidate other)
    {
        if (current == null)
            return other;

        Candidate c = current.Value;

        if (other.Priority > c.Priority)
            return other;

        if (other.Priority == c.Priority && other.DistanceSquared < c.DistanceSquared)
            return other;

        return current;
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
            if (body is not PickUpObj { AIGatherable: true } target ||
                ReferenceEquals(target, _owner) ||
                target.CurrentTaskOwner != null ||
                IsIgnored(target))
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

    private Chest FindAvailableLootTask()
    {
        Chest closest = null;
        float closestDistanceSquared = float.MaxValue;

        foreach (Node3D body in DetectionArea.GetOverlappingBodies())
        {
            if (body is not Chest target ||
                target.CurrentTaskOwner != null ||
                IsIgnored(target))
                continue;

            TaskData data = target.GetTask();
            if (data == null ||
                data.State != TaskData.TaskStates.Required ||
                data.Type != TaskData.TaskTypes.Loot ||
                !IsForeignChest(data) ||
                !HasRequiredSkills(data) ||
                !ChestHasLoot(target))
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

    private void TryAcceptLootTask(Chest target)
    {
        if (!IsInstanceValid(target) || target.CurrentTaskOwner != null)
            return;

        TaskData data = target.GetTask();
        if (data == null ||
            data.State != TaskData.TaskStates.Required ||
            data.Type != TaskData.TaskTypes.Loot ||
            !IsForeignChest(data) ||
            !HasRequiredSkills(data) ||
            !ChestHasLoot(target))
            return;

        target.CurrentTaskOwner = _owner;
        data.State = TaskData.TaskStates.InProgress;
        SetActiveTask(new AITask(target, data));
        GD.Print($"{_owner.Name} accepted loot task from {target.Name}");
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
    }

    private void ReleaseActiveTask(bool completed)
    {
        if (_activeTask == null)
            return;

        Entity target = _activeTask.Target;
        TaskData data = _activeTask.Data;
        bool isOwnedTask =
            data.Type == TaskData.TaskTypes.Collect ||
            data.Type == TaskData.TaskTypes.Loot;

        if (IsInstanceValid(target) && isOwnedTask)
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
        bool isKill = _activeTask.Data.Type == TaskData.TaskTypes.Kill;
        bool isLoot = _activeTask.Data.Type == TaskData.TaskTypes.Loot;

        float range = isKill ? AttackRange : CollectionRange;

        if (horizontalOffset.Length() <= range)
        {
            StopMoving();
            _owner.FaceMoveDirection(horizontalOffset);

            if (isKill)
                TryAttack(target);
            else if (isLoot)
                LootChest(target);
            else if (_activeTask.Data.Type == TaskData.TaskTypes.Collect)
                CompleteCollectionTask(target);

            return;
        }

        UpdateNavigationTarget();
        Vector3 nextPosition = _navigationAgent?.GetNextPathPosition() ?? target.GlobalPosition;
        Vector3 direction = nextPosition - _owner.GlobalPosition;
        direction.Y = 0.0f;
        if (direction.LengthSquared() < 0.001f)
            direction = horizontalOffset;

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
            _owner.Skills?.Contains(TaskData.Skills.MeleeCombat) != true ||
            !string.IsNullOrEmpty(_owner.ReplicatedAttack) ||
            _attackCooldown > 0.0 ||
            target is not AnimatedEntity)
            return;

        _owner.PlayAttack1();
        _attackCooldown = 1.0;
    }

    private void CompleteCollectionTask(Entity target)
    {
        if (target is not PickUpObj pickup)
        {
            GD.PushError($"Collection task target '{target.Name}' is not a PickUpObj.");
            ReleaseActiveTask(completed: false);
            return;
        }

        InventoryService inventoryService = InventoryService.Instance;
        if (inventoryService == null)
        {
            GD.PushError("Cannot collect item: InventoryService is unavailable.");
            ReleaseActiveTask(completed: false);
            return;
        }

        // Имя берём заранее: после подбора объект может быть уже освобождён
        string targetName = target.Name;

        if (!inventoryService.PickupForEntity(_owner, pickup))
        {
            // Инвентарь полон и т.п. — отпускаем и временно игнорируем предмет
            _ignoredUntilMsec[target] = Time.GetTicksMsec() + (ulong)PickupRetryDelayMs;
            ReleaseActiveTask(completed: false);
            return;
        }

        ReleaseActiveTask(completed: true);
        GD.Print($"{_owner.Name} collected {targetName}");
    }

    private void LootChest(Entity target)
    {
        if (target is not Chest chest || !ChestHasLoot(chest))
        {
            ReleaseActiveTask(completed: true);
            return;
        }

        InventoryService inventoryService = InventoryService.Instance;
        if (inventoryService == null)
        {
            GD.PushError("Cannot loot chest: InventoryService is unavailable.");
            IgnoreAndReleaseLootTask(chest);
            return;
        }

        string chestName = chest.Name;
        if (!inventoryService.LootChestForEntity(_owner, chest))
        {
            IgnoreAndReleaseLootTask(chest);
            return;
        }

        if (!ChestHasLoot(chest))
        {
            ReleaseActiveTask(completed: true);
            GD.Print($"{_owner.Name} looted {chestName}");
        }
    }

    private void IgnoreAndReleaseLootTask(Chest chest)
    {
        _ignoredUntilMsec[chest] = Time.GetTicksMsec() + (ulong)PickupRetryDelayMs;
        ReleaseActiveTask(completed: false);
    }

    private static bool ChestHasLoot(Chest chest)
    {
        if (chest?.Inventory?.Slots == null)
            return false;

        foreach (Stack stack in chest.Inventory.Slots)
        {
            if (stack?.Item != null && stack.ItemCount > 0)
                return true;
        }

        return false;
    }

    private bool IsForeignChest(TaskData data) =>
        data.RequiredRace == TaskData.Races.None ||
        (_owner.Race != TaskData.Races.None && data.RequiredRace != _owner.Race);

    private bool IsIgnored(Entity target)
    {
        if (!_ignoredUntilMsec.TryGetValue(target, out ulong until))
            return false;

        if (Time.GetTicksMsec() >= until || !IsInstanceValid(target))
        {
            _ignoredUntilMsec.Remove(target);
            return false;
        }

        return true;
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

        return task.Data.State == TaskData.TaskStates.InProgress &&
            (task.Data.Type == TaskData.TaskTypes.Collect ||
             task.Data.Type == TaskData.TaskTypes.Loot && task.Target is Chest);
    }

    private static bool IsAlive(Entity entity) =>
        entity.Health == null || entity.Health.CurrentHealth > 0.0f;

    private void StopMoving()
    {
        _owner.Velocity = new Vector3(0.0f, _owner.Velocity.Y, 0.0f);

        // Не перебиваем анимацию атаки
        if (string.IsNullOrEmpty(_owner.ReplicatedAttack))
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