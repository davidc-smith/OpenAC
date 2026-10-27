using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using DatReaderWriter.Lib.IO;

namespace AcDream.Content;

public sealed partial class DatDatabaseWrapper
{
    /// <summary>
    /// Reads one record. Textures and palettes are decoded in bulk from the
    /// raw bytes; every other type goes through the package. The caller
    /// holds the database lock.
    /// </summary>
    private bool TryReadObject<T>(uint fileId, [MaybeNullWhen(false)] out T value)
        where T : IDBObj
    {
        if (!DatBulkObjectReader.Handles<T>())
            return _db.TryGet<T>(fileId, out value);

        if (!_db.Tree.TryGetFile(fileId, out var entry))
        {
            value = default;
            return false;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent((int)entry.Size);
        byte[] buffer = rented;
        try
        {
            // A compressed record grows the buffer to its unpacked size.
            if (!_db.TryGetFileBytes(fileId, ref buffer, out int length))
            {
                value = default;
                return false;
            }

            value = DatBulkObjectReader.Read<T>(buffer.AsSpan(0, length), fileId);
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
