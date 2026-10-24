using System.Buffers.Binary;
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
