using System.Numerics;
using AcDream.App.Tests.UI.Layout;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

/// <summary>
/// The two looks of a plugin's Confirm box: the retail confirmation art for
/// Classic windows, and a drawn box for themed windows (or when the art is
/// not to hand). Both pick the first button on Enter and the last on Escape.
/// </summary>
public sealed class UiPluginDialogTests
{
    private static UiDrawnPluginDialog Drawn(
        string title = "Delete", string message = "Delete it?",
        IReadOnlyList<string>? buttons = null, Func<PluginUiPalette?>? palette = null) =>
        new(title, message, buttons ?? ["OK", "Cancel"], font: null, palette ?? (static () => null));

    private static UiRetailPluginDialog Retail(params string[] buttons) =>
        UiRetailPluginDialog.TryCreate(
            FixtureLoader.LoadConfirmationDialog(),
            FixtureLoader.LoadConfirmationDialog,
            "Title",
            "Message",
            buttons)!;

    private static List<int> Record(UiPluginDialog dialog)
    {
        var chosen = new List<int>();
        dialog.Chosen = chosen.Add;
        return chosen;
    }

    private static bool Press(UiPluginDialog dialog, Key key) =>
        dialog.OnEvent(new UiEvent(dialog.EventId, dialog, UiEventType.KeyDown, Data0: (int)key));

    private static UiRoot Mount(UiElement dialog)
    {
        var root = new UiRoot { Width = 800f, Height = 600f };
        root.AddChild(dialog);
        MarkupDrawProbe.Draw(root, 800f, 600f); // lays out anchored retail parts
        return root;
    }

    private static void ClickAt(UiRoot root, UiElement element)
    {
        Vector2 p = element.ScreenPosition;
        int x = (int)p.X + 4, y = (int)p.Y + 4;
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
    }

    [Fact]
    public void EnterPicksTheFirstButtonAndEscapeTheLast()
    {
        var dialog = Drawn(buttons: ["Yes", "No", "Later"]);
        List<int> chosen = Record(dialog);

        Assert.True(Press(dialog, Key.Enter));
        Assert.True(Press(dialog, Key.KeypadEnter));
        Assert.True(Press(dialog, Key.Escape));
        Assert.False(Press(dialog, Key.W));

        Assert.Equal([0, 0, 2], chosen);
        Assert.Equal(2, dialog.CancelIndex);
    }

    [Fact]
    public void TheDrawnBoxIsLaidOutFromItsText()
    {
        var dialog = Drawn();

        Assert.Equal(260f, dialog.Width);
        Assert.Equal(86f, dialog.Height);
        Assert.Equal([62f, 134f], dialog.Buttons.Select(b => b.Left));
        Assert.All(dialog.Buttons, b => Assert.Equal(54f, b.Top));
        Assert.All(dialog.Buttons, b => Assert.Equal(64f, b.Width));
        Assert.Equal(["OK", "Cancel"], dialog.Buttons.Select(b => b.Text));
        Assert.Equal((242f, 2f, 16f, 16f),
            (dialog.CloseButton.Left, dialog.CloseButton.Top, dialog.CloseButton.Width, dialog.CloseButton.Height));
        Assert.Equal(62f, Drawn(message: "").Height);
    }

    [Fact]
    public void ALongTitleWidensTheBoxAndALongMessageWraps()
    {
        string title = new('T', 40);                       // 280 px without a font
        var wide = Drawn(title: title);
        Assert.Equal(280f + 2f * UiDrawnPluginDialog.Pad + UiDrawnPluginDialog.CloseSize + 4f, wide.Width);

        var wrapped = Drawn(message: string.Join(' ', Enumerable.Repeat("word", 40)));
        Assert.True(wrapped.Lines.Count > 1);
        Assert.All(wrapped.Lines, line => Assert.True(line.Length * 7f <= wrapped.Width - 20f));
    }

    [Fact]
    public void ClickingADrawnButtonOrTheCloseButtonChooses()
    {
        var dialog = Drawn(buttons: ["Yes", "No", "Later"]);
        List<int> chosen = Record(dialog);
        UiRoot root = Mount(dialog);

        ClickAt(root, dialog.Buttons[1]);
        ClickAt(root, dialog.CloseButton);

        Assert.Equal([1, 2], chosen);
    }

    [Fact]
    public void TheDrawnBoxTakesClassicColoursOrThePalette()
    {
        var classic = MarkupDrawProbe.Draw(Drawn());
        Assert.Equal(1, MarkupDrawProbe.CountQuads(classic, UiDrawnPluginDialog.ClassicBackground));
        Assert.Equal(1, MarkupDrawProbe.CountQuads(classic, UiDrawnPluginDialog.ClassicTitleBar));

        PluginUiPalette? palette = PluginUiPalette.Moss;
        var themed = Drawn(palette: () => palette);
        Assert.Equal(1, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(themed), PluginUiPalette.Moss.Background));
        Assert.Equal(PluginUiPalette.Moss.Accent, themed.Buttons[0].BorderColor);

        palette = PluginUiPalette.Brass;
        themed.TickSelfAndChildren(0.016d);
        Assert.Equal(PluginUiPalette.Brass.Accent, themed.Buttons[0].BorderColor);
        Assert.Equal(PluginUiPalette.Brass.Field, themed.Buttons[1].BackgroundColor);
    }

    [Fact]
    public void APressOnTheBodyFocusesTheDialog()
    {
        var drawn = Drawn();
        UiRoot root = Mount(drawn);
        root.OnMouseDown(UiMouseButton.Left, (int)drawn.Left + 20, (int)drawn.Top + 35);
        Assert.Same(drawn, root.KeyboardFocus);

        var retail = Retail("Yes", "No");
        UiRoot retailRoot = Mount(retail);
        retailRoot.OnMouseDown(UiMouseButton.Left, (int)retail.Left + 200, (int)retail.Top + 20);
        Assert.Same(retail, retailRoot.KeyboardFocus);
    }

    [Fact]
    public void TwoRetailButtonsSitWhereYesAndNoDo()
    {
        var dialog = Retail("Yes", "No");

        Assert.Equal(400f, dialog.Width);
        Assert.Equal([80f, 240f], dialog.Buttons.Select(b => b.Left));
        Assert.Equal(["Yes", "No"], dialog.Buttons.Select(b => b.Label));
    }

    [Fact]
    public void OneRetailButtonIsCentred()
    {
        ImportedLayout layout = FixtureLoader.LoadConfirmationDialog();
        var dialog = UiRetailPluginDialog.TryCreate(
            layout, FixtureLoader.LoadConfirmationDialog, "", "Done.", ["OK"])!;

        UiButton only = Assert.Single(dialog.Buttons);
        Assert.Equal(160f, only.Left);
        Assert.False(((UiButton)layout.FindElement(RetailConfirmationDialogView.RejectButtonId)!).Visible);
    }

    [Fact]
    public void FourRetailButtonsWidenTheBox()
    {
        var dialog = Retail("A", "B", "C", "D");

        Assert.Equal(416f, dialog.Width);
        Assert.Equal([24f, 120f, 216f, 312f], dialog.Buttons.Select(b => b.Left));
        Assert.Equal(4, dialog.Buttons.Distinct().Count());
        Assert.All(dialog.Buttons, b => Assert.Same(dialog.Buttons[0].Parent, b.Parent));
    }

    [Fact]
    public void TheRetailTitleIsAGoldFirstLine()
    {
        var dialog = Retail("Yes", "No");

        UiText.Line[] lines = dialog.Message.LinesProvider!().ToArray();
        Assert.Equal("Title", lines[0].Text);
        Assert.Equal(UiRetailPluginDialog.TitleColor, lines[0].Color);
        Assert.Equal("Message", lines[1].Text);
        Assert.Equal(109f, dialog.Height);   // 95 + (2 × 16 − 18)
        Assert.Equal(dialog.Popup.Height, dialog.Height);
    }

    [Fact]
    public void ClickingARetailButtonChoosesIt()
    {
        var dialog = Retail("A", "B", "C");
        List<int> chosen = Record(dialog);
        UiRoot root = Mount(dialog);

        ClickAt(root, dialog.Buttons[2]);
        ClickAt(root, dialog.Buttons[1]);

        Assert.Equal([2, 1], chosen);
    }

    [Fact]
    public void ALayoutWithoutTheConfirmationButtonsGivesNoRetailBox()
    {
        ImportedLayout message = RetailDialogFactoryTests.BuildDialogLayout(RetailDialogType.Message);

        Assert.Null(UiRetailPluginDialog.TryCreate(message, () => null, "T", "M", ["OK", "Cancel"]));
    }

    [Fact]
    public void PlaceCentresOnTheOwnerOrTheScreenAndStaysOnScreen()
    {
        var dialog = Drawn();                                    // 260 × 86
        var owner = new UiPanel { Left = 100f, Top = 100f, Width = 201f, Height = 150f };

        dialog.Place(owner, new Vector2(800f, 600f));
        Assert.Equal((MathF.Round(100f + 100.5f - 130f), 132f), (dialog.Left, dialog.Top));

        dialog.Place(null, new Vector2(800f, 600f));
        Assert.Equal((270f, 257f), (dialog.Left, dialog.Top));

        owner.Left = 700f;
        dialog.Place(owner, new Vector2(800f, 600f));
        Assert.Equal(540f, dialog.Left);                          // clamped to 800 − 260
    }
}
