using Godot;

public partial class AnimatedEntity : Entity
{
	[Export] public float HealthBarHeight { get; set; } = 2.2f;
	[ExportGroup("Hitboxes")]
	[Export(PropertyHint.Range, "0.01,1.0,0.01")]
	public float HeadHitboxRadius { get; set; } = 0.208f;
	[Export(PropertyHint.Range, "0.01,1.0,0.01")]
	public float FootHitboxRadius { get; set; } = 0.13f;
	[Export(PropertyHint.Range, "0.01,1.0,0.01")]
	public float DefaultHitboxRadius { get; set; } = 0.169f;
	private Skeleton3D _skeleton;
	private ProgressBar _healthBar;

	private void CreateHealthBar()
	{
		if (Health == null)
			return;

		var viewport = new SubViewport
		{
			Name = "HealthBarViewport",
			Size = new Vector2I(180, 22),
			TransparentBg = true,
			OwnWorld3D = false,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always
		};
		AddChild(viewport);

		var background = new ColorRect
		{
			Color = new Color(0.08f, 0.08f, 0.08f, 0.85f),
			Position = Vector2.Zero,
			Size = new Vector2(180, 22),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		viewport.AddChild(background);

		_healthBar = new ProgressBar
		{
			Position = new Vector2(2, 2),
			Size = new Vector2(176, 18),
			MinValue = 0,
			MaxValue = Health.MaxHealth,
			ShowPercentage = false,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_healthBar.AddThemeStyleboxOverride("background", new StyleBoxFlat
		{
			BgColor = new Color(0.2f, 0.12f, 0.12f),
			ContentMarginLeft = 0,
			ContentMarginRight = 0,
			ContentMarginTop = 0,
			ContentMarginBottom = 0
		});
		_healthBar.AddThemeStyleboxOverride("fill", new StyleBoxFlat
		{
			BgColor = new Color(0.82f, 0.12f, 0.14f),
			ContentMarginLeft = 0,
			ContentMarginRight = 0,
			ContentMarginTop = 0,
			ContentMarginBottom = 0
		});
		viewport.AddChild(_healthBar);

		var sprite = new Sprite3D
		{
			Name = "HealthBar",
			Texture = viewport.GetTexture(),
			Position = new Vector3(0, HealthBarHeight, 0),
			PixelSize = 0.01f,
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			Shaded = false,
			NoDepthTest = true,
			AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass
		};
		AddChild(sprite);

		Health.HealthChanged += UpdateHealthBar;
		UpdateHealthBar(Health.CurrentHealth, Health.MaxHealth);
	}

	private void UpdateHealthBar(float currentHealth, float maxHealth)
	{
		if (_healthBar == null)
			return;

		_healthBar.MaxValue = maxHealth;
		_healthBar.Value = currentHealth;
	}

	private void ReplicateHealth(float currentHealth, float maxHealth)
	{
		if (Multiplayer.HasMultiplayerPeer() && Multiplayer.IsServer())
			Rpc(nameof(ReceiveReplicatedHealth), currentHealth);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceiveReplicatedHealth(float currentHealth)
	{
		if (Multiplayer.IsServer() || Multiplayer.GetRemoteSenderId() != 1)
		{
			GD.PushWarning($"[Combat] Rejected health update from peer {Multiplayer.GetRemoteSenderId()} for {GetPath()}.");
			return;
		}

		Health?.SetReplicatedHealth(currentHealth);
	}

	public void RequestHealthSync()
	{
		if (!Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer())
			return;

		RpcId(1, nameof(RequestReplicatedHealth));
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestReplicatedHealth()
	{
		if (!Multiplayer.IsServer() || Health == null)
			return;

		long peerId = Multiplayer.GetRemoteSenderId();
		if (peerId > 1)
			RpcId(peerId, nameof(ReceiveReplicatedHealth), Health.CurrentHealth);
	}

	private void GenerateBoneHitboxes()
	{
		_skeleton = FindSkeleton(_modelRoot);
		if (_skeleton == null)
		{
			GD.PushWarning($"AnimatedEntity '{Name}' has no Skeleton3D; bone hitboxes were not created.");
			return;
		}

		for (int i = 0; i < _skeleton.GetBoneCount(); i++)
		{
			string boneName = _skeleton.GetBoneName(i).ToString();
			if (!BoneHitbox.ShouldCreateForBone(boneName))
				continue;

			var attachment = new BoneAttachment3D
			{
				Name = $"HitboxAttachment_{i}",
				BoneName = boneName
			};
			_skeleton.AddChild(attachment);
			var hitbox = new BoneHitbox
			{
				Name = $"Hitbox_{i}"
			};
			hitbox.Initialize(this, boneName, GetBoneHitboxRadius(boneName));
			attachment.AddChild(hitbox);
		}
	}

	private float GetBoneHitboxRadius(string boneName)
	{
		if (boneName.Contains("head", System.StringComparison.OrdinalIgnoreCase))
			return HeadHitboxRadius;
		if (boneName.Contains("foot", System.StringComparison.OrdinalIgnoreCase))
			return FootHitboxRadius;
		return DefaultHitboxRadius;
	}

	private static Skeleton3D FindSkeleton(Node node)
	{
		if (node is Skeleton3D skeleton)
			return skeleton;

		foreach (Node child in node.GetChildren())
		{
			Skeleton3D result = FindSkeleton(child);
			if (result != null)
				return result;
		}

		return null;
	}

}
