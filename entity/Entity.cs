using Godot;

public partial class Entity : CharacterBody3D
{
    [Export] public HealthComponent Health { get; private set; }

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