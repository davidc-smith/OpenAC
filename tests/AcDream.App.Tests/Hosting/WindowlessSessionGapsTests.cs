using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Navigation;

namespace AcDream.App.Tests.Hosting;

/// <summary>
/// A graphical client's session with no window does what it does with one,
/// and what the windowless client does: walks, confirmations, the declared
/// character options, logging in again after the server falls silent.
/// </summary>
public sealed class WindowlessSessionGapsTests
{
    /// <summary>
    /// A walk a plugin (or <c>/nav</c>) asks for with no window is taken by
    /// the session's walks, and the session's own frame steps it. The
    /// windowless client's equivalent:
    /// <c>HeadlessSessionNavigationTests.PluginsInAHeadlessSessionGetTheRuntimesNavigation</c>.
    /// </summary>
    [Fact]
    public void A_walk_asked_for_with_no_window_is_taken_and_stepped_by_the_sessions_frame()
    {
        using var session = new WindowlessSessionFixture();
        session.EnterWorld();
        INavigationAutomation navigation = session.Automation.Navigation;

        PluginNavigationCommandStatus status = navigation.GoTo(
            new PluginNavigationPosition(0xA9B40001u, 50d, 50d, 0d, 0f, true),
            2.5f);

        Assert.Equal(PluginNavigationCommandStatus.Accepted, status);
        Assert.Equal(PluginGoToState.Planning, navigation.GoToReport.State);

        session.Tick();

        // The stand-in server never placed the character's body, so the
        // walk, once stepped, ends at once; what matters is that it was
        // stepped with no window.
        NavigationWalkReport report = session.Host.Navigation.Walk.Report;
        Assert.Equal(NavigationWalkState.Lost, report.State);
        Assert.Equal("the character is not in the world", report.Reason);
    }
}
