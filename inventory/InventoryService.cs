using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

// One server-owned service at a stable scene path. Clients send intent, never item data.
public partial class InventoryService : Node
{
    public static InventoryService Instance { get; private set; }
    public int Revision { get; private set; }
    private int _nextDrop;
    private Snapshot _pending;
    private readonly Dictionary<int, string> _sessions = new();
    public bool IsServer => !Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer();
    public class SlotData { public string Item { get; set; } public int Count { get; set; } }
    public class InventoryData { public string Path { get; set; } public List<SlotData> Slots { get; set; } = new(); }
    public class DropData { public string Path { get; set; } public SlotData Slot { get; set; } public float X { get; set; } public float Y { get; set; } public float Z { get; set; } }
    public class Snapshot
    {
        public int Revision { get; set; }
        public List<InventoryData> Inventories { get; set; } = new();
        public List<DropData> Drops { get; set; } = new();
        public Dictionary<int, string> Sessions { get; set; } = new();
        public Dictionary<int, float> Health { get; set; } = new();
    }
    public override void _EnterTree() { Instance = this; SetMultiplayerAuthority(1); }
    public override void _ExitTree()
    {
        Multiplayer.PeerDisconnected -= Disconnected;
        if (Instance == this) Instance = null;
    }
    public override void _Ready() { Multiplayer.PeerDisconnected += Disconnected; }
    private void Disconnected(long id)
    {
        if (!IsServer) return;
        _sessions.Remove((int)id);
        Callable.From(() => Publish()).CallDeferred();
    }
    public void Send(string action, Player player, string source = "", int from = -1, string target = "", int to = -1, int revision = -1)
    {
        revision = revision < 0 ? Revision : revision;
        if (IsServer) Execute(action, player, source, from, target, to, revision);
        else RpcId(1, nameof(Request), action, source, from, target, to, revision);
    }
    public void RequestSnapshot()
    {
        if (IsServer) Publish(); else RpcId(1, nameof(Request), "sync", "", -1, "", -1, Revision);
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Request(string action, string source, int from, string target, int to, int revision)
    {
        if (!IsServer) return;
        int sender = Multiplayer.GetRemoteSenderId();
        var player = GetParent().GetNodeOrNull<Player>($"Players/{sender}");
        if (player != null) Execute(action, player, source, from, target, to, revision);
    }
    private bool Near(Player player, Node3D obj) => GodotObject.IsInstanceValid(obj) && player.GlobalPosition.DistanceTo(obj.GlobalPosition) <= 3.5f;
    private InventoryComponent Resolve(Player player, string path)
    {
        if (path == player.Inventory.GetPath().ToString()) return player.Inventory;
        if (_sessions.TryGetValue(player.GetMultiplayerAuthority(), out var chestPath))
        {
            var chest = GetNodeOrNull<Chest>(chestPath);
            if (chest != null && Near(player, chest) && path == chest.Inventory.GetPath().ToString()) return chest.Inventory;
        }
        return null;
    }
    private void Execute(string action, Player player, string source, int from, string target, int to, int revision)
    {
        int peer = player.GetMultiplayerAuthority();
        if (action == "sync") { Publish(false); return; }
        if (action == "close") { _sessions.Remove(peer); Publish(); return; }
        if (action == "open")
        {
            var chest = GetNodeOrNull<Chest>(source);
            if (chest != null && Near(player, chest)) _sessions[peer] = source;
            Publish(); return;
        }
        // Reject stale drags and simultaneous requests rather than moving a different stack.
        if (revision != Revision) { Publish(false); return; }
        if (action == "pickup")
        {
            var pickup = GetNodeOrNull<PickUpObj>(source);
            if (pickup?.Slot?.Item == null || pickup.IsQueuedForDeletion() || !Near(player, pickup)) return;
            pickup.Slot.ItemCount = player.Inventory.Add(pickup.Slot.Item, pickup.Slot.ItemCount);
            if (pickup.Slot.ItemCount == 0) { pickup.GetParent().RemoveChild(pickup); pickup.QueueFree(); }
            Publish(); return;
        }
        var inventory = Resolve(player, source);
        if (inventory == null || !inventory.Valid(from) || inventory.Slots[from] == null) return;
        var slot = inventory.Slots[from];
        switch (action)
        {
            case "move":
                var destination = Resolve(player, target);
                if (destination == null || !inventory.Transfer(from, destination, to)) return;
                break;
            case "use":
                if (inventory != player.Inventory || slot.Item is not UsableItem usable || !usable.Use(player)) return;
                if (--slot.ItemCount == 0) inventory.Slots[from] = null;
                inventory.NotifyChanged();
                break;
            case "drop":
                if (inventory != player.Inventory) return;
                var pickup = GD.Load<PackedScene>("res://entity/interact/PickUp_obj.tscn").Instantiate<PickUpObj>();
                pickup.Name = $"Drop_{++_nextDrop}";
                pickup.Slot = slot.Copy();
                GetParent().AddChild(pickup);
                pickup.GlobalPosition = player.GlobalPosition - player.GlobalBasis.Z * 1.2f;
                inventory.Slots[from] = null;
                inventory.NotifyChanged();
                break;
            default: return;
        }
        Publish();
    }
    private SlotData Encode(Stack slot) => slot == null ? null : new() { Item = slot.Item.ResourcePath, Count = slot.ItemCount };
    private Stack Decode(SlotData slot) => slot == null ? null : new() { Item = GD.Load<Item>(slot.Item), ItemCount = slot.Count };
    public void Publish(bool increment = true)
    {
        if (!IsServer) return;
        if (increment) Revision++;
        var snapshot = new Snapshot { Revision = Revision, Sessions = new(_sessions) };
        foreach (var node in GetTree().GetNodesInGroup("inventories"))
        {
            var inventory = (InventoryComponent)node;
            var data = new InventoryData { Path = inventory.GetPath().ToString() };
            foreach (var slot in inventory.Slots) data.Slots.Add(Encode(slot));
            snapshot.Inventories.Add(data);
        }
        foreach (var node in GetTree().GetNodesInGroup("pickups"))
        {
            var pickup = (PickUpObj)node;
            if (pickup.Slot?.Item == null) continue;
            snapshot.Drops.Add(new() { Path = pickup.GetPath().ToString(), Slot = Encode(pickup.Slot), X = pickup.GlobalPosition.X, Y = pickup.GlobalPosition.Y, Z = pickup.GlobalPosition.Z });
        }
        foreach (Player p in GetTree().GetNodesInGroup("players"))
            if (p.Health != null) snapshot.Health[p.GetMultiplayerAuthority()] = p.Health.CurrentHealth;
        ApplySessions(snapshot);
        if (Multiplayer.HasMultiplayerPeer()) Rpc(nameof(Receive), JsonSerializer.Serialize(snapshot));
    }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Receive(string json)
    {
        _pending = JsonSerializer.Deserialize<Snapshot>(json);
        Revision = _pending.Revision;
        ApplyPending();
    }
    public override void _Process(double delta)
    {
        if (!IsServer) { if (_pending != null) ApplyPending(); return; }
        bool changed = false;
        foreach (var entry in new Dictionary<int, string>(_sessions))
        {
            var player = GetParent().GetNodeOrNull<Player>($"Players/{entry.Key}");
            var chest = GetNodeOrNull<Chest>(entry.Value);
            if (player == null || chest == null || !Near(player, chest)) { _sessions.Remove(entry.Key); changed = true; }
        }
        if (changed) Publish();
    }
    private void ApplyPending()
    {
        var snapshot = _pending;
        bool missing = false;
        foreach (var data in snapshot.Inventories)
        {
            var inventory = GetNodeOrNull<InventoryComponent>(data.Path);
            if (inventory == null) { missing = true; continue; }
            inventory.MaxSlots = data.Slots.Count;
            inventory.Slots.Clear();
            foreach (var slot in data.Slots) inventory.Slots.Add(Decode(slot));
            inventory.NotifyChanged();
        }
        var paths = new HashSet<string>();
        foreach (var data in snapshot.Drops)
        {
            paths.Add(data.Path);
            var pickup = GetNodeOrNull<PickUpObj>(data.Path);
            if (pickup == null)
            {
                pickup = GD.Load<PackedScene>("res://entity/interact/PickUp_obj.tscn").Instantiate<PickUpObj>();
                pickup.Name = data.Path.Substring(data.Path.LastIndexOf('/') + 1);
                pickup.Slot = Decode(data.Slot);
                GetParent().AddChild(pickup);
            }
            else pickup.Slot = Decode(data.Slot);
            pickup.GlobalPosition = new Vector3(data.X, data.Y, data.Z);
        }
        foreach (PickUpObj pickup in GetTree().GetNodesInGroup("pickups"))
            if (!paths.Contains(pickup.GetPath().ToString())) { pickup.GetParent().RemoveChild(pickup); pickup.QueueFree(); }
        ApplySessions(snapshot);
        if (!missing) _pending = null;
    }
    private void ApplySessions(Snapshot snapshot)
    {
        foreach (Player player in GetTree().GetNodesInGroup("players"))
        {
            if (!IsServer && snapshot.Health.TryGetValue(player.GetMultiplayerAuthority(), out float health)) player.Health?.SetReplicatedHealth(health);
            string path = snapshot.Sessions.GetValueOrDefault(player.GetMultiplayerAuthority());
            player.SetChest(path == null ? null : GetNodeOrNull<Chest>(path));
        }
    }
}
