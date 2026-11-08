using Godot;

public partial class Entity : CharacterBody3D
{


	[Export] public HealthComponent Health { get; private set; }
	[Export] public bool ApplyGravity = true;

    [ExportGroup("AI")]
    [Export] public TaskData Task { get; set; } = null;
    public Node3D CurrentTaskOwner;
	

	public TaskData GetTask()
	{
		return Task;
	}

	protected virtual bool HandlesMovement => false;

	public override void _PhysicsProcess(double delta)
	{
		if (ApplyGravity)
		{
			Vector3 velocity = Velocity;
			Velocity = new Vector3(
				velocity.X,
				IsOnFloor() ? 0 : velocity.Y + GetGravity().Y * (float)delta,
				velocity.Z);
			if (!HandlesMovement)
				MoveAndSlide();
		}
		base._PhysicsProcess(delta);
	}

	public override void _Ready()
	{
		Health = GetNodeOrNull<HealthComponent>("HealthComponent");

		if (Health != null)
		{
			Health.Died += OnDied;
		}
	}

	protected virtual void OnDied()

	{
		QueueFree(); // Удаляем объект из сцены при смерти
		// Базовая логика смерти (например, проигрывание эффекта или QueueFree)
	}
}
