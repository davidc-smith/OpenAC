using System.Text.Json;

namespace AcDream.App.Tests;

// The windowed client collects with the server collector, whose collections
// run on several threads: a pause the player sees as a stutter is a fraction
// as long. (Headless keeps the workstation collector, tuned for memory.)
public sealed class GarbageCollectorConfigurationTests
{
    [Fact]
    public void WindowedClientRunsTheServerCollector()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "AcDream.App.runtimeconfig.json");
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement options = config.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");

        Assert.True(options.TryGetProperty("System.GC.Server", out JsonElement server) && server.GetBoolean());
    }
}
