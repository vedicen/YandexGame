using Godot;

public partial class interact_obj : Entity
{
    [Export] public Area3D zone_using;
    [Export] public bool disable_input_on_interact { get; set; } = false;

    public override void _Ready()
    {
        base._Ready();
        if (zone_using == null) return;
        zone_using.BodyEntered += body => { if (body is Player player) player.interact_show(this); };
        zone_using.BodyExited += body =>
        {
            if (body is not Player player) return;
            player.EndInteraction(this);
            player.interact_hide(this);
        };
    }

    public virtual void interact(Player player) { }

    public void interaction_end(Player player) => player?.EndInteraction(this);
}
