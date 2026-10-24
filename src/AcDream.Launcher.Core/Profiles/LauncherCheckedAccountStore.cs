using System.Text.Json;
using AcDream.Platform;

namespace AcDream.Launcher.Core.Profiles;

/// <summary>Remembers checked account/server rows independently of credential profiles.</summary>
public sealed class LauncherCheckedAccountStore
{
    public const string FileName = "launcher-checked-accounts.json";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>A checked row is its server, compared exactly, and its account: a selection saved
    /// as "fefe" checks the row for "Fefe".</summary>
    private static readonly IEqualityComparer<LauncherCheckedAccount> SameAccount =
        EqualityComparer<LauncherCheckedAccount>.Create(
            (left, right) => left is null || right is null
                ? ReferenceEquals(left, right)
                : AccountNames.KeyComparer.Equals((left.Server, left.Account), (right.Server, right.Account)),
            row => AccountNames.KeyComparer.GetHashCode((row.Server, row.Account)));
    private HashSet<LauncherCheckedAccount> _checked = new(SameAccount);

    public LauncherCheckedAccountStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public static LauncherCheckedAccountStore ForApplicationPaths(ApplicationPathSet paths) =>
        new(Path.Combine(paths.ConfigDirectory, FileName));

    public bool Contains(string server, string account) =>
        _checked.Contains(new LauncherCheckedAccount(server, account));

    public void Load()
    {
        LauncherCheckedAccount[] rows = File.Exists(FilePath)
            ? JsonSerializer.Deserialize<LauncherCheckedAccount[]>(File.ReadAllBytes(FilePath), Options)
                ?? throw new InvalidDataException("The checked-account selection file is empty.")
            : [];
        if (rows.Any(row => row is null || string.IsNullOrWhiteSpace(row.Server)
            || string.IsNullOrWhiteSpace(row.Account)))
        {
            throw new InvalidDataException("Each checked account needs a server and account name.");
        }
        _checked = new(rows, SameAccount);
    }

    public void Save(IEnumerable<LauncherCheckedAccount> rows)
    {
        HashSet<LauncherCheckedAccount> selected = new(rows, SameAccount);
        string directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(
                selected.OrderBy(row => row.Server, StringComparer.Ordinal)
                    .ThenBy(row => row.Account, StringComparer.Ordinal).ToArray(), Options));
            File.Move(temporary, FilePath, overwrite: true);
            _checked = selected;
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}

/// <summary>The account and server identifying one checked launch row, as saved.</summary>
public sealed record LauncherCheckedAccount(string Server, string Account);
