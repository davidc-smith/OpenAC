using System.Buffers.Binary;
using AcDream.App.Hosting;
using AcDream.App.Rendering;
using AcDream.Core.Net;
using AcDream.Core.Net.Messages;
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

    /// <summary>
    /// A confirmation the server asks for with no window is told to plugins
    /// and answered by them, with the server's context id; an answer to any
    /// other id, or a second answer, goes nowhere. The windowless client's
    /// equivalent: <c>HeadlessSessionHostTests</c> and
    /// <c>HeadlessPluginApiSurfaceTests</c> (a plugin's answer reaches
    /// <c>HeadlessSessionHost.RespondToConfirmation</c>); Rust
    /// <c>dialog_answer</c> answers the server's open question by context.
    /// </summary>
    [Fact]
    public void A_confirmation_with_no_window_is_told_to_plugins_and_their_answer_goes_to_the_server()
    {
        using var session = new WindowlessSessionFixture();
        session.EnterWorld();
        var told = new List<PluginConfirmation>();
        session.Events.ConfirmationRequested += told.Add;
        var sent = new List<byte[]>();
        WorldSession connection = session.Connections.Sessions[^1];
        connection.GameActionCapture = sent.Add;
        IDialogAutomation dialogs = ((IAutomationSurface)session.Automation).Dialogs;

        connection.GameEvents.Dispatch(
            GameEventEnvelope.TryParse(ConfirmationRequest(5u, 77u, "continue?"))!.Value);

        PluginConfirmation confirmation = Assert.Single(told);
        Assert.Equal(77u, confirmation.ContextId);
        Assert.Equal(5, confirmation.Type);
        Assert.False(dialogs.Answer(78u, true));
        Assert.Empty(sent);

        Assert.True(dialogs.Answer(77u, true));

        byte[] response = Assert.Single(sent);
        Assert.Equal(
            ClientCommandRequests.ConfirmationResponseOpcode,
            BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(8)));
        Assert.Equal(5u, BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(12)));
        Assert.Equal(77u, BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(16)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(20)));
        Assert.False(dialogs.Answer(77u, true));
        Assert.Single(sent);
    }

    /// <summary>
    /// A plugin's logout with no window logs the character off: one log-off
    /// request goes to the server, no second one while it is under way, and
    /// once the server confirms it the character is back at character
    /// selection, as with a window (and in the Rust client); the connection
    /// stays. Port of
    /// <c>HeadlessSessionHostTests.PluginDrivenLogoutRoutesThroughTheHostsOwnStopAndClearsTheSession</c>
    /// and <c>RequestLogoutSendsOneLogoffAndCanRequestLogoutGoesFalseWhilePending</c>.
    /// </summary>
    [Fact]
    public void A_plugin_logout_with_no_window_logs_the_character_off()
    {
        using var session = new WindowlessSessionFixture();
        session.EnterWorld();
        ILoginAutomation login = ((IAutomationSurface)session.Automation).Login;

        Assert.True(login.CanRequestLogout);
        Assert.True(login.Logout());

        Assert.Equal(1, session.Connections.LogOffRequests);
        Assert.True(session.Host.Runtime.TransitOwner.IsLogoutActive);
        Assert.False(login.CanRequestLogout);
        Assert.False(login.RequestLogout());
        Assert.Equal(1, session.Connections.LogOffRequests);

        // Nothing happens until the server confirms it.
        Assert.False(session.Update());
        Assert.True(session.Host.Runtime.Session.IsInWorld);

        session.Connections.Sessions[^1].ConfirmCharacterLogOffForTesting();
        Assert.False(session.Update());

        Assert.Equal(1, session.Connections.ReturnsToCharacterSelection);
        Assert.False(session.Host.Runtime.Session.IsInWorld);
        Assert.False(session.Host.Runtime.TransitOwner.IsLogoutActive);
        Assert.NotNull(session.Host.LiveSession.Host.CurrentSession);
        Assert.False(login.CanRequestLogout);

        // Back in the world on a new login (as after a lost server), a second
        // log off runs to its end as the first did.
        var commands = session.Host.CommandsWithoutWindow.Session;
        commands.Stop(session.Host.Runtime.Generation);
        commands.Start(session.Host.Runtime.Generation);
        for (int frame = 0; frame < 20 && !session.Host.Runtime.Session.IsInWorld; frame++)
            Assert.False(session.Update());
        Assert.True(session.Host.Runtime.Session.IsInWorld);
        Assert.True(login.RequestLogout());
        session.Connections.Sessions[^1].ConfirmCharacterLogOffForTesting();
        Assert.False(session.Update());

        Assert.Equal(2, session.Connections.ReturnsToCharacterSelection);
        Assert.False(session.Host.Runtime.Session.IsInWorld);
        Assert.False(session.Host.Runtime.TransitOwner.IsLogoutActive);
    }

    /// <summary>
    /// The server settling a question on its own leaves nothing to answer.
    /// Port of
    /// <c>HeadlessSessionHostTests.AConfirmationDoneOnTheWireClearsThePendingConfirmationThroughProductionRouting</c>.
    /// </summary>
    [Fact]
    public void A_confirmation_the_server_settles_with_no_window_can_no_longer_be_answered()
    {
        using var session = new WindowlessSessionFixture();
        session.EnterWorld();
        var sent = new List<byte[]>();
        WorldSession connection = session.Connections.Sessions[^1];
        connection.GameActionCapture = sent.Add;
        IDialogAutomation dialogs = ((IAutomationSurface)session.Automation).Dialogs;
        connection.GameEvents.Dispatch(
            GameEventEnvelope.TryParse(ConfirmationRequest(5u, 77u, "continue?"))!.Value);
        Assert.Equal(77u, session.Host.Confirmations.Pending?.ContextId);

        // A done for another question leaves this one waiting.
        connection.GameEvents.Dispatch(
            GameEventEnvelope.TryParse(ConfirmationDone(5u, 78u))!.Value);
        Assert.Equal(77u, session.Host.Confirmations.Pending?.ContextId);

        connection.GameEvents.Dispatch(
            GameEventEnvelope.TryParse(ConfirmationDone(5u, 77u))!.Value);

        Assert.Null(session.Host.Confirmations.Pending);
        Assert.False(dialogs.Answer(77u, true));
        Assert.Empty(sent);
    }

    /// <summary>
    /// The character options a session document declares are put on the
    /// character with no window, once login complete is out and the server
    /// has said what the options are, and only where they differ. Port of
    /// <c>HeadlessCharacterOptionsSeederWiringTests.DeclaredOptionSendsOnceBothLoginCompleteAndTheRealPlayerDescriptionEventLand</c>.
    /// </summary>
    [Fact]
    public void Declared_options_are_put_on_the_character_with_no_window()
    {
        using var session = new WindowlessSessionFixture(
            declaredOptions: new Dictionary<CharacterOptionId, bool>
            {
                [CharacterOptionId.IgnoreAllegianceRequests] = true,
            });
        session.EnterWorld();
        var sent = new List<byte[]>();
        WorldSession connection = session.Connections.Sessions[^1];
        connection.GameActionCapture = sent.Add;
        Assert.NotNull(session.Host.OptionsSeeder);

        // Login complete first (a legitimate order); with no options from
        // the server yet nothing can be compared.
        session.Host.OptionsSeeder!.NoteLoginCompleteSent();
        Assert.Empty(sent);

        connection.GameEvents.Dispatch(
            GameEventEnvelope.TryParse(PlayerDescription(options1: 0u, options2: 0u))!.Value);

        byte[] action = Assert.Single(sent);
        Assert.Equal(
            SocialActions.SetSingleCharacterOptionOpcode,
            BinaryPrimitives.ReadUInt32LittleEndian(action.AsSpan(8)));
        Assert.Equal(
            (uint)CharacterOptionId.IgnoreAllegianceRequests,
            BinaryPrimitives.ReadUInt32LittleEndian(action.AsSpan(12)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(action.AsSpan(16)));
    }

    /// <summary>
    /// A server that already has the declared options is sent nothing. Port
    /// of <c>HeadlessCharacterOptionsSeederWiringTests.ReconnectAgainstAServerThatNowAgreesSendsNothing</c>.
    /// </summary>
    [Fact]
    public void Declared_options_the_character_already_has_send_nothing_with_no_window()
    {
        using var session = new WindowlessSessionFixture(
            declaredOptions: new Dictionary<CharacterOptionId, bool>
            {
                [CharacterOptionId.IgnoreAllegianceRequests] = true,
            });
        session.EnterWorld();
        var sent = new List<byte[]>();
        WorldSession connection = session.Connections.Sessions[^1];
        connection.GameActionCapture = sent.Add;

        session.Host.OptionsSeeder!.NoteLoginCompleteSent();
        connection.GameEvents.Dispatch(
            GameEventEnvelope.TryParse(
                PlayerDescription(options1: 0x00000004u, options2: 0u))!.Value);

        Assert.True(session.Host.Runtime.CharacterOwner.Options.HasServerSeed);
        Assert.Empty(sent);
    }

    /// <summary>
    /// A session with no window whose server falls silent logs in again
    /// every 30 s until an attempt gets back into the world, instead of
    /// ending; none is made early. Port of
    /// <c>HeadlessProcessSchedulerTests.LostServer_IsRetriedEvery30SecondsUntilALoginGetsBackIn</c>
    /// and Rust <c>relogin.rs</c>
    /// <c>a_lost_server_is_retried_every_30_seconds_until_a_login_gets_back_in</c>.
    /// </summary>
    [Fact]
    public void A_server_falling_silent_with_no_window_is_logged_into_again_every_30_seconds()
    {
        var time = new WindowlessSessionFixture.ManualTime();
        using var session = new WindowlessSessionFixture(time: time);
        session.EnterWorld();
        TimeSpan retry = TimeSpan.FromSeconds(30);
        Assert.Equal(retry, GraphicalSessionHost.LostConnectionRetryInterval);
        Assert.False(session.Update());

        // The server goes away: the session finds it lost, ends the stay and
        // waits to log in again instead of ending.
        session.Connections.ServerDown = true;
        Assert.False(session.Update());
        Assert.False(session.Host.Runtime.Session.IsInWorld);
        Assert.True(session.Host.IsRecoveringLostConnection);
        Assert.Single(session.Connections.Sessions);
        Assert.Equal("disconnected", session.StatusEvents()[^1]);

        // Every attempt against the gone server fails and the next waits
        // another 30 s; none is made a moment early.
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            time.Advance(retry - TimeSpan.FromTicks(1));
            Assert.False(session.Update());
            Assert.Equal(attempt, session.Connections.Sessions.Count);

            time.Advance(TimeSpan.FromTicks(1));
            Assert.False(session.Update());
            Assert.Equal(attempt + 1, session.Connections.Sessions.Count);
            Assert.True(session.Host.IsRecoveringLostConnection);
            Assert.False(session.Host.Runtime.Session.IsInWorld);
        }

        // The server is back: the next attempt logs in and play resumes.
        session.Connections.ServerDown = false;
        time.Advance(retry);
        for (int frame = 0; frame < 5 && !session.Host.Runtime.Session.IsInWorld; frame++)
            Assert.False(session.Update());
        Assert.Equal(5, session.Connections.Sessions.Count);
        Assert.True(session.Host.Runtime.Session.IsInWorld);
        Assert.False(session.Host.IsRecoveringLostConnection);
        Assert.False(session.Update());
        Assert.Equal(
            ["enteredWorld", "disconnected", "enteredWorld"],
            session.StatusEvents()
                .Where(e => e is "enteredWorld" or "disconnected")
                .ToArray());

        Assert.Equal(GraphicalSessionEnd.Graceful, session.End());
    }

    /// <summary>
    /// An account that no longer has the character to enter ends the
    /// recovery, and the session ends with the connection's exit code. Port
    /// of Rust <c>runner_relogin_tests.rs</c>
    /// <c>a_failed_attempt_waits_another_interval_and_a_missing_character_ends_the_recovery</c>
    /// (its second half; the first is the test above).
    /// </summary>
    [Fact]
    public void Logging_in_again_to_an_account_without_the_character_ends_the_session()
    {
        var time = new WindowlessSessionFixture.ManualTime();
        using var session = new WindowlessSessionFixture(time: time);
        session.EnterWorld();
        session.Connections.ServerDown = true;
        Assert.False(session.Update());
        Assert.True(session.Host.IsRecoveringLostConnection);

        session.Connections.ServerDown = false;
        session.Connections.Roster.Clear();
        session.Connections.Roster.Add(
            new CharacterList.Character(0x50000002u, "+Someone", 0u));
        time.Advance(GraphicalSessionHost.LostConnectionRetryInterval);
        bool ended = false;
        for (int frame = 0; frame < 5 && !ended; frame++)
            ended = session.Update();

        Assert.True(ended);
        Assert.Equal(2, session.Connections.Sessions.Count);
        Assert.Equal(GraphicalSessionEnd.ConnectionLost, session.End(connectionEnded: true));
        // The loss is said once, when it happens; the attempt that found no
        // character closes its connection with why, as Rust's status file
        // does.
        Assert.Equal(["connection-lost", NoWindowlessCharacter], DisconnectedReasons(session));
        AssertExited(session, 5, NoWindowlessCharacter);
    }

    /// <summary>The end Rust's session reports when no character matches the document's.</summary>
    private const string NoWindowlessCharacter =
        "no available character matches Name(\"+Windowless\")";

    /// <summary>
    /// A first start that connects to an account without the document's
    /// character ends the session with the connection's exit code, saying
    /// why in Rust's words; before, it waited forever. Port of Rust
    /// <c>runner_relogin_tests.rs</c>
    /// <c>a_failed_attempt_waits_another_interval_and_a_missing_character_ends_the_recovery</c>
    /// (its end, for the first login) and of the headless client's exit 5.
    /// </summary>
    [Fact]
    public void A_first_start_on_an_account_without_the_character_ends_the_session()
    {
        using var session = new WindowlessSessionFixture();
        session.Connections.Roster.Clear();
        session.Connections.Roster.Add(
            new CharacterList.Character(0x50000002u, "+Someone", 0u));
        session.Host.ApplyAutomationWithoutWindow();
        session.Host.StartSession();
        bool ended = false;
        for (int frame = 0; frame < 20 && !ended; frame++)
            ended = session.Update();

        Assert.True(ended, "the session ended");
        Assert.Equal(NoWindowlessCharacter, session.Host.NoCharacterToEnter);
        Assert.Single(session.Connections.Sessions);
        Assert.Equal(GraphicalSessionEnd.ConnectionLost, session.End(connectionEnded: true));
        Assert.Equal([NoWindowlessCharacter], DisconnectedReasons(session));
        AssertExited(session, 5, NoWindowlessCharacter);
    }

    /// <summary>
    /// The same start with a window: the window closes (the loop asks for it
    /// when <see cref="GraphicalSessionHost.NoCharacterToEnter"/> is set) and
    /// its end reports the connection's exit code, not a graceful one.
    /// </summary>
    [Fact]
    public void A_window_whose_start_finds_no_character_ends_with_the_connection_exit_code()
    {
        using var session = new WindowlessSessionFixture();
        session.Connections.Roster.Clear();
        session.Host.ApplyAutomationWithoutWindow();
        session.Host.StartSession();
        for (int frame = 0; frame < 20 && session.Host.NoCharacterToEnter is null; frame++)
            session.Tick();
        const string why = "no available character matches Name(\"+Windowless\")";
        Assert.Equal(why, session.Host.NoCharacterToEnter);

        GraphicalSessionEnd end = session.Host.ReportExited(
            new GameWindowLifetimeReport(GameWindowLifetimeStatus.Complete, null, [], null),
            crashed: false);

        Assert.Equal(GraphicalSessionEnd.ConnectionLost, end);
        Assert.Equal([why], DisconnectedReasons(session));
        AssertExited(session, 5, why);
    }

    private static string[] DisconnectedReasons(WindowlessSessionFixture session) =>
        [.. session.StatusLines()
            .Where(line => line.Contains("\"e\":\"disconnected\"", StringComparison.Ordinal))
            .Select(line => System.Text.Json.JsonDocument.Parse(line)
                .RootElement.GetProperty("reason").GetString()!)];

    private static void AssertExited(WindowlessSessionFixture session, int code, string reason)
    {
        System.Text.Json.JsonElement exited = System.Text.Json.JsonDocument
            .Parse(session.StatusLines()[^1])
            .RootElement;
        Assert.Equal("exited", exited.GetProperty("e").GetString());
        Assert.Equal(code, exited.GetProperty("code").GetInt32());
        Assert.Equal(reason, exited.GetProperty("reason").GetString());
    }

    private static byte[] PlayerDescription(uint options1, uint options2)
    {
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(0u);       // property flags
            writer.Write(0x52u);    // player weenie type
            writer.Write(0u);       // vector flags
            writer.Write(0u);       // has health
            writer.Write(0x40u);
            writer.Write(options1);
            writer.Write(0u);
            writer.Write(0u);       // spellbook filters
            writer.Write(options2);
            writer.Write(0u);
            writer.Write(0u);
        }
        return GameEvent(GameEventType.PlayerDescription, stream.ToArray());
    }

    private static byte[] ConfirmationRequest(uint type, uint contextId, string message)
    {
        var payload = new List<byte>();
        payload.AddRange(BitConverter.GetBytes(type));
        payload.AddRange(BitConverter.GetBytes(contextId));
        byte[] text = System.Text.Encoding.ASCII.GetBytes(message);
        payload.AddRange(BitConverter.GetBytes((ushort)text.Length));
        payload.AddRange(text);
        while (payload.Count % 4 != 0)
            payload.Add(0);
        return GameEvent(GameEventType.CharacterConfirmationRequest, [.. payload]);
    }

    private static byte[] ConfirmationDone(uint type, uint contextId)
    {
        byte[] payload = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, type);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), contextId);
        return GameEvent(GameEventType.CharacterConfirmationDone, payload);
    }

    private static byte[] GameEvent(GameEventType type, byte[] payload)
    {
        byte[] body = new byte[GameEventEnvelope.HeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(body, GameEventEnvelope.Opcode);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(12), (uint)type);
        payload.CopyTo(body, GameEventEnvelope.HeaderSize);
        return body;
    }
}
