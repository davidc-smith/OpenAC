using AcDream.App.Extensions.Overlay;
using AcDream.App.Plugins;

namespace AcDream.App.Tests.Extensions;

public sealed class OverlayIconsTests
{
    private readonly List<string> _log = new();
    private readonly FakeBackend _backend = new();
    private readonly OverlayIcons _icons;

    public OverlayIconsTests() => _icons = new OverlayIcons(_log.Add);

    [Fact]
    public void UnboundEverythingIsZero()
    {
        Assert.Equal(IntPtr.Zero, _icons.Icon(0x06001234u));
        Assert.Equal(IntPtr.Zero, _icons.SpellIcon(1));
        Assert.Equal(IntPtr.Zero, _icons.ObjectIcon(0x80000001u));
        Assert.False(_icons.IsIssued((IntPtr)5));
    }

    [Fact]
    public void IconIdsAreNormalisedAndZeroAsksNothing()
    {
        _icons.Bind(_backend);
        _backend.Answer = 11;

        Assert.Equal((IntPtr)11, _icons.Icon(0x1234u));
        Assert.Equal((IntPtr)11, _icons.Icon(0x06001234u));
        Assert.Equal(IntPtr.Zero, _icons.Icon(0));
        Assert.Equal(new[] { "art:06001234" }, _backend.Asked);
    }

    [Fact]
    public void SpellsAndObjectsGoToTheirOwnLookups()
    {
        _icons.Bind(_backend);
        _backend.Answer = 12;

        Assert.Equal((IntPtr)12, _icons.SpellIcon(1234));
        Assert.Equal((IntPtr)12, _icons.ObjectIcon(0x80000001u));
        Assert.Equal(new[] { "spell:000004D2", "object:80000001" }, _backend.Asked);
    }

    [Fact]
    public void OnlyHandedOutHandlesAreIssued()
    {
        _icons.Bind(_backend);
        _backend.Answer = 13;
        _icons.SpellIcon(1);

        Assert.True(_icons.IsIssued((IntPtr)13));
        Assert.False(_icons.IsIssued((IntPtr)14));
        Assert.False(_icons.IsIssued(IntPtr.Zero));
        Assert.False(_icons.IsIssued((IntPtr)(-13)));
        Assert.False(_icons.IsIssued(new IntPtr(0x1_0000_000DL)));
    }

    [Fact]
    public void ANotFoundAnswerIsZeroAndNotIssued()
    {
        _icons.Bind(_backend);
        _backend.Answer = 0;
        Assert.Equal(IntPtr.Zero, _icons.SpellIcon(1));
        Assert.False(_icons.IsIssued(IntPtr.Zero));
    }

    [Fact]
    public void AThrowingBackendIsZeroAndLoggedOncePerLookup()
    {
        _icons.Bind(_backend);
        _backend.Throw = true;

        Assert.Equal(IntPtr.Zero, _icons.SpellIcon(1));
        Assert.Equal(IntPtr.Zero, _icons.SpellIcon(1));
        Assert.Equal(IntPtr.Zero, _icons.SpellIcon(2));
        Assert.Equal(2, _log.Count(line => line.Contains("icon lookup failed", StringComparison.Ordinal)));
    }

    [Fact]
    public void BindingTwiceKeepsTheFirstBackend()
    {
        _icons.Bind(_backend);
        var second = new FakeBackend { Answer = 99 };
        _icons.Bind(second);
        _icons.Bind(second);
        _backend.Answer = 15;

        Assert.Equal((IntPtr)15, _icons.SpellIcon(1));
        Assert.Single(_log, line => line.Contains("already bound", StringComparison.Ordinal));
    }

    [Fact]
    public void RepeatedIconAndSpellLookupsAreMemoisedHitsAndMisses()
    {
        _icons.Bind(_backend);
        _backend.Answer = 21;
        Assert.Equal((IntPtr)21, _icons.Icon(0x1234u));
        Assert.Equal((IntPtr)21, _icons.Icon(0x06001234u));
        Assert.Equal((IntPtr)21, _icons.SpellIcon(7));
        Assert.Equal((IntPtr)21, _icons.SpellIcon(7));
        _backend.Answer = 0;
        Assert.Equal(IntPtr.Zero, _icons.Icon(0x2222u));
        Assert.Equal(IntPtr.Zero, _icons.Icon(0x2222u));
        Assert.Equal(IntPtr.Zero, _icons.SpellIcon(8));
        Assert.Equal(IntPtr.Zero, _icons.SpellIcon(8));
        Assert.Equal(new[] { "art:06001234", "spell:00000007", "art:06002222", "spell:00000008" }, _backend.Asked);
    }

    [Fact]
    public void ObjectIconsAreNeverMemoised()
    {
        _icons.Bind(_backend);
        _backend.Answer = 22;
        _icons.ObjectIcon(0x80000001u);
        _icons.ObjectIcon(0x80000001u);
        Assert.Equal(new[] { "object:80000001", "object:80000001" }, _backend.Asked);
    }

    [Fact]
    public void LookupsBeforeBindAreNotMemoised()
    {
        Assert.Equal(IntPtr.Zero, _icons.Icon(0x1234u));
        Assert.Equal(IntPtr.Zero, _icons.SpellIcon(3));
        _icons.Bind(_backend);
        _backend.Answer = 23;
        Assert.Equal((IntPtr)23, _icons.Icon(0x1234u));
        Assert.Equal((IntPtr)23, _icons.SpellIcon(3));
    }

    [Fact]
    public void AFoundAnswerWithNoTextureIsZeroAndNotIssued()
    {
        _icons.Bind(_backend);
        _backend.AlwaysFound = true;
        _backend.Answer = 0;

        Assert.Equal(IntPtr.Zero, _icons.SpellIcon(5));
        Assert.Equal(IntPtr.Zero, _icons.ObjectIcon(0x80000005u));
        Assert.False(_icons.IsIssued(IntPtr.Zero));
    }

    [Fact]
    public void UploadedPicturesAreIssuedAndReleasedOnce()
    {
        _icons.Bind(_backend);
        _backend.Answer = 21;

        IntPtr tex = _icons.Upload(new byte[2 * 3 * 4], 2, 3, "retail 06004D0A");
        Assert.Equal((IntPtr)21, tex);
        Assert.True(_icons.IsIssued(tex));
        Assert.True(_icons.Release(tex));
        Assert.False(_icons.IsIssued(tex));
        Assert.False(_icons.Release(tex));
        Assert.False(_icons.Release((IntPtr)99));
        Assert.Equal(new[] { "upload:2x3:retail 06004D0A", "release:21" }, _backend.Asked);
    }

    [Fact]
    public void BadPicturesAndAnUnboundServiceUploadNothing()
    {
        Assert.Equal(IntPtr.Zero, _icons.Upload(new byte[16], 2, 2, "unbound"));
        _icons.Bind(_backend);
        _backend.Answer = 22;
        Assert.Equal(IntPtr.Zero, _icons.Upload(new byte[15], 2, 2, "short"));
        Assert.Equal(IntPtr.Zero, _icons.Upload(new byte[16], 0, 2, "empty"));
        _backend.Throw = true;
        Assert.Equal(IntPtr.Zero, _icons.Upload(new byte[16], 2, 2, "boom"));
        Assert.Single(_log);
    }

    private sealed class FakeBackend : IPluginImageBackend
    {
        public uint Answer { get; set; }
        public bool Throw { get; set; }
        /// <summary>Report success even with a zero texture (a backend that "finds" nothing).</summary>
        public bool AlwaysFound { get; set; }
        public List<string> Asked { get; } = new();

        public bool TryGetClientArt(uint surfaceId, out uint texture, out int width, out int height) =>
            Reply($"art:{surfaceId:X8}", out texture, out width, out height);

        public bool TryGetSpellIcon(uint spellId, out uint texture, out int width, out int height) =>
            Reply($"spell:{spellId:X8}", out texture, out width, out height);

        public bool TryGetObjectIcon(uint objectId, out uint texture, out int width, out int height) =>
            Reply($"object:{objectId:X8}", out texture, out width, out height);

        public uint UploadOwned(byte[] rgba, int width, int height, string debugName)
        {
            if (Throw)
                throw new InvalidOperationException("upload failed");
            Asked.Add($"upload:{width}x{height}:{debugName}");
            return Answer;
        }

        public bool ReleaseOwned(uint texture)
        {
            if (Throw)
                throw new InvalidOperationException("release failed");
            Asked.Add($"release:{texture}");
            return true;
        }

        public bool IsLinearFiltered(uint texture) => true;

        private bool Reply(string what, out uint texture, out int width, out int height)
        {
            if (Throw)
                throw new InvalidOperationException("dat read failed");
            Asked.Add(what);
            texture = Answer;
            width = height = Answer == 0 ? 0 : 32;
            return AlwaysFound || Answer != 0;
        }
    }
}
