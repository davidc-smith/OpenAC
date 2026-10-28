namespace AcDream.App.UI.Hud;

/// <summary>The pinned deck's background fading out when nothing has happened for a while. Text never fades.</summary>
internal sealed class ChatDeckIdleFade
{
    public const double FadeSeconds = 0.4;
    private double _idle;

    /// <summary>1 = the background as set; 0 = faded out.</summary>
    public float Factor { get; private set; } = 1f;

    public void Tick(double deltaSeconds, bool active, bool enabled, int afterSeconds)
    {
        if (!enabled || active)
        {
            Wake();
            return;
        }
        _idle += deltaSeconds;
        double over = _idle - afterSeconds;
        Factor = over <= 0 ? 1f : (float)Math.Max(0, 1 - over / FadeSeconds);
    }

    public void Wake()
    {
        _idle = 0;
        Factor = 1f;
    }
}
