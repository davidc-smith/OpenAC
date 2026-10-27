using AcDream.App.Configuration;

namespace AcDream.App.Tests.Configuration;

public sealed class SessionConfigArgumentParsingTests
{
    private const string Flag = "--session-config";

    /// <summary>
    /// The command lines the launcher starts a play session with, exactly as
    /// it sends them (its own test pins the same lines): a graphical row, and
    /// a headless row that starts with no window. The password comes as the
    /// first line on standard input, not on the command line.
    /// </summary>
    [Theory]
    [InlineData(new[] { "--session-config", "session.json", "--console" }, true, false)]
    [InlineData(new[] { "--session-config", "session.json", "--console", "--no-window" }, true, true)]
    [InlineData(new[] { "--session-config", "session.json" }, false, false)]
    public void TheLaunchersCommandLinesAreAccepted(string[] arguments, bool console, bool noWindow)
    {
        string? path = SessionConfigArgumentParsing.ExtractFlagValue(
            arguments, Flag, out bool present);
        string[] positional = SessionConfigArgumentParsing.WithoutFlagAndValue(arguments, Flag);

        SessionConfigArgumentParsing.ConsoleFlags? flags =
            SessionConfigArgumentParsing.ExtractConsoleFlags(
                positional, present, out string[] remaining, out string? error);

        Assert.Equal("session.json", path);
        Assert.Null(error);
        Assert.Empty(remaining);
        Assert.Equal(new SessionConfigArgumentParsing.ConsoleFlags(console, noWindow), flags);
    }

    [Theory]
    [InlineData("--session-config s.json --no-window")]
    [InlineData("--console")]
    [InlineData("--session-config s.json --console --console")]
    public void ConsoleFlagsOutsideALauncherStartAreUsageErrors(string commandLine)
    {
        string[] arguments = commandLine.Split(' ');
        _ =SessionConfigArgumentParsing.ExtractFlagValue(arguments, Flag, out bool present);
        string[] positional = SessionConfigArgumentParsing.WithoutFlagAndValue(arguments, Flag);

        Assert.Null(SessionConfigArgumentParsing.ExtractConsoleFlags(
            positional, present, out _, out string? error));
        Assert.NotNull(error);
    }

    [Fact]
    public void FlagWithAFollowingValueReturnsThatValueAndIsPresent()
    {
        string? value = SessionConfigArgumentParsing.ExtractFlagValue(
            ["D:\\dats", Flag, "session.json"],
            Flag,
            out bool present);

        Assert.True(present);
        Assert.Equal("session.json", value);
    }

    [Fact]
    public void FlagAbsentReturnsNullAndIsNotPresent()
    {
        string? value = SessionConfigArgumentParsing.ExtractFlagValue(
            ["D:\\dats"],
            Flag,
            out bool present);

        Assert.False(present);
        Assert.Null(value);
    }

    [Fact]
    public void TrailingFlagWithNoValueIsPresentWithANullValue()
    {
        string? value = SessionConfigArgumentParsing.ExtractFlagValue(
            ["D:\\dats", Flag],
            Flag,
            out bool present);

        Assert.True(present);
        Assert.Null(value);
    }

    [Fact]
    public void FlagAloneAsTheOnlyArgumentIsPresentWithANullValue()
    {
        string? value = SessionConfigArgumentParsing.ExtractFlagValue(
            [Flag],
            Flag,
            out bool present);

        Assert.True(present);
        Assert.Null(value);
    }

    [Fact]
    public void WithoutFlagAndValueDropsTheFlagAndItsValue()
    {
        string[] positional = SessionConfigArgumentParsing.WithoutFlagAndValue(
            ["D:\\dats", Flag, "session.json", "extra"],
            Flag);

        Assert.Equal(["D:\\dats", "extra"], positional);
    }

    [Fact]
    public void WithoutFlagAndValueTrailingFlagDropsOnlyTheFlagItself()
    {
        string[] positional = SessionConfigArgumentParsing.WithoutFlagAndValue(
            ["D:\\dats", Flag],
            Flag);

        Assert.Equal(["D:\\dats"], positional);
    }

    [Fact]
    public void WithoutFlagAndValueLeavesArgumentsUnchangedWhenFlagIsAbsent()
    {
        string[] positional = SessionConfigArgumentParsing.WithoutFlagAndValue(
            ["D:\\dats"],
            Flag);

        Assert.Equal(["D:\\dats"], positional);
    }
}
