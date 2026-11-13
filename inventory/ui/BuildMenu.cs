using Godot;
using System.Linq;

public partial class BuildMenu : PanelContainer
{
    [Export] public PackedScene CellScene { get; set; }
    private GridContainer _grid;
    private Player _player;
    public override void _Ready()
    {
        _grid = GetNode<GridContainer>("Margin/VBox/Grid");
        Hide();
    }

    public void Open(Player player, Hammer hammer)
    {
        _player = player;
        GD.Print($"[BuildDebug][Menu] open; hammer={hammer?.DisplayName ?? "<null>"}, " +
            $"recipes={hammer?.BuildingRecipes?.Count ?? 0}, player={player?.Name}.");
        foreach (Node child in _grid.GetChildren()) child.QueueFree();
        var recipes = hammer?.BuildingRecipes?.Where(r => r?.BuildingScene != null)
            .OrderByDescending(r => r.Costs.All(cost => Count(player.Inventory, cost.Item) >= cost.ItemCount))
            .ThenBy(r => r.DisplayName).ToArray() ?? System.Array.Empty<BuildingRecipe>();
        foreach (BuildingRecipe recipe in recipes)
        {
            bool available = recipe.Costs.All(cost => Count(player.Inventory, cost.Item) >= cost.ItemCount);
            if (CellScene == null)
            {
                GD.PushError("BuildMenu requires a BuildCell scene.");
                return;
            }
            BuildCell cell = CellScene.Instantiate<BuildCell>();
            _grid.AddChild(cell);
            cell.Bind(recipe, available);
            cell.Pressed += () =>
            {
                GD.Print($"[BuildDebug][Cell] pressed='{recipe.DisplayName}', disabled={cell.Disabled}, " +
                    $"playerState={_player?.States?.Current}, inputEnabled={_player?.playerInputComponent?.IsInputEnabled}.");
                _player.SelectBuilding(recipe);
            };
        }
        Show();
    }

    private static int Count(InventoryComponent inventory, Item item)
    {
        if (inventory?.Slots == null) return 0;
        return inventory.Slots.Skip(inventory.FirstItemSlot)
            .Where(stack => stack?.Item != null &&
                (stack.Item == item || stack.Item.ResourcePath == item?.ResourcePath))
            .Sum(stack => stack.ItemCount);
    }
}
