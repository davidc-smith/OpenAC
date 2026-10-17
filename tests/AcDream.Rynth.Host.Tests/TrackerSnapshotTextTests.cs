using AcDream.Rynth.Host;

namespace AcDream.Rynth.Host.Tests;

public sealed class TrackerSnapshotTextTests
{
    [Fact]
    public void Format_SummarisesTheSnapshot()
    {
        const string json =
            "{\"ss\":1800.0,\"xt\":600,\"xh\":1200,\"lt\":0,\"lh\":0,\"kt\":1,\"kh\":2.0,\"xk\":600,\"dt\":0}";

        Assert.Equal(
            "Rynth tracker: 1 kills (2.0/h), 600 XP (1,200/h), 0 lum (0/h), session 30m 0s",
            TrackerSnapshotText.Format(json));
    }

    [Theory]
    [InlineData("{\"ss\":1800.0,\"xt\":600,\"xh\":1200,\"lt\":0,\"kt\":1,\"kh\":2.0}")]
    [InlineData("{\"ss\":\"x\",\"xt\":600,\"xh\":1200,\"lt\":0,\"lh\":0,\"kt\":1,\"kh\":2.0}")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public void Format_ReportsAnUnreadableSnapshot(string json)
    {
        Assert.Equal("Rynth tracker: unreadable snapshot", TrackerSnapshotText.Format(json));
    }

    [Fact]
    public void Format_SaysSoWhenThereIsNoSnapshot()
    {
        Assert.Equal("Rynth tracker: no data yet", TrackerSnapshotText.Format(null));
    }
}
