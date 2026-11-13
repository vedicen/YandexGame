using Godot;

public partial class WorldHealthBar : Node3D
{
    private HealthComponent _health;
    private ProgressBar _bar;

    public override void _Ready()
    {
        SubViewport viewport = GetNode<SubViewport>("Viewport");
        GetNode<Sprite3D>("Sprite").Texture = viewport.GetTexture();
        _bar = GetNode<ProgressBar>("Viewport/HealthBar");
    }

    public void Bind(HealthComponent health, float height)
    {
        _health = health;
        Position = new Vector3(0, height, 0);
        if (_bar == null)
            Callable.From(UpdateFromHealth).CallDeferred();
        _health.HealthChanged += UpdateHealth;
        UpdateHealth(_health.CurrentHealth, _health.MaxHealth);
    }

    public override void _ExitTree()
    {
        if (_health != null && GodotObject.IsInstanceValid(_health))
            _health.HealthChanged -= UpdateHealth;
    }

    private void UpdateFromHealth()
    {
        if (_health != null && GodotObject.IsInstanceValid(_health))
            UpdateHealth(_health.CurrentHealth, _health.MaxHealth);
    }

    private void UpdateHealth(float current, float maximum)
    {
        if (_bar == null)
            return;
        _bar.MaxValue = maximum;
        _bar.Value = current;
    }
}
