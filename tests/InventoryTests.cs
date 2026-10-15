using Godot;
using System;
using System.Linq;

// Run with Godot --headless --path . res://tests/inventory_tests.tscn
// Add -- --network-host or --network-client for the two-process smoke test.
public partial class InventoryTests : Node
{
    private void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        GD.Print("PASS: " + message);
    }
    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://scenes/base_scene.tscn").Instantiate<NetworkManager>();
            GetTree().Root.CallDeferred(Node.MethodName.AddChild, world);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var args = OS.GetCmdlineUserArgs();
            if (args.Contains("--network-client"))
            {
                world.JoinGame();
                await ToSignal(GetTree().CreateTimer(3), SceneTreeTimer.SignalName.Timeout);
                var local = world.GetNode<Player>($"Players/{Multiplayer.GetUniqueId()}");
                Check(local.IsMultiplayerAuthority(), "client player authority");
                Check(world.GetNode("Players").GetChildCount() == 2, "exactly two replicated players");
                Check(world.GetNode<Player>("Players/1").Inventory.Slots[1]?.ItemCount == 7, "late join receives existing inventory");
                var chest = world.GetNode<Chest>("Chest");
                local.GlobalPosition = chest.GlobalPosition + Vector3.Right;
                await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
                InventoryService.Instance.Send("open", local, chest.GetPath());
                await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
                Check(local.States.Current == PlayerStateMachine.State.Chest, "server approved client chest session");
                InventoryService.Instance.Send("move", local, chest.Inventory.GetPath(), 0, local.Inventory.GetPath(), 2);
                await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
                Check(local.Inventory.Slots[2]?.ItemCount == 12 && chest.Inventory.Slots[0] == null, "replicated chest transfer");
                InventoryService.Instance.Send("drop", local, local.Inventory.GetPath(), 2);
                await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
                Check(local.Inventory.Slots[2] == null && GetTree().GetNodesInGroup("pickups").Count == 2, "replicated drop");
                local.CloseInventory();
                GD.Print("NETWORK CLIENT PASS");
                GetTree().Quit();
                return;
            }
            world.HostGame();
            if (args.Contains("--network-host"))
            {
                world.GetNode<Player>("Players/1").Inventory.Add(GD.Load<Item>("res://inventory/items/crystal.tres"), 7);
                InventoryService.Instance.Publish();
                await ToSignal(GetTree().CreateTimer(12), SceneTreeTimer.SignalName.Timeout);
                Check(world.GetNode<Chest>("Chest").Inventory.Slots[0] == null, "server chest updated by client");
                Check(GetTree().GetNodesInGroup("pickups").Count == 2, "server retains client drop");
                GD.Print("NETWORK HOST PASS");
                GetTree().Quit();
                return;
            }
            var player = world.GetNode<Player>("Players/1");
            var service = InventoryService.Instance;
            var item = GD.Load<Item>("res://inventory/items/crystal.tres");
            var a = new InventoryComponent { MaxSlots = 2 };
            var b = new InventoryComponent { MaxSlots = 1 };
            AddChild(a); AddChild(b);
            Check(a.Add(item, 45) == 5 && a.Slots[0].ItemCount == 20 && a.Slots[1].ItemCount == 20, "stack limits and overflow");
            Check(a.Transfer(0, b, 0) && a.Slots[0] == null && b.Slots[0].ItemCount == 20, "cross-inventory transfer");
            Check(!a.Transfer(1, b, 0), "full destination preserves source");
            Check(!a.Transfer(-1, b, 0), "invalid index rejected");
            var chestTest = world.GetNode<Chest>("Chest");
            player.GlobalPosition = chestTest.GlobalPosition + Vector3.Right;
            service.Send("move", player, chestTest.Inventory.GetPath(), 0, player.Inventory.GetPath(), 2);
            Check(chestTest.Inventory.Slots[0]?.ItemCount == 12, "closed chest access rejected");
            service.Send("open", player, chestTest.GetPath());
            Check(player.States.Current == PlayerStateMachine.State.Chest && !player.States.CanMove, "chest locks locomotion");
            int oldRevision = service.Revision;
            service.Send("move", player, chestTest.Inventory.GetPath(), 0, player.Inventory.GetPath(), 2);
            Check(player.Inventory.Slots[2]?.ItemCount == 12 && chestTest.Inventory.Slots[0] == null, "chest transfer");
            service.Send("drop", player, player.Inventory.GetPath(), 2, revision: oldRevision);
            Check(player.Inventory.Slots[2] != null, "stale request rejected");
            service.Send("use", player, player.Inventory.GetPath(), 0);
            Check(player.Inventory.Slots[0].ItemCount == 3, "full health does not consume potion");
            player.Health.TakeDamage(40);
            service.Send("use", player, player.Inventory.GetPath(), 0);
            Check(player.Health.CurrentHealth == 85 && player.Inventory.Slots[0].ItemCount == 2, "consumable effect and decrement");
            service.Send("drop", player, player.Inventory.GetPath(), 2);
            Check(player.Inventory.Slots[2] == null, "drop removes source");
            var drop = world.GetNode<PickUpObj>("Drop_1");
            Check(drop.Slot.ItemCount == 12, "drop initializes stack");
            service.Send("pickup", player, drop.GetPath());
            Check(player.Inventory.Slots[1]?.ItemCount == 12, "pickup restores stack");
            player.CloseInventory();
            Check(player.States.CanMove, "closing chest restores locomotion");
            player.InventoryUI.Open(null);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (args.Contains("--preview"))
            {
                service.Send("open", player, chestTest.GetPath());
                await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(OS.GetEnvironment("TEMP") + "/inventory-preview.png");
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print("INVENTORY TESTS PASS");
            GetTree().Quit();
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
}

