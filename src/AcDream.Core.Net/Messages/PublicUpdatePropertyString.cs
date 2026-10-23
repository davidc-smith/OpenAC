using System;
using System.Buffers.Binary;
using System.Text;

namespace AcDream.Core.Net.Messages;

/// <summary>
/// 0x02D6 — one PropertyString changed on a visible object. Layout after the
/// opcode: sequence byte, property, guid, pad to 4 (measured from the opcode),
/// then a String16L. Note the property comes before the guid here.
/// </summary>
public static class PublicUpdatePropertyString
{
    public const uint Opcode = 0x02D6u;

    public readonly record struct Parsed(uint Guid, uint Property, string Value);

    public static Parsed? TryParse(ReadOnlySpan<byte> body)
    {
        if (body.Length < 4 + 1 + 4 + 4) return null;
        if (BinaryPrimitives.ReadUInt32LittleEndian(body) != Opcode) return null;
        int pos = 5;
        uint prop = BinaryPrimitives.ReadUInt32LittleEndian(body[pos..]); pos += 4;
        uint guid = BinaryPrimitives.ReadUInt32LittleEndian(body[pos..]); pos += 4;
        pos = (pos + 3) & ~3;
        string? value = UpdatePropertyStringReader.Read(body, pos);
        return value is null ? null : new Parsed(guid, prop, value);
    }
}

/// <summary>
/// 0x02D5 — one PropertyString changed on the player. Layout after the opcode:
/// sequence byte, property, pad to 4 (measured from the opcode), String16L.
/// </summary>
public static class PrivateUpdatePropertyString
{
    public const uint Opcode = 0x02D5u;

    public readonly record struct Parsed(uint Property, string Value);

    public static Parsed? TryParse(ReadOnlySpan<byte> body)
    {
        if (body.Length < 4 + 1 + 4) return null;
        if (BinaryPrimitives.ReadUInt32LittleEndian(body) != Opcode) return null;
        int pos = 5;
        uint prop = BinaryPrimitives.ReadUInt32LittleEndian(body[pos..]); pos += 4;
        pos = (pos + 3) & ~3;
        string? value = UpdatePropertyStringReader.Read(body, pos);
        return value is null ? null : new Parsed(prop, value);
    }
}

internal static class UpdatePropertyStringReader
{
    /// <summary>Reads a u16-length cp1252 string at <paramref name="pos"/>; null if truncated.</summary>
    public static string? Read(ReadOnlySpan<byte> body, int pos)
    {
        if (body.Length - pos < 2) return null;
        ushort length = BinaryPrimitives.ReadUInt16LittleEndian(body[pos..]);
        pos += 2;
        if (body.Length - pos < length) return null;
        return Encoding.GetEncoding(1252).GetString(body.Slice(pos, length));
    }
}
