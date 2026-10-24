using System;
using System.Numerics;
using AcDream.App.Rendering;

namespace AcDream.App.UI;

public sealed class UiTextureElement : UiElement
{
    public UiTextureElement() => ClickThrough = true;

    private DeferredTexture _texture;

    /// <summary>The texture; one given by <see cref="TextureSource"/> is made when first needed.</summary>
    public uint Texture
    {
        get => _texture.Get();
        set => _texture.Set(value);
    }

    /// <summary>Makes the texture the first time it is drawn or read.</summary>
    public Func<uint>? TextureSource { init => _texture.Defer(value); }
    public Vector4 Tint { get; set; } = Vector4.One;

    protected override void OnDraw(UiRenderContext ctx)
    {
        if (Texture != 0u)
            ctx.DrawSprite(Texture, 0f, 0f, Width, Height, 0f, 0f, 1f, 1f, Tint);
    }
}
