namespace AcDream.App.Rendering;

/// <summary>
/// Several places the one paper doll can be shown (the retail inventory's
/// viewport, the Gear Deck's figure). Each frame the first visible one gets
/// the render, at its size and pose; the others keep whatever they last
/// showed, which nobody sees while they are hidden.
/// </summary>
internal sealed class PaperdollFrameViews : IPaperdollFrameView
{
    private readonly IPaperdollFrameView[] _views;
    private IPaperdollFrameView? _active;

    public PaperdollFrameViews(params IPaperdollFrameView[] views)
    {
        ArgumentNullException.ThrowIfNull(views);
        _views = views;
    }

    public PaperdollPose Pose => _active?.Pose ?? PaperdollPose.Default;

    public bool TryGetVisibleSize(out int width, out int height)
    {
        foreach (IPaperdollFrameView view in _views)
        {
            if (view.TryGetVisibleSize(out width, out height))
            {
                _active = view;
                return true;
            }
        }
        _active = null;
        width = 0;
        height = 0;
        return false;
    }

    public void SetTextureHandle(uint textureHandle) => _active?.SetTextureHandle(textureHandle);

    public void ClearTextureHandle()
    {
        foreach (IPaperdollFrameView view in _views) view.ClearTextureHandle();
    }
}
