using System.Numerics;
using AcDream.App.Tests.UI.Layout;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

/// <summary>
/// Plugin dialogs on screen: one at a time per owner window, each answered
/// exactly once, the owner blocked while it shows and focus handed back
/// when it closes.
/// </summary>
public sealed class PluginDialogHostTests
{
    private sealed class Fixture
    {
        public UiRoot Root { get; } = new() { Width = 800f, Height = 600f };
        public UiPanel Owner { get; } = new() { Left = 100f, Top = 100f, Width = 200f, Height = 150f };
        public UiSimpleButton OwnerField { get; } = new()
        {
            Left = 10f, Top = 10f, Width = 60f, Height = 20f, AcceptsFocus = true, TabStop = true,
        };
        public UiPanel Other { get; } = new() { Left = 500f, Top = 100f, Width = 200f, Height = 150f };
        public UiSimpleButton OtherField { get; } = new()
        {
            Left = 10f, Top = 10f, Width = 60f, Height = 20f, AcceptsFocus = true, TabStop = true,
        };
        public PluginDialogHost Host { get; }

        public Fixture(Func<ImportedLayout?>? retailLayout = null)
        {
            Owner.AddChild(OwnerField);
            Other.AddChild(OtherField);
            Root.AddChild(Owner);
            Root.AddChild(Other);
            Host = new PluginDialogHost(Root, retailLayout ?? (static () => null), font: null);
        }

        public IDisposable Confirm(UiElement? owner, List<int> answers, params string[] buttons) =>
            Host.Confirm("Title", "Message", buttons.Length == 0 ? ["OK", "Cancel"] : buttons, owner, answers.Add)!;

        /// <summary>A key pressed and released.</summary>
        public void Press(Key key)
        {
            Root.OnKeyDown((int)key);
            Root.OnKeyUp((int)key);
        }

        public void ClickAt(UiElement element)
        {
            Vector2 p = element.ScreenPosition;
            int x = (int)p.X + 4, y = (int)p.Y + 4;
            Root.OnMouseDown(UiMouseButton.Left, x, y);
            Root.OnMouseUp(UiMouseButton.Left, x, y);
        }
    }

    [Fact]
    public void AConfirmShowsCentredOnItsOwnerBlocksItAndTakesFocus()
    {
        var f = new Fixture();

        IDisposable handle = f.Confirm(f.Owner, []);

        UiPluginDialog view = f.Host.ShownFor(f.Owner)!;
        Assert.NotNull(handle);
        Assert.IsType<UiDrawnPluginDialog>(view);
        Assert.Same(f.Root, view.Parent);
        Assert.Equal(1, f.Owner.ModalBlockCount);
        Assert.Equal(MathF.Round(200f - view.Width * 0.5f), view.Left);
        Assert.Same(view, f.Root.KeyboardFocus);
    }

    [Fact]
    public void AButtonAnswersOnceAfterTheDialogIsGone()
    {
        var f = new Fixture();
        var answers = new List<int>();
        bool goneFirst = false;
        IDisposable handle = f.Host.Confirm("T", "M", ["OK", "Cancel"], f.Owner, index =>
        {
            answers.Add(index);
            goneFirst = f.Host.ShownFor(f.Owner) is null && f.Owner.ModalBlockCount == 0;
        })!;
        UiPluginDialog view = f.Host.ShownFor(f.Owner)!;

        f.Press(Key.Enter);
        handle.Dispose();

        Assert.Equal([0], answers);
        Assert.True(goneFirst);
        Assert.Null(view.Parent);
    }

    [Fact]
    public void DialogsForOneOwnerQueueAndOthersShowAtOnce()
    {
        var f = new Fixture();
        var first = new List<int>();
        var second = new List<int>();
        var other = new List<int>();
        f.Confirm(f.Owner, first);
        f.Confirm(f.Owner, second);
        f.Confirm(f.Other, other);
        UiPluginDialog firstView = f.Host.ShownFor(f.Owner)!;
        Assert.NotNull(f.Host.ShownFor(f.Other));
        Assert.Equal(3, f.Host.PendingCount);

        f.Root.SetKeyboardFocus(firstView);
        f.Press(Key.Escape);

        Assert.Equal([1], first);
        UiPluginDialog secondView = f.Host.ShownFor(f.Owner)!;
        Assert.NotSame(firstView, secondView);
        f.Press(Key.Enter);
        Assert.Equal([0], second);
        Assert.Empty(other);
    }

    [Fact]
    public void AnOwnerClosingAnswersEveryDialogQueuedOnIt()
    {
        var f = new Fixture();
        var answers = new List<int>();
        f.Host.Confirm("T", "M", ["A", "B"], f.Owner, i => answers.Add(10 + i));
        f.Host.Confirm("T", "M", ["A", "B", "C"], f.Owner, i => answers.Add(20 + i));

        f.Owner.Visible = false;
        f.Host.Tick();

        Assert.Equal([11, 22], answers);
        Assert.Equal(0, f.Host.PendingCount);
        Assert.Equal(0, f.Owner.ModalBlockCount);
    }

    [Fact]
    public void AnOwnerRemovedAnswersItsDialog()
    {
        var f = new Fixture();
        var answers = new List<int>();
        f.Confirm(f.Owner, answers);

        f.Root.RemoveChild(f.Owner);
        f.Host.Tick();

        Assert.Equal([1], answers);
    }

    [Fact]
    public void DisposingTheHandleAnswersTheLastButtonOnce()
    {
        var f = new Fixture();
        var shown = new List<int>();
        var queued = new List<int>();
        IDisposable shownHandle = f.Confirm(f.Owner, shown, "Yes", "No", "Later");
        IDisposable queuedHandle = f.Confirm(f.Owner, queued);

        queuedHandle.Dispose();
        Assert.Equal([1], queued);
        Assert.NotNull(f.Host.ShownFor(f.Owner));

        shownHandle.Dispose();
        shownHandle.Dispose();
        Assert.Equal([2], shown);
        Assert.Null(f.Host.ShownFor(f.Owner));
        Assert.Equal([1], queued); // never shown, never answered twice
    }

    [Fact]
    public void AHiddenOwnerOrNoOwnerCentresOnTheScreenAndBlocksNothing()
    {
        var f = new Fixture();
        f.Owner.Visible = false;
        var answers = new List<int>();

        f.Confirm(f.Owner, answers);
        f.Host.Tick();

        UiPluginDialog view = f.Host.ShownFor(null)!;
        Assert.Equal(MathF.Round(400f - view.Width * 0.5f), view.Left);
        Assert.Equal(0, f.Owner.ModalBlockCount);
        Assert.Empty(answers);
    }

    [Fact]
    public void FocusComesBackWhenTheDialogCloses()
    {
        var f = new Fixture();
        f.Root.SetKeyboardFocus(f.OtherField);
        f.Confirm(f.Owner, []);
        f.Press(Key.Enter);
        Assert.Same(f.OtherField, f.Root.KeyboardFocus);

        f.Root.SetKeyboardFocus(f.OwnerField);
        f.Confirm(f.Owner, []);
        Assert.NotSame(f.OwnerField, f.Root.KeyboardFocus);
        f.Press(Key.Enter);
        Assert.Same(f.OwnerField, f.Root.KeyboardFocus);

        IDisposable handle = f.Confirm(f.Owner, []);
        f.Root.SetKeyboardFocus(f.OtherField);   // the player moved on
        handle.Dispose();
        Assert.Same(f.OtherField, f.Root.KeyboardFocus);
    }

    [Fact]
    public void AThemedOwnerGetsADrawnBoxInItsPalette()
    {
        var f = new Fixture();
        var themed = new UiPluginMarkupPanel(
            _ => (0u, 0, 0), new PluginUiThemeSettings { Theme = PluginUiTheme.Moss })
        {
            Left = 100f, Top = 300f, Width = 200f, Height = 150f,
        };
        f.Root.AddChild(themed);

        f.Confirm(themed, []);

        var view = Assert.IsType<UiDrawnPluginDialog>(f.Host.ShownFor(themed));
        Assert.Equal(1, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(view), PluginUiPalette.Moss.Background));
    }

    [Fact]
    public void AClassicOwnerGetsTheRetailBoxWhenTheArtIsThere()
    {
        var f = new Fixture(FixtureLoader.LoadConfirmationDialog);

        f.Confirm(f.Owner, [], "Yes", "No", "Later");

        var view = Assert.IsType<UiRetailPluginDialog>(f.Host.ShownFor(f.Owner));
        Assert.Equal(["Yes", "No", "Later"], view.Buttons.Select(b => b.Label));
    }

    [Fact]
    public void WithoutUsableArtTheClassicBoxIsDrawn()
    {
        var throwing = new Fixture(static () => throw new InvalidOperationException("no catalog"));
        throwing.Confirm(throwing.Owner, []);
        Assert.IsType<UiDrawnPluginDialog>(throwing.Host.ShownFor(throwing.Owner));

        var partless = new Fixture(static () => RetailDialogFactoryTests.BuildDialogLayout(RetailDialogType.Message));
        partless.Confirm(partless.Owner, []);
        Assert.IsType<UiDrawnPluginDialog>(partless.Host.ShownFor(partless.Owner));
    }

    [Fact]
    public void DisposingTheHostAnswersEverythingAndEndsModals()
    {
        var f = new Fixture();
        var answers = new List<int>();
        f.Host.Confirm("T", "M", ["A", "B"], f.Owner, i => answers.Add(10 + i));
        f.Host.Confirm("T", "M", ["A", "B", "C"], f.Owner, i => answers.Add(20 + i));
        f.Host.Confirm("T", "M", ["A"], null, i => answers.Add(30 + i));
        var panel = new UiPanel { Left = 300f, Top = 300f, Width = 100f, Height = 100f };
        f.Root.AddChild(panel);
        f.Host.ShowModal(panel, f.Other);

        f.Host.Dispose();

        Assert.Equal([11, 22, 30], answers);
        Assert.Equal(0, f.Owner.ModalBlockCount);
        Assert.Equal(0, f.Other.ModalBlockCount);
        Assert.Null(f.Host.Confirm("T", "M", ["OK"], null, _ => { }));
    }

    [Fact]
    public void AThrowingAnswerDoesNotStopTheQueue()
    {
        var f = new Fixture();
        var second = new List<int>();
        f.Host.Confirm("T", "M", ["OK"], f.Owner, _ => throw new InvalidOperationException("plugin bug"));
        f.Confirm(f.Owner, second);

        f.Press(Key.Enter);

        Assert.NotNull(f.Host.ShownFor(f.Owner));
        f.Press(Key.Enter);
        Assert.Equal([0], second);
    }

    [Fact]
    public void AModalPanelBlocksItsOwnerUntilItHides()
    {
        var f = new Fixture();
        var panel = new UiPanel { Left = 300f, Top = 300f, Width = 100f, Height = 100f };
        f.Root.AddChild(panel);

        f.Host.ShowModal(panel, f.Owner);
        f.Host.ShowModal(panel, f.Owner);
        Assert.Equal(1, f.Owner.ModalBlockCount);
        Assert.True(panel.ZOrder > f.Owner.ZOrder);

        f.Root.OnMouseDown(UiMouseButton.Left, 115, 115);
        f.Root.OnMouseUp(UiMouseButton.Left, 115, 115);
        Assert.Equal(UiElement.ModalFlashDuration, panel.ModalFlashSeconds);

        panel.Visible = false;
        f.Host.Tick();
        Assert.Equal(0, f.Owner.ModalBlockCount);
    }

    [Fact]
    public void AModalPanelEndsWhenRemovedAndMovesToANewOwner()
    {
        var f = new Fixture();
        var panel = new UiPanel { Left = 300f, Top = 300f, Width = 100f, Height = 100f };
        f.Root.AddChild(panel);

        f.Host.ShowModal(panel, f.Owner);
        f.Host.ShowModal(panel, f.Other);
        Assert.Equal(0, f.Owner.ModalBlockCount);
        Assert.Equal(1, f.Other.ModalBlockCount);

        f.Root.RemoveChild(panel);
        f.Host.Tick();
        Assert.Equal(0, f.Other.ModalBlockCount);
    }

    [Fact]
    public void AModalPanelWithoutAUsableOwnerBlocksNothing()
    {
        var f = new Fixture();
        var panel = new UiPanel { Left = 300f, Top = 300f, Width = 100f, Height = 100f };
        f.Root.AddChild(panel);

        f.Host.ShowModal(panel, null);
        f.Host.ShowModal(panel, panel);

        Assert.Equal(0, panel.ModalBlockCount);
        Assert.Equal(0, f.Owner.ModalBlockCount);
    }

    [Fact]
    public void ADialogFollowsItsOwner()
    {
        var f = new Fixture();
        f.Confirm(f.Owner, []);
        UiPluginDialog view = f.Host.ShownFor(f.Owner)!;
        float left = view.Left;

        f.Owner.Left += 50f;
        f.Host.Tick();

        Assert.Equal(left + 50f, view.Left);
    }

    [Fact]
    public void AQueuedDialogShowsOnTickWhenItsOwnerReturnsAfterAStall()
    {
        var f = new Fixture();
        var first = new List<int>();
        var second = new List<int>();
        f.Confirm(f.Owner, first);
        f.Confirm(f.Owner, second);

        f.Owner.Visible = false;
        f.Press(Key.Enter);
        f.Owner.Visible = true;
        f.Host.Tick();

        Assert.Equal([0], first);
        Assert.NotNull(f.Host.ShownFor(f.Owner));
        f.Press(Key.Enter);
        Assert.Equal([0], second);
    }

    [Fact]
    public void FocusComesBackAfterADrawnButtonAnswers()
    {
        var f = new Fixture();
        f.Root.SetKeyboardFocus(f.OtherField);
        var answers = new List<int>();
        f.Confirm(f.Owner, answers);
        var view = Assert.IsType<UiDrawnPluginDialog>(f.Host.ShownFor(f.Owner));

        f.ClickAt(view.Buttons[1]);

        Assert.Equal([1], answers);
        Assert.Same(f.OtherField, f.Root.KeyboardFocus);
    }

    [Fact]
    public void FocusComesBackAfterARetailButtonAnswers()
    {
        var f = new Fixture(FixtureLoader.LoadConfirmationDialog);
        f.Root.SetKeyboardFocus(f.OtherField);
        var answers = new List<int>();
        f.Confirm(f.Owner, answers, "Yes", "No");
        var view = Assert.IsType<UiRetailPluginDialog>(f.Host.ShownFor(f.Owner));
        MarkupDrawProbe.Draw(f.Root, 800f, 600f); // lays out anchored retail parts

        f.ClickAt(view.Buttons[0]);

        Assert.Equal([0], answers);
        Assert.Same(f.OtherField, f.Root.KeyboardFocus);
    }

    [Fact]
    public void APressAnswerLeavesFocusThePlayerMovedElsewhere()
    {
        var f = new Fixture();
        f.Root.SetKeyboardFocus(f.OwnerField);
        var answers = new List<int>();
        IDisposable handle = f.Confirm(f.Owner, answers);
        var view = Assert.IsType<UiDrawnPluginDialog>(f.Host.ShownFor(f.Owner));

        // The press lands on a button, then the player clicks into another
        // window before the answer is taken.
        Vector2 p = view.Buttons[0].ScreenPosition;
        f.Root.OnMouseDown(UiMouseButton.Left, (int)p.X + 4, (int)p.Y + 4);
        f.Root.SetKeyboardFocus(f.OtherField);
        f.Root.OnMouseUp(UiMouseButton.Left, (int)p.X + 4, (int)p.Y + 4);
        handle.Dispose();

        Assert.Same(f.OtherField, f.Root.KeyboardFocus);
    }

    [Fact]
    public void AHeldEnterAnswersOneDialogUntilItIsReleased()
    {
        var f = new Fixture();
        var first = new List<int>();
        var second = new List<int>();
        f.Confirm(f.Owner, first);
        f.Confirm(f.Owner, second);

        f.Root.OnKeyDown((int)Key.Enter);
        f.Root.OnKeyDown((int)Key.Enter);   // auto-repeat, no release yet
        Assert.Equal([0], first);
        Assert.Empty(second);
        Assert.NotNull(f.Host.ShownFor(f.Owner));

        f.Root.OnKeyUp((int)Key.Enter);
        f.Root.OnKeyDown((int)Key.Enter);
        Assert.Equal([0], second);
    }

    [Fact]
    public void AnAnswerKeysCharDoesNotLandInTheFieldThatGetsFocusBack()
    {
        var f = new Fixture();
        var field = new UiField { Left = 10f, Top = 40f, Width = 150f, Height = 40f, OneLine = false }; // takes Enter's CR as a newline
        f.Other.AddChild(field);
        f.Root.SetKeyboardFocus(field);
        f.Confirm(f.Owner, []);

        f.Root.OnKeyDown((int)Key.Enter);
        f.Root.OnChar('\r');
        f.Root.OnKeyUp((int)Key.Enter);

        Assert.Same(field, f.Root.KeyboardFocus);
        Assert.Equal(string.Empty, field.Text);
    }
}
