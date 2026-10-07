using Godot;

public partial class MainMenu : Control
{
    private Button _hostButton;
    private Button _joinButton;

    public override void _Ready()
    {
        _hostButton = GetNode<Button>("CenterContainer/VBoxContainer/CreateGameButton");
        _joinButton = GetNode<Button>("CenterContainer/VBoxContainer/JoinGameButton");

        _hostButton.Pressed += OnHostPressed;
        _joinButton.Pressed += OnJoinPressed;
    }

    private void OnHostPressed()
    {
        StartGame(true);
    }

    private void OnJoinPressed()
    {
        StartGame(false);
    }

    private void StartGame(bool host)
    {
        var scene = ResourceLoader.Load<PackedScene>("res://scenes/base_scene.tscn");
        if (scene == null)
        {
            GD.PrintErr("Не найдена сцена base_scene.tscn");
            return;
        }

        var instance = scene.Instantiate<Node>();
        GetTree().Root.AddChild(instance);

        if (instance is NetworkManager networkManager)
        {
            if (host)
            {
                networkManager.HostGame();
            }
            else
            {
                networkManager.JoinGame("127.0.0.1");
            }
        }
        else
        {
            GD.PrintErr("Корневой узел сцены не является NetworkManager.");
        }

        QueueFree();
    }
}
