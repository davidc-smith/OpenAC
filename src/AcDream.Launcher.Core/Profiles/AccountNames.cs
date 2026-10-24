namespace AcDream.Launcher.Core.Profiles;

/// <summary>
/// Whether two account names are the same account. Servers match account names without regard to
/// case, so "Fefe" and "fefe" log in to one account: every launcher check of account identity goes
/// through here. Names themselves are kept and sent as typed; only the comparison ignores case.
/// An account is always identified by its server and its name; server names are compared exactly.
/// </summary>
public static class AccountNames
{
    /// <summary>The comparison for account names, for sets, dictionaries and grouping.</summary>
    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>The comparison for (server, account) keys: server exact, account through <see cref="Same"/>.</summary>
    public static IEqualityComparer<(string Server, string Account)> KeyComparer { get; } = new ServerAccountComparer();

    public static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The entry for <paramref name="name"/>: the one spelled exactly so, or else the first in order
    /// that is the same account. A file saved before names were compared this way can hold two
    /// spellings of one account; each still finds its own entry.
    /// </summary>
    public static T? Find<T>(IEnumerable<T> entries, Func<T, string?> nameOf, string? name)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(nameOf);
        T? sameAccount = null;
        foreach (T entry in entries)
        {
            string? candidate = nameOf(entry);
            if (string.Equals(candidate, name, StringComparison.Ordinal))
            {
                return entry;
            }

            if (sameAccount is null && Same(candidate, name))
            {
                sameAccount = entry;
            }
        }

        return sameAccount;
    }

    private sealed class ServerAccountComparer : IEqualityComparer<(string Server, string Account)>
    {
        public bool Equals((string Server, string Account) x, (string Server, string Account) y) =>
            string.Equals(x.Server, y.Server, StringComparison.Ordinal) && Same(x.Account, y.Account);

        public int GetHashCode((string Server, string Account) key) =>
            HashCode.Combine(
                key.Server is null ? 0 : StringComparer.Ordinal.GetHashCode(key.Server),
                key.Account is null ? 0 : Comparer.GetHashCode(key.Account));
    }
}
