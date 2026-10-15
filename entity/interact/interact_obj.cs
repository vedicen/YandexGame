using Godot;
using System;

public partial class interact_obj : Entity { 
	[Export] public Area3D zone_using;
  


    public override void _Ready()
	{
		if (zone_using != null)
		{
			zone_using.BodyEntered += OnBodyEntered;
			zone_using.BodyExited += OnBodyExited;

		}
	}

	
	private void OnBodyEntered(Node3D body)
	{
		
		if (body is Player player)
		{
			GD.Print("Зашел в зону");
			player.interact_show(this);

		}
	}
	private void OnBodyExited(Node3D body)
	{
        if(body is Player player)
        {
			GD.Print("Игрок вышел из зоны");
			player.interact_hide();
		}
	}
	public virtual void interact(Player player)
	{
		QueueFree();
	}


}
