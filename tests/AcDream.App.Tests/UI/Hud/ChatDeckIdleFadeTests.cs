using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckIdleFadeTests
{
    [Fact]
    public void FadesAfterTheDelay_OverFadeSeconds()
    {
        var fade = new ChatDeckIdleFade();
        fade.Tick(9.9, active: false, enabled: true, afterSeconds: 10);
        Assert.Equal(1f, fade.Factor);
        fade.Tick(0.3, active: false, enabled: true, afterSeconds: 10);   // 0.2 s into the fade
        Assert.Equal(0.5f, fade.Factor, 3);
        fade.Tick(1.0, active: false, enabled: true, afterSeconds: 10);
        Assert.Equal(0f, fade.Factor);
    }

    [Fact]
    public void ActivityWakeOrDisabled_RestoreAndRestart()
    {
        var fade = new ChatDeckIdleFade();
        fade.Tick(20, active: false, enabled: true, afterSeconds: 10);
        fade.Tick(0.016, active: true, enabled: true, afterSeconds: 10);
        Assert.Equal(1f, fade.Factor);
        fade.Tick(20, active: false, enabled: true, afterSeconds: 10);
        fade.Wake();
        Assert.Equal(1f, fade.Factor);
        fade.Tick(20, active: false, enabled: false, afterSeconds: 10);
        Assert.Equal(1f, fade.Factor);
    }
}
