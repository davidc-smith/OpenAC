using AcDream.Core.Audio;

namespace AcDream.App.UI.Hud;

/// <summary>The sound an alert line makes, at most once every <see cref="MinGapSeconds"/>.</summary>
internal sealed class ChatDeckAlertBeep(Action play)
{
    public const SoundId Sound = SoundId.UI_TransientMessage;
    public const double MinGapSeconds = 2.0;
    private double _sincePlayed = MinGapSeconds;

    public void Tick(double deltaSeconds) => _sincePlayed = Math.Min(MinGapSeconds, _sincePlayed + deltaSeconds);

    public void Lines(bool hasAlert, bool enabled)
    {
        if (!hasAlert || !enabled || _sincePlayed < MinGapSeconds) return;
        play();
        _sincePlayed = 0;
    }
}
