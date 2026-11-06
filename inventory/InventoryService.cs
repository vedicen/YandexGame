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
    private readonly Dictionary<int, int> _money = new();
    private readonly Dictionary<int, TradeOffer> _tradeOffers = new();
    private readonly Dictionary<int, string> _tradeSessions = new();
    public bool IsServer => !Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer();
    private sealed class TradeOffer
    {
        public string StandPath { get; set; }
        public int ItemId { get; set; }
        public int Price { get; set; }
    }

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
        public Dictionary<int, int> Money { get; set; } = new();
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
        _money.Remove((int)id);
        _tradeOffers.Remove((int)id);
        EndTradeForPeer((int)id);
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

    public void RequestTradeOffer(Player player, TradingStand stand, int id, int price)
    {
        if (IsServer) ProcessTradeOffer(player, stand, id, price);
        else RpcId(1, nameof(RequestTradeOfferRpc), stand.GetPath().ToString(), id, price);
    }

    public void BeginTradeSession(Player player, TradingStand stand)
    {
        if (IsServer) ProcessBeginTrade(player, stand);
        else RpcId(1, nameof(BeginTradeSessionRpc), stand.GetPath().ToString());
    }

    public void EndTradeSession(Player player, TradingStand stand)
    {
        if (IsServer) ProcessEndTrade(player, stand);
        else RpcId(1, nameof(EndTradeSessionRpc), stand.GetPath().ToString());
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void BeginTradeSessionRpc(string standPath)
    {
        if (!IsServer) return;
        int sender = Multiplayer.GetRemoteSenderId();
        var player = GetParent().GetNodeOrNull<Player>($"Players/{sender}");
        if (player != null) ProcessBeginTrade(player, GetNodeOrNull<TradingStand>(standPath));
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void EndTradeSessionRpc(string standPath)
    {
        if (!IsServer) return;
        int sender = Multiplayer.GetRemoteSenderId();
        var player = GetParent().GetNodeOrNull<Player>($"Players/{sender}");
        if (player != null) ProcessEndTrade(player, GetNodeOrNull<TradingStand>(standPath));
    }

    private void ProcessBeginTrade(Player player, TradingStand stand)
    {
        if (!GodotObject.IsInstanceValid(player) || !GodotObject.IsInstanceValid(stand) ||
            !GodotObject.IsInstanceValid(stand.npc_owner) || !Near(player, stand))
            return;

        int peer = player.GetMultiplayerAuthority();
        string standPath = stand.GetPath().ToString();
        if (_tradeSessions.TryGetValue(peer, out string currentStand) && currentStand == standPath)
            return;
        if (stand.npc_owner.IsTrading)
        {
            if (stand.npc_owner.TradingPlayer == player && stand.npc_owner.ActiveTradingStand == stand)
            {
                _tradeSessions[peer] = standPath;
                return;
            }
            if (stand.npc_owner.TradingPlayer != player)
                DeliverTradeResult(peer, player, "Торговец уже занят.", true);
            return;
        }
        if (stand.npc_owner.BeginTrading(stand, player))
            _tradeSessions[peer] = standPath;
    }

    private void ProcessEndTrade(Player player, TradingStand stand)
    {
        if (!GodotObject.IsInstanceValid(player))
            return;
        int peer = player.GetMultiplayerAuthority();
        if (_tradeSessions.TryGetValue(peer, out string standPath) &&
            (stand == null || standPath == stand.GetPath().ToString()))
        {
            _tradeSessions.Remove(peer);
            _tradeOffers.Remove(peer);
            if (GodotObject.IsInstanceValid(stand) && GodotObject.IsInstanceValid(stand.npc_owner))
                stand.npc_owner.EndTrading(stand, player);
        }
    }

    private void EndTradeForPeer(int peer)
    {
        if (!_tradeSessions.TryGetValue(peer, out string standPath))
            return;
        _tradeSessions.Remove(peer);
        var stand = GetNodeOrNull<TradingStand>(standPath);
        var tradingPlayer = GodotObject.IsInstanceValid(stand?.npc_owner)
            ? stand.npc_owner.TradingPlayer
            : null;
        if (tradingPlayer?.GetMultiplayerAuthority() == peer)
            stand.npc_owner.EndTrading(stand, tradingPlayer);
    }

    public void RequestTradePurchase(Player player, TradingStand stand, int id, int price, bool accepted)
    {
        if (IsServer) ProcessTradePurchase(player, stand, id, price, accepted);
        else RpcId(1, nameof(RequestTradePurchaseRpc), stand.GetPath().ToString(), id, price, accepted);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestTradeOfferRpc(string standPath, int id, int price)
    {
        if (!IsServer) return;
        int sender = Multiplayer.GetRemoteSenderId();
        var player = GetParent().GetNodeOrNull<Player>($"Players/{sender}");
        var stand = GetNodeOrNull<TradingStand>(standPath);
        if (player != null) ProcessTradeOffer(player, stand, id, price);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestTradePurchaseRpc(string standPath, int id, int price, bool accepted)
    {
        if (!IsServer) return;
        int sender = Multiplayer.GetRemoteSenderId();
        var player = GetParent().GetNodeOrNull<Player>($"Players/{sender}");
        var stand = GetNodeOrNull<TradingStand>(standPath);
        if (player != null) ProcessTradePurchase(player, stand, id, price, accepted);
    }

    private bool CanTrade(Player player, TradingStand stand)
    {
        return GodotObject.IsInstanceValid(player) &&
            GodotObject.IsInstanceValid(stand) &&
            GodotObject.IsInstanceValid(stand.npc_owner) &&
            Near(player, stand) &&
            _tradeSessions.TryGetValue(player.GetMultiplayerAuthority(), out string sessionPath) &&
            sessionPath == stand.GetPath().ToString() &&
            stand.npc_owner.IsTrading &&
            stand.npc_owner.ActiveTradingStand == stand &&
            stand.npc_owner.TradingPlayer == player;
    }

    private void ProcessTradeOffer(Player player, TradingStand stand, int id, int price)
    {
        int peer = player.GetMultiplayerAuthority();
        if (!CanTrade(player, stand) || !stand.npc_owner.SellsItem(id) ||
            !TradeItemDatabase.TryGet(id, out TradeItemRecord item) ||
            (long)price * 10 < (long)item.Price * 3 || price <= 0)
        {
            _tradeOffers.Remove(peer);
            DeliverTradeResult(peer, player, "Предложение не прошло проверку.", false);
            return;
        }

        _tradeOffers[peer] = new TradeOffer
        {
            StandPath = stand.GetPath().ToString(),
            ItemId = id,
            Price = price
        };
        DeliverTradeOffer(peer, player, id, price, item.Item.DisplayName);
    }

    private void ProcessTradePurchase(Player player, TradingStand stand, int id, int price, bool accepted)
    {
        int peer = player.GetMultiplayerAuthority();
        if (!_tradeOffers.TryGetValue(peer, out TradeOffer offer) ||
            offer.StandPath != stand?.GetPath().ToString() ||
            offer.ItemId != id || offer.Price != price)
        {
            DeliverTradeResult(peer, player, "Предложение больше недействительно.", false);
            return;
        }

        _tradeOffers.Remove(peer);
        if (!accepted)
        {
            DeliverTradeResult(peer, player, "Покупка отклонена.", true);
            return;
        }

        if (!CanTrade(player, stand) || !stand.npc_owner.SellsItem(id) ||
            !TradeItemDatabase.TryGet(id, out TradeItemRecord item) ||
            (long)price * 10 < (long)item.Price * 3 || price <= 0)
        {
            DeliverTradeResult(peer, player, "Не удалось подтвердить покупку.", false);
            return;
        }

        int balance = GetMoney(player);
        if (balance < price)
        {
            DeliverTradeResult(peer, player, "Недостаточно денег.", false);
            return;
        }
        if (!player.Inventory.CanAdd(item.Item, 1))
        {
            DeliverTradeResult(peer, player, "В инвентаре нет места.", false);
            return;
        }
        if (player.Inventory.Add(item.Item, 1) != 0)
        {
            DeliverTradeResult(peer, player, "Не удалось добавить предмет в инвентарь.", false);
            return;
        }

        _money[peer] = balance - price;
        Publish();
        DeliverTradeResult(peer, player, $"Куплено: {item.Item.DisplayName}.", true);
    }

    private int GetMoney(Player player)
    {
        int peer = player.GetMultiplayerAuthority();
        if (!_money.TryGetValue(peer, out int balance))
            _money[peer] = balance = Math.Max(0, player.StartingMoney);
        return balance;
    }

    private void DeliverTradeOffer(int peer, Player player, int id, int price, string itemName)
    {
        if (!Multiplayer.HasMultiplayerPeer() || peer == Multiplayer.GetUniqueId())
            player.ShowTradeOffer(id, price, itemName);
        else
            RpcId(peer, nameof(ReceiveTradeOffer), peer, id, price, itemName);
    }

    private void DeliverTradeResult(int peer, Player player, string message, bool closeOffer)
    {
        if (!Multiplayer.HasMultiplayerPeer() || peer == Multiplayer.GetUniqueId())
            player.ShowTradeResult(message, closeOffer);
        else
            RpcId(peer, nameof(ReceiveTradeResult), peer, message, closeOffer);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveTradeOffer(int peer, int id, int price, string itemName)
    {
        GetParent().GetNodeOrNull<Player>($"Players/{peer}")?.ShowTradeOffer(id, price, itemName);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveTradeResult(int peer, string message, bool closeOffer)
    {
        GetParent().GetNodeOrNull<Player>($"Players/{peer}")?.ShowTradeResult(message, closeOffer);
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
        if (path == player.EquipmentInventory.GetPath().ToString()) return player.EquipmentInventory;
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
                if (destination == null || !destination.Valid(to)) return;
                bool sourceIsWeaponSlot = inventory == player.EquipmentInventory;
                bool targetIsWeaponSlot = destination == player.EquipmentInventory;
                Item movingItem = slot.Item;
                Item targetItem = destination.Slots[to]?.Item;
                if (sourceIsWeaponSlot && movingItem is not Weapon ||
                    targetIsWeaponSlot && movingItem is not Weapon ||
                    sourceIsWeaponSlot && targetItem != null && targetItem is not Weapon)
                    return;
                if (!inventory.Transfer(from, destination, to)) return;
                break;
            case "use":
                if (inventory != player.Inventory || slot.Item is not UsableItem usable || !usable.Use(player)) return;
                if (--slot.ItemCount == 0) inventory.Slots[from] = null;
                inventory.NotifyChanged();
                break;
            case "drop":
                if (inventory != player.Inventory && inventory != player.EquipmentInventory) return;
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
        var snapshot = new Snapshot
        {
            Revision = Revision,
            Sessions = new(_sessions)
        };
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
        {
            if (p.Health != null) snapshot.Health[p.GetMultiplayerAuthority()] = p.Health.CurrentHealth;
            snapshot.Money[p.GetMultiplayerAuthority()] = GetMoney(p);
        }
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
        foreach (var entry in new Dictionary<int, string>(_tradeSessions))
        {
            var player = GetParent().GetNodeOrNull<Player>($"Players/{entry.Key}");
            var stand = GetNodeOrNull<TradingStand>(entry.Value);
            if (player != null && stand != null && Near(player, stand))
                continue;
            if (player != null)
                ProcessEndTrade(player, stand);
            else
                EndTradeForPeer(entry.Key);
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
            if (snapshot.Money.TryGetValue(player.GetMultiplayerAuthority(), out int money))
            {
                if (IsServer) _money[player.GetMultiplayerAuthority()] = money;
                player.SetMoney(money);
            }
            string path = snapshot.Sessions.GetValueOrDefault(player.GetMultiplayerAuthority());
            player.SetChest(path == null ? null : GetNodeOrNull<Chest>(path));
        }
    }
}
