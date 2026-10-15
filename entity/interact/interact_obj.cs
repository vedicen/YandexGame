using Godot;

public partial class interact_obj : Entity
{
    [Export] public Area3D zone_using;
    public override void _Ready()
    {
        base._Ready();
        if (zone_using == null) return;
        zone_using.BodyEntered += body => { if (body is Player player) player.interact_show(this); };
        zone_using.BodyExited += body => { if (body is Player player) player.interact_hide(this); };
    }
    public virtual void interact(Player player) { }
}
