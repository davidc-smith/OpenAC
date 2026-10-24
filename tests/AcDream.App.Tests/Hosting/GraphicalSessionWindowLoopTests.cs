using AcDream.App.Hosting;

namespace AcDream.App.Tests.Hosting;

/// <summary>
/// Which ends of a window log the character off. Ported from the Rust
/// client's session_config.rs tests
/// the_player_closing_a_launched_graphical_clients_window_logs_it_off and
/// the_session_logs_off_when_the_window_is_gone_for_good.
/// </summary>
public sealed class GraphicalSessionWindowLoopTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_player_closing_the_window_logs_off_with_or_without_the_console(bool console) =>
        Assert.True(GraphicalSessionWindowLoop.LogsOff(
            WindowDetachReason.ClosedByUser, attachFailed: false, console));

    [Fact]
    public void A_close_the_console_asked_for_plays_on()
    {
        Assert.False(GraphicalSessionWindowLoop.LogsOff(
            WindowDetachReason.Requested, attachFailed: false, console: true));
        Assert.False(GraphicalSessionWindowLoop.LogsOff(
            WindowDetachReason.Shutdown, attachFailed: false, console: true));
    }

    [Fact]
    public void A_lost_window_plays_on_only_with_the_console()
    {
        Assert.False(GraphicalSessionWindowLoop.LogsOff(
            WindowDetachReason.RenderFailed, attachFailed: false, console: true));
        Assert.True(GraphicalSessionWindowLoop.LogsOff(
            WindowDetachReason.RenderFailed, attachFailed: false, console: false));
        Assert.False(GraphicalSessionWindowLoop.LogsOff(
            closed: null, attachFailed: true, console: true));
        Assert.True(GraphicalSessionWindowLoop.LogsOff(
            closed: null, attachFailed: true, console: false));
    }
}
