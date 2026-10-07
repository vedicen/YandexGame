using Godot;

public partial class NetworkManager : Node3D
{
	[Export] public PackedScene PlayerScene;
	[Export] public Node3D PlayersContainer;
	[Export] public Node3D SpawnPointsContainer;

	private const int Port = 7000;
	private const string DefaultIp = "127.0.0.1";

	public override void _Ready()
	{
		Multiplayer.PeerConnected += OnPeerConnected;
		Multiplayer.PeerDisconnected += OnPeerDisconnected;
		Multiplayer.ConnectedToServer += OnConnectedToServer;
		Multiplayer.ConnectionFailed += OnConnectionFailed;

		if (PlayersContainer == null)
			PlayersContainer = GetNodeOrNull<Node3D>("Players");

		if (SpawnPointsContainer == null)
			SpawnPointsContainer = GetNodeOrNull<Node3D>("SpawnPoints");
	}

    private void AutoStartNetwork()
    {
        // Получаем аргументы командной строки через OS
        string[] args = OS.GetCmdlineArgs();

        // Проверяем, передан ли флаг --join или -j
        bool isClient = false;
        foreach (string arg in args)
        {
            if (arg == "--join" || arg == "-j")
            {
                isClient = true;
                break;
            }
        }

        if (isClient)
        {
            GD.Print("[AutoStart] Запуск в режиме КЛИЕНТА...");
            JoinGame(DefaultIp);
        }
        else
        {
            GD.Print("[AutoStart] Запуск в режиме ХОСТА...");
            HostGame();
        }
    }

	public void HostGame()
	{
		var peer = new ENetMultiplayerPeer();
		Error error = peer.CreateServer(Port);

		if (error != Error.Ok)
		{
			GD.PrintErr($"Не удалось создать сервер: {error}");
			return;
		}

		Multiplayer.MultiplayerPeer = peer;
		GD.Print("Сервер успешно запущен!");

		// Спавним хоста
		SpawnPlayer(1);
	}

	public void JoinGame(string ip = DefaultIp)
	{
		var peer = new ENetMultiplayerPeer();
		Error error = peer.CreateClient(ip, Port);

		if (error != Error.Ok)
		{
			GD.PrintErr($"Не удалось подключиться: {error}");
			return;
		}

		Multiplayer.MultiplayerPeer = peer;
		GD.Print($"Подключение к {ip}...");
	}

	private void OnPeerConnected(long id)
	{
		GD.Print($"Игрок подключился: {id}");

		if (Multiplayer.IsServer())
		{
			SpawnPlayer(id);
		}
	}

	private void OnPeerDisconnected(long id)
	{
		GD.Print($"Игрок отключился: {id}");

		if (PlayersContainer == null)
			return;

		var playerNode = PlayersContainer.GetNodeOrNull(id.ToString());
		playerNode?.QueueFree();
	}

	private void OnConnectedToServer()
	{
		GD.Print("Подключение к серверу установлено. Создаём локального игрока...");
		SpawnPlayer(Multiplayer.GetUniqueId());
	}

	private void OnConnectionFailed()
	{
		GD.PrintErr("Не удалось подключиться к серверу.");
	}

	private void SpawnPlayer(long id)
	{
		if (PlayerScene == null)
		{
			GD.PrintErr("PlayerScene не назначена в Инспекторе!");
			return;
		}

		if (PlayersContainer == null)
		{
			GD.PrintErr("PlayersContainer не найден. Проверь узел 'Players' в сцене.");
			return;
		}

		var player = PlayerScene.Instantiate<CharacterBody3D>();
		player.Name = id.ToString();
		player.SetMultiplayerAuthority((int)id);

		Vector3 spawnPosition = Vector3.Zero;
		if (SpawnPointsContainer != null && SpawnPointsContainer.GetChildCount() > 0)
		{
			var points = SpawnPointsContainer.GetChildren();
			var randomPoint = points[(int)(id % points.Count)] as Node3D;
			if (randomPoint != null)
				spawnPosition = randomPoint.GlobalPosition;
		}

		player.GlobalPosition = spawnPosition;
		PlayersContainer.AddChild(player, true);
	}
}