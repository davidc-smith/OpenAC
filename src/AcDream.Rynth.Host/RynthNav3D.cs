using System.Numerics;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Navigation;

namespace AcDream.Rynth.Host;

/// <summary>
/// RynthCore's Nav3D buffer: rings, lines and triangles a plugin adds during a
/// host tick, in metres from the player's landblock (X east, Y up, Z north),
/// shaped as RynthCore's DX9 backend shapes them and drawn as world triangles.
/// A tick in which the player crossed into another landblock is dropped, as
/// RynthCore's CommitFrame drops it.
/// </summary>
internal sealed class RynthNav3D(IRynthGameView view, IPluginWorldTriangleLayer? layer) : IDisposable
{
    internal const int MaxRings = 256, MaxLines = 512, MaxTriangles = 8192;
    internal const int RingSegments = 32;
    internal const float DefaultRingHeight = 0.5f;
    private const float MinimumRingHeight = 0.1f, MinimumInnerRadius = 0.01f, LineWallHeight = 0.15f;

    private readonly record struct Ring(Vector3 Centre, float Radius, float Thickness, float Height, uint Argb);
    private readonly record struct Line(Vector3 From, Vector3 To, float Thickness, uint Argb);
    private readonly record struct Face(Vector3 A, Vector3 B, Vector3 C, uint Argb);

    private readonly List<Ring> _rings = [];
    private readonly List<Line> _lines = [];
    private readonly List<Face> _faces = [];
    private readonly List<PluginWorldTriangle> _built = [];
    private PluginWorldTriangle[] _shown = [];
    private bool _everShown;
    private uint _anchorCell;

    /// <summary>Starts a tick: empties the buffer and anchors it to the player's cell.</summary>
    public void BeginTick()
    {
        Clear();
        _anchorCell = view.TryGetPlayerPose(out uint cell, out _, out _) ? cell : 0;
    }

    /// <summary>RynthCore's Nav3DClear: empties what this tick has added.</summary>
    public void Clear()
    {
        _rings.Clear();
        _lines.Clear();
        _faces.Clear();
    }

    public void AddRing(float x, float y, float z, float radius, float thickness, uint argb) =>
        AddRingEx(x, y, z, radius, thickness, DefaultRingHeight, argb);

    public void AddRingEx(float x, float y, float z, float radius, float thickness, float height, uint argb)
    {
        if (_rings.Count < MaxRings)
            _rings.Add(new Ring(new Vector3(x, y, z), radius, thickness, height, argb));
    }

    public void AddLine(float x1, float y1, float z1, float x2, float y2, float z2, float thickness, uint argb)
    {
        if (_lines.Count < MaxLines)
            _lines.Add(new Line(new Vector3(x1, y1, z1), new Vector3(x2, y2, z2), thickness, argb));
    }

    public void AddTriangle(float x1, float y1, float z1, float x2, float y2, float z2, float x3, float y3, float z3, uint argb)
    {
        if (_faces.Count < MaxTriangles)
            _faces.Add(new Face(new Vector3(x1, y1, z1), new Vector3(x2, y2, z2), new Vector3(x3, y3, z3), argb));
    }

    /// <summary>Ends a tick: shows what it added, unless the player changed landblock during it.</summary>
    public void CommitTick()
    {
        if (!view.TryGetPlayerPose(out uint cell, out _, out _) || _anchorCell == 0)
        {
            Show([]);
            return;
        }

        if (cell >> 16 != _anchorCell >> 16)
            return;

        _built.Clear();
        foreach (Face face in _faces)
            _built.Add(Triangle(face.A, face.B, face.C, face.Argb));
        foreach (Line line in _lines)
            BuildLine(line);
        foreach (Ring ring in _rings)
            BuildRing(ring);
        Show(_built);
    }

    internal (int Rings, int Lines, int Triangles) Counts => (_rings.Count, _lines.Count, _faces.Count);

    public void Dispose() => layer?.Dispose();

    private void Show(List<PluginWorldTriangle> triangles)
    {
        if (_everShown && triangles.Count == _shown.Length && triangles.SequenceEqual(_shown))
            return;
        _shown = [.. triangles];
        _everShown = true;
        layer?.SetTriangles(_shown);
    }

    private void BuildRing(Ring ring)
    {
        uint top = Faded(ring.Argb);
        float height = MathF.Max(ring.Height, MinimumRingHeight);
        float inner = MathF.Max(MinimumInnerRadius, ring.Radius - ring.Thickness);
        float rim = ring.Radius + ring.Thickness;
        for (int i = 0; i < RingSegments; i++)
        {
            float a0 = 2f * MathF.PI * i / RingSegments, a1 = 2f * MathF.PI * (i + 1) / RingSegments;
            Vector3 o0 = ring.Centre + new Vector3(ring.Radius * MathF.Cos(a0), 0f, ring.Radius * MathF.Sin(a0));
            Vector3 o1 = ring.Centre + new Vector3(ring.Radius * MathF.Cos(a1), 0f, ring.Radius * MathF.Sin(a1));
            Vector3 up = new(0f, height, 0f);
            _built.Add(Triangle(o0, o1, o1 + up, ring.Argb, ring.Argb, top));
            _built.Add(Triangle(o0, o1 + up, o0 + up, ring.Argb, top, top));
            // The ground band spans radius - thickness to radius + thickness; the wall stands at radius.
            Vector3 r0 = ring.Centre + new Vector3(rim * MathF.Cos(a0), 0f, rim * MathF.Sin(a0));
            Vector3 r1 = ring.Centre + new Vector3(rim * MathF.Cos(a1), 0f, rim * MathF.Sin(a1));
            Vector3 i0 = ring.Centre + new Vector3(inner * MathF.Cos(a0), 0f, inner * MathF.Sin(a0));
            Vector3 i1 = ring.Centre + new Vector3(inner * MathF.Cos(a1), 0f, inner * MathF.Sin(a1));
            _built.Add(Triangle(r0, r1, i1, ring.Argb));
            _built.Add(Triangle(r0, i1, i0, ring.Argb));
        }
    }

    private void BuildLine(Line line)
    {
        float dx = line.To.X - line.From.X, dz = line.To.Z - line.From.Z;
        float length = MathF.Sqrt(dx * dx + dz * dz);
        if (length < 0.001f)
            return;
        var side = new Vector3(-dz / length * line.Thickness, 0f, dx / length * line.Thickness);
        _built.Add(Triangle(line.From - side, line.From + side, line.To + side, line.Argb));
        _built.Add(Triangle(line.From - side, line.To + side, line.To - side, line.Argb));
        uint top = Faded(line.Argb);
        Vector3 up = new(0f, LineWallHeight, 0f);
        _built.Add(Triangle(line.From, line.To, line.To + up, line.Argb, line.Argb, top));
        _built.Add(Triangle(line.From, line.To + up, line.From + up, line.Argb, top, top));
    }

    private static uint Faded(uint argb) => (argb & 0x00FFFFFFu) | 0x10000000u;

    private PluginWorldTriangle Triangle(Vector3 a, Vector3 b, Vector3 c, uint argb) =>
        new(Position(a), Position(b), Position(c), Colour(argb));

    private PluginWorldTriangle Triangle(Vector3 a, Vector3 b, Vector3 c, uint argbA, uint argbB, uint argbC) =>
        new PluginWorldTriangle(Position(a), Position(b), Position(c), Colour(argbA))
        {
            ColorB = argbB == argbA ? null : Colour(argbB),
            ColorC = argbC == argbA ? null : Colour(argbC),
        };

    // RynthCore's frame is Y up; the client's cell frame is Z up.
    private PluginNavigationPosition Position(Vector3 rynth) =>
        RuntimeNavigationPositions.FromCellLocal(_anchorCell, new Vector3(rynth.X, rynth.Z, rynth.Y));

    private static PluginColor Colour(uint argb) =>
        new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));
}
