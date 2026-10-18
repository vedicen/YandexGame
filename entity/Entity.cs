using Godot;

public partial class Entity : CharacterBody3D
{
	[Export] public HealthComponent Health { get; private set; }
	[Export] public bool ApplyGravity = true;

	public override void _PhysicsProcess(double delta)
	{
		if (ApplyGravity)
		{
			Vector3 velocity = Velocity;
			Velocity = new Vector3(0, IsOnFloor() ? 0 : velocity.Y + GetGravity().Y * (float)delta, 0);
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
