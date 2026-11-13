using Godot;
using System.Collections.Generic;

public partial class AnimatedEnemy : AnimatedEntity
{
    private static uint _nextLootChestId;

    public EnemyInventoryManager InventoryManager =>
        GetNodeOrNull<EnemyInventoryManager>("EnemyInventoryManager");

    public override void OnDied()
    {
        if (!Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer())
            SpawnLootChest();

        base.OnDied();
    }

    private void SpawnLootChest()
    {
        var loot = new List<Stack>();
        AddInventoryLoot(Inventory, loot);
        GD.Print($"[Loot] Enemy died: {Name}, inventory slots={Inventory?.Slots?.Count ?? 0}, loot stacks={loot.Count}.");
        if (loot.Count == 0)
        {
            GD.PushWarning($"[Loot] No loot to spawn for '{Name}'.");
            return;
        }

        Node current = this;
        Node3D container = null;
        while (current != null && container == null)
        {
            container = current.GetNodeOrNull<Node3D>("LootChests");
            current = current.GetParent();
        }

        if (container == null)
        {
            Node3D currentScene = GetTree().CurrentScene as Node3D;
            GD.PushWarning($"[Loot] LootChests container not found among '{GetPath()}' ancestors. " +
                "Falling back to current scene root.");
            container = currentScene;
            if (container == null)
            {
                GD.PushError($"Cannot spawn loot for '{Name}': LootChests container and Node3D current scene root are unavailable.");
                return;
            }
        }

        PackedScene chestScene = GD.Load<PackedScene>("res://entity/interact/DeadChestLoot.tscn");
        if (chestScene == null)
        {
            GD.PushError("Cannot spawn enemy loot: DeadChestLoot scene could not be loaded.");
            return;
        }

        Chest chest = chestScene.Instantiate<Chest>();
        InventoryComponent chestInventory =
            chest.GetNodeOrNull<InventoryComponent>("InventoryComponent");
        if (chestInventory == null)
        {
            chest.Free();
            GD.PushError("Cannot spawn enemy loot: chest has no InventoryComponent.");
            return;
        }

        chest.Name = $"DeadChestLoot_{++_nextLootChestId}";
        chest.Task = new TaskData
        {
            Type = TaskData.TaskTypes.Loot,
            State = TaskData.TaskStates.Required,
            RequiredRace = Race
        };
        chestInventory.MaxSlots = loot.Count;
        chestInventory.Slots = new Godot.Collections.Array<Stack>(loot);

        container.AddChild(chest, true);
        chest.GlobalPosition = GlobalPosition;
        if (InventoryService.Instance != null)
            InventoryService.Instance.Publish();
        else
            GD.PushWarning("[Loot] InventoryService is unavailable; spawned chest inventory may not replicate.");

        GD.Print($"[Loot] Spawned chest '{chest.GetPath()}' with {loot.Count} slots at {chest.GlobalPosition}.");
    }

    private static void AddInventoryLoot(InventoryComponent source, List<Stack> loot)
    {
        if (source?.Slots == null)
            return;

        for (int i = 0; i < source.Slots.Count; i++)
        {
            Stack stack = source.Slots[i];
            if (stack?.Item != null && stack.ItemCount > 0)
                loot.Add(stack.Copy());
        }
    }
}
