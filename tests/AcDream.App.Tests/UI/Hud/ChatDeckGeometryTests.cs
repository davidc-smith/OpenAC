using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckGeometryTests
{
    private static float Measure(string s) => s.Length * 7f;

    private static ChatDeckGeometry Make(float w = 460, float h = 220, int tabs = 6, bool search = false, string chip = "Say") =>
        ChatDeckGeometry.Create(w, h,
            Enumerable.Range(0, tabs).Select(i => new ChatDeckTabLabel("Tab" + i, i == 1 ? "3" : "")).ToList(),
            search, chip, Measure);

    [Fact]
    public void Tabs_NaturalWidth_LeftToRight()
    {
        ChatDeckGeometry g = Make(tabs: 3);
        Assert.Equal(ChatDeckGeometry.Pad, g.Tabs[0].X);
        float plain = 2 * ChatDeckGeometry.TabPadX + Measure("Tab0");
        Assert.Equal(plain, g.Tabs[0].W, 2);
        Assert.True(g.Tabs[1].W > plain);   // the badge adds room
        Assert.Equal(g.Tabs[0].X + g.Tabs[0].W + ChatDeckGeometry.TabGap, g.Tabs[1].X, 2);
    }

    [Fact]
    public void ManyTabs_ShareTheRow_NeverBelowMinimum_RestHidden()
    {
        ChatDeckGeometry squeezed = Make(w: 460, tabs: 9);
        Assert.All(squeezed.Tabs, t => Assert.True(t.W >= ChatDeckGeometry.MinTabWidth));
        Assert.Single(squeezed.Tabs.Select(t => MathF.Round(t.W, 2)).Distinct());

        ChatDeckGeometry overflow = Make(w: 280, tabs: 16);
        Assert.Contains(overflow.Tabs, t => t.W == 0);
        Assert.All(overflow.Tabs.Where(t => t.W > 0), t => Assert.True(t.X + t.W <= overflow.SearchStud.X));
    }

    [Fact]
    public void Rows_StackTopToBottom()
    {
        ChatDeckGeometry g = Make();
        Assert.Equal(460 - ChatDeckGeometry.Pad - ChatDeckGeometry.StudSize, g.SearchStud.X, 2);
        Assert.Equal(220 - ChatDeckGeometry.Pad - ChatDeckGeometry.InputHeight, g.InputRow.Y, 2);
        Assert.Equal(g.InputRow.X, g.Chip.X);
        Assert.Equal(g.Chip.X + g.Chip.W + 4, g.Input.X, 2);
        Assert.Equal(g.InputRow.X + g.InputRow.W, g.Input.X + g.Input.W, 2);
        Assert.True(g.Transcript.Y > g.Tabs[0].Y + g.Tabs[0].H);
        Assert.True(g.Transcript.Y + g.Transcript.H < g.InputRow.Y);
        Assert.Equal(0, g.SearchField.W);
    }

    [Fact]
    public void SearchOpen_PushesTheTranscriptDown()
    {
        ChatDeckGeometry closed = Make(), open = Make(search: true);
        Assert.True(open.SearchField.W > 0);
        Assert.Equal(closed.Transcript.Y + ChatDeckGeometry.SearchHeight + 4, open.Transcript.Y, 2);
    }

    [Fact]
    public void HitAt_FindsEachZone()
    {
        ChatDeckGeometry g = Make();
        DeckRect t2 = g.Tabs[2];
        Assert.Equal(new ChatDeckHit(ChatDeckZone.Tab, 2), g.HitAt(t2.X + 2, t2.Y + 2));
        Assert.Equal(ChatDeckZone.SearchStud, g.HitAt(g.SearchStud.X + 2, g.SearchStud.Y + 2).Zone);
        Assert.Equal(ChatDeckZone.Chip, g.HitAt(g.Chip.X + 2, g.Chip.Y + 2).Zone);
        Assert.Equal(ChatDeckZone.Frame, g.HitAt(2, 2).Zone);
        Assert.Equal(ChatDeckZone.None, g.HitAt(-1, 5).Zone);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(-3, "")]
    [InlineData(7, "7")]
    [InlineData(99, "99")]
    [InlineData(100, "99+")]
    [InlineData(5000, "99+")]
    public void Badge_IsCapped(int unread, string text) => Assert.Equal(text, ChatDeckBadges.Text(unread));
}
