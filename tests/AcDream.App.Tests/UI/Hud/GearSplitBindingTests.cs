using AcDream.App.UI;
using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class GearSplitBindingTests
{
    [Fact]
    public void OpensAtHalf_ClampsAmount_AndOkSendsOnceThenCloses()
    {
        var sent = new List<(uint, uint, int, int)>();
        int closed = 0;
        var b = new GearSplitBinding { Close = () => closed++ };
        b.Open(new GearSplitRequest(0xA1, "Pyreal Mote", 25, 0x50000001, 4), (s, c, slot, n) => { sent.Add((s, c, slot, n)); return true; });
        Assert.Equal(12, b.Amount);
        Assert.Equal("Split Pyreal Mote (25)", b.Title);
        b.SetAmount(99); Assert.Equal(24, b.Amount);
        b.SetAmount(0); Assert.Equal(1, b.Amount);
        b.Half(); Assert.Equal(12, b.Amount);
        b.Ok();
        b.Ok();
        Assert.Equal([(0xA1u, 0x50000001u, 4, 12)], sent);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Ok_StaysOpenWhenTheSendIsRefused_AndClosesOnALaterSuccess()
    {
        int closed = 0;
        var b = new GearSplitBinding { Close = () => closed++ };
        b.Open(new GearSplitRequest(1, "x", 10, 2, 0), (_, _, _, _) => false);
        b.Ok();
        Assert.Equal(0, closed);
        int sends = 0;
        b.Open(new GearSplitRequest(1, "x", 10, 2, 0), (_, _, _, _) => { sends++; return true; });
        b.Ok();
        Assert.Equal((1, 1), (sends, closed));
    }

    [Fact]
    public void Cancel_SendsNothing()
    {
        int sends = 0;
        var b = new GearSplitBinding { Close = () => { } };
        b.Open(new GearSplitRequest(1, "x", 2, 2, 0), (_, _, _, _) => { sends++; return true; });
        b.Cancel();
        b.Ok();
        Assert.Equal(0, sends);
    }

    [Fact]
    public void Markup_Builds()
    {
        var b = new GearSplitBinding { Close = () => { } };
        b.Open(new GearSplitRequest(1, "x", 10, 2, 0), (_, _, _, _) => true);
        UiElement window = MarkupDocument.Build(GearSplitBinding.Markup, b, _ => (0u, 0, 0));
        Assert.NotNull(window);
    }
}
