namespace AcDream.App.Streaming;

/// <summary>
/// What a frame's render-thread uploads may spend, taken from the frame's
/// streaming work budget so that streaming and uploads run under one budget:
/// the budget's GPU upload bytes, and how far the budget is widened over the
/// ordinary one (1 ordinarily; more while a reveal withholds the world, when
/// nothing is drawn and the frame has the time).
/// </summary>
internal readonly record struct FrameUploadAllowance(
    long GpuUploadBytes,
    double Scale)
{
    /// <summary>The allowance of <paramref name="frame"/>, a budget that is
    /// <paramref name="ordinary"/> or a widening of it.</summary>
    public static FrameUploadAllowance Of(
        StreamingWorkBudget frame,
        StreamingWorkBudget ordinary) =>
        new(
            frame.MaxGpuUploadBytes,
            Math.Max(
                1.0,
                frame.MaxUpdateTime.Ticks
                    / (double)ordinary.MaxUpdateTime.Ticks));

    /// <summary>An ordinary per-frame count, widened by the allowance.</summary>
    public int ScaleCount(int ordinary) =>
        Scale <= 1.0
            ? ordinary
            : StreamingWorkBudgetOptions.Scale(ordinary, Scale);

    /// <summary>An ordinary per-frame byte limit, widened by the allowance.</summary>
    public long ScaleBytes(long ordinary) =>
        Scale <= 1.0
            ? ordinary
            : StreamingWorkBudgetOptions.Scale(ordinary, Scale);
}

/// <summary>The frame budget's upload allowance, ordinary and during a reveal.</summary>
internal interface IFrameUploadAllowanceSource
{
    FrameUploadAllowance Ordinary { get; }

    FrameUploadAllowance Reveal { get; }
}

/// <summary>The allowance of a fixed budget profile; used until the streaming
/// controller that owns the frame budget is bound, and by tools and tests
/// that run uploads without streaming.</summary>
internal sealed class ProfileFrameUploadAllowanceSource : IFrameUploadAllowanceSource
{
    public static ProfileFrameUploadAllowanceSource Default { get; } =
        new(StreamingWorkBudgetOptions.Default);

    public ProfileFrameUploadAllowanceSource(StreamingWorkBudgetOptions profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        StreamingWorkBudget ordinary = profile.ToBudget();
        Ordinary = FrameUploadAllowance.Of(ordinary, ordinary);
        Reveal = FrameUploadAllowance.Of(
            ordinary.WidenForDestinationHold(
                profile.HoldDestinationCeilingMilliseconds),
            ordinary);
    }

    public FrameUploadAllowance Ordinary { get; }

    public FrameUploadAllowance Reveal { get; }
}
