using System.Collections.Immutable;
using AcDream.App.Composition;
using AcDream.Content;
using DatReaderWriter.DBObjs;

namespace AcDream.App.Hosting;

/// <summary>
/// The game files the session is played from, opened once by the session
/// host and kept for the life of the process: the installed data files, the
/// prepared assets, the spell tables, and the land's height table. The
/// session's collision and its windowless route read them with no window; a
/// window borrows them for drawing and never closes them.
/// </summary>
internal sealed class GraphicalSessionContent : IDisposable
{
    private bool _disposed;

    internal GraphicalSessionContent(
        IDatReaderWriter dats,
        IPreparedAssetSource preparedAssets,
        MagicCatalog magic,
        ImmutableArray<float> heightTable)
    {
        Dats = dats ?? throw new ArgumentNullException(nameof(dats));
        PreparedAssets = preparedAssets
            ?? throw new ArgumentNullException(nameof(preparedAssets));
        PreparedCollision = preparedAssets as IPreparedCollisionSource
            ?? throw new NotSupportedException(
                "Production prepared assets must expose collision data.");
        Magic = magic ?? throw new ArgumentNullException(nameof(magic));
        HeightTable = heightTable;
    }

    internal IDatReaderWriter Dats { get; }

    internal IPreparedAssetSource PreparedAssets { get; }

    internal IPreparedCollisionSource PreparedCollision { get; }

    internal MagicCatalog Magic { get; }

    internal ImmutableArray<float> HeightTable { get; }

    /// <summary>
    /// The one lock every reader of these files takes, the window's drawing
    /// and the session's collision alike: the files are shared.
    /// </summary>
    internal object DatLock { get; } = new();

    /// <summary>Opens the files the options name.</summary>
    internal static GraphicalSessionContent Open(
        RuntimeOptions options,
        Action<string> diagnostic)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(diagnostic);
        var factory = new RetailContentEffectsAudioCompositionFactory();
        IDatReaderWriter? dats = null;
        IPreparedAssetSource? prepared = null;
        Task<(MagicCatalog Magic, float[] Heights)>? tables = null;
        try
        {
            dats = factory.OpenDatCollection(options.DatDir);
            // The spell tables and the land's height table are read from the
            // game files on a worker while the prepared assets open here.
            // Opening the prepared assets reads only the game files'
            // iteration numbers, which are fixed once the files are open, so
            // the worker is the files' only reader until it is joined below.
            IDatReaderWriter openDats = dats;
            tables = Task.Run(() => ReadTables(factory, openDats));
            prepared = factory.OpenPreparedAssetSource(
                options.PreparedAssetPath,
                options.PreparedAssetOverlayPath,
                options.PreparedAssetBaseRecipeVersion,
                options.PreparedAssetEffectiveRecipeVersion,
                dats,
                diagnostic);
            (MagicCatalog magic, float[] heights) = tables.GetAwaiter().GetResult();
            return new GraphicalSessionContent(
                dats,
                prepared,
                magic,
                ImmutableArray.CreateRange(heights));
        }
        catch
        {
            // The worker reads the files: it is finished before they close.
            try
            {
                tables?.Wait();
            }
            catch (AggregateException)
            {
                // When its failure is what stopped the open, it is the one
                // rethrown below.
            }
            try
            {
                prepared?.Dispose();
            }
            finally
            {
                dats?.Dispose();
            }
            throw;
        }
    }

    private static (MagicCatalog Magic, float[] Heights) ReadTables(
        RetailContentEffectsAudioCompositionFactory factory,
        IDatReaderWriter dats)
    {
        MagicCatalog magic = factory.LoadMagicCatalog(dats);
        Region region = dats.Get<Region>(0x13000000u)
            ?? throw new InvalidOperationException(
                "Region dat id 0x13000000 is missing.");
        float[]? heights = region.LandDefs.LandHeightTable;
        if (heights is null || heights.Length < 256)
        {
            throw new InvalidOperationException(
                "Region.LandDefs.LandHeightTable is missing or truncated.");
        }
        return (magic, heights);
    }

    /// <summary>What the session's collision is built from.</summary>
    internal GraphicalCollisionContent CreateCollisionContent() =>
        new(Dats, DatLock, PreparedCollision, HeightTable);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            PreparedAssets.Dispose();
        }
        finally
        {
            Dats.Dispose();
        }
    }
}
