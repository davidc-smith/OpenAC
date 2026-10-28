using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

/// <summary>
/// The collapsed Chat Deck's strip: one stud per tab from the top, the ⇥ stud at the bottom,
/// and where the strip and the flown-out window sit on screen.
/// </summary>
internal sealed class ChatDeckStripGeometry
{
    public const float Width = 34f;
    public const float StudWidth = 24f;
    public const float StudHeight = 22f;
    public const float StudGap = 5f;
    public const float PadTop = 10f;
    public const float PadBottom = 8f;
    /// <summary>How far short of the strip a fly-out starts its slide.</summary>
    public const float SlideDistance = 16f;

    public required DeckRect Frame { get; init; }
    /// <summary>One per tab; zero size for a tab with no room above the ⇥ stud.</summary>
    public required IReadOnlyList<DeckRect> Studs { get; init; }
    public required DeckRect PinStud { get; init; }

    /// <summary>The height that shows every tab's stud and the ⇥ stud.</summary>
    public static float FullHeight(int tabs) => PadTop + tabs * (StudHeight + StudGap) + StudHeight + PadBottom;

    public static ChatDeckStripGeometry Create(float height, int tabCount)
    {
        float x = (Width - StudWidth) / 2f;
        var pin = new DeckRect(x, height - PadBottom - StudHeight, StudWidth, StudHeight);
        var studs = new List<DeckRect>(tabCount);
        float y = PadTop;
        for (int i = 0; i < tabCount; i++)
        {
            bool fits = y + StudHeight <= pin.Y - StudGap + 0.01f;
            studs.Add(fits ? new DeckRect(x, y, StudWidth, StudHeight) : new DeckRect(x, y, 0f, 0f));
            y += StudHeight + StudGap;
        }
        return new ChatDeckStripGeometry { Frame = new DeckRect(0f, 0f, Width, height), Studs = studs, PinStud = pin };
    }

    public ChatDeckHit HitAt(float x, float y)
    {
        for (int i = 0; i < Studs.Count; i++)
            if (Studs[i].W > 0 && Studs[i].Contains(x, y)) return new(ChatDeckZone.Tab, i);
        if (PinStud.Contains(x, y)) return new(ChatDeckZone.PinStud);
        return Frame.Contains(x, y) ? new(ChatDeckZone.Frame) : new(ChatDeckZone.None);
    }

    /// <summary>The strip on screen: on <paramref name="edge"/>, from the window's top, tall enough for every stud where the screen allows.</summary>
    public static DeckRect Place(
        ChatDeckEdge edge, float windowTop, float windowHeight, int tabCount, float screenWidth, float screenHeight)
    {
        float h = MathF.Min(screenHeight, MathF.Max(windowHeight, FullHeight(tabCount)));
        float top = Math.Clamp(windowTop, 0f, MathF.Max(0f, screenHeight - h));
        float left = edge == ChatDeckEdge.Left ? 0f : screenWidth - Width;
        return new DeckRect(left, top, Width, h);
    }

    /// <summary>The flown-out window's left edge beside the strip; <paramref name="slide"/> 0..1 eases it the last <see cref="SlideDistance"/> out.</summary>
    public static float FlyoutLeft(ChatDeckEdge edge, float windowWidth, float screenWidth, double slide)
    {
        float back = (float)(1 - Math.Clamp(slide, 0, 1)) * SlideDistance;
        return edge == ChatDeckEdge.Left ? Width - back : screenWidth - Width - windowWidth + back;
    }
}
