using Godot;
using System.Collections.Generic;

public partial class Chest : interact_obj
{
    [Export] public bool DeadLoot { get; set; }
    [Export] public float DeadLootLifetime { get; set; } = 60.0f;

    public InventoryComponent Inventory { get; private set; }
    private readonly Dictionary<Item, int> _itemCounts = new();
    private double _deadLootTimeLeft;

    public override void _Ready()
    {
        base._Ready();
        Inventory = GetNode<InventoryComponent>("InventoryComponent");
        Inventory.Changed += OnInventoryChanged;
        UpdateItemCounts();
        _deadLootTimeLeft = DeadLootLifetime;

        if (DeadLoot && IsInventoryEmpty())
            QueueFree();
    }

    public override void _Process(double delta)
    {
        if (!DeadLoot || !IsServerAuthority())
            return;

        if (IsInventoryEmpty())
        {
            QueueFree();
            return;
        }

        _deadLootTimeLeft -= delta;
        if (_deadLootTimeLeft <= 0.0)
            QueueFree();
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Inventory))
            Inventory.Changed -= OnInventoryChanged;
    }

    public override void interact(Player player)
    {
        if (InventoryService.Instance == null)
        {
            GD.PushError("Cannot open chest: InventoryService is unavailable.");
            return;
        }

        InventoryService.Instance.Send("open", player, GetPath());
    }

    private void OnInventoryChanged()
    {
        bool hasNewItems = false;
        var currentCounts = new Dictionary<Item, int>();

        if (Inventory?.Slots != null)
        {
            foreach (Stack stack in Inventory.Slots)
            {
                if (stack?.Item == null || stack.ItemCount <= 0)
                    continue;

                currentCounts.TryGetValue(stack.Item, out int count);
                currentCounts[stack.Item] = count + stack.ItemCount;

                if (!_itemCounts.TryGetValue(stack.Item, out int previousCount) ||
                    count + stack.ItemCount > previousCount)
                    hasNewItems = true;
            }
        }

        _itemCounts.Clear();
        foreach (var itemCount in currentCounts)
            _itemCounts.Add(itemCount.Key, itemCount.Value);

        if (!DeadLoot && hasNewItems && Task?.Type == TaskData.TaskTypes.Loot)
            Task.State = TaskData.TaskStates.Required;
    }

    private void UpdateItemCounts()
    {
        _itemCounts.Clear();
        if (Inventory?.Slots == null)
            return;

        foreach (Stack stack in Inventory.Slots)
        {
            if (stack?.Item == null || stack.ItemCount <= 0)
                continue;

            _itemCounts.TryGetValue(stack.Item, out int count);
            _itemCounts[stack.Item] = count + stack.ItemCount;
        }
    }

    private bool IsInventoryEmpty()
    {
        if (Inventory?.Slots == null)
            return true;

        foreach (Stack stack in Inventory.Slots)
        {
            if (stack?.Item != null && stack.ItemCount > 0)
                return false;
        }

        return true;
    }

    private bool IsServerAuthority() =>
        !Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer();
}
