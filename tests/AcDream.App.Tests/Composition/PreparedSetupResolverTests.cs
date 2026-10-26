using AcDream.App.Composition;
using AcDream.Content;
using AcDream.Content.Pak;
using DatReaderWriter.DBObjs;

namespace AcDream.App.Tests.Composition;

public sealed class PreparedSetupResolverTests
{
    [Fact]
    public void MissingPreparedSetupDoesNotTouchDatLoader()
    {
        int loads = 0;
        var resolver = new PreparedSetupResolver(
            new PresenceSource(PreparedAssetPresence.Missing),
            _ =>
            {
                loads++;
                return new Setup();
            },
            _ => { });

        Assert.False(resolver.TryResolve(0x0100_0001u, out Setup? setup));
        Assert.Null(setup);
        Assert.Equal(0, loads);
    }

    [Fact]
    public void AvailablePreparedSetupLoadsMatchingDatRecord()
    {
        var expected = new Setup();
        var resolver = new PreparedSetupResolver(
            new PresenceSource(PreparedAssetPresence.Available),
            id =>
            {
                Assert.Equal(0x0200_0001u, id);
                return expected;
            },
            _ => { });

        Assert.True(resolver.TryResolve(0x0200_0001u, out Setup? actual));
        Assert.Same(expected, actual);
    }

    [Theory]
    [InlineData(PreparedAssetPresence.Available, true)]
    [InlineData(PreparedAssetPresence.Available, false)]
    [InlineData(PreparedAssetPresence.Missing, false)]
    public void RepeatedResolutionReadsTheContentOnce(
        PreparedAssetPresence presence,
        bool datRecordExists)
    {
        // Every static object of a landblock asks for its Setup when the
        // landblock is shown, and most of them share a handful of Setups.
        // The content does not change, so each Setup is read once.
        var source = new PresenceSource(presence);
        int loads = 0;
        var expected = new Setup();
        var resolver = new PreparedSetupResolver(
            source,
            _ =>
            {
                loads++;
                return datRecordExists ? expected : null;
            },
            _ => { });

        bool first = resolver.TryResolve(0x0200_0001u, out Setup? firstSetup);
        bool second = resolver.TryResolve(0x0200_0001u, out Setup? secondSetup);

        Assert.Equal(first, second);
        Assert.Same(firstSetup, secondSetup);
        Assert.Equal(1, source.Probes);
        Assert.Equal(presence == PreparedAssetPresence.Available ? 1 : 0, loads);
    }

    [Fact]
    public void CorruptPreparedEntryFailsLoudlyWithoutDatGuess()
    {
        int loads = 0;
        var resolver = new PreparedSetupResolver(
            new PresenceSource(PreparedAssetPresence.Corrupt),
            _ =>
            {
                loads++;
                return null;
            },
            _ => { });

        Assert.Throws<InvalidDataException>(() =>
            resolver.TryResolve(0x0200_0001u, out _));
        Assert.Equal(0, loads);
    }

    [Theory]
    [MemberData(nameof(KnownMalformedDatExceptions))]
    public void KnownMalformedDatRecordIsDiagnosedOnce(
        Func<Exception> exceptionFactory)
    {
        var diagnostics = new List<string>();
        var resolver = new PreparedSetupResolver(
            new PresenceSource(PreparedAssetPresence.Available),
            _ => throw exceptionFactory(),
            diagnostics.Add);

        Assert.False(resolver.TryResolve(0x0200_0001u, out _));
        Assert.False(resolver.TryResolve(0x0200_0001u, out _));
        Assert.Single(diagnostics);
        Assert.Contains("malformed", diagnostics[0], StringComparison.Ordinal);
    }

    [Fact]
    public void UnexpectedDatFailurePropagates()
    {
        var expected = new InvalidOperationException("unexpected");
        var resolver = new PreparedSetupResolver(
            new PresenceSource(PreparedAssetPresence.Available),
            _ => throw expected,
            _ => { });

        InvalidOperationException actual = Assert.Throws<InvalidOperationException>(
            () => resolver.TryResolve(0x0200_0001u, out _));
        Assert.Same(expected, actual);
    }

    public static TheoryData<Func<Exception>> KnownMalformedDatExceptions => new()
    {
        () => new InvalidDataException("bad payload"),
        () => new EndOfStreamException("truncated"),
        () => new ArgumentOutOfRangeException("index"),
    };

    private sealed class PresenceSource(PreparedAssetPresence presence)
        : IPreparedAssetSource
    {
        public PreparedAssetSourceStats Stats => default;
        public CacheStats DecodedTextureCacheStats => default;

        public int Probes { get; private set; }

        public PreparedAssetPresence Probe(
            PakAssetType type,
            uint sourceFileId)
        {
            Assert.Equal(PakAssetType.SetupMesh, type);
            Probes++;
            return presence;
        }

        public PreparedAssetReadResult Read(
            in PreparedAssetRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
