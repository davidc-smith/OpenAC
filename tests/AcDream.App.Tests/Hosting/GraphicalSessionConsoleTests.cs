using AcDream.App.Hosting;

namespace AcDream.App.Tests.Hosting;

/// <summary>
/// The client console's verbs, as the launcher sends them and as the Rust
/// client reads them: whole words, any case; every other line is chat.
/// </summary>
public sealed class GraphicalSessionConsoleTests
{
    [Theory]
    [InlineData("/window", "Window")]
    [InlineData("/WINDOW s1", "Window")]
    [InlineData("/window other", "NoSuchSession")]
    [InlineData("/close", "Close")]
    [InlineData("  /Quit  ", "Quit")]
    [InlineData("", "None")]
    [InlineData("hello there", "Chat")]
    [InlineData("/closet", "Chat")]
    [InlineData("/quit now", "Chat")]
    [InlineData("@loc", "Chat")]
    public void A_typed_line_reads_as_one_verb(string line, string verb) =>
        Assert.Equal(
            Enum.Parse<GraphicalConsoleVerb>(verb),
            GraphicalSessionConsole.Parse(line, "s1").Verb);

    [Fact]
    public void A_chat_line_goes_on_unchanged_but_trimmed() =>
        Assert.Equal(
            "/tell Bob hi",
            GraphicalSessionConsole.Parse("  /tell Bob hi ", "s1").Argument);

    [Theory]
    [InlineData("Requested", "requested")]
    [InlineData("ClosedByUser", "closedByUser")]
    [InlineData("RenderFailed", "renderFailed")]
    [InlineData("Shutdown", "shutdown")]
    public void A_closed_window_is_reported_in_the_status_files_words(
        string reason,
        string words) =>
        Assert.Equal(
            words,
            GraphicalSessionConsole.StatusReason(Enum.Parse<WindowDetachReason>(reason)));
}
