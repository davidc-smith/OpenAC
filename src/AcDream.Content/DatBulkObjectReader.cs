using System.Buffers.Binary;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;

namespace AcDream.Content;

/// <summary>
/// Decodes textures and palettes from a record's raw bytes in bulk. The
/// package reader copies a texture's pixels one byte at a time and builds a
/// palette's colors through a factory lookup per color; at startup that is
/// a large share of the time spent loading the terrain and interface
/// textures. The result is field-for-field what the package reader produces.
/// </summary>
internal static class DatBulkObjectReader
{
    /// <summary>Whether <typeparamref name="T"/> is read here instead of by the package.</summary>
    internal static bool Handles<T>() =>
        typeof(T) == typeof(RenderSurface) || typeof(T) == typeof(Palette);

    /// <summary>Reads a <typeparamref name="T"/> that <see cref="Handles{T}"/> accepts.</summary>
    internal static T Read<T>(ReadOnlySpan<byte> record, uint fileId)
    {
        if (typeof(T) == typeof(RenderSurface))
            return (T)(object)ReadRenderSurface(record, fileId);
        if (typeof(T) == typeof(Palette))
            return (T)(object)ReadPalette(record, fileId);
        throw new NotSupportedException($"{typeof(T).Name} is not read in bulk.");
    }

    /// <summary>
    /// Record layout: id, data category, width, height, pixel format, pixel
    /// byte count, the pixels, then a default palette id for the two indexed
    /// formats. Every number is a little-endian 32-bit value.
    /// </summary>
    internal static RenderSurface ReadRenderSurface(ReadOnlySpan<byte> record, uint fileId)
    {
        var surface = new RenderSurface
        {
            Id = ReadUInt32(record, 0),
            DataCategory = ReadUInt32(record, 4),
            Width = (int)ReadUInt32(record, 8),
            Height = (int)ReadUInt32(record, 12),
            Format = (PixelFormat)ReadUInt32(record, 16),
        };
        int length = (int)ReadUInt32(record, 20);
        surface.SourceData = record.Slice(24, length).ToArray();
        if (surface.Format is PixelFormat.PFID_INDEX16 or PixelFormat.PFID_P8)
            surface.DefaultPaletteId = ReadUInt32(record, 24 + length);

        // The requested id wins over the stored one, as in the package's read.
        surface.Id = fileId;
        return surface;
    }

    /// <summary>
    /// Record layout: id, color count, then one blue, green, red, alpha
    /// byte quad per color.
    /// </summary>
    internal static Palette ReadPalette(ReadOnlySpan<byte> record, uint fileId)
    {
        var palette = new Palette { Id = ReadUInt32(record, 0) };
        // A negative count reads no colors, as in the package's loop.
        int count = Math.Max(0, (int)ReadUInt32(record, 4));
        ReadOnlySpan<byte> quads = record.Slice(8, checked(count * 4));
        var colors = new List<ColorARGB>(count);
        for (int i = 0; i < quads.Length; i += 4)
        {
            colors.Add(new ColorARGB
            {
                Blue = quads[i],
                Green = quads[i + 1],
                Red = quads[i + 2],
                Alpha = quads[i + 3],
            });
        }
        palette.Colors = colors;

        palette.Id = fileId;
        return palette;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> record, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(offset, 4));
}
