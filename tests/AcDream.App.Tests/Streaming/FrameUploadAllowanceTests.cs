using AcDream.App.Rendering.Wb;
using AcDream.App.Streaming;

namespace AcDream.App.Tests.Streaming;

public sealed class FrameUploadAllowanceTests
{
    private const long MiB = 1024L * 1024L;

    [Fact]
    public void OrdinaryMeshLimits_AreTheDefaultFrameBudgetsUploadBytes()
    {
        MeshUploadBudgetLimits limits = WbMeshAdapter.LimitsFor(
            ProfileFrameUploadAllowanceSource.Default.Ordinary);

        Assert.Equal(8, limits.MaximumObjects);
        Assert.Equal(8 * MiB, limits.MaximumSourceBytes);
        Assert.Equal(8 * MiB, limits.MaximumArrayAllocationBytes);
        Assert.Equal(8 * MiB, limits.MaximumMipmapBytes);
        Assert.Equal(8 * MiB, limits.MaximumBufferUploadBytes);
        Assert.Equal(1, limits.MaximumNewArrays);
    }

    [Fact]
    public void RevealMeshLimits_WidenEveryDimensionWithTheFrameBudget()
    {
        // The default budget is 2 ms; a reveal widens it to the 4 ms hold
        // ceiling, twice, and every upload limit with it.
        MeshUploadBudgetLimits limits = WbMeshAdapter.LimitsFor(
            ProfileFrameUploadAllowanceSource.Default.Reveal);

        Assert.Equal(16, limits.MaximumObjects);
        Assert.Equal(16 * MiB, limits.MaximumSourceBytes);
        Assert.Equal(16 * MiB, limits.MaximumArrayAllocationBytes);
        Assert.Equal(16 * MiB, limits.MaximumMipmapBytes);
        Assert.Equal(16 * MiB, limits.MaximumBufferUploadBytes);
        Assert.Equal(2, limits.MaximumNewArrays);
    }

    [Fact]
    public void MeshUploadBytes_FollowTheStreamingBudgetsGpuBytes()
    {
        var profile = StreamingWorkBudgetOptions.Default with
        {
            MaxGpuUploadBytes = 24 * MiB,
        };

        MeshUploadBudgetLimits limits = WbMeshAdapter.LimitsFor(
            new ProfileFrameUploadAllowanceSource(profile).Ordinary);

        Assert.Equal(24 * MiB, limits.MaximumSourceBytes);
        Assert.Equal(24 * MiB, limits.MaximumMipmapBytes);
        Assert.Equal(8, limits.MaximumObjects);
    }

    [Fact]
    public void StreamingController_PublishesTheBudgetItRunsUnder()
    {
        StreamingController streaming = Controller();
        IFrameUploadAllowanceSource source = streaming;

        Assert.Equal(new FrameUploadAllowance(8 * MiB, 1.0), source.Ordinary);
        Assert.Equal(new FrameUploadAllowance(16 * MiB, 2.0), source.Reveal);
        Assert.Equal(
            streaming.RevealWorkBudget.MaxGpuUploadBytes,
            source.Reveal.GpuUploadBytes);
    }

    [Fact]
    public void StreamingController_QualityScaledBudgetScalesTheUploads()
    {
        // Eight completions a frame is twice the default profile's work: a
        // 4 ms frame, already the hold ceiling, so a reveal widens nothing.
        StreamingController streaming = Controller();
        streaming.MaxCompletionsPerFrame = 8;
        IFrameUploadAllowanceSource source = streaming;

        Assert.Equal(16 * MiB, source.Ordinary.GpuUploadBytes);
        Assert.Equal(1.0, source.Ordinary.Scale);
        Assert.Equal(16 * MiB, source.Reveal.GpuUploadBytes);
        Assert.Equal(1.0, source.Reveal.Scale);
    }

    [Fact]
    public void Allowance_ScalesCountsAndBytesOnlyWhenWidened()
    {
        var ordinary = new FrameUploadAllowance(8 * MiB, 1.0);
        var widened = new FrameUploadAllowance(32 * MiB, 4.0);

        Assert.Equal(16, ordinary.ScaleCount(16));
        Assert.Equal(64, widened.ScaleCount(16));
        Assert.Equal(8 * MiB, ordinary.ScaleBytes(8 * MiB));
        Assert.Equal(32 * MiB, widened.ScaleBytes(8 * MiB));
    }

    private static StreamingController Controller() =>
        new(
            enqueueLoad: static (_, _) => { },
            enqueueUnload: static _ => { },
            drainCompletions: static _ => Array.Empty<LandblockStreamResult>(),
            applyTerrain: static (_, _) => { },
            new GpuWorldState(),
            nearRadius: 0,
            farRadius: 0);
}
