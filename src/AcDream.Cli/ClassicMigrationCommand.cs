using AcDream.Launcher.Core.Migration;
using AcDream.Platform;

namespace AcDream.Cli;

/// <summary>
/// <c>migrate-classic [--vtank &lt;dir&gt;] [--ub &lt;dir&gt;] [--root &lt;dir&gt;]
/// [--overwrite] [--dry-run]</c>: copies a classic VirindiTank and
/// UtilityBelt setup into an OpenAC install.
/// </summary>
public static class ClassicMigrationCommand
{
    private const string Usage =
        "usage: AcDream.Cli migrate-classic [--vtank <dir>] [--ub <dir>] [--root <openac-root>] [--overwrite] [--dry-run]";

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        string? vtank = null;
        string? ub = null;
        string? root = null;
        bool overwrite = false;
        bool dryRun = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--vtank" when i + 1 < args.Length:
                    vtank = args[++i];
                    break;
                case "--ub" when i + 1 < args.Length:
                    ub = args[++i];
                    break;
                case "--root" when i + 1 < args.Length:
                    root = args[++i];
                    break;
                case "--overwrite":
                    overwrite = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                default:
                    error.WriteLine(Usage);
                    return 2;
            }
        }

        vtank ??= ClassicMigration.FindVirindiTank();
        ub ??= ClassicMigration.FindUtilityBelt();
        if (vtank is null && ub is null)
        {
            error.WriteLine("No VirindiTank or UtilityBelt folder was found; name one with --vtank or --ub.");
            error.WriteLine(Usage);
            return 2;
        }

        foreach ((string? path, string name) in new[] { (vtank, "VirindiTank"), (ub, "UtilityBelt") })
        {
            if (path is not null && !Directory.Exists(path))
            {
                error.WriteLine($"The {name} folder {path} does not exist.");
                return 2;
            }
        }

        ApplicationPathSet paths = ApplicationPathSet.Resolve(rootDirectory: root);
        output.WriteLine($"VirindiTank: {vtank ?? "(none)"}");
        output.WriteLine($"UtilityBelt: {ub ?? "(none)"}");
        output.WriteLine($"OpenAC:      {Path.Combine(paths.VtankProfilesDirectory, ClassicMigration.MossTankFolder)}");
        output.WriteLine();

        IReadOnlyList<ClassicMigrationItem> items =
            ClassicMigration.Plan(vtank, ub, paths.VtankProfilesDirectory, overwrite);
        foreach (ClassicMigrationItem item in items)
            output.WriteLine(ClassicMigration.Describe(item));
        if (!dryRun)
            ClassicMigration.Apply(items);

        output.WriteLine();
        output.WriteLine(ClassicMigration.Summarize(items, applied: !dryRun));
        if (items.Any(static item => item.Action == ClassicMigrationAction.Conflict))
            output.WriteLine("Conflicting files were kept; use --overwrite to replace them.");
        return 0;
    }
}
