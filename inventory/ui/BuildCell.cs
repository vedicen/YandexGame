using Godot;
using System.Linq;

public partial class BuildCell : Button
{
    private SubViewport _preview;
    private Label _title;
    private Label _cost;
    private BuildingRecipe _recipe;
    public BuildingRecipe Recipe => _recipe;

    public override void _Ready()
    {
        _preview = GetNode<SubViewport>("VBox/PreviewContainer/Preview");
        _title = GetNode<Label>("VBox/Title");
        _cost = GetNode<Label>("VBox/Cost");
    }

    public void Bind(BuildingRecipe recipe, bool available)
    {
        _recipe = recipe;
        _title.Text = recipe?.DisplayName ?? "Строение";
        _cost.Text = string.Join("\n", recipe?.Costs?.Where(s => s?.Item != null)
            .Select(s => $"{s.Item.DisplayName}: {s.ItemCount}") ?? System.Array.Empty<string>());
        Disabled = !available;
        Modulate = available ? Colors.White : new Color(0.55f, 0.55f, 0.55f, 0.8f);
        GD.Print($"[BuildDebug][Cell] bound='{recipe?.DisplayName ?? "<null>"}', available={available}, " +
            $"disabled={Disabled}, costs={_cost.Text.Replace("\n", ", ")}.");
        if (recipe?.BuildingScene == null) return;
        foreach (Node child in _preview.GetChildren()) child.QueueFree();
        Node3D model = recipe.BuildingScene.Instantiate<Node3D>();
        model.SetPhysicsProcess(false);
        _preview.AddChild(model);
        model.Position = Vector3.Zero;
        model.Scale = Vector3.One * 0.55f;
        Camera3D camera = _preview.GetNode<Camera3D>("Camera3D");
        camera.Current = true;
    }
}
