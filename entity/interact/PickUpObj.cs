using Godot;
using System;

public partial class PickUpObj : interact_obj
{
    public override void interact(Player player)
    {
        base.interact(player);
        GD.Print("Дал придмет");
        
    }

}
