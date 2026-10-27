namespace AcDream.Launcher.Core.Migration;

/// <summary>What happens to one file found in a classic installation.</summary>
public enum ClassicMigrationAction
{
    /// <summary>Copied to its new place.</summary>
    Copy,

    /// <summary>Copied over a different file that was already there.</summary>
    Overwrite,

    /// <summary>The same bytes are already in the new place.</summary>
    AlreadyPresent,

    /// <summary>A different file is already in the new place and is kept.</summary>
    Conflict,

    /// <summary>Not brought over; the reason says why.</summary>
    Skip,
}

/// <summary>One file of a classic installation and where it goes.</summary>
/// <param name="Source">The file in the classic installation.</param>
/// <param name="Destination">Where it goes, or null when it is skipped.</param>
/// <param name="Action">What is done with it.</param>
/// <param name="Reason">Why it is skipped, or null.</param>
public sealed record ClassicMigrationItem(
    string Source,
    string? Destination,
    ClassicMigrationAction Action,
    string? Reason = null);

/// <summary>
/// Copies the profiles of a classic VirindiTank and UtilityBelt setup into the
/// shared profile folder of an OpenAC install, in the layout MossTank reads:
/// <c>vtank/mosstank/</c> with <c>profiles/</c>, <c>metas/</c>, <c>navs/</c>,
/// <c>loot/</c> and the UtilityBelt tree under <c>ub/</c>. The originals are
/// only ever read.
/// </summary>
public static class ClassicMigration
{
    /// <summary>The plugin the imported profiles are for.</summary>
    public const string MossTankPluginId = "acdream.mosstank";

    /// <summary>The folder MossTank keeps everything a person edits in.</summary>
    public const string MossTankFolder = "mosstank";

    /// <summary>
    /// The UtilityBelt profile folders MossTank reads, at the plugin, server
    /// and character tiers.
    /// </summary>
    private static readonly HashSet<string> UbProfileFolders = new(
        ["autovendor", "equip", "itemgiver", "maps", "dungeonmaps"],
        StringComparer.OrdinalIgnoreCase);

    private const string UbAliasFileName = "aliases.txt";

    /// <summary>
    /// The VirindiTank folder of a usual Windows install, or null when none
    /// is found.
    /// </summary>
    public static string? FindVirindiTank()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        string[] candidates =
        [
            @"C:\Games\VirindiPlugins\VirindiTank",
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "VirindiPlugins",
                "VirindiTank"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "VirindiPlugins",
                "VirindiTank"),
        ];
        return candidates.FirstOrDefault(Directory.Exists);
    }

    /// <summary>
    /// The UtilityBelt folder of a usual Windows install, or null when none
    /// is found.
    /// </summary>
    public static string? FindUtilityBelt()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        string candidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Decal Plugins",
            "UtilityBelt");
        return Directory.Exists(candidate) ? candidate : null;
    }

    /// <summary>
    /// Every file of the two classic folders and what would be done with it.
    /// Nothing is written.
    /// </summary>
    /// <param name="virindiTankDirectory">The VirindiTank folder, or null.</param>
    /// <param name="utilityBeltDirectory">The UtilityBelt folder, or null.</param>
    /// <param name="vtankProfilesDirectory">The install's shared profile folder.</param>
    /// <param name="overwrite">Whether a different file already there is replaced.</param>
    public static IReadOnlyList<ClassicMigrationItem> Plan(
        string? virindiTankDirectory,
        string? utilityBeltDirectory,
        string vtankProfilesDirectory,
        bool overwrite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vtankProfilesDirectory);
        string target = Path.Combine(vtankProfilesDirectory, MossTankFolder);
        var items = new List<ClassicMigrationItem>();
        if (virindiTankDirectory is not null)
            PlanVirindiTank(virindiTankDirectory, target, overwrite, items);
        if (utilityBeltDirectory is not null)
            PlanUtilityBelt(utilityBeltDirectory, Path.Combine(target, "ub"), overwrite, items);
        return items;
    }

    /// <summary>Copies every planned file that has somewhere to go.</summary>
    public static void Apply(IEnumerable<ClassicMigrationItem> items)
    {
        foreach (ClassicMigrationItem item in items)
        {
            if (item.Action is not (ClassicMigrationAction.Copy or ClassicMigrationAction.Overwrite))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(item.Destination!)!);
            File.Copy(item.Source, item.Destination!, overwrite: item.Action == ClassicMigrationAction.Overwrite);
        }
    }

    /// <summary>One line naming what is done with <paramref name="item"/>.</summary>
    public static string Describe(ClassicMigrationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Action switch
        {
            ClassicMigrationAction.Copy => $"copy       {item.Source} -> {item.Destination}",
            ClassicMigrationAction.Overwrite => $"overwrite  {item.Source} -> {item.Destination}",
            ClassicMigrationAction.AlreadyPresent => $"present    {item.Destination}",
            ClassicMigrationAction.Conflict => $"conflict   {item.Destination} differs; kept",
            _ => $"skip       {item.Source}: {item.Reason}",
        };
    }

    /// <summary>The counts of a plan in one sentence.</summary>
    /// <param name="items">The plan.</param>
    /// <param name="applied">Whether the plan was carried out or only looked at.</param>
    public static string Summarize(IReadOnlyCollection<ClassicMigrationItem> items, bool applied)
    {
        ArgumentNullException.ThrowIfNull(items);
        int copied = items.Count(static item => item.Action
            is ClassicMigrationAction.Copy or ClassicMigrationAction.Overwrite);
        return $"{(applied ? "Copied" : "Would copy")} {copied} file(s); "
            + $"{items.Count(static item => item.Action == ClassicMigrationAction.AlreadyPresent)} already present, "
            + $"{items.Count(static item => item.Action == ClassicMigrationAction.Conflict)} conflict(s), "
            + $"{items.Count(static item => item.Action == ClassicMigrationAction.Skip)} skipped.";
    }

    private static void PlanVirindiTank(
        string source,
        string target,
        bool overwrite,
        List<ClassicMigrationItem> items)
    {
        // VirindiTank keeps every profile flat in its own folder; the
        // extension says which MossTank folder it belongs in.
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(file);
            string? folder = Path.GetExtension(file).ToLowerInvariant() switch
            {
                ".usd" or ".cdf" => "profiles",
                ".met" => "metas",
                ".nav" => "navs",
                ".utl" => "loot",
                _ => null,
            };
            if (folder is null)
            {
                string reason = name.Equals("gameinfodb.ugd", StringComparison.OrdinalIgnoreCase)
                    ? "MossTank downloads a current game database itself"
                    : "not a profile";
                items.Add(new ClassicMigrationItem(file, null, ClassicMigrationAction.Skip, reason));
                continue;
            }

            items.Add(Classify(file, Path.Combine(target, folder, name), overwrite));
        }
    }

    private static void PlanUtilityBelt(
        string source,
        string target,
        bool overwrite,
        List<ClassicMigrationItem> items)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            string relative = Path.GetRelativePath(source, file);
            string[] segments = relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
            string? reason = UbSkipReason(segments);
            items.Add(reason is null
                ? Classify(file, Path.Combine([target, .. segments]), overwrite)
                : new ClassicMigrationItem(file, null, ClassicMigrationAction.Skip, reason));
        }
    }

    /// <summary>
    /// Why a file of the UtilityBelt tree is not brought over, or null when
    /// it is. The tree is mirrored as it is, keeping its plugin, server and
    /// character tiers, but only for the files MossTank reads.
    /// </summary>
    private static string? UbSkipReason(string[] segments)
    {
        string name = segments[^1];
        if (name.Equals("settings.json", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".settings.json", StringComparison.OrdinalIgnoreCase))
        {
            return "settings files have a different format in MossTank; set these again in game";
        }

        // A profile folder sits at the plugin tier (index 0), the server
        // tier (index 1) or the character tier (index 2).
        for (int i = 0; i < Math.Min(3, segments.Length - 1); i++)
        {
            if (UbProfileFolders.Contains(segments[i]))
                return null;
        }

        bool isCharacterAliases = segments.Length == 3
            && name.Equals(UbAliasFileName, StringComparison.OrdinalIgnoreCase);
        bool isNamedAliases = segments.Length == 2
            && segments[0].Equals("profiles", StringComparison.OrdinalIgnoreCase)
            && name.EndsWith("." + UbAliasFileName, StringComparison.OrdinalIgnoreCase);
        return isCharacterAliases || isNamedAliases ? null : "not used by MossTank";
    }

    private static ClassicMigrationItem Classify(string source, string destination, bool overwrite)
    {
        if (!File.Exists(destination))
            return new ClassicMigrationItem(source, destination, ClassicMigrationAction.Copy);
        if (SameContent(source, destination))
            return new ClassicMigrationItem(source, destination, ClassicMigrationAction.AlreadyPresent);
        return new ClassicMigrationItem(
            source,
            destination,
            overwrite ? ClassicMigrationAction.Overwrite : ClassicMigrationAction.Conflict);
    }

    private static bool SameContent(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        return leftInfo.Length == rightInfo.Length
            && File.ReadAllBytes(left).AsSpan().SequenceEqual(File.ReadAllBytes(right));
    }
}
