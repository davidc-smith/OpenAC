using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckAlertBeepTests
{
    [Fact]
    public void Alert_PlaysOnce_ThenWaitsTwoSeconds()
    {
        int played = 0;
        var beep = new ChatDeckAlertBeep(() => played++);
        beep.Lines(hasAlert: true, enabled: true);
        beep.Lines(hasAlert: true, enabled: true);
        Assert.Equal(1, played);
        beep.Tick(2.0);
        beep.Lines(hasAlert: true, enabled: true);
        Assert.Equal(2, played);
    }

    [Fact]
    public void NoAlertOrDisabled_PlaysNothing()
    {
        int played = 0;
        var beep = new ChatDeckAlertBeep(() => played++);
        beep.Lines(hasAlert: false, enabled: true);
        beep.Lines(hasAlert: true, enabled: false);
        Assert.Equal(0, played);
    }
}
