using Godot;

public partial class InventoryUI : CanvasLayer
{
    [Export] public PackedScene InventoryGridScene { get; set; }
    [Export] public PackedScene ItemScene { get; set; }
    [Export] public PackedScene HotbarSlotScene { get; set; }

    public Player Player;
    public bool IsOpen => _root?.Visible == true;

    private Control _root;
    private HBoxContainer _grids;
    private ItemUI _equipmentSlot;
    private DropZone _dropZone;
    private Chest _chest;
    private TradingStand _tradingStand;
    private bool _dirty;
    private bool _layoutDirty;
    private Label _moneyLabel;
    private Label _saleTotalLabel;
    private Button _sellButton;
    private GridContainer _tradeGoodsGrid;
    private Control _tradeSidePanel;
    private VBoxContainer _saleGridContainer;

    public override void _Ready()
    {
        Layer = 10;
        _root = GetNode<Control>("Root");
        _grids = GetNode<HBoxContainer>("Root/Panel/Margin/VBox/MainArea/Scroll/Content/Grids");
        _equipmentSlot = GetNode<ItemUI>("Root/Panel/Margin/VBox/MainArea/Scroll/Content/EquipmentColumn/EquipmentItem");
        _moneyLabel = GetNode<Label>("Root/Panel/Margin/VBox/MoneyLabel");
        _dropZone = GetNode<DropZone>("Root");
        _tradeSidePanel = GetNode<Control>("Root/Panel/Margin/VBox/MainArea/TradeSidePanel");
        _saleGridContainer = GetNode<VBoxContainer>("Root/Panel/Margin/VBox/MainArea/TradeSidePanel/SideVBox/SaleGridContainer");
        _saleTotalLabel = GetNode<Label>("Root/Panel/Margin/VBox/MainArea/TradeSidePanel/SideVBox/SaleFooter/SaleTotal");
        _sellButton = GetNode<Button>("Root/Panel/Margin/VBox/MainArea/TradeSidePanel/SideVBox/SaleFooter/SellButton");
        _tradeGoodsGrid = GetNode<GridContainer>("Root/Panel/Margin/VBox/MainArea/TradeSidePanel/SideVBox/TradeGoodsScroll/TradeGoodsGrid");

        HBoxContainer hotbar = GetNode<HBoxContainer>("Root/Panel/Margin/VBox/Hotbar");
        for (int i = 0; i < 4; i++)
        {
            HotbarSlot slot = HotbarSlotScene.Instantiate<HotbarSlot>();
            hotbar.AddChild(slot);
            slot.AllowShiftClickClear = true;
            slot.Bind(Player, i);
        }

        _dropZone.Player = Player;
        _equipmentSlot.Bind(Player, Player.Equipment, 0);
        Player.Equipment.Changed += UpdateEquipmentSlot;
        _tradeSidePanel.Hide();
        _moneyLabel.Text = $"Деньги: {Player.Money}";
        Player.MoneyChanged += UpdateMoney;
        Player.Inventory.Changed += MarkDirty;
        GetNode<Button>("Root/Panel/Margin/VBox/CloseButton").Pressed += Player.CloseInventory;
        _sellButton.Pressed += SellStagedItems;
        _root.Hide();
    }

    private void MarkDirty() => _dirty = true;
    private void UpdateEquipmentSlot() => _equipmentSlot.Bind(Player, Player.Equipment, 0);
    private void UpdateMoney(int amount)
    {
        _moneyLabel.Text = $"Деньги: {amount} золотых";
        MarkDirty();
    }

    public void OpenTrading(TradingStand stand)
    {
        UnsubscribeFromOpenInventories();
        _chest = null;
        _tradingStand = stand;
        _tradingStand.SaleStorage.Changed += MarkDirty;
        _layoutDirty = true;
        _equipmentSlot.Hide();
        _dropZone.Hide();
        _tradeSidePanel.Show();
        GetNode<Button>("Root/Panel/Margin/VBox/CloseButton").Text = "Закрыть торговлю";
        GetNode<Label>("Root/Panel/Margin/VBox/TitleLabel").Text = "Торговля • перетащите предметы в сундук для продажи";
        _root.Show();
        MarkDirty();
    }

    public void Open(Chest chest)
    {
        UnsubscribeFromOpenInventories();
        _chest = chest;
        _tradingStand = null;
        _layoutDirty = true;
        if (_chest != null) _chest.Inventory.Changed += MarkDirty;
        _root.Show();
        _equipmentSlot.Show();
        _dropZone.Show();
        _tradeSidePanel.Hide();
        GetNode<Button>("Root/Panel/Margin/VBox/CloseButton").Text = "Закрыть";
        GetNode<Label>("Root/Panel/Margin/VBox/TitleLabel").Text = "ИНВЕНТАРЬ • ЛКМ: перенос • ПКМ: использовать • Tab / Esc: закрыть";
        MarkDirty();
    }

    private void UnsubscribeFromOpenInventories()
    {
        if (GodotObject.IsInstanceValid(_chest)) _chest.Inventory.Changed -= MarkDirty;
        if (GodotObject.IsInstanceValid(_tradingStand?.SaleStorage)) _tradingStand.SaleStorage.Changed -= MarkDirty;
    }

    public void Close()
    {
        UnsubscribeFromOpenInventories();
        _chest = null;
        _tradingStand = null;
        _root.Hide();
    }

    public override void _ExitTree()
    {
        if (Player != null && GodotObject.IsInstanceValid(Player))
        {
            Player.Inventory.Changed -= MarkDirty;
            Player.Equipment.Changed -= UpdateEquipmentSlot;
            Player.MoneyChanged -= UpdateMoney;
        }
        UnsubscribeFromOpenInventories();
    }

    public override void _Process(double delta)
    {
        if (!_dirty || !_root.Visible || GetViewport().GuiIsDragging()) return;
        _dirty = false;
        if (!_layoutDirty)
        {
            if (_tradingStand != null && GodotObject.IsInstanceValid(_tradingStand))
            {
                UpdateSaleTotal();
                ClearChildren(_tradeGoodsGrid);
                PopulateTradeGoods();
            }
            return;
        }
        _layoutDirty = false;
        ClearChildren(_grids);
        ClearChildren(_saleGridContainer);
        ClearChildren(_tradeGoodsGrid);
        AddGrid("Рюкзак", Player.Inventory, _tradingStand != null);
        if (_chest != null) AddGrid("Сундук", _chest.Inventory);
        if (_tradingStand != null && GodotObject.IsInstanceValid(_tradingStand))
        {
            AddGrid("Сундук на продажу", _tradingStand.SaleStorage, true);
            PopulateTradeGoods();
            UpdateSaleTotal();
        }
    }

    private static void ClearChildren(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void AddGrid(string title, InventoryComponent inventory, bool tradeMode = false)
    {
        InventoryGrid grid = InventoryGridScene.Instantiate<InventoryGrid>();
        grid.Title = title;
        grid.Inventory = inventory;
        grid.FirstSlotIndex = inventory == Player.Inventory ? inventory.FirstItemSlot : 0;
        grid.Player = Player;
        grid.TradeMode = tradeMode;
        grid.Columns = 4;
        if (inventory.IsTradeSaleStorage)
            _saleGridContainer.AddChild(grid);
        else
            _grids.AddChild(grid);
    }

    private void PopulateTradeGoods()
    {
        foreach (Godot.Collections.Dictionary offerData in _tradingStand.npc_owner.GetTradeCatalog())
        {
            int id = offerData["id"].AsInt32();
            if (!TradeItemDatabase.TryGet(id, out TradeItemRecord record)) continue;
            ItemUI offer = ItemScene.Instantiate<ItemUI>();
            offer.BindTradeOffer(Player, _tradingStand, id, record.Item, record.Price, Player.Money >= record.Price);
            _tradeGoodsGrid.AddChild(offer);
        }
    }

    private void UpdateSaleTotal()
    {
        long total = 0;
        foreach (Stack stack in _tradingStand.SaleStorage.Slots)
            if (stack?.Item != null && stack.Item.SalePrice > 0)
                total += (long)stack.Item.SalePrice * stack.ItemCount;
        _saleTotalLabel.Text = $"К продаже: {total} золотых";
        _sellButton.Disabled = total <= 0 || total > int.MaxValue;
    }

    private void SellStagedItems()
    {
        if (_tradingStand != null)
            InventoryService.Instance?.RequestTradeSale(Player, _tradingStand);
    }
}
