using Godot;
using System.Collections.Generic;

public partial class WeaponProjectileScene : Area3D
{
    private const float TrailLifetime = 0.35f;
    private const float TrailWidth = 0.08f;
    private const int MaxTrailPoints = 48;

    private struct TrailPoint
    {
        public Vector3 Position;
        public float Age;

        public TrailPoint(Vector3 position)
        {
            Position = position;
            Age = 0f;
        }
    }

    private AnimatedEntity _attacker;
    private WeaponProjectile _projectile;
    private Vector3 _direction;
    private float _speed;
    private float _damage;
    private float _lifetime;
    private bool _initialized;
    private bool _resolved;
    private MeshInstance3D _trailInstance;
    private ArrayMesh _trailMesh;
    private readonly List<TrailPoint> _trailPoints = new();

    public void Initialize(
        AnimatedEntity attacker,
        WeaponProjectile projectile,
        Vector3 direction,
        float speed,
        float damage)
    {
        if (projectile?.Model == null)
        {
            GD.PushError("WeaponProjectileScene requires a projectile resource with a model.");
            return;
        }

        if (direction.LengthSquared() < 0.0001f ||
            !float.IsFinite(speed) || speed <= 0 ||
            !float.IsFinite(damage) || damage < 0)
        {
            GD.PushError("WeaponProjectileScene received invalid flight or damage values.");
            return;
        }

        _attacker = attacker;
        _projectile = projectile;
        _direction = direction.Normalized();
        _speed = speed;
        _damage = damage;

        Node3D model = projectile.Model.Instantiate<Node3D>();
        if (model == null)
        {
            GD.PushError($"Projectile model '{projectile.Model.ResourcePath}' is not a Node3D scene.");
            return;
        }

        AddChild(model);
        CreateTrail(projectile.TrailShader);
        LookAt(GlobalPosition + _direction, Vector3.Up);
        _initialized = true;
    }

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = (1u << 0) | (1u << 1);
        Monitoring = true;
        Monitorable = false;
        AreaEntered += OnAreaEntered;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_initialized)
            return;

        Vector3 previousPosition = GlobalPosition;
        Vector3 nextPosition = previousPosition + _direction * _speed * (float)delta;
        AddTrailPoint(nextPosition);

        if (IsServerAuthority())
        {
            var query = PhysicsRayQueryParameters3D.Create(
                previousPosition, nextPosition, (1u << 0) | (1u << 1));
            query.CollideWithAreas = true;
            query.CollideWithBodies = true;
            Godot.Collections.Dictionary result = GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (result.TryGetValue("collider", out Variant collider))
            {
                if (collider.AsGodotObject() is BoneHitbox hitbox)
                    ApplyHit(hitbox);
                else
                    RemoveFromPeers();
            }

            _lifetime += (float)delta;
            if (!_resolved && _lifetime >= 10.0f)
                RemoveFromPeers();
        }

        if (GodotObject.IsInstanceValid(this) && !IsQueuedForDeletion())
            GlobalPosition = nextPosition;
    }

    public override void _Process(double delta)
    {
        if (!_initialized || _trailInstance == null)
            return;

        for (int i = 0; i < _trailPoints.Count; i++)
        {
            TrailPoint point = _trailPoints[i];
            point.Age += (float)delta;
            _trailPoints[i] = point;
        }

        _trailPoints.RemoveAll(point => point.Age >= TrailLifetime);
        UpdateTrailMesh();
    }

    private void CreateTrail(Shader shader)
    {
        shader ??= GD.Load<Shader>("res://VFX/SlashTrail.gdshader");
        if (shader == null)
        {
            GD.PushError(
                $"WeaponProjectile '{_projectile?.DisplayName}' has no trail shader and the default shader could not be loaded.");
            return;
        }

        _trailMesh = new ArrayMesh();
        _trailInstance = new MeshInstance3D
        {
            Name = "DynamicMeshTrail",
            Mesh = _trailMesh,
            MaterialOverride = new ShaderMaterial { Shader = shader }
        };
        AddChild(_trailInstance);
    }

    private void AddTrailPoint(Vector3 position)
    {
        if (_trailInstance == null)
            return;

        if (_trailPoints.Count > 0 &&
            _trailPoints[^1].Position.DistanceSquaredTo(position) < 0.0004f)
            return;

        _trailPoints.Add(new TrailPoint(position));
        if (_trailPoints.Count > MaxTrailPoints)
            _trailPoints.RemoveAt(0);
    }

    private void UpdateTrailMesh()
    {
        _trailMesh.ClearSurfaces();
        if (_trailPoints.Count < 2)
            return;

        int count = _trailPoints.Count;
        Vector3[] vertices = new Vector3[count * 2];
        Color[] colors = new Color[count * 2];
        Vector2[] uvs = new Vector2[count * 2];
        int[] indices = new int[(count - 1) * 6];
        Camera3D camera = GetViewport()?.GetCamera3D();

        for (int i = 0; i < count; i++)
        {
            TrailPoint point = _trailPoints[i];
            Vector3 tangent = GetTrailTangent(i);
            Vector3 viewDirection = camera != null
                ? camera.GlobalPosition - point.Position
                : Vector3.Up;
            Vector3 side = tangent.Cross(viewDirection);

            if (side.LengthSquared() < 0.0001f)
                side = tangent.Cross(Vector3.Up);
            if (side.LengthSquared() < 0.0001f)
                side = tangent.Cross(Vector3.Right);

            side = side.Normalized() * (TrailWidth * 0.5f);
            float age = Mathf.Clamp(point.Age / TrailLifetime, 0f, 1f);
            int vertex = i * 2;

            vertices[vertex] = ToLocal(point.Position - side);
            vertices[vertex + 1] = ToLocal(point.Position + side);
            colors[vertex] = new Color(1f, 1f, 1f, 1f - age);
            colors[vertex + 1] = colors[vertex];
            uvs[vertex] = new Vector2(age, 0f);
            uvs[vertex + 1] = new Vector2(age, 1f);
        }

        for (int i = 0; i < count - 1; i++)
        {
            int vertex = i * 2;
            int index = i * 6;
            indices[index] = vertex;
            indices[index + 1] = vertex + 1;
            indices[index + 2] = vertex + 2;
            indices[index + 3] = vertex + 1;
            indices[index + 4] = vertex + 3;
            indices[index + 5] = vertex + 2;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Godot.Mesh.ArrayType.Max);
        arrays[(int)Godot.Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Godot.Mesh.ArrayType.Color] = colors;
        arrays[(int)Godot.Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Godot.Mesh.ArrayType.Index] = indices;
        _trailMesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
    }

    private Vector3 GetTrailTangent(int index)
    {
        Vector3 tangent = index == 0
            ? _trailPoints[1].Position - _trailPoints[0].Position
            : index == _trailPoints.Count - 1
                ? _trailPoints[index].Position - _trailPoints[index - 1].Position
                : _trailPoints[index + 1].Position - _trailPoints[index - 1].Position;

        return tangent.LengthSquared() > 0.0001f ? tangent.Normalized() : _direction;
    }

    private void OnAreaEntered(Area3D area)
    {
        if (!_initialized || !IsServerAuthority() || area is not BoneHitbox hitbox)
            return;

        ApplyHit(hitbox);
    }

    private void ApplyHit(BoneHitbox hitbox)
    {
        if (_resolved)
            return;

        AnimatedEntity target = hitbox.Target;
        if (target == null || target == _attacker || target.Health == null ||
            target.Health.CurrentHealth <= 0)
            return;

        target.Health.TakeDamage(_damage);
        InventoryService.Instance?.Publish();
        RemoveFromPeers();
    }

    private void RemoveFromPeers()
    {
        if (_resolved)
            return;
        _resolved = true;
        if (Multiplayer.HasMultiplayerPeer())
            Rpc(nameof(RemoveProjectile), Name);
        else
            QueueFree();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RemoveProjectile(string projectileName)
    {
        if (Multiplayer.HasMultiplayerPeer())
        {
            long sender = Multiplayer.GetRemoteSenderId();
            if (sender != 0 && sender != 1)
                return;
        }

        if (Name == projectileName)
            QueueFree();
    }

    private bool IsServerAuthority() =>
        !Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer();
}
