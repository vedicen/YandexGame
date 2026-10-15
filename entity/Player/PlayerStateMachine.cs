using Godot;
using System;

public partial class PlayerStateMachine : Node
{
    public enum State { Locomotion, Inventory, Chest, Disabled }
    public enum Motion { Idle, Walking, Crouching, CrouchWalking, Airborne }
    public State Current { get; private set; }
    public State Previous { get; private set; }
    public Motion MovementState { get; private set; }
    public bool CanMove => Current == State.Locomotion;
    public bool CanLook => CanMove;
    public bool CanAttack => CanMove;
    public event Action<State> Changed;
    public event Action<Motion> MovementChanged;
    public bool Transition(State next)
    {
        if (Current == next || Current == State.Disabled && next != State.Locomotion) return false;
        Exit(Current);
        Previous = Current;
        Current = next;
        Enter(next);
        Changed?.Invoke(next);
        return true;
    }
    // Locomotion is a nested state: entering an interaction always interrupts it.
    public void UpdateMotion(bool grounded, bool moving, bool crouching)
    {
        var next = !CanMove ? Motion.Idle : !grounded ? Motion.Airborne :
            crouching ? (moving ? Motion.CrouchWalking : Motion.Crouching) : moving ? Motion.Walking : Motion.Idle;
        if (MovementState == next) return;
        MovementState = next;
        MovementChanged?.Invoke(next);
    }
    private void Exit(State state) => (GetParent() as Player)?.playerInputComponent?.ConsumeLookMotion();
    private void Enter(State state)
    {
        var player = GetParent<Player>();
        if (!CanMove) { player.Velocity = Vector3.Zero; player.PlayIdle(); UpdateMotion(true, false, false); }
        if (player.playerInputComponent?.HasInputAuthority == true)
            Input.MouseMode = CanMove ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
    }
}
