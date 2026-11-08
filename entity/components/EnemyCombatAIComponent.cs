using Godot;

public partial class EnemyCombatAIComponent : Node
{
    private const float MaxBowAimPitch = Mathf.Pi / 4.0f;
    private const float GravityEpsilon = 0.0001f;
    private const float BowTargetHeightOffset = -0.25f;
    private const double CombatStateTransitionInterval = 0.5;

    private enum CombatState
    {
        None,
        Running,
        Shooting
    }

    [Export] public float MoveSpeed { get; set; } = 3.5f;
    [Export] public float MeleeAttackRange { get; set; } = 1.6f;
    [Export] public float BowMinRange { get; set; } = 10.0f;
    [Export] public float BowMaxRange { get; set; } = 20.0f;
    [Export] public double AttackCooldown { get; set; } = 1.0;

    private AnimatedEntity _owner;
    private NavigationAgent3D _navigationAgent;
    private AnimatedEntity _target;
    private Weapon _chargingWeapon;
    private double _attackCooldown;
    private double _bowChargeTime;
    private double _timeSinceCombatStateChange = CombatStateTransitionInterval;
    private CombatState _combatState;

    public override void _Ready()
    {
        _owner = GetParent() as AnimatedEntity;
        if (_owner == null)
        {
            GD.PushError("EnemyCombatAIComponent must be a child of AnimatedEntity.");
            return;
        }

        _navigationAgent = _owner.GetNodeOrNull<NavigationAgent3D>("NavigationAgent3D");
        if (_navigationAgent == null)
            GD.PushWarning($"Enemy '{_owner.Name}' has no NavigationAgent3D; combat movement will be direct.");

        if (BowMinRange < 0 || BowMaxRange <= BowMinRange)
            GD.PushError($"EnemyCombatAIComponent on '{_owner.Name}' requires BowMaxRange to be greater than BowMinRange.");
    }

    public void ProcessTarget(AnimatedEntity target, double delta)
    {
        if (_owner == null || target == null)
            return;

        _attackCooldown = Mathf.Max(0.0, _attackCooldown - delta);
        _timeSinceCombatStateChange += delta;
        if (!ReferenceEquals(_target, target))
        {
            StopCombat();
            _target = target;
        }

        Vector3 offset = target.GlobalPosition - _owner.GlobalPosition;
        Vector3 horizontalOffset = new(offset.X, 0.0f, offset.Z);
        Weapon weapon = _owner.EquippedWeapon;

        if (weapon is RangedWeapon bow &&
            HasSkill(TaskData.Skills.RangedCombat) &&
            bow.HasValidConfiguration)
        {
            ProcessRangedCombat(target, bow, horizontalOffset, delta);
            return;
        }

        CancelBowCharge();
        if (weapon is MeleeWeapon && HasSkill(TaskData.Skills.MeleeCombat))
        {
            ProcessMeleeCombat(target, horizontalOffset);
            return;
        }

        StopMoving();
    }

    public void StopCombat()
    {
        CancelBowCharge();
        _target = null;
        _chargingWeapon = null;
        _attackCooldown = 0.0;
        _combatState = CombatState.None;
        _timeSinceCombatStateChange = CombatStateTransitionInterval;
        _owner?.BroadcastBowAnimationState("Idle");
    }

    private void ProcessMeleeCombat(AnimatedEntity target, Vector3 horizontalOffset)
    {
        if (horizontalOffset.Length() > MeleeAttackRange)
        {
            MoveToward(target.GlobalPosition);
            return;
        }

        StopMoving();
        _owner.FaceMoveDirection(horizontalOffset);
        if (!string.IsNullOrEmpty(_owner.ReplicatedAttack) || _attackCooldown > 0.0)
            return;

        _owner.PlayAttack1();
        _attackCooldown = AttackCooldown;
    }

    private void ProcessRangedCombat(
        AnimatedEntity target,
        RangedWeapon bow,
        Vector3 horizontalOffset,
        double delta)
    {
        float distance = horizontalOffset.Length();
        if (distance > BowMaxRange)
        {
            CancelBowCharge();
            MoveToward(target.GlobalPosition);
            return;
        }

        if (distance < BowMinRange)
        {
            CancelBowCharge();
            MoveAwayFrom(horizontalOffset);
            return;
        }

        StopMoving(updateAnimation: false);
        if (!TryChangeCombatState(CombatState.Shooting))
        {
            CancelBowCharge();
            return;
        }

        if (string.IsNullOrEmpty(_owner.ReplicatedAttack))
            _owner.PlayIdle();

        _owner.FaceMoveDirection(horizontalOffset);
        Vector3 targetPosition = target.GlobalPosition + Vector3.Up * BowTargetHeightOffset;
        if (!TryGetBallisticDirection(bow, targetPosition, out Vector3 shotDirection))
        {
            _owner.ReplicatedBowAimPitch = 0.0f;
            CancelBowCharge();
            return;
        }

        AimBowAt(shotDirection);

        if (!_owner.HasProjectileInInventory(bow.RequiredProjectile))
        {
            CancelBowCharge();
            return;
        }

        if (_attackCooldown > 0.0)
            return;

        if (_chargingWeapon != bow)
        {
            _chargingWeapon = bow;
            _bowChargeTime = 0.0;
            _owner.BroadcastBowAnimationState("Pulling", bow.MaxChargeTime);
        }

        _bowChargeTime += delta;
        if (_bowChargeTime < bow.MaxChargeTime)
            return;

        if (_owner.TryFireBowAt(targetPosition, (float)_bowChargeTime, shotDirection))
        {
            _attackCooldown = AttackCooldown;
            _chargingWeapon = null;
            _bowChargeTime = 0.0;
        }
        else
        {
            CancelBowCharge();
        }
    }

    private bool TryGetBallisticDirection(
        RangedWeapon bow,
        Vector3 targetPosition,
        out Vector3 direction)
    {
        Vector3 originDirection = targetPosition - _owner.GlobalPosition;
        if (originDirection.LengthSquared() <= 0.0001f)
        {
            direction = Vector3.Zero;
            return false;
        }

        originDirection = originDirection.Normalized();
        WeaponScene bowScene = _owner.EquippedWeaponScene;
        Vector3 origin = bowScene != null
            ? bowScene.GlobalPosition + originDirection * 0.25f
            : _owner.GlobalPosition + Vector3.Up * 1.35f + originDirection * 0.8f;

        Vector3 displacement = targetPosition - origin;
        Vector3 horizontalDisplacement = new(displacement.X, 0.0f, displacement.Z);
        float horizontalDistance = horizontalDisplacement.Length();
        if (horizontalDistance <= 0.0001f)
        {
            direction = displacement.Normalized();
            return direction.LengthSquared() > 0.0001f;
        }

        float gravity = Mathf.Abs(_owner.GetGravity().Y);
        if (gravity <= GravityEpsilon)
        {
            direction = displacement.Normalized();
            return true;
        }

        float speed = bow.ProjectileSpeed;
        float speedSquared = speed * speed;
        float verticalDistance = displacement.Y;
        float discriminant = speedSquared * speedSquared -
            gravity * (gravity * horizontalDistance * horizontalDistance +
                2.0f * verticalDistance * speedSquared);
        float pitch = MaxBowAimPitch;
        if (discriminant >= 0.0f)
        {
            float sqrtDiscriminant = Mathf.Sqrt(discriminant);
            float denominator = gravity * horizontalDistance;
            float lowPitch = Mathf.Atan((speedSquared - sqrtDiscriminant) / denominator);
            float highPitch = Mathf.Atan((speedSquared + sqrtDiscriminant) / denominator);
            if (Mathf.Abs(lowPitch) <= MaxBowAimPitch)
                pitch = lowPitch;
            else if (Mathf.Abs(highPitch) <= MaxBowAimPitch)
                pitch = highPitch;
        }

        Vector3 horizontalDirection = horizontalDisplacement / horizontalDistance;
        direction = (horizontalDirection * Mathf.Cos(pitch) + Vector3.Up * Mathf.Sin(pitch)).Normalized();
        return true;
    }

    private void AimBowAt(Vector3 shotDirection)
    {
        Vector3 horizontalDirection = new(shotDirection.X, 0.0f, shotDirection.Z);
        float pitch = Mathf.Atan2(shotDirection.Y, horizontalDirection.Length());
        _owner.ReplicatedBowAimPitch = Mathf.Clamp(pitch, -MaxBowAimPitch, MaxBowAimPitch);
    }

    private void MoveToward(Vector3 targetPosition)
    {
        if (_navigationAgent != null &&
            _navigationAgent.TargetPosition.DistanceSquaredTo(targetPosition) > 0.25f)
            _navigationAgent.TargetPosition = targetPosition;

        Vector3 nextPosition = _navigationAgent?.GetNextPathPosition() ?? targetPosition;
        Vector3 direction = nextPosition - _owner.GlobalPosition;
        direction.Y = 0.0f;
        if (direction.LengthSquared() < 0.001f)
            direction = targetPosition - _owner.GlobalPosition;

        MoveInDirection(direction);
    }

    private void MoveAwayFrom(Vector3 targetOffset)
    {
        if (targetOffset.LengthSquared() <= 0.001f)
        {
            StopMoving();
            return;
        }

        MoveInDirection(-targetOffset);
    }

    private void MoveInDirection(Vector3 direction)
    {
        direction.Y = 0.0f;
        if (direction.LengthSquared() <= 0.001f)
        {
            StopMoving();
            return;
        }

        direction = direction.Normalized();
        _owner.Velocity = new Vector3(
            direction.X * MoveSpeed,
            _owner.Velocity.Y,
            direction.Z * MoveSpeed);
        _owner.FaceMoveDirection(direction);
        if (TryChangeCombatState(CombatState.Running))
            _owner.PlayRunForEquippedWeapon();
    }

    private void StopMoving(bool updateAnimation = true)
    {
        _owner.Velocity = new Vector3(0.0f, _owner.Velocity.Y, 0.0f);
        if (updateAnimation && string.IsNullOrEmpty(_owner.ReplicatedAttack))
            _owner.PlayIdle();
    }

    private bool TryChangeCombatState(CombatState nextState)
    {
        if (_combatState == nextState)
            return true;

        if (_timeSinceCombatStateChange < CombatStateTransitionInterval)
            return false;

        _combatState = nextState;
        _timeSinceCombatStateChange = 0.0;
        return true;
    }

    private void CancelBowCharge()
    {
        if (_chargingWeapon == null)
            return;

        _chargingWeapon = null;
        _bowChargeTime = 0.0;
        _owner.BroadcastBowAnimationState("Idle");
    }

    private bool HasSkill(TaskData.Skills skill) =>
        _owner.Skills?.Contains(skill) == true;
}
