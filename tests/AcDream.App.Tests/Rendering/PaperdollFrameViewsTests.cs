using AcDream.App.Rendering;
using AcDream.Core.World;

namespace AcDream.App.Tests.Rendering;

public sealed class PaperdollFrameViewsTests
{
    private sealed class View : IPaperdollFrameView
    {
        public bool Visible;
        public int W = 10, H = 20;
        public PaperdollPose Pose { get; set; } = PaperdollPose.Default;
        public readonly List<uint> Published = [];
        public int Cleared;
        public bool TryGetVisibleSize(out int width, out int height) { width = W; height = H; return Visible; }
        public void SetTextureHandle(uint textureHandle) => Published.Add(textureHandle);
        public void ClearTextureHandle() => Cleared++;
    }

    private sealed class Renderer : IPaperdollDollRenderer
    {
        public readonly List<string> Calls = [];
        public void SetHeritage(uint heritageId) { }
        public void SetDoll(WorldEntity? doll) { }
        public void Prepare() { }
        public void SetPose(PaperdollPose pose) => Calls.Add(FormattableString.Invariant($"pose {pose.Yaw:0.00} {pose.Zoom:0.00}"));
        public uint Render(int width, int height) { Calls.Add($"render {width}x{height}"); return 7u; }
    }

    private sealed class Factory : IPaperdollDollFactory
    {
        public bool TryBuild(uint heritageId, out WorldEntity? doll) { doll = null; return false; }
    }

    private sealed class Heritage : IPaperdollHeritageSource
    {
        public uint HeritageGroup => 1u;
    }

    [Fact]
    public void FirstVisibleView_GetsTheFrame()
    {
        var retail = new View { Visible = false };
        var deck = new View { Visible = true, W = 140, H = 214 };
        var views = new PaperdollFrameViews(retail, deck);

        Assert.True(views.TryGetVisibleSize(out int w, out int h));
        Assert.Equal((140, 214), (w, h));
        views.SetTextureHandle(9u);
        Assert.Empty(retail.Published);
        Assert.Equal([9u], deck.Published);

        retail.Visible = true;
        Assert.True(views.TryGetVisibleSize(out w, out _));
        Assert.Equal(10, w);
        views.SetTextureHandle(4u);
        Assert.Equal([4u], retail.Published);
    }

    [Fact]
    public void NoVisibleView_PublishesNothing_AndPoseIsDefault()
    {
        var a = new View { Pose = new PaperdollPose(1f, 2f) };
        var views = new PaperdollFrameViews(a);
        Assert.False(views.TryGetVisibleSize(out _, out _));
        views.SetTextureHandle(3u);
        Assert.Empty(a.Published);
        Assert.Equal(PaperdollPose.Default, views.Pose);
    }

    [Fact]
    public void Pose_FollowsTheChosenView_AndClearReachesEveryView()
    {
        var a = new View { Visible = false };
        var b = new View { Visible = true, Pose = new PaperdollPose(0.5f, 1.5f) };
        var views = new PaperdollFrameViews(a, b);
        views.TryGetVisibleSize(out _, out _);
        Assert.Equal(b.Pose, views.Pose);
        views.ClearTextureHandle();
        Assert.Equal((1, 1), (a.Cleared, b.Cleared));
    }

    [Fact]
    public void DeckPose_DoesNotLeakIntoTheRetailDoll()
    {
        var retail = new View { Visible = true };
        var deck = new View { Visible = true, Pose = new PaperdollPose(0.5f, 1.5f) };
        var views = new PaperdollFrameViews(retail, deck);
        Assert.True(views.TryGetVisibleSize(out _, out _));
        Assert.Equal(PaperdollPose.Default, views.Pose);

        var renderer = new Renderer();
        var presenter = new PaperdollFramePresenter(renderer, views, new Factory(), new Heritage());
        presenter.Render();
        Assert.Equal("pose 0.00 1.00", renderer.Calls[0]);
    }

    [Fact]
    public void Presenter_AppliesTheViewPose_BeforeRendering()
    {
        var renderer = new Renderer();
        var view = new View { Visible = true, W = 140, H = 214, Pose = new PaperdollPose(0.5f, 1.5f) };
        var presenter = new PaperdollFramePresenter(renderer, view, new Factory(), new Heritage());
        presenter.Render();
        Assert.Equal(["pose 0.50 1.50", "render 140x214"], renderer.Calls);
    }
}
