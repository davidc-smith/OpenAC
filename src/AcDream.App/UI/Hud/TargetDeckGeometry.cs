namespace AcDream.App.UI.Hud;

public enum TargetZone { None, Portrait, Name, Health, Stamina, Mana, Row0, Row1, Row2, Row3, PluginRow0, PluginRow1, Stud0, Stud1, Stud2, Stud3 }

/// <summary>Fixed layout of the Target Deck at scale 1 (shape B: info left, action column right).</summary>
public static class TargetDeckGeometry
{
    public const float Width = 340f, Height = 104f;
    public const float Pad = 10f, StudW = 24f, StudH = 20f, StudGap = 2f;
    public static readonly DeckRect Frame = new(0, 0, Width, Height);

    /// <summary>Where the frame body is drawn: inset so its 2-point dark edge and its anti-aliasing stay inside the deck.</summary>
    public static readonly DeckRect Body = new(3, 3, Width - 6, Height - 6);
    public static readonly DeckRect Portrait = new(Pad, Pad, 30, 30);
    public static readonly DeckRect Name = new(Pad + 36, Pad - 1, Width - Pad * 2 - 36 - StudW - 8, 16);
    public static readonly DeckRect Sub = new(Pad + 36, Pad + 16, Name.W, 13);
    public static readonly DeckRect Health = new(Pad, 46, Width - Pad * 2 - StudW - 8, 14);
    public static readonly DeckRect Stamina = new(Pad, 63, Health.W, 6);
    public static readonly DeckRect Mana = new(Pad, 72, Health.W, 6);

    /// <summary>The most studs the action column holds.</summary>
    public const int StudCount = 4;

    /// <summary>Where the stud column starts: centred on the plate's height, so all four
    /// studs (4 × 20 + 3 × 2 = 86) sit inside the frame's bronze rule.</summary>
    private const float StudTop = (Height - (StudCount * StudH + (StudCount - 1) * StudGap)) / 2f;

    /// <summary>Info line i (0-3) below the identity block: kind rows, then plugin rows.</summary>
    public static DeckRect Line(int i) => new(Pad, 46 + i * 13, Health.W, 13);

    /// <summary>Plugin line i (0-1) under the vitals bars, both above the frame's bottom rule.</summary>
    public static DeckRect PluginLine(int i) => new(Pad, 79 + i * 10, Health.W, 10);

    public static DeckRect Stud(int i) => new(Width - Pad - StudW, StudTop + i * (StudH + StudGap), StudW, StudH);
}
