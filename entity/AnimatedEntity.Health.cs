using Godot;

public partial class AnimatedEntity : Entity
{
	[Export] public float DeathDissolveDuration { get; set; } = 1.5f;
	[Export] public float HealthBarHeight { get; set; } = 2.2f;
	[ExportGroup("Hitboxes")]
	[Export(PropertyHint.Range, "0.01,1.0,0.01")]
	public float HeadHitboxRadius { get; set; } = 0.208f;
	[Export(PropertyHint.Range, "0.01,1.0,0.01")]
	public float FootHitboxRadius { get; set; } = 0.13f;
	[Export(PropertyHint.Range, "0.01,1.0,0.01")]
	public float DefaultHitboxRadius { get; set; } = 0.169f;
	private Skeleton3D _skeleton;
	private bool _deathDissolveStarted;
	private readonly System.Collections.Generic.List<(MeshInstance3D Mesh, int Surface, ShaderMaterial Material)> _deathMaterials = new();
	private readonly System.Collections.Generic.List<(MeshInstance3D Mesh, int Surface, Material Original, ShaderMaterial Fade)> _appearanceMaterials = new();
	private Tween _appearanceTween;

	private Shader GetDissolveShader() => GetNodeOrNull<Global>("/root/Global")?.SkeletonDissolveShader;

	private void PlayAppearanceDissolve()
	{
		Shader shader = GetDissolveShader();
		if (shader == null || _modelRoot == null)
			return;
		PrepareAppearanceMaterials(_modelRoot, shader);
		if (_appearanceMaterials.Count == 0)
			return;
		foreach (var entry in _appearanceMaterials)
		{
			entry.Mesh.SetSurfaceOverrideMaterial(entry.Surface, entry.Fade);
			entry.Fade.SetShaderParameter("dissolve_amount", 1.0f);
		}
		_appearanceTween = CreateTween();
		_appearanceTween.TweenMethod(Callable.From<float>(SetAppearanceDissolve), 1.0f, 0.0f, 1.5f);
		_appearanceTween.TweenCallback(Callable.From(RestoreAppearanceMaterials));
	}

	private void PrepareAppearanceMaterials(Node node, Shader shader)
	{
		if (node is MeshInstance3D mesh && mesh.Mesh != null)
		{
			for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
			{
				Material original = mesh.GetActiveMaterial(surface) ?? mesh.Mesh.SurfaceGetMaterial(surface);
				ShaderMaterial fade = CreateDissolveMaterial(shader, original);
				_appearanceMaterials.Add((mesh, surface, original, fade));
			}
		}
		foreach (Node child in node.GetChildren())
			PrepareAppearanceMaterials(child, shader);
	}

	private void SetAppearanceDissolve(float value)
	{
		foreach (var entry in _appearanceMaterials)
			if (GodotObject.IsInstanceValid(entry.Fade))
				entry.Fade.SetShaderParameter("dissolve_amount", value);
	}

	private void RestoreAppearanceMaterials()
	{
		foreach (var entry in _appearanceMaterials)
			if (GodotObject.IsInstanceValid(entry.Mesh))
				entry.Mesh.SetSurfaceOverrideMaterial(entry.Surface, null);
		_appearanceMaterials.Clear();
		_appearanceTween = null;
	}

	public override void OnDied()
	{
		if (_deathDissolveStarted)
			return;
		_deathDissolveStarted = true;
		_appearanceTween?.Kill();
		RestoreAppearanceMaterials();
		SetPhysicsProcess(false);
		SetProcess(false);

		Shader shader = GetDissolveShader();
		if (shader == null || _modelRoot == null)
		{
			QueueFree();
			return;
		}
		PrepareDeathMaterials(_modelRoot, shader);
		if (_deathMaterials.Count == 0)
		{
			QueueFree();
			return;
		}

		var tween = CreateTween();
		tween.TweenMethod(Callable.From<float>(SetDeathDissolve), 0.0f, 1.0f, Mathf.Max(0.05f, DeathDissolveDuration));
		tween.TweenCallback(Callable.From(QueueFree));
	}

	private void PrepareDeathMaterials(Node node, Shader shader)
	{
		if (node is MeshInstance3D mesh && mesh.Mesh != null)
		{
			for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
			{
				Material source = mesh.GetActiveMaterial(surface) ?? mesh.Mesh.SurfaceGetMaterial(surface);
				var fadeMaterial = CreateDissolveMaterial(shader, source);
				mesh.SetSurfaceOverrideMaterial(surface, fadeMaterial);
				_deathMaterials.Add((mesh, surface, fadeMaterial));
			}
		}
		foreach (Node child in node.GetChildren())
			PrepareDeathMaterials(child, shader);
	}

	private static ShaderMaterial CreateDissolveMaterial(Shader shader, Material source)
	{
		var fadeMaterial = new ShaderMaterial { Shader = shader };
		if (source is BaseMaterial3D baseMaterial)
		{
			fadeMaterial.SetShaderParameter("base_color", baseMaterial.AlbedoColor);
			fadeMaterial.SetShaderParameter("albedo_texture", baseMaterial.AlbedoTexture);
			fadeMaterial.SetShaderParameter("roughness", baseMaterial.Roughness);
			fadeMaterial.SetShaderParameter("metallic", baseMaterial.Metallic);
			fadeMaterial.SetShaderParameter("normal_texture", baseMaterial.NormalTexture);
			fadeMaterial.SetShaderParameter("normal_scale", baseMaterial.NormalScale);
			fadeMaterial.SetShaderParameter("ao_texture", baseMaterial.AOTexture);
			fadeMaterial.SetShaderParameter("emission_texture", baseMaterial.EmissionTexture);
			fadeMaterial.SetShaderParameter("emission_color", baseMaterial.Emission);
		}
		return fadeMaterial;
	}

	private void SetDeathDissolve(float value)
	{
		foreach (var entry in _deathMaterials)
			if (GodotObject.IsInstanceValid(entry.Material))
				entry.Material.SetShaderParameter("dissolve_amount", value);
	}

	private void CreateHealthBar()
	{
		if (Health == null)
			return;
		if (this is Player player && player.playerInputComponent?.HasInputAuthority == true)
			return;

		PackedScene scene = GD.Load<PackedScene>("res://entity/components/WorldHealthBar.tscn");
		WorldHealthBar worldBar = scene?.Instantiate<WorldHealthBar>();
		if (worldBar == null)
			return;
		AddChild(worldBar);
		worldBar.Bind(Health, HealthBarHeight);
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
