using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class VitalTrailTests
{
    [Fact]
    public void FirstSample_SeedsWithoutAnimation()
    {
        var trail = new VitalTrail();
        Assert.Equal(0.6f, trail.Update(0.6f, now: 10.0));
    }

    [Fact]
    public void Drop_HoldsThenShrinks()
    {
        var trail = new VitalTrail();
        trail.Update(1.0f, 0.0);

        Assert.Equal(1.0f, trail.Update(0.5f, 1.0));
        Assert.Equal(1.0f, trail.Update(0.5f, 1.29));
        Assert.Equal(0.75f, trail.Update(0.5f, 1.6), 3);
        Assert.Equal(0.5f, trail.Update(0.5f, 1.9));
        Assert.Equal(0.5f, trail.Update(0.5f, 5.0));
    }

    [Fact]
    public void DropDuringTrail_KeepsDrawnEdgeAndRestartsHold()
    {
        var trail = new VitalTrail();
        trail.Update(1.0f, 0.0);
        trail.Update(0.6f, 1.0);                       // trail held at 1.0 until 1.3
        float drawn = trail.Update(0.6f, 1.6);         // halfway: 0.8
        Assert.Equal(0.8f, drawn, 3);

        Assert.Equal(0.8f, trail.Update(0.4f, 1.6), 3);
        Assert.Equal(0.8f, trail.Update(0.4f, 1.85), 3); // held again
        Assert.Equal(0.4f, trail.Update(0.4f, 2.6));
    }

    [Fact]
    public void Rise_HasNoTrail()
    {
        var trail = new VitalTrail();
        trail.Update(0.5f, 0.0);
        Assert.Equal(0.7f, trail.Update(0.7f, 1.0));
    }

    [Fact]
    public void RiseAboveTrail_CutsIt()
    {
        var trail = new VitalTrail();
        trail.Update(0.8f, 0.0);
        trail.Update(0.4f, 1.0);                       // trail held at 0.8
        Assert.Equal(0.9f, trail.Update(0.9f, 1.1));
        Assert.Equal(0.9f, trail.Update(0.9f, 1.5));
    }

    [Fact]
    public void Null_ClearsAndNextValueSeeds()
    {
        var trail = new VitalTrail();
        trail.Update(1.0f, 0.0);
        trail.Update(0.5f, 1.0);
        Assert.Equal(0f, trail.Update(null, 1.1));
        Assert.Equal(0.3f, trail.Update(0.3f, 1.2));
    }

    [Fact]
    public void Reset_Seeds()
    {
        var trail = new VitalTrail();
        trail.Update(1.0f, 0.0);
        trail.Reset();
        Assert.Equal(0.2f, trail.Update(0.2f, 0.1));
    }
}
