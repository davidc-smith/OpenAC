using AcDream.Launcher.Core.Migration;

namespace AcDream.Launcher.Core.Tests.Migration;

public sealed class ClassicMigrationTests
{
    [Fact]
    public void VirindiTankProfilesGoToTheFolderOfTheirKind()
    {
        using var directory = new TemporaryDirectory();
        string vtank = directory.Create("VirindiTank");
        Write(vtank, "Hunter.usd", "settings");
        Write(vtank, "--Bob_Coldeve.usd", "character settings");
        Write(vtank, "Coldeve_Bob.cdf", "uTank2 CDF 1.0");
        Write(vtank, "Hunt.met", "meta");
        Write(vtank, "Route.nav", "nav");
        Write(vtank, "Loot.utl", "loot");
        Write(vtank, "gameinfodb.ugd", "db");
        Write(vtank, "uTank2.dll", "code");
        string profiles = directory.Create("openac", "vtank");

        ClassicMigration.Apply(ClassicMigration.Plan(vtank, null, profiles, overwrite: false));

        Assert.Equal(
            [
                "mosstank/loot/Loot.utl",
                "mosstank/metas/Hunt.met",
                "mosstank/navs/Route.nav",
                "mosstank/profiles/--Bob_Coldeve.usd",
                "mosstank/profiles/Coldeve_Bob.cdf",
                "mosstank/profiles/Hunter.usd",
            ],
            Directory.EnumerateFiles(profiles, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(profiles, path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            "settings",
            File.ReadAllText(Path.Combine(profiles, "mosstank", "profiles", "Hunter.usd")));
    }

    [Fact]
    public void UtilityBeltProfileFoldersKeepTheirTiers()
    {
        using var directory = new TemporaryDirectory();
        string ub = directory.Create("UtilityBelt");
        Write(ub, Path.Combine("autovendor", "Shared.utl"), "plugin tier");
        Write(ub, Path.Combine("Coldeve", "equip", "Server.json"), "server tier");
        Write(ub, Path.Combine("Coldeve", "Bob", "autovendor", "Bob.utl"), "character tier");
        Write(ub, Path.Combine("Coldeve", "Bob", "aliases.txt"), "hi = /s hi");
        Write(ub, Path.Combine("profiles", "group.aliases.txt"), "gg = /f gg");
        Write(ub, Path.Combine("maps", "markers.csv"), "markers");
        Write(ub, Path.Combine("dungeonmaps", "visited", "0x01AB.json"), "visited");
        Write(ub, Path.Combine("itemgiver", "Give.utl"), "give");
        Write(ub, "settings.json", "{}");
        Write(ub, Path.Combine("profiles", "default.settings.json"), "{}");
        Write(ub, Path.Combine("Coldeve", "Bob", "settings.json"), "{}");
        Write(ub, Path.Combine("logs", "ub.log"), "log");
        string profiles = directory.Create("openac", "vtank");

        IReadOnlyList<ClassicMigrationItem> plan =
            ClassicMigration.Plan(null, ub, profiles, overwrite: false);
        ClassicMigration.Apply(plan);

        string target = Path.Combine(profiles, "mosstank", "ub");
        string[] copied = Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(target, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [
                "Coldeve/Bob/aliases.txt",
                "Coldeve/Bob/autovendor/Bob.utl",
                "Coldeve/equip/Server.json",
                "autovendor/Shared.utl",
                "dungeonmaps/visited/0x01AB.json",
                "itemgiver/Give.utl",
                "maps/markers.csv",
                "profiles/group.aliases.txt",
            ],
            copied);
        Assert.Equal(4, plan.Count(static item => item.Action == ClassicMigrationAction.Skip));
    }

    [Fact]
    public void ADifferentFileAlreadyThereIsKeptUnlessOverwriteIsAsked()
    {
        using var directory = new TemporaryDirectory();
        string vtank = directory.Create("VirindiTank");
        Write(vtank, "Hunt.met", "classic");
        Write(vtank, "Same.met", "same");
        string profiles = directory.Create("openac", "vtank");
        string metas = directory.Create("openac", "vtank", "mosstank", "metas");
        Write(metas, "Hunt.met", "edited in OpenAC");
        Write(metas, "Same.met", "same");

        IReadOnlyList<ClassicMigrationItem> kept =
            ClassicMigration.Plan(vtank, null, profiles, overwrite: false);
        ClassicMigration.Apply(kept);

        Assert.Equal(
            [ClassicMigrationAction.Conflict, ClassicMigrationAction.AlreadyPresent],
            kept.Select(static item => item.Action));
        Assert.Equal("edited in OpenAC", File.ReadAllText(Path.Combine(metas, "Hunt.met")));

        ClassicMigration.Apply(ClassicMigration.Plan(vtank, null, profiles, overwrite: true));

        Assert.Equal("classic", File.ReadAllText(Path.Combine(metas, "Hunt.met")));
        Assert.Equal("classic", File.ReadAllText(Path.Combine(vtank, "Hunt.met")));
    }

    private static void Write(string folder, string relative, string text)
    {
        string path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "openac-classic-migration-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Create(params string[] parts)
        {
            string path = System.IO.Path.Combine([Path, .. parts]);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
