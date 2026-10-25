using System;

namespace AcDream.App.UI;

/// <summary>
/// A texture handle that can be given as a source and made the first time it
/// is needed (drawn, dragged or read) instead of when its element is built.
/// A list rebuilt for hundreds of spells then composes only the icons that
/// are actually shown.
/// </summary>
internal struct DeferredTexture
{
    private uint _texture;
    private Func<uint>? _source;

    public uint Get()
    {
        if (_source is { } source)
        {
            _source = null;
            _texture = source();
        }
        return _texture;
    }

    public void Set(uint texture)
    {
        _source = null;
        _texture = texture;
    }

    public void Defer(Func<uint>? source)
    {
        _texture = 0u;
        _source = source;
    }
}
