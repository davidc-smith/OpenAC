using System.Numerics;

namespace AcDream.App.Rendering;

/// <summary>
/// How a doll view is turned and zoomed: a yaw about the doll's up axis
/// (radians, added to the pose the doll was built with) and a zoom that
/// narrows the camera's field of view. The retail paper doll is always at
/// <see cref="Default"/>; the Gear Deck's figure turns when dragged and zooms
/// with the wheel.
/// </summary>
internal readonly record struct PaperdollPose(float Yaw, float Zoom)
{
    public const float MinZoom = 1f, MaxZoom = 2.5f, ZoomStep = 1.12f;

    public static PaperdollPose Default => new(0f, 1f);

    /// <summary>Unturned and unzoomed: the only pose the retail click map matches.</summary>
    public bool IsDefault => Yaw == 0f && Zoom == 1f;

    /// <summary>Turned by <paramref name="radians"/> more, kept within one turn.</summary>
    public PaperdollPose Turned(float radians)
    {
        float yaw = (Yaw + radians) % MathF.Tau;
        if (yaw > MathF.PI) yaw -= MathF.Tau;
        else if (yaw <= -MathF.PI) yaw += MathF.Tau;
        return this with { Yaw = yaw };
    }

    /// <summary>Zoomed by <paramref name="notches"/> wheel steps (in is positive), within
    /// [<see cref="MinZoom"/>, <see cref="MaxZoom"/>]; a zoom back to within 0.001 of 1 is 1.</summary>
    public PaperdollPose Zoomed(int notches)
    {
        float zoom = Math.Clamp(Zoom * MathF.Pow(ZoomStep, notches), MinZoom, MaxZoom);
        if (MathF.Abs(zoom - 1f) < 1e-3f) zoom = 1f;
        return this with { Zoom = zoom };
    }

    /// <summary>The doll's rotation: <paramref name="baseRotation"/> turned about +Z by the yaw.</summary>
    public Quaternion Apply(Quaternion baseRotation) => Yaw == 0f
        ? baseRotation
        : Quaternion.Concatenate(baseRotation, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Yaw));

    /// <summary>The vertical field of view that makes the doll <see cref="Zoom"/> times as tall.</summary>
    public float Fov(float baseFov) => Zoom == 1f
        ? baseFov
        : 2f * MathF.Atan(MathF.Tan(baseFov / 2f) / Zoom);
}
