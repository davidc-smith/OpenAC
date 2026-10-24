namespace AcDream.App.Configuration;

internal static class SessionConfigArgumentParsing
{
    internal static string? ExtractFlagValue(
        string[] arguments,
        string flag,
        out bool present)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(flag);

        for (int i = 0; i < arguments.Length; i++)
        {
            if (!string.Equals(arguments[i], flag, StringComparison.Ordinal))
                continue;

            present = true;
            return i == arguments.Length - 1 ? null : arguments[i + 1];
        }

        present = false;
        return null;
    }

    /// <summary>The console flags of a launch.</summary>
    /// <param name="Console">
    /// Typed lines are read from standard input (after the credential line)
    /// and chat is printed on standard error.
    /// </param>
    /// <param name="NoWindow">The session starts with no window; <c>/window</c> opens one.</param>
    internal readonly record struct ConsoleFlags(bool Console, bool NoWindow);

    /// <summary>
    /// Takes <c>--console</c> and <c>--no-window</c> off the arguments. Both
    /// belong to a session-config launch, and a session with no window can
    /// only be reached through its console, so <c>--no-window</c> needs
    /// <c>--console</c>. Null with a usage error otherwise.
    /// </summary>
    internal static ConsoleFlags? ExtractConsoleFlags(
        string[] arguments,
        bool sessionConfigPresent,
        out string[] remaining,
        out string? usageError)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        int console = arguments.Count(argument =>
            string.Equals(argument, "--console", StringComparison.Ordinal));
        int noWindow = arguments.Count(argument =>
            string.Equals(argument, "--no-window", StringComparison.Ordinal));
        remaining = [.. arguments.Where(argument =>
            !string.Equals(argument, "--console", StringComparison.Ordinal)
            && !string.Equals(argument, "--no-window", StringComparison.Ordinal))];
        usageError = null;
        if (console > 1 || noWindow > 1)
            usageError = "--console and --no-window may each be given once.";
        else if ((console == 1 || noWindow == 1) && !sessionConfigPresent)
            usageError = "--console and --no-window go with --session-config <file>.";
        else if (noWindow == 1 && console == 0)
            usageError = "--no-window needs --console: a session with no window is reached through its console.";
        return usageError is null
            ? new ConsoleFlags(console == 1, noWindow == 1)
            : null;
    }

    internal static string[] WithoutFlagAndValue(string[] arguments, string flag)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(flag);

        var result = new List<string>(arguments.Length);
        for (int i = 0; i < arguments.Length; i++)
        {
            if (string.Equals(arguments[i], flag, StringComparison.Ordinal))
            {
                i++; // also skip the flag's value, if any
                continue;
            }
            result.Add(arguments[i]);
        }
        return [.. result];
    }
}
