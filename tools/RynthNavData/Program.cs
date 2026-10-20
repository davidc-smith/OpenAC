using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AcDream.Rynth.NavData;
using AcDream.Tools.RynthNavData;
using RynthCore.App.Avalonia;

// pin --url <zip> --rynthsuite <commit> [--pin <file>] [--cache <dir>]
// install [--dir <NavData>] [--force] [--pin <file>] [--cache <dir>]
// check-feed
return await Run(args);

static async Task<int> Run(string[] args)
{
    string verb = args.FirstOrDefault() ?? string.Empty;
    string? Option(string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    string pinPath = Option("--pin") ?? FindPin();
    string cache = Option("--cache") ?? NavDataFolders.Cache();
    try
    {
        switch (verb)
        {
            case "pin":
            {
                string url = Option("--url") ?? throw new ArgumentException("pin needs --url <zip>");
                string suite = Option("--rynthsuite") ?? throw new ArgumentException("pin needs --rynthsuite <commit>");
                using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
                NavDataPin pin = await NavDataPinner.PinAsync(url, cache, suite, DateOnly.FromDateTime(DateTime.UtcNow), http, Console.Out);
                pin.Save(pinPath);
                Console.WriteLine($"Pinned {pin.Files} files ({pin.Bytes >> 20} MB unpacked), sha256 {pin.Sha256}, into {pinPath}.");
                return 0;
            }
            case "install":
            {
                NavDataPin pin = NavDataPin.Load(pinPath);
                string dir = Option("--dir") ?? NavDataFolders.NavData();
                var progress = new Progress<NavDataProgress>(p => Console.WriteLine($"  {p.Detail}"));
                Console.WriteLine(await NavDataInstaller.InstallAsync(pin, dir, cache, progress, force: args.Contains("--force")));
                return 0;
            }
            case "check-feed":
            {
                var updater = new RynthUpdater(stateDir: Path.Combine(cache, "feed-state"));
                RynthUpdater.CheckResult result = await updater.CheckAsync(Array.Empty<string>());
                if (result.Manifest.NavData is not { } feed)
                {
                    Console.WriteLine($"Their feed (release {result.Manifest.Release}) has no navdata entry.");
                    return 0;
                }

                Console.WriteLine($"Their feed (release {result.Manifest.Release}) navdata: release {feed.Release}, {feed.Files} files, {feed.Size} bytes, sha256 {feed.Sha256}, {feed.Url}");
                if (File.Exists(pinPath))
                    Console.WriteLine(string.Equals(NavDataPin.Load(pinPath).Sha256, feed.Sha256, StringComparison.OrdinalIgnoreCase)
                        ? "Same archive as our pin."
                        : "A different archive from our pin: test it against the pinned RynthSuite before re-pinning.");
                return 0;
            }
            default:
                Console.Error.WriteLine("usage: pin --url <zip> --rynthsuite <commit> | install [--dir <NavData>] [--force] | check-feed  [--pin <file>] [--cache <dir>]");
                return 2;
        }
    }
    catch (Exception error) when (error is IOException or InvalidDataException or HttpRequestException or ArgumentException or InvalidOperationException or OperationCanceledException or UnauthorizedAccessException or System.Text.Json.JsonException)
    {
        Console.Error.WriteLine($"{verb} failed: {error.Message}");
        return 1;
    }
}

// external/rynth-navdata.json from the current folder or any folder above it.
static string FindPin()
{
    for (DirectoryInfo? dir = new(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        string candidate = Path.Combine(dir.FullName, "external", "rynth-navdata.json");
        if (File.Exists(candidate) || Directory.Exists(Path.Combine(dir.FullName, "external", "RynthCore")))
            return candidate;
    }

    return Path.Combine("external", "rynth-navdata.json");
}
