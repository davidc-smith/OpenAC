using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;
using Xunit;

namespace AcDream.App.Tests.UI;

public sealed class ListButtonColumnTests
{
    private static (uint, int, int) Sprite(uint id) => (1u, 32, 32);

    private sealed class FoundBinding
    {
        public IReadOnlyList<string> Names { get; } = new[] { "Drudge", "Rat", "Mosswart" };
        public IReadOnlyList<string> Captions { get; } = new[] { "Select", "", "Select" };
        public int Selected { get; set; } = -1;
        public List<int> Acted { get; } = new();
        public Action<int> Act => row => Acted.Add(row);
    }

    private static string FoundXml(string buttonColumn) =>
        "<panel x=\"0\" y=\"0\" w=\"200\" h=\"100\">" +
        "<list x=\"0\" y=\"0\" w=\"180\" h=\"60\" rowheight=\"18\" selected=\"{Selected}\">" +
        "  <column type=\"text\" width=\"100\" items=\"{Names}\"/>" +
        buttonColumn +
        "</list></panel>";

    [Fact]
    public void ButtonColumn_BindsItsCaptionsAndOnclick()
    {
        var binding = new FoundBinding();
        var panel = MarkupDocument.Build(
            FoundXml("<column type=\"button\" width=\"60\" items=\"{Captions}\" onclick=\"{Act}\"/>"),
            binding, Sprite);
        var list = Assert.IsType<UiMarkupList>(panel.Children[0]);

        var column = list.Columns![1];
        Assert.Equal(UiMarkupListColumnKind.Button, column.Kind);
        Assert.Equal(binding.Captions, column.TextSource!());
        Assert.Equal(3, column.RowCount());
        column.ButtonClicked!(2);
        Assert.Equal(new[] { 2 }, binding.Acted);
    }

    [Fact]
    public void ButtonColumn_MissingOnclick_ThrowsAtBuild()
    {
        var ex = Assert.Throws<FormatException>(() => MarkupDocument.Build(
            FoundXml("<column type=\"button\" width=\"60\" items=\"{Captions}\"/>"),
            new FoundBinding(), Sprite));
        Assert.Contains("column[1] type=\"button\" onclick", ex.Message);
    }

    [Fact]
    public void ButtonColumn_MissingItems_ThrowsAtBuild()
    {
        var ex = Assert.Throws<FormatException>(() => MarkupDocument.Build(
            FoundXml("<column type=\"button\" width=\"60\" onclick=\"{Act}\"/>"),
            new FoundBinding(), Sprite));
        Assert.Contains("column[1] type=\"button\" items", ex.Message);
    }

    [Fact]
    public void UnknownColumnType_NamesButtonAmongTheExpectedTypes()
    {
        var ex = Assert.Throws<FormatException>(() => MarkupDocument.Build(
            FoundXml("<column type=\"bogus\" width=\"60\" items=\"{Captions}\"/>"),
            new FoundBinding(), Sprite));
        Assert.Contains("button", ex.Message);
    }

    private static UiMarkupList MakeList(
        List<int> acted, List<int> selections, string[]? captions = null)
    {
        captions ??= new[] { "Go", "", "Go" };
        var list = new UiMarkupList
        {
            Width = 100f, Height = 60f, RowHeight = 20f,
            SelectedIndexSource = () => -1,
            SelectionChanged = row => selections.Add(row),
            BackgroundColor = default, BorderColor = default,
            Columns = new[]
            {
                UiMarkupListColumn.Text(50f, () => new[] { "a", "b", "c" }, null),
                UiMarkupListColumn.Button(50f, () => captions, row => acted.Add(row)),
            },
        };
        var (_, ctx) = MakeContext(200f, 200f);
        list.DrawSelfAndChildren(ctx);
        return list;
    }

    [Fact]
    public void PressAndReleaseOnAButton_FiresOnclickWithTheRow_WithoutSelecting()
    {
        var acted = new List<int>();
        var selections = new List<int>();
        var list = MakeList(acted, selections);

        // Row 2 (y = 40..60), button column (x = 50..100).
        list.OnEvent(new UiEvent { Type = UiEventType.MouseDown, Data1 = 70, Data2 = 45 });
        Assert.Empty(acted);
        list.OnEvent(new UiEvent { Type = UiEventType.MouseUp, Data1 = 72, Data2 = 47 });

        Assert.Equal(new[] { 2 }, acted);
        Assert.Empty(selections);
    }

    [Fact]
    public void ReleaseOffThePressedButton_DoesNotFire()
    {
        var acted = new List<int>();
        var list = MakeList(acted, new List<int>());

        list.OnEvent(new UiEvent { Type = UiEventType.MouseDown, Data1 = 70, Data2 = 5 });
        list.OnEvent(new UiEvent { Type = UiEventType.MouseUp, Data1 = 70, Data2 = 45 });
        list.OnEvent(new UiEvent { Type = UiEventType.MouseDown, Data1 = 70, Data2 = 5 });
        list.OnEvent(new UiEvent { Type = UiEventType.MouseUp, Data1 = 10, Data2 = 5 });

        Assert.Empty(acted);
    }

    [Fact]
    public void EmptyCaption_IsNoButton_AndAClickOnItDoesNothing()
    {
        var acted = new List<int>();
        var selections = new List<int>();
        var list = MakeList(acted, selections);

        list.OnEvent(new UiEvent { Type = UiEventType.MouseDown, Data1 = 70, Data2 = 25 });
        list.OnEvent(new UiEvent { Type = UiEventType.MouseUp, Data1 = 70, Data2 = 25 });

        Assert.Empty(acted);
        Assert.Empty(selections);
        Assert.Equal(UiControlState.Normal, list.ButtonStateForTest(1, 1));
    }

    [Fact]
    public void ButtonCell_ShowsHoverAndPressOnlyOnTheCellUnderThePointer()
    {
        var list = MakeList(new List<int>(), new List<int>());
        Assert.True(list.ReceivesHoverMouseMove);

        list.OnEvent(new UiEvent { Type = UiEventType.HoverEnter, Data1 = 70, Data2 = 5 });
        Assert.Equal(UiControlState.Hovered, list.ButtonStateForTest(0, 1));
        Assert.Equal(UiControlState.Normal, list.ButtonStateForTest(2, 1));

        list.OnEvent(new UiEvent { Type = UiEventType.MouseMove, Data1 = 70, Data2 = 45 });
        Assert.Equal(UiControlState.Normal, list.ButtonStateForTest(0, 1));
        Assert.Equal(UiControlState.Hovered, list.ButtonStateForTest(2, 1));

        list.OnEvent(new UiEvent { Type = UiEventType.MouseDown, Data1 = 70, Data2 = 45 });
        Assert.Equal(UiControlState.Pressed, list.ButtonStateForTest(2, 1));

        // Dragged off the pressed button: it lets go of its pressed look.
        list.OnEvent(new UiEvent { Type = UiEventType.MouseMove, Data1 = 70, Data2 = 5 });
        Assert.Equal(UiControlState.Normal, list.ButtonStateForTest(2, 1));
        Assert.Equal(UiControlState.Normal, list.ButtonStateForTest(0, 1));

        list.OnEvent(new UiEvent { Type = UiEventType.MouseUp, Data1 = 70, Data2 = 5 });
        Assert.Equal(UiControlState.Hovered, list.ButtonStateForTest(0, 1));

        list.OnEvent(new UiEvent { Type = UiEventType.HoverLeave });
        Assert.Equal(UiControlState.Normal, list.ButtonStateForTest(0, 1));
    }

    [Fact]
    public void ListWithoutButtonColumns_TakesNoHoverMoves()
    {
        var list = new UiMarkupList
        {
            Columns = new[] { UiMarkupListColumn.Text(50f, () => new[] { "a" }, null) },
        };
        Assert.False(list.ReceivesHoverMouseMove);
    }

    [Fact]
    public void DisabledList_ShowsItsButtonsDisabled()
    {
        var list = MakeList(new List<int>(), new List<int>());
        list.Enabled = false;
        Assert.Equal(UiControlState.Disabled, list.ButtonStateForTest(0, 1));
    }

    [Fact]
    public void ClassicButtonCell_DrawsAFaceInsideItsCell_OnlyWhereThereIsACaption()
    {
        var face = new Vector4(0.2f, 0.4f, 0.6f, 1f);
        var list = new UiMarkupList
        {
            Width = 100f, Height = 60f, RowHeight = 20f,
            SelectedIndexSource = () => -1,
            BackgroundColor = default, BorderColor = default,
            ButtonColor = face,
            Columns = new[]
            {
                UiMarkupListColumn.Text(50f, () => new[] { "a", "b", "c" }, null),
                UiMarkupListColumn.Button(50f, () => new[] { "Go", "", "Go" }, _ => { }),
            },
        };
        var (renderer, ctx) = MakeContext(200f, 200f);

        list.DrawSelfAndChildren(ctx);

        var faceVerts = renderer.DebugSpriteSegmentVerts
            .Where(s => s.Texture == 0u)
            .SelectMany(s => Chunk(s.Verts))
            .Where(v => ColorMatches(v, face))
            .ToList();
        Assert.NotEmpty(faceVerts);
        Assert.All(faceVerts, v => Assert.InRange(v[0], 50f, 100f));
        // Rows 0 and 2 only: nothing in row 1 (y = 20..40).
        Assert.Contains(faceVerts, v => v[1] < 20f);
        Assert.Contains(faceVerts, v => v[1] > 40f);
        Assert.DoesNotContain(faceVerts, v => v[1] > 21f && v[1] < 39f);
    }

    [Fact]
    public void ButtonColumn_BindsItsButtonSize()
    {
        var panel = MarkupDocument.Build(
            FoundXml("<column type=\"button\" width=\"60\" items=\"{Captions}\" onclick=\"{Act}\" buttonw=\"40\" buttonh=\"12\"/>"),
            new FoundBinding(), Sprite);
        var column = Assert.IsType<UiMarkupList>(panel.Children[0]).Columns![1];
        Assert.Equal(40f, column.ButtonWidth);
        Assert.Equal(12f, column.ButtonHeight);
    }

    [Fact]
    public void ButtonColumn_NegativeButtonSize_ThrowsAtBuild()
    {
        var ex = Assert.Throws<FormatException>(() => MarkupDocument.Build(
            FoundXml("<column type=\"button\" width=\"60\" items=\"{Captions}\" onclick=\"{Act}\" buttonh=\"-1\"/>"),
            new FoundBinding(), Sprite));
        Assert.Contains("column[1] type=\"button\" buttonh", ex.Message);
    }

    [Fact]
    public void SizedButton_IsCentredInItsCell_AndOnlyTheButtonTakesClicks()
    {
        var face = new Vector4(0.2f, 0.4f, 0.6f, 1f);
        var acted = new List<int>();
        var list = new UiMarkupList
        {
            Width = 100f, Height = 60f, RowHeight = 20f,
            SelectedIndexSource = () => -1,
            BackgroundColor = default, BorderColor = default,
            ButtonColor = face,
            Columns = new[]
            {
                UiMarkupListColumn.Text(50f, () => new[] { "a" }, null),
                // Cell x = 50..100, y = 0..20; a 30x10 button sits at x = 60..90, y = 5..15.
                UiMarkupListColumn.Button(50f, () => new[] { "Go" }, row => acted.Add(row),
                    buttonWidth: 30f, buttonHeight: 10f),
            },
        };
        var (renderer, ctx) = MakeContext(200f, 200f);
        list.DrawSelfAndChildren(ctx);

        var faceVerts = renderer.DebugSpriteSegmentVerts
            .Where(s => s.Texture == 0u)
            .SelectMany(s => Chunk(s.Verts))
            .Where(v => ColorMatches(v, face))
            .ToList();
        Assert.NotEmpty(faceVerts);
        Assert.All(faceVerts, v => Assert.InRange(v[0], 60f, 90f));
        Assert.All(faceVerts, v => Assert.InRange(v[1], 5f, 15f));

        // Inside the cell but beside the button: nothing.
        list.OnEvent(new UiEvent { Type = UiEventType.MouseDown, Data1 = 55, Data2 = 10 });
        list.OnEvent(new UiEvent { Type = UiEventType.MouseUp, Data1 = 55, Data2 = 10 });
        list.OnEvent(new UiEvent { Type = UiEventType.MouseMove, Data1 = 75, Data2 = 2 });
        Assert.Equal(UiControlState.Normal, list.ButtonStateForTest(0, 1));
        Assert.Empty(acted);

        list.OnEvent(new UiEvent { Type = UiEventType.MouseDown, Data1 = 75, Data2 = 10 });
        list.OnEvent(new UiEvent { Type = UiEventType.MouseUp, Data1 = 75, Data2 = 10 });
        Assert.Equal(new[] { 0 }, acted);
    }

    [Fact]
    public void SizedButton_NeverGrowsPastItsCell()
    {
        var face = new Vector4(0.2f, 0.4f, 0.6f, 1f);
        var list = new UiMarkupList
        {
            Width = 100f, Height = 60f, RowHeight = 20f,
            SelectedIndexSource = () => -1,
            BackgroundColor = default, BorderColor = default,
            ButtonColor = face,
            Columns = new[]
            {
                UiMarkupListColumn.Text(50f, () => new[] { "a" }, null),
                UiMarkupListColumn.Button(50f, () => new[] { "Go" }, _ => { },
                    buttonWidth: 500f, buttonHeight: 500f),
            },
        };
        var (renderer, ctx) = MakeContext(200f, 200f);
        list.DrawSelfAndChildren(ctx);

        var faceVerts = renderer.DebugSpriteSegmentVerts
            .Where(s => s.Texture == 0u)
            .SelectMany(s => Chunk(s.Verts))
            .Where(v => ColorMatches(v, face))
            .ToList();
        Assert.All(faceVerts, v => Assert.InRange(v[0], 51f, 99f));
        Assert.All(faceVerts, v => Assert.InRange(v[1], 1f, 19f));
    }

    private static IEnumerable<float[]> Chunk(IReadOnlyList<float> verts)
    {
        for (int i = 0; i + 8 <= verts.Count; i += 8)
            yield return verts.Skip(i).Take(8).ToArray();
    }

    private static bool ColorMatches(float[] vertex, Vector4 color) =>
        MathF.Abs(vertex[4] - color.X) < 0.001f
        && MathF.Abs(vertex[5] - color.Y) < 0.001f
        && MathF.Abs(vertex[6] - color.Z) < 0.001f
        && MathF.Abs(vertex[7] - color.W) < 0.001f;

    private static (TextRenderer renderer, UiRenderContext ctx) MakeContext(float w, float h)
    {
        var device = new RecordingGpuDevice();
        var renderer = new TextRenderer(device, new NullGpuFrameSource(), "unused");
        renderer.Begin(new Vector2(w, h));
        var ctx = new UiRenderContext(renderer, new Vector2(w, h));
        return (renderer, ctx);
    }

    private sealed class NullGpuFrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame => null;
    }
}
