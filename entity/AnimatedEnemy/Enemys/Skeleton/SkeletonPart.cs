using Godot;

/// <summary>Detached skeleton part that fades and sinks after it lands.</summary>
public partial class SkeletonPart : Node3D
{
	[Export] public Node3D ModelContainer { get; set; }
	[Export] public float FadeDelay { get; set; } = 5.0f;
	[Export] public float FadeDuration { get; set; } = 3.0f;
	[Export] public float SinkDistance { get; set; } = 0.8f;
	private float _flightTime;
	private float _flightDuration;
	private bool _landed;
	private readonly System.Collections.Generic.List<(MeshInstance3D Mesh, int Surface, ShaderMaterial FadeMaterial)> _fadeMaterials = new();

	public override void _Ready()
	{
		if (ModelContainer == null)
			ModelContainer = GetNodeOrNull<Node3D>("Model");
		ApplyFadeMaterials(this);
	}

	public Vector3 SettlePosition { get; private set; }

	public void SetModel(PackedScene modelScene)
	{
		if (modelScene == null)
			return;
		ModelContainer ??= GetNodeOrNull<Node3D>("Model");
		if (ModelContainer == null)
		{
			GD.PushError("SkeletonPart scene is missing its Model container.");
			return;
		}
		foreach (Node child in ModelContainer.GetChildren())
		{
			ModelContainer.RemoveChild(child);
			child.QueueFree();
		}
		Node3D model = modelScene.Instantiate<Node3D>();
		ModelContainer.AddChild(model);
		ApplyFadeMaterials(model);
	}

	public void SetSettlePosition(Vector3 position, float flightDuration)
	{
		SettlePosition = position;
		_flightDuration = Mathf.Max(0f, flightDuration);
	}

	public override void _Process(double delta)
	{
		if (_landed)
			return;
		_flightTime += (float)delta;
		if (_flightTime < _flightDuration)
			return;
		_landed = true;
		GlobalPosition = SettlePosition;
		var tween = CreateTween();
		tween.TweenInterval(FadeDelay);
		tween.TweenMethod(Callable.From<float>(SetDissolve), 0.0f, 1.0f, FadeDuration);
		tween.Parallel().TweenProperty(this, "global_position", SettlePosition - Vector3.Up * SinkDistance, FadeDuration)
			.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
		tween.TweenCallback(Callable.From(QueueFree));
	}

	private void SetDissolve(float value)
	{
		foreach (var entry in _fadeMaterials)
		{
			if (!GodotObject.IsInstanceValid(entry.Mesh) || !GodotObject.IsInstanceValid(entry.FadeMaterial))
				continue;
			entry.Mesh.SetSurfaceOverrideMaterial(entry.Surface, entry.FadeMaterial);
			entry.FadeMaterial.SetShaderParameter("dissolve_amount", value);
		}
	}

	private void ApplyFadeMaterials(Node node)
	{
		if (node is MeshInstance3D mesh && mesh.Mesh != null)
		{
			Shader shader = GD.Load<Shader>("res://entity/AnimatedEnemy/Enemys/Skeleton/skeleton_part_fade.gdshader");
			if (shader == null)
			{
				GD.PushError("Skeleton part dissolve shader could not be loaded.");
				return;
			}
			for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
			{
				Material source = mesh.GetActiveMaterial(surface) ?? mesh.Mesh.SurfaceGetMaterial(surface);
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
				_fadeMaterials.Add((mesh, surface, fadeMaterial));
			}
		}
		foreach (Node child in node.GetChildren())
			ApplyFadeMaterials(child);
	}
}
