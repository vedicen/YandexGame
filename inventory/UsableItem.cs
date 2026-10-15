using Godot;

[GlobalClass]
public partial class UsableItem : Item
{
    [Export] public float Healing { get; set; } = 25;
    // Runs on the server. Override for other consumables; false preserves the item.
    public virtual bool Use(Player player)
    {
        if (player.Health == null || player.Health.CurrentHealth <= 0 ||
            player.Health.CurrentHealth >= player.Health.MaxHealth) return false;
        player.Health.Heal(Healing);
        return true;
    }
}
