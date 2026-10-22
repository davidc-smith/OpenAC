using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// A markup separator: a 1 px line across the middle of its box, horizontal
/// unless the box is taller than wide. It takes no input; clicks pass through
/// to whatever lies beneath.
/// </summary>
public sealed class UiMarkupSeparator : UiElement
{
    public static readonly Vector4 ClassicColor = UiCheckLamp.UncheckedInner;

    public Vector4 Color { get; set; } = ClassicColor;

    /// <summary>Re-read every frame in place of <see cref="Color"/>.</summary>
    public Func<Vector4>? ColorSource { get; set; }

    public bool Vertical => Height > Width;

    public UiMarkupSeparator()
    {
        ClickThrough = true;
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        if (Width <= 0f || Height <= 0f)
            return;
        Vector4 color = ColorSource?.Invoke() ?? Color;
        if (Vertical)
            ctx.DrawFill(MathF.Floor((Width - 1f) * 0.5f), 0f, 1f, Height, color);
        else
            ctx.DrawFill(0f, MathF.Floor((Height - 1f) * 0.5f), Width, 1f, color);
    }
}
