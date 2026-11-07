using Godot;

public partial class InventoryUI : CanvasLayer
{
    [Export] public PackedScene InventoryGridScene { get; set; }

    public Player Player;

    private Control _root;
    private HBoxContainer _grids;
    private EquipmentSlot _equipmentSlot;
    private DropZone _dropZone;
    private Chest _chest;
    private bool _dirty;
    private Label _moneyLabel;

    public override void _Ready()
    {
        Layer = 10;
        _root = GetNode<Control>("Root");
        _grids = GetNode<HBoxContainer>("Root/Panel/Margin/VBox/Scroll/Content/Grids");
        _equipmentSlot = GetNode<EquipmentSlot>("Root/Panel/Margin/VBox/Scroll/Content/EquipmentSlot");
        _moneyLabel = GetNode<Label>("Root/Panel/Margin/VBox/MoneyLabel");
        _dropZone = GetNode<DropZone>("Root/Panel/Margin/VBox/DropZone");

        _dropZone.Player = Player;
        _equipmentSlot.Bind(Player);
        _moneyLabel.Text = $"Деньги: {Player.Money}";
        Player.MoneyChanged += UpdateMoney;
        Player.Inventory.Changed += MarkDirty;
        GetNode<Button>("Root/Panel/Margin/VBox/CloseButton").Pressed += Player.CloseInventory;
        _root.Hide();
    }

    private void MarkDirty() => _dirty = true;
    private void UpdateMoney(int amount) => _moneyLabel.Text = $"Деньги: {amount}";

    public void Open(Chest chest)
    {
        if (_chest != null)
            _chest.Inventory.Changed -= MarkDirty;

        _chest = chest;
        if (_chest != null)
            _chest.Inventory.Changed += MarkDirty;

        _root.Show();
        MarkDirty();
    }

    public void Close()
    {
        if (_chest != null && GodotObject.IsInstanceValid(_chest))
            _chest.Inventory.Changed -= MarkDirty;

        _chest = null;
        _root.Hide();
    }

    public override void _ExitTree()
    {
        if (Player != null && GodotObject.IsInstanceValid(Player))
        {
            Player.Inventory.Changed -= MarkDirty;
            Player.MoneyChanged -= UpdateMoney;
        }

        if (GodotObject.IsInstanceValid(_chest))
            _chest.Inventory.Changed -= MarkDirty;
    }

    public override void _Process(double delta)
    {
        if (!_dirty || !_root.Visible || GetViewport().GuiIsDragging())
            return;

        _dirty = false;
        foreach (Node child in _grids.GetChildren())
        {
            _grids.RemoveChild(child);
            child.QueueFree();
        }

        AddGrid("Рюкзак", Player.Inventory);
        if (_chest != null)
            AddGrid("Сундук", _chest.Inventory);
    }

    private void AddGrid(string title, InventoryComponent inventory)
    {
        InventoryGrid grid = InventoryGridScene.Instantiate<InventoryGrid>();
        grid.Title = title;
        grid.Inventory = inventory;
        grid.Player = Player;
        _grids.AddChild(grid);
    }
}
