namespace AcDream.Core.Textures;

/// <summary>
/// Decoded-pixel arrays handed out again and again to one owner that uploads
/// each decoded texture before it decodes the next, so that decoding a
/// texture for upload does not allocate a new image every time.
/// </summary>
/// <remarks>
/// <para>An array is kept per exact length, since a
/// <see cref="DecodedTexture"/>'s pixels are exactly its image. The kept
/// arrays hold at most <see cref="MaximumRetainedBytes"/> together: a new
/// length lets go of the least recently used ones until it fits, and a
/// length larger than the whole budget is a new array that is not kept.</para>
/// <para>The array <see cref="Get"/> returns is the one the next request of
/// the same length returns: a texture decoded into it must be finished with
/// (uploaded, copied) before the owner decodes another. The buffers belong
/// to the first thread that asks; another thread is given a new array every
/// time, since two decodes sharing one array would corrupt each other.</para>
/// </remarks>
public sealed class ReusedPixelBuffers
{
    private readonly Dictionary<int, (byte[] Array, long LastUse)> _byLength = new();
    private long _useSequence;
    private int _ownerThreadId;

    public ReusedPixelBuffers(long maximumRetainedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRetainedBytes);
        MaximumRetainedBytes = maximumRetainedBytes;
    }

    /// <summary>The most the kept arrays may hold together, in bytes.</summary>
    public long MaximumRetainedBytes { get; }

    /// <summary>The bytes the kept arrays hold together.</summary>
    public long RetainedBytes { get; private set; }

    /// <summary>An array of exactly <paramref name="length"/> bytes, with
    /// whatever the last texture of that length left in it.</summary>
    public byte[] Get(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        int thread = Environment.CurrentManagedThreadId;
        if (_ownerThreadId == 0)
            _ownerThreadId = thread;
        else if (_ownerThreadId != thread)
            return new byte[length];

        long use = ++_useSequence;
        if (_byLength.TryGetValue(length, out var kept))
        {
            _byLength[length] = (kept.Array, use);
            return kept.Array;
        }

        var array = new byte[length];
        if (length > MaximumRetainedBytes)
            return array;
        while (RetainedBytes + length > MaximumRetainedBytes)
        {
            int oldest = 0;
            long oldestUse = long.MaxValue;
            foreach (var (keptLength, entry) in _byLength)
            {
                if (entry.LastUse < oldestUse)
                {
                    oldest = keptLength;
                    oldestUse = entry.LastUse;
                }
            }
            _byLength.Remove(oldest);
            RetainedBytes -= oldest;
        }
        _byLength.Add(length, (array, use));
        RetainedBytes += length;
        return array;
    }
}
