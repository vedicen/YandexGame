using Godot;

public partial class PlayerHUD : Control
{
    [Export] public PackedScene HotbarSlotScene { get; set; }

    public Player Player { get; set; }

    private ProgressBar _healthBar;
    private Label _healthText;

    public override void _Ready()
    {
        _healthBar = GetNode<ProgressBar>("TopRight/HealthBar");
        _healthText = GetNode<Label>("TopRight/HealthText");
        Player.Health.HealthChanged += UpdateHealth;
        Player.States.Changed += OnPlayerStateChanged;
        OnPlayerStateChanged(Player.States.Current);
        UpdateHealth(Player.Health.CurrentHealth, Player.Health.MaxHealth);

        HBoxContainer bar = GetNode<HBoxContainer>("BottomCenter/Hotbar");
        for (int i = 0; i < 4; i++)
        {
            HotbarSlot slot = HotbarSlotScene.Instantiate<HotbarSlot>();
            slot.HideWhenEmpty = true;
            bar.AddChild(slot);
            slot.Bind(Player, i);
        }
    }

    public override void _ExitTree()
    {
        if (Player != null && GodotObject.IsInstanceValid(Player?.Health))
        {
            Player.Health.HealthChanged -= UpdateHealth;
            if (GodotObject.IsInstanceValid(Player.States))
                Player.States.Changed -= OnPlayerStateChanged;
        }
    }

    private void OnPlayerStateChanged(PlayerStateMachine.State state) =>
        Visible = state == PlayerStateMachine.State.Locomotion;

    private void UpdateHealth(float current, float maximum)
    {
        _healthBar.MaxValue = maximum;
        _healthBar.Value = current;
        _healthText.Text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(maximum)}";
    }
}
