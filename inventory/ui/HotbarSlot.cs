using Godot;

public partial class HotbarSlot : PanelContainer
{
    private Player _player;
    private int _index;
    private Label _keyLabel;
    private ItemUI _preview;
    [Export] public bool HideWhenEmpty { get; set; }

    public override void _Ready()
    {
        _keyLabel = GetNode<Label>("Content/Key");
        _preview = GetNode<ItemUI>("Content/Preview");
        Refresh();
    }

    public void Bind(Player player, int index)
    {
        if (_player != null)
            _player.HotbarChanged -= OnHotbarChanged;
        _player = player;
        _index = index;
        if (_player != null)
        {
            _player.HotbarChanged += OnHotbarChanged;
            _player.Inventory.Changed += Refresh;
        }
        Refresh();
    }

    public override void _ExitTree()
    {
        if (_player != null && GodotObject.IsInstanceValid(_player))
        {
            _player.HotbarChanged -= OnHotbarChanged;
            _player.Inventory.Changed -= Refresh;
        }
    }

    private void OnHotbarChanged(int changedIndex)
    {
        if (changedIndex == _index)
            Refresh();
    }

    private void Refresh()
    {
        if (_keyLabel == null || _preview == null)
            return;

        Item item = _player?.GetHotbarItem(_index);
        _keyLabel.Text = (_index + 1).ToString();
        int count = _player?.CountInInventory(item) ?? 0;
        TooltipText = item?.DisplayName ?? "Перетащите сюда предмет";
        bool hasVisibleItem = item != null && count > 0;
        Visible = !HideWhenEmpty || hasVisibleItem;
        Modulate = hasVisibleItem ? Colors.White : new Color(0.65f, 0.68f, 0.72f);
        int inventoryIndex = item == null ? -1 : _player.FindHotbarItemSlot(item);
        _preview.Visible = inventoryIndex >= 0;
        if (inventoryIndex >= 0)
            _preview.BindHotbar(_player, _player.Inventory, inventoryIndex);
    }

    public override Variant _GetDragData(Vector2 position)
    {
        Item item = _player?.GetHotbarItem(_index);
        if (item == null)
            return default;

        SetDragPreview(new Label { Text = item.DisplayName });
        return new Godot.Collections.Dictionary
        {
            ["hotbar_source"] = _index,
            ["hotbar_item"] = item.ResourcePath
        };
    }

    public override bool _CanDropData(Vector2 position, Variant data)
    {
        if (_player == null || data.VariantType != Variant.Type.Dictionary)
            return false;

        var drag = data.AsGodotDictionary();
        if (drag.ContainsKey("hotbar_source"))
            return true;

        if (!ItemUI.IsStackDrag(data) || !drag.ContainsKey("source") || !drag.ContainsKey("index") ||
            drag["source"].AsString() != _player.Inventory.GetPath().ToString())
            return false;

        int sourceIndex = drag["index"].AsInt32();
        return _player.Inventory.Valid(sourceIndex) &&
            _player.IsHotbarItemType(_player.Inventory.Slots[sourceIndex]?.Item);
    }

    public override void _DropData(Vector2 position, Variant data)
    {
        if (_player == null || data.VariantType != Variant.Type.Dictionary)
            return;

        var drag = data.AsGodotDictionary();
        if (drag.ContainsKey("hotbar_source"))
        {
            int sourceIndex = drag["hotbar_source"].AsInt32();
            if (sourceIndex >= 0 && sourceIndex < 4)
                _player.SetHotbarItem(_index, _player.GetHotbarItem(sourceIndex));
            return;
        }

        if (!ItemUI.IsStackDrag(data) || drag["source"].AsString() != _player.Inventory.GetPath().ToString())
            return;
        int inventoryIndex = drag["index"].AsInt32();
        if (_player.Inventory.Valid(inventoryIndex))
            _player.SetHotbarItem(_index, _player.Inventory.Slots[inventoryIndex]?.Item);
    }
}
