
using Godot;
using System.Collections.Generic;

public partial class SwordTrail : MeshInstance3D
{
    [Export] public float TrailLifetime { get; set; } = 0.3f;
    [Export] public float MinPointDistance { get; set; } = 0.005f;
    [Export] public int MaxPoints { get; set; } = 48;

    private const float MaxSegmentLength = 0.1f;
    private const int MaxInterpolatedPoints = 8;

    private struct TrailPoint
    {
        public Vector3 Base;
        public Vector3 Tip;
        public float Age;

        public TrailPoint(Vector3 bladeBase, Vector3 bladeTip)
        {
            Base = bladeBase;
            Tip = bladeTip;
            Age = 0f;
        }
    }

    private readonly List<TrailPoint> _points = new();

    private Node3D _bladeBase;
    private Node3D _bladeTip;

    private ArrayMesh _mesh;
    private bool _recording;

    public override void _Ready()
    {
        _mesh = new ArrayMesh();
        Mesh = _mesh;

        MaterialOverride = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://VFX/SlashTrail.gdshader")
        };


    }

    public void Configure(Node3D bladeBase, Node3D bladeTip)
    {
        _bladeBase = bladeBase;
        _bladeTip = bladeTip;

    }

    public void SetTrailShader(Shader shader)
    {
        if (shader != null && MaterialOverride is ShaderMaterial material)
            material.Shader = shader;
    }

    public void BeginTrail()
    {


        if (_bladeBase == null || _bladeTip == null)
        {
            GD.PushError("[SwordTrail] Blade markers are not configured!");
            return;
        }

        _points.Clear();
        _mesh.ClearSurfaces();
        _recording = true;

        AddPoint();
    }

    public void EndTrail()
    {
        if (_recording)
            AddPoint();
        _recording = false;
    }

    public override void _Process(double delta)
    {
        if (_recording)
            AddPoint();

        UpdateAges((float)delta);
        RemoveExpiredPoints();
        UpdateMesh();
    }

    private void AddPoint()
    {
        if (_bladeBase == null || _bladeTip == null)
            return;

        // Сохраняем позиции в глобальных координатах.
        Vector3 basePosition = _bladeBase.GlobalPosition;
        Vector3 tipPosition = _bladeTip.GlobalPosition;

        if (_points.Count == 0)
        {
            _points.Add(new TrailPoint(basePosition, tipPosition));
            return;
        }

        TrailPoint previous = _points[^1];
        float distance = Mathf.Max(
            basePosition.DistanceTo(previous.Base),
            tipPosition.DistanceTo(previous.Tip));
        if (distance < MinPointDistance)
            return;

        int subdivisions = Mathf.Min(
            Mathf.CeilToInt(distance / MaxSegmentLength),
            MaxInterpolatedPoints);

        for (int i = 1; i <= subdivisions; i++)
        {
            float weight = (float)i / subdivisions;
            _points.Add(new TrailPoint(
                previous.Base.Lerp(basePosition, weight),
                previous.Tip.Lerp(tipPosition, weight)));
        }

        while (_points.Count > MaxPoints)
            _points.RemoveAt(0);
    }

    private void UpdateAges(float delta)
    {
        for (int i = 0; i < _points.Count; i++)
        {
            TrailPoint point = _points[i];
            point.Age += delta;
            _points[i] = point;
        }
    }

    private void RemoveExpiredPoints()
    {
        _points.RemoveAll(
            point => point.Age >= TrailLifetime
        );
    }

    private void UpdateMesh()
    {
        _mesh.ClearSurfaces();

        if (_points.Count < 2)
            return;

        int count = _points.Count;

        Vector3[] vertices = new Vector3[count * 2];
        Color[] colors = new Color[count * 2];
        Vector2[] uvs = new Vector2[count * 2];
        int[] indices = new int[(count - 1) * 6];

        for (int i = 0; i < count; i++)
        {
            TrailPoint point = _points[i];

            float alpha = 1f - point.Age / TrailLifetime;
            alpha = Mathf.Clamp(alpha, 0f, 1f);
            alpha *= alpha;

            int index = i * 2;

            // Переводим глобальные координаты в локальные.
            vertices[index] = ToLocal(point.Base);
            vertices[index + 1] = ToLocal(point.Tip);

            colors[index] = new Color(1, 1, 1, alpha);
            colors[index + 1] = new Color(1, 1, 1, alpha);

            float progress = (float)i / (count - 1);

            uvs[index] = new Vector2(0, progress);
            uvs[index + 1] = new Vector2(1, progress);
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

        _mesh.AddSurfaceFromArrays(
            Godot.Mesh.PrimitiveType.Triangles,
            arrays
        );


    }
}
