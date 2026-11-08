using Godot;

public partial class EquipmentSlot : VBoxContainer
{
    private Player _player;
    private ItemUI _item;

    public override void _Ready()
    {
        _item = GetNode<ItemUI>("WeaponItem");
        GetNode<Label>("Title").Text = "Оружие";
    }

    public void Bind(Player player)
    {
        if (_player != null && GodotObject.IsInstanceValid(_player))
            _player.Equipment.Changed -= UpdateSlot;

        _player = player;
        _player.Equipment.Changed += UpdateSlot;
        UpdateSlot();
    }

    public override void _ExitTree()
    {
        if (_player != null && GodotObject.IsInstanceValid(_player))
            _player.Equipment.Changed -= UpdateSlot;
    }

    private void UpdateSlot()
    {
        _item.Bind(_player, _player.Equipment, 0);
    }
}
