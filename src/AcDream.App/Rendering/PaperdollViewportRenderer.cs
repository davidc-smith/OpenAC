using AcDream.App.Rendering.Wb;
using AcDream.App.UI;
using AcDream.Core.Lighting;
using AcDream.Core.World;

namespace AcDream.App.Rendering;

public sealed class PaperdollViewportRenderer :
    IUiViewportRenderer,
    IPaperdollDollRenderer,
    IDisposable
{
    private readonly PrivateEntityViewportRenderer _renderer;
    private readonly DollViewportCamera _camera = new();

    internal PaperdollViewportRenderer(
        IWorldPassScope scope,
        AcDream.App.Rendering.Gpu.IGpuDevice device,
        ICurrentGpuFrameSource frames,
        WbDrawDispatcher dispatcher,
        SceneLightingUboBinding lightUbo,
        IEntityTextureLifetime textureLifetime,
        IWbMeshAdapter meshAdapter)
    {
        _baseFov = _camera.FovRadians;
        _renderer = new PrivateEntityViewportRenderer(
            scope,
            device,
            frames,
            dispatcher,
            lightUbo,
            textureLifetime,
            meshAdapter,
            DollEntityBuilder.DollRenderId,
            _camera,
            "paperdoll");
    }

    public bool TextureIsBottomUp => _renderer.TextureIsBottomUp;

    public void SetHeritage(uint heritageId) => _camera.SetHeritage(heritageId);

    /// <summary>The camera's field of view at zoom 1 (the camera's own default, read at construction).</summary>
    private readonly float _baseFov;

    private WorldEntity? _doll;
    private System.Numerics.Quaternion _dollBaseRotation = System.Numerics.Quaternion.Identity;
    private PaperdollPose _pose = PaperdollPose.Default;

    public void SetDoll(WorldEntity? doll)
    {
        _doll = doll;
        if (doll is not null) _dollBaseRotation = doll.Rotation;
        ApplyPose();
        _renderer.SetEntity(doll);
    }

    void IPaperdollDollRenderer.SetPose(PaperdollPose pose)
    {
        if (pose == _pose) return;
        _pose = pose;
        ApplyPose();
    }

    /// <summary>The doll's rotation is read every draw (the private renderer's snapshot keys on meshes only),
    /// so turning the entity in place is enough.</summary>
    private void ApplyPose()
    {
        if (_doll is not null) _doll.Rotation = _pose.Apply(_dollBaseRotation);
        _camera.FovRadians = _pose.Fov(_baseFov);
    }

    public void Prepare() => _renderer.Prepare();

    public uint Render(int width, int height) =>
        _renderer.Render(width, height);

    public void Dispose() => _renderer.Dispose();
}
