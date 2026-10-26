using AcDream.Content.Pak;

namespace AcDream.Content.Tests;

// Allocation cuts on the world-loading path: each of these made garbage on
// every call while the world loads, which the collector then paid for in
// frame pauses.
public sealed class AllocationCutTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"acdream-pak-alloc-{Guid.NewGuid():N}.pak");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void CompressedBlobRead_AllocatesTheDecodedBytesOnly()
    {
        // Low-entropy but not repetitive: compresses to roughly half.
        byte[] decoded = new byte[256 * 1024];
        var random = new Random(1234);
        for (int i = 0; i < decoded.Length; i++)
            decoded[i] = (byte)random.Next(16);
        ulong key = PakKey.Compose(PakAssetType.GfxObjCollision, 9);
        using (var writer = new PakWriter(_path, new PakHeader { PortalIteration = 1, CellIteration = 2, HighResIteration = 3, LanguageIteration = 4 }))
        {
            writer.AddBlob(key, decoded);
            writer.Finish();
        }

        using var reader = new PakReader(_path);
        PakTocEntry entry = reader.GetTocEntryForTest(key);
        Assert.True(entry.IsCompressed);
        Assert.True(entry.StoredLength > decoded.Length / 4);

        foreach (bool firstRead in new[] { true, false })
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal(PakObjectReadStatus.Loaded, reader.ReadBlobBytes(key, out byte[]? bytes));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(decoded, bytes);
            Assert.True(allocated < decoded.Length + 4096,
                $"read {(firstRead ? "first" : "again")}: {allocated} bytes for a {decoded.Length}-byte blob stored in {entry.StoredLength}");
        }
    }
}
