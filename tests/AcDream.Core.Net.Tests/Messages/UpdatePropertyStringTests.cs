using System.Text;
using AcDream.Core.Net.Messages;

namespace AcDream.Core.Net.Tests.Messages;

/// <summary>Public/private string property updates (0x02D6 / 0x02D5), as ACE sends them.</summary>
public sealed class UpdatePropertyStringTests
{
    private static byte[] Text(string s) =>
        new byte[] { (byte)s.Length, 0 }.Concat(Encoding.GetEncoding(1252).GetBytes(s)).ToArray();

    // opcode, seq, property=1, guid=0x5000002A, 3 pad, len 18, text (2+18=20, no pad)
    private static byte[] PublicSample() => new byte[]
        { 0xD6, 0x02, 0, 0,  7,  1, 0, 0, 0,  0x2A, 0, 0, 0x50,  0, 0, 0 }
        .Concat(Text("RabidDee's Cottage")).ToArray();

    // opcode, seq, property=1, 3 pad, len 18, text
    private static byte[] PrivateSample() => new byte[]
        { 0xD5, 0x02, 0, 0,  7,  1, 0, 0, 0,  0, 0, 0 }
        .Concat(Text("RabidDee's Cottage")).ToArray();

    [Fact]
    public void Public_ParsesPropertyGuidAndName()
    {
        var p = PublicUpdatePropertyString.TryParse(PublicSample());
        Assert.NotNull(p);
        Assert.Equal(0x5000002Au, p!.Value.Guid);
        Assert.Equal(1u, p.Value.Property);
        Assert.Equal("RabidDee's Cottage", p.Value.Value);
    }

    [Fact]
    public void Private_ParsesPropertyAndName()
    {
        var p = PrivateUpdatePropertyString.TryParse(PrivateSample());
        Assert.NotNull(p);
        Assert.Equal(1u, p!.Value.Property);
        Assert.Equal("RabidDee's Cottage", p.Value.Value);
    }

    [Fact]
    public void Public_EmptyString_Parses()
    {
        byte[] b = new byte[] { 0xD6, 2, 0, 0, 7, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        Assert.Equal("", PublicUpdatePropertyString.TryParse(b)!.Value.Value);
    }

    [Fact]
    public void Truncated_ReturnsNull()
    {
        byte[] pub = PublicSample();
        byte[] priv = PrivateSample();
        Assert.Null(PublicUpdatePropertyString.TryParse(pub[..^5]));
        Assert.Null(PublicUpdatePropertyString.TryParse(pub[..12]));
        Assert.Null(PrivateUpdatePropertyString.TryParse(priv[..^5]));
        Assert.Null(PrivateUpdatePropertyString.TryParse(priv[..8]));
    }

    [Fact]
    public void WrongOpcode_ReturnsNull()
    {
        Assert.Null(PublicUpdatePropertyString.TryParse(PrivateSample()));
        Assert.Null(PrivateUpdatePropertyString.TryParse(PublicSample()));
    }
}
