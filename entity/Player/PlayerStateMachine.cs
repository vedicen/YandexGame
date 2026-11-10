using Godot;
using System;

public partial class PlayerStateMachine : Node
{
    public enum State { Locomotion, Inventory, Chest, Trading, BuildMenu, Disabled }
    public enum Motion { Idle, Walking, Running, Crouching, CrouchWalking, Airborne }
    public State Current { get; private set; }
    public State Previous { get; private set; }
    public Motion MovementState { get; private set; }
    public bool CanMove => Current == State.Locomotion;
    public bool CanLook => CanMove;
    public bool CanAttack => Current == State.Locomotion &&
        (GetParent() as Player)?.IsBuildPlacementActive != true;
    public event Action<State> Changed;
    public event Action<Motion> MovementChanged;
    public bool Transition(State next)
    {
        if (Current == next || Current == State.Disabled && next != State.Locomotion) return false;
        State previous = Current;
        Exit(Current);
        Previous = Current;
        Current = next;
        Enter(next);
        GD.Print($"[BuildDebug][State] {previous} -> {Current}; canMove={CanMove}, canLook={CanLook}, " +
            $"inputEnabled={(GetParent() as Player)?.playerInputComponent?.IsInputEnabled}, " +
            $"mouseMode={Input.MouseMode}.");
        Changed?.Invoke(next);
        return true;
    }
    // Locomotion is a nested state: entering an interaction always interrupts it.
    public void UpdateMotion(bool grounded, bool moving, bool crouching, bool running = false)
    {
        var next = !CanMove ? Motion.Idle : !grounded ? Motion.Airborne :
            crouching ? (moving ? Motion.CrouchWalking : Motion.Crouching) :
            moving ? (running ? Motion.Running : Motion.Walking) : Motion.Idle;
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
            Input.MouseMode = CanMove
                ? Input.MouseModeEnum.Captured
                : Input.MouseModeEnum.Visible;
    }
}
