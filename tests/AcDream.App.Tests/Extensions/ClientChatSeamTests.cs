using AcDream.App.Extensions;

namespace AcDream.App.Tests.Extensions;

public sealed class ClientChatSeamTests
{
    private readonly List<string> _log = new();

    [Fact]
    public void NoClaimsMeansTheClientKeepsTheKey()
    {
        var seam = new ClientChatSeam(_log.Add);
        Assert.False(seam.TryClaimEntry());
    }

    [Fact]
    public void TheFirstClaimThatSaysYesWins()
    {
        var seam = new ClientChatSeam(_log.Add);
        int second = 0;
        seam.ClaimChatEntry(() => false);
        seam.ClaimChatEntry(() => true);
        seam.ClaimChatEntry(() => { second++; return true; });
        Assert.True(seam.TryClaimEntry());
        Assert.Equal(0, second);
    }

    [Fact]
    public void AThrowingClaimIsDroppedAndCountsAsNo()
    {
        var seam = new ClientChatSeam(_log.Add);
        seam.ClaimChatEntry(() => throw new InvalidOperationException("claim failed"));
        Assert.False(seam.TryClaimEntry());
        Assert.False(seam.TryClaimEntry());
        Assert.Single(_log, l => l.Contains("claim failed", StringComparison.Ordinal));
    }

    [Fact]
    public void DisposingAClaimRemovesIt()
    {
        var seam = new ClientChatSeam(_log.Add);
        seam.ClaimChatEntry(() => true).Dispose();
        Assert.False(seam.TryClaimEntry());
    }

    [Fact]
    public void HiddenIsRemembered()
    {
        var seam = new ClientChatSeam(_log.Add);
        seam.SetChatWindowHidden(true);
        Assert.True(seam.ChatWindowHidden);
        seam.SetChatWindowHidden(false);
        Assert.False(seam.ChatWindowHidden);
    }
}
