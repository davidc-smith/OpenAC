using AcDream.App.UI;
using AcDream.App.UI.Markup;

namespace AcDream.App.Tests.UI;

/// <summary>
/// <c>&lt;repeat&gt;</c> each tick: copies are kept, moved, added and removed
/// as the list changes (R4/R5), kept keys follow new item objects, duplicate
/// keys fall back to index matching, and a removed copy gives back everything
/// it had (elements, flex items, focus, theme actions, popups, disposables).
/// Not run alongside other tests: one captures <see cref="Console.Out"/>.
/// </summary>
[Collection(MarkupRepeatConsoleCollection.Name)]
public sealed class MarkupRepeatReconcileTests
{
    private sealed class Row
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public uint Color { get; set; } = 0xFFFF0000u;
        public Action<uint> SetColor => v => Color = v;
    }

    private sealed class Section
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public bool Broken { get; set; }
        private IReadOnlyList<Row> _rows = [];
        public IReadOnlyList<Row> Rows
        {
            get => Broken ? throw new InvalidOperationException("broken rows") : _rows;
            set => _rows = value;
        }
    }

    private sealed class Binding
    {
        public IReadOnlyList<Row>? Rows { get; set; } = [];
        public IReadOnlyList<Section>? Sections { get; set; } = [];
    }

    /// <summary>A list built on every read, as <c>=&gt; _all.Where(...).ToList()</c> is.</summary>
    private sealed class ComputedBinding(Row[] rows)
    {
        public IReadOnlyList<Row> Rows => rows.ToList();
    }

    private sealed class FakeState : IMarkupControlState, IForgetsKeyedState
    {
        public Dictionary<(string, string), string> Values { get; } = new();
        public int Generation => 1;
        public int ForgetCalls { get; private set; }
        public string? Get(string control, string kind) => Values.GetValueOrDefault((control, kind));
        public void Set(string control, string kind, string value) => Values[(control, kind)] = value;
        public void Remove(string control, string kind) => Values.Remove((control, kind));
        public void ForgetKeyed(HashSet<string> names, HashSet<string> paths)
        {
            ForgetCalls++;
            var nameLookup = names.GetAlternateLookup<ReadOnlySpan<char>>();
            var pathLookup = paths.GetAlternateLookup<ReadOnlySpan<char>>();
            foreach (var key in Values.Keys.Where(k => KeyedControlState.IsUnder(k.Item1, nameLookup, pathLookup)).ToList())
                Values.Remove(key);
        }
    }

    private sealed class FakeTextures : IMarkupTextureService
    {
        private uint _next = 100;
        public List<uint> Uploads { get; } = new();
        public List<uint> Released { get; } = new();
        public uint Upload(byte[] rgba, int width, int height, string debugName)
        {
            Uploads.Add(_next);
            return _next++;
        }
        public void Release(uint texture) => Released.Add(texture);
    }

    private sealed class DisposableProbe : UiElement, IDisposable
    {
        public int Disposed { get; private set; }
        public void Dispose() => Disposed++;
    }

    private sealed class Mounted(UiRoot root, MarkupWindow window, PluginUiThemeSettings? themes)
    {
        private long _now;
        public UiRoot Root { get; } = root;
        public MarkupWindow Window { get; } = window;
        public PluginUiThemeSettings? Themes { get; } = themes;
        public UiPluginContentHost Content => Assert.IsType<UiPluginContentHost>(Window.ContentRoot);
        public UiMarkupRepeat Marker => Assert.IsType<UiMarkupRepeat>(Content.Children[0]);
        public UiElement[] Copies => Content.Children.Skip(1).ToArray();
        public UiElement[] Items => Content.Flex!.Items.Select(item => item.Element).ToArray();

        public void Frame()
        {
            Root.Tick(0.016, _now += 16);
            ThemeDrawCapture.Draw(Root);
        }
    }

    private static Row[] Rows(params string[] ids) =>
        ids.Select(id => new Row { Id = id, Label = id }).ToArray();

    private static Mounted Mount(string body, object binding, bool themed = false,
        IMarkupControlState? state = null, IMarkupTextureService? textures = null)
    {
        PluginUiThemeSettings? themes = themed ? new PluginUiThemeSettings() : null;
        var window = MarkupDocument.BuildWindow(
            $"<panel x=\"0\" y=\"0\" w=\"300\" h=\"400\" layout=\"column\"{(themed ? " theme=\"plugin\"" : "")}>{body}</panel>",
            binding, _ => (0u, 0, 0), themes: themes, fallbackTitle: "Demo", controlState: state, textures: textures);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(window.Frame);
        var mounted = new Mounted(root, window, themes);
        mounted.Frame();
        return mounted;
    }

    private static string[] Labels(IEnumerable<UiElement> elements) =>
        elements.OfType<UiLabel>().Select(label => label.TextSource!() ?? "").ToArray();

    private static int ThemeActions(UiElement frame) =>
        Assert.IsType<UiPluginMarkupPanel>(frame).ThemeActionCount;

    private static void Click(UiRoot root, UiElement element)
    {
        var at = element.ScreenPosition;
        root.OnMouseDown(UiMouseButton.Left, (int)at.X + 4, (int)at.Y + 4);
        root.OnMouseUp(UiMouseButton.Left, (int)at.X + 4, (int)at.Y + 4);
    }

    private const string Keyed = "<repeat items=\"{Rows}\" key=\"{Id}\"><label text=\"{Label}\" h=\"20\"/></repeat>";
    private const string Keyless = "<repeat items=\"{Rows}\"><label text=\"{Label}\" h=\"20\"/></repeat>";

    private readonly record struct Spot(uint Id, string Label);

    private sealed class SpotBinding
    {
        public IReadOnlyList<Spot> Rows { get; set; } = [];
    }

    [Fact]
    public void An_unchanged_tick_of_struct_items_allocates_nothing()
    {
        var binding = new SpotBinding { Rows = [new Spot(1, "a"), new Spot(2, "b")] };
        var m = Mount(Keyed, binding);
        for (int i = 0; i < 3; i++) m.Marker.TickSelfAndChildren(0.016);

        long start = GC.GetAllocatedBytesForCurrentThread();
        m.Marker.TickSelfAndChildren(0.016);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);

        // The typed key read still sees a change.
        binding.Rows = [new Spot(2, "b"), new Spot(3, "c")];
        m.Frame();
        Assert.Equal(new[] { "b", "c" }, Labels(m.Copies));
    }

    // 1
    [Fact]
    public void Editing_an_item_in_place_rebuilds_nothing()
    {
        var rows = Rows("a", "b");
        var m = Mount(Keyed, new Binding { Rows = rows });
        var before = m.Copies;

        rows[0].Label = "edited";
        m.Frame();

        Assert.Equal(before, m.Copies);
        Assert.Equal(new[] { "edited", "b" }, Labels(m.Copies));

        // Unchanged: the marker's tick reads the list and keys and allocates nothing.
        for (int i = 0; i < 3; i++) m.Marker.TickSelfAndChildren(0.016);
        long start = GC.GetAllocatedBytesForCurrentThread();
        m.Marker.TickSelfAndChildren(0.016);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);

        // A key changed in place is a change: that item gets a copy of its own.
        rows[1].Id = "c";
        m.Frame();
        Assert.Same(before[0], m.Copies[0]);
        Assert.NotSame(before[1], m.Copies[1]);
    }

    // 2
    [Fact]
    public void A_new_list_with_the_same_keys_keeps_every_copy_and_reads_the_new_objects()
    {
        var binding = new Binding { Rows = Rows("a", "b", "c") };
        var m = Mount(Keyed, binding);
        var before = m.Copies;

        var fresh = Rows("a", "b", "c");
        foreach (var row in fresh) row.Label = row.Id + "2";
        binding.Rows = fresh;
        m.Frame();

        Assert.Equal(before, m.Copies);
        Assert.Equal(new[] { "a2", "b2", "c2" }, Labels(m.Copies));
        Assert.Equal(fresh, m.Marker.Copies.Select(copy => copy.Source.Current));

        // The same list instance changed in place is read again too.
        var list = new List<Row>(fresh);
        binding.Rows = list;
        m.Frame();
        list[1] = new Row { Id = "b", Label = "b3" };
        m.Frame();
        Assert.Equal(before, m.Copies);
        Assert.Equal(new[] { "a2", "b3", "c2" }, Labels(m.Copies));
        list.RemoveAt(2);
        m.Frame();
        Assert.Equal(before[..2], m.Copies);
    }

    // 2b
    [Fact]
    public void A_list_built_on_every_read_with_the_same_items_does_nothing()
    {
        var binding = new ComputedBinding(Rows("a", "b", "c"));
        var m = Mount(Keyed, binding);
        var before = m.Copies;
        m.Frame();
        Assert.Equal(before, m.Copies);

        // Each tick costs reading the list and nothing more: no matching, no moves.
        for (int i = 0; i < 3; i++) m.Marker.TickSelfAndChildren(0.016);
        long start = GC.GetAllocatedBytesForCurrentThread();
        _ = binding.Rows;
        long read = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        m.Marker.TickSelfAndChildren(0.016);
        Assert.Equal(read, GC.GetAllocatedBytesForCurrentThread() - start);
        Assert.Equal(before, m.Copies);
    }

    // 3
    [Fact]
    public void A_focused_field_in_a_kept_copy_keeps_its_focus_and_text()
    {
        var binding = new Binding { Rows = Rows("a", "b") };
        var m = Mount("<repeat items=\"{Rows}\" key=\"{Id}\"><field w=\"120\" h=\"20\"/></repeat>", binding);
        var field = Assert.IsType<UiField>(m.Copies[1]);
        Click(m.Root, field);
        Assert.Same(field, m.Root.KeyboardFocus);
        foreach (char c in "hal") m.Root.OnChar(c);

        binding.Rows = [.. Rows("b", "a")];
        m.Frame();

        Assert.Same(field, m.Copies[0]);
        Assert.Same(field, m.Root.KeyboardFocus);
        Assert.Equal("hal", field.Text);
        m.Root.OnChar('f');
        Assert.Equal("half", field.Text);
    }

    // 4
    [Fact]
    public void A_removed_key_drops_its_copy_its_theme_actions_and_its_focus()
    {
        const string body = "<repeat items=\"{Rows}\" key=\"{Id}\"><group layout=\"row\" h=\"22\">"
            + "<field w=\"100\" h=\"20\"/><label text=\"{Label}\" grow=\"1\"/></group></repeat>";
        var binding = new Binding { Rows = Rows("a", "b", "c") };
        var m = Mount(body, binding, themed: true);
        var copies = m.Copies;
        var removed = copies[1];
        var field = removed.Children.OfType<UiField>().Single();
        Click(m.Root, field);
        Assert.Same(field, m.Root.KeyboardFocus);
        var keptLabel = copies[0].Children.OfType<UiLabel>().Single();
        var removedLabel = removed.Children.OfType<UiLabel>().Single();
        var classic = removedLabel.TextColor;

        binding.Rows = [binding.Rows[0], binding.Rows[2]];
        m.Frame();

        Assert.Equal(new[] { copies[0], copies[2] }, m.Copies);
        Assert.Equal(new[] { copies[0], copies[2] }, m.Items);
        Assert.Null(removed.Parent);
        Assert.Null(m.Root.KeyboardFocus);
        Assert.Equal(2, m.Marker.Copies.Count);
        var fresh = Mount(body, new Binding { Rows = Rows("a", "c") }, themed: true);
        Assert.Equal(ThemeActions(fresh.Window.Frame), ThemeActions(m.Window.Frame));

        m.Themes!.Theme = PluginUiTheme.Moss;
        m.Frame();
        Assert.Equal(m.Themes.Palette!.Text, keptLabel.TextColor);
        Assert.Equal(classic, removedLabel.TextColor);
    }

    // 5
    [Fact]
    public void A_reorder_moves_the_copies()
    {
        var binding = new Binding { Rows = Rows("a", "b", "c") };
        var m = Mount(Keyed, binding);
        var before = m.Copies;

        binding.Rows = [binding.Rows[2], binding.Rows[0], binding.Rows[1]];
        m.Frame();

        var expected = new[] { before[2], before[0], before[1] };
        Assert.Equal(expected, m.Copies);
        Assert.Equal(expected, m.Items);
        Assert.True(expected[0].Top < expected[1].Top && expected[1].Top < expected[2].Top,
            string.Join(",", expected.Select(e => e.Top)));
        Assert.Equal(new[] { 0, 1, 2 }, m.Marker.Copies.Select(copy => copy.Index));
    }

    // 6
    [Fact]
    public void A_key_added_in_the_middle_inserts_a_copy_there()
    {
        var a = Rows("a", "b", "c");
        var binding = new Binding { Rows = [a[0], a[2]] };
        var m = Mount(Keyed, binding);
        var before = m.Copies;

        binding.Rows = a;
        m.Frame();

        var after = m.Copies;
        Assert.Equal(3, after.Length);
        Assert.Same(before[0], after[0]);
        Assert.Same(before[1], after[2]);
        Assert.Equal(new[] { "a", "b", "c" }, Labels(after));
        Assert.Equal(after, m.Items);
        Assert.True(after[0].Top < after[1].Top && after[1].Top < after[2].Top,
            string.Join(",", after.Select(e => e.Top)));
    }

    // 7
    [Fact]
    public void Without_a_key_copies_match_by_index()
    {
        var binding = new Binding { Rows = Rows("a", "b", "c") };
        var m = Mount(Keyless, binding);
        var before = m.Copies;

        binding.Rows = Rows("x", "y");
        m.Frame();
        Assert.Equal(before[..2], m.Copies);
        Assert.Equal(new[] { "x", "y" }, Labels(m.Copies));
        Assert.Equal(before[..2], m.Items);

        binding.Rows = Rows("p", "q", "r", "s");
        m.Frame();
        var after = m.Copies;
        Assert.Equal(before[..2], after[..2]);
        Assert.Equal(new[] { "p", "q", "r", "s" }, Labels(after));
        Assert.Equal(after, m.Items);
        Assert.True(after[2].Top < after[3].Top && after[1].Top < after[2].Top,
            string.Join(",", after.Select(e => e.Top)));
    }

    // 7: an appended copy changes nothing the flex box measures, but it is laid out.
    [Fact]
    public void A_copy_appended_at_the_end_is_laid_out()
    {
        var binding = new Binding { Rows = Rows("a") };
        var m = Mount("<repeat items=\"{Rows}\"><field w=\"100\" h=\"20\"/></repeat>", binding);

        binding.Rows = Rows("a", "b");
        m.Frame();

        var fields = m.Copies;
        Assert.Equal(2, fields.Length);
        Assert.Equal(20f, fields[1].Height);
        Assert.True(fields[1].Top > fields[0].Top, $"{fields[1].Top} > {fields[0].Top}");
    }

    // 8
    [Fact]
    public void Duplicate_keys_log_once_and_match_by_index_until_the_keys_are_unique_again()
    {
        var binding = new Binding { Rows = Rows("a", "b") };
        var m = Mount(Keyed, binding);
        var before = m.Copies;
        var output = new StringWriter();
        var console = Console.Out;
        Console.SetOut(output);
        try
        {
            var dup = new[] { new Row { Id = "a", Label = "a1" }, new Row { Id = "a", Label = "a2" }, new Row { Id = "b", Label = "b1" } };
            binding.Rows = dup;
            m.Frame();
            m.Frame();
            binding.Rows = [.. dup];
            m.Frame();

            Assert.Equal(new[] { "markup <repeat items=\"{Rows}\">: duplicate or missing keys; rows are matched by position" },
                RepeatLines(output));
            var indexed = m.Copies;
            Assert.Equal(before, indexed[..2]);
            Assert.Equal(new[] { "a1", "a2", "b1" }, Labels(indexed));

            // Unique again: keyed matching. The copy built for "b" follows "b".
            var b = new Row { Id = "b", Label = "B" };
            var a = new Row { Id = "a", Label = "A" };
            binding.Rows = [b, a];
            m.Frame();
            Assert.Equal(new[] { before[1], before[0] }, m.Copies);
            Assert.Equal(new[] { "B", "A" }, Labels(m.Copies));
            Assert.Equal(m.Copies, m.Items);

            // A null key also falls back, and the warning comes back once.
            binding.Rows = [b, new Row { Id = null!, Label = "N" }];
            m.Frame();
            Assert.Equal(2, RepeatLines(output).Length);
            Assert.Equal(new[] { before[1], before[0] }, m.Copies);
            Assert.Equal(new[] { "B", "N" }, Labels(m.Copies));
        }
        finally
        {
            Console.SetOut(console);
        }
    }

    private static string[] RepeatLines(StringWriter output) =>
        output.ToString().Split('\n').Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("markup <repeat", StringComparison.Ordinal)).ToArray();

    private const string NestedInGroup =
        "<repeat items=\"{Sections}\" key=\"{Id}\"><group layout=\"column\"><label text=\"{Title}\" h=\"20\"/>"
        + "<repeat items=\"{Rows}\" key=\"{Id}\"><label text=\"{Label}\" h=\"20\"/></repeat></group></repeat>";

    private static Section Sec(string id, params string[] rows) =>
        new() { Id = id, Title = id.ToUpperInvariant(), Rows = Rows(rows.Select(r => id + r).ToArray()) };

    // 9
    [Fact]
    public void Nested_repeats_reconcile_at_both_levels()
    {
        var a = Sec("a", "1", "2");
        var b = Sec("b", "1");
        var binding = new Binding { Sections = [a, b] };
        var m = Mount(NestedInGroup, binding, themed: true);
        var groups = m.Copies;
        var aChildren = groups[0].Children.ToArray();
        var bChildren = groups[1].Children.ToArray();

        // An inner list change touches only its own section's copies.
        b.Rows = [b.Rows[0], new Row { Id = "b2", Label = "b2" }];
        m.Frame();
        Assert.Equal(groups, m.Copies);
        Assert.Equal(aChildren, groups[0].Children);
        Assert.Equal(bChildren, groups[1].Children.Take(3));
        Assert.Equal(new[] { "B", "b1", "b2" }, Labels(groups[1].Children));
        Assert.Equal(new[] { "B", "b1", "b2" },
            Labels(((UiFlexGroup)groups[1]).Flex.Items.Select(item => item.Element)));

        // Removing a section removes its inner rows and their scopes.
        var innerA = Assert.IsType<UiMarkupRepeat>(aChildren[1]);
        a.Rows = [.. a.Rows, new Row { Id = "a3", Label = "a3" }];   // read by nothing once A has gone
        binding.Sections = [b];
        m.Frame();
        Assert.Equal(new[] { groups[1] }, m.Copies);
        Assert.Empty(innerA.Copies);
        var fresh = Mount(NestedInGroup, new Binding { Sections = [Sec("b", "1", "2")] }, themed: true);
        Assert.Equal(ThemeActions(fresh.Window.Frame), ThemeActions(m.Window.Frame));
    }

    // 9, a repeat directly inside a repeat: its copies are the outer copy's siblings.
    [Fact]
    public void A_repeat_directly_inside_a_repeat_moves_and_drops_with_its_section()
    {
        const string body = "<repeat items=\"{Sections}\" key=\"{Id}\"><label text=\"{Title}\" h=\"20\"/>"
            + "<repeat items=\"{Rows}\" key=\"{Id}\"><label text=\"{Label}\" h=\"20\"/></repeat></repeat>"
            + "<label text=\"end\" h=\"20\"/>";
        var a = Sec("a", "1", "2");
        var b = Sec("b", "1");
        var binding = new Binding { Sections = [a, b] };
        var m = Mount(body, binding, themed: true);
        Assert.Equal(new[] { "A", "a1", "a2", "B", "b1", "end" }, Labels(m.Content.Children));

        binding.Sections = [b, a];
        m.Frame();
        Assert.Equal(new[] { "B", "b1", "A", "a1", "a2", "end" }, Labels(m.Content.Children));
        Assert.Equal(new[] { "B", "b1", "A", "a1", "a2", "end" }, Labels(m.Items));

        a.Rows = [a.Rows[1]];
        binding.Sections = [b, a, Sec("c", "1")];
        m.Frame();
        Assert.Equal(new[] { "B", "b1", "A", "a2", "C", "c1", "end" }, Labels(m.Content.Children));
        Assert.Equal(new[] { "B", "b1", "A", "a2", "C", "c1", "end" }, Labels(m.Items));

        binding.Sections = [binding.Sections[2]];
        m.Frame();
        Assert.Equal(new[] { "C", "c1", "end" }, Labels(m.Content.Children));
        Assert.Equal(new[] { "C", "c1", "end" }, Labels(m.Items));
        var fresh = Mount(body, new Binding { Sections = [Sec("c", "1")] }, themed: true);
        Assert.Equal(ThemeActions(fresh.Window.Frame), ThemeActions(m.Window.Frame));
    }

    // 10
    [Fact]
    public void A_named_expander_in_a_keyed_template_is_remembered_per_key()
    {
        const string body = "<repeat items=\"{Rows}\" key=\"{Id}\">"
            + "<expander name=\"section\" open=\"false\" h=\"60\" text=\"{Label}\"/></repeat>";
        var state = new FakeState();
        var m = Mount(body, new Binding { Rows = Rows("a", "b") }, state: state);
        Assert.IsType<UiMarkupExpander>(m.Copies[1]).Toggle();

        Assert.Equal("true", state.Values[("section#b", "expanded")]);
        Assert.DoesNotContain(state.Values.Keys, k => k.Item1 == "section#a" || k.Item1 == "section");

        var rebuilt = Mount(body, new Binding { Rows = Rows("a", "b") }, state: state);
        Assert.False(Assert.IsType<UiMarkupExpander>(rebuilt.Copies[0]).Expanded);
        Assert.True(Assert.IsType<UiMarkupExpander>(rebuilt.Copies[1]).Expanded);

        // A copy built in a tick reads its key's state too.
        var binding = new Binding { Rows = Rows("a") };
        var later = Mount(body, binding, state: state);
        binding.Rows = Rows("a", "b");
        later.Frame();
        Assert.True(Assert.IsType<UiMarkupExpander>(later.Copies[1]).Expanded);

        var sections = new Binding { Sections = [new Section { Id = "o", Rows = Rows("i") }] };
        var nested = Mount("<repeat items=\"{Sections}\" key=\"{Id}\"><group layout=\"column\">"
            + "<repeat items=\"{Rows}\" key=\"{Id}\"><expander name=\"inner\" open=\"false\" h=\"60\" text=\"{Label}\"/>"
            + "</repeat></group></repeat>", sections, state: state);
        Assert.IsType<UiMarkupExpander>(nested.Copies[0].Children[1]).Toggle();
        Assert.Equal("true", state.Values[("inner#o/i", "expanded")]);
    }

    [Fact]
    public void A_removed_key_forgets_its_remembered_state_in_one_call()
    {
        const string body = "<repeat items=\"{Rows}\" key=\"{Id}\">"
            + "<expander name=\"section\" open=\"false\" h=\"60\" text=\"{Label}\"/></repeat>";
        var state = new FakeState();
        state.Values[("other", "expanded")] = "true";
        var binding = new Binding { Rows = Rows("a", "b", "c") };
        var m = Mount(body, binding, state: state);
        foreach (UiElement copy in m.Copies)
            Assert.IsType<UiMarkupExpander>(copy).Toggle();

        binding.Rows = Rows("b");
        m.Frame();

        Assert.Equal(1, state.ForgetCalls);
        Assert.Equal(["other", "section#b"], state.Values.Keys.Select(k => k.Item1).Order());
    }

    [Fact]
    public void A_removed_section_forgets_its_nested_rows_state_and_a_removed_row_only_its_own()
    {
        var state = new FakeState();
        var keep = new Section { Id = "o", Rows = Rows("i", "j") };
        var sections = new Binding { Sections = [keep, new Section { Id = "p", Rows = Rows("i") }] };
        var m = Mount("<repeat items=\"{Sections}\" key=\"{Id}\"><group layout=\"column\">"
            + "<repeat items=\"{Rows}\" key=\"{Id}\"><expander name=\"inner\" open=\"false\" h=\"60\" text=\"{Label}\"/>"
            + "</repeat></group></repeat>", sections, state: state);
        foreach (UiMarkupExpander expander in m.Copies.SelectMany(c => c.Children).OfType<UiMarkupExpander>())
            expander.Toggle();
        Assert.Equal(3, state.Values.Count);

        keep.Rows = Rows("i");
        sections.Sections = [keep];
        m.Frame();

        Assert.Equal([("inner#o/i", "expanded")], state.Values.Keys);
    }

    [Fact]
    public void A_removed_key_leaves_another_repeats_state_under_the_same_key()
    {
        var state = new FakeState();
        state.Values[("x#5", "expanded")] = "true";
        state.Values[("y#5", "expanded")] = "true";
        var binding = new Binding { Rows = Rows("5"), Sections = [new Section { Id = "5" }] };
        var m = Mount("<repeat items=\"{Rows}\" key=\"{Id}\"><expander name=\"x\" open=\"false\" h=\"60\" text=\"{Label}\"/></repeat>"
            + "<repeat items=\"{Sections}\" key=\"{Id}\"><expander name=\"y\" open=\"false\" h=\"60\" text=\"{Title}\"/></repeat>",
            binding, state: state);

        binding.Rows = [];
        m.Frame();

        Assert.Equal([("y#5", "expanded")], state.Values.Keys);
    }

    [Fact]
    public void A_key_with_a_slash_is_not_read_as_a_nested_path()
    {
        const string body = "<repeat items=\"{Rows}\" key=\"{Id}\">"
            + "<expander name=\"section\" open=\"false\" h=\"60\" text=\"{Label}\"/></repeat>";
        var state = new FakeState();
        var binding = new Binding { Rows = Rows("a", "a/b") };
        var m = Mount(body, binding, state: state);
        Assert.IsType<UiMarkupExpander>(m.Copies[0]).Toggle();
        Assert.IsType<UiMarkupExpander>(m.Copies[1]).Toggle();
        Assert.Contains(("section#a%2Fb", "expanded"), state.Values.Keys);

        binding.Rows = Rows("a/b");
        m.Frame();

        Assert.Equal([("section#a%2Fb", "expanded")], state.Values.Keys);
    }

    [Fact]
    public void With_duplicate_keys_a_removed_copy_whose_key_is_still_listed_keeps_its_state()
    {
        const string body = "<repeat items=\"{Rows}\" key=\"{Id}\">"
            + "<expander name=\"section\" open=\"false\" h=\"60\" text=\"{Label}\"/></repeat>";
        var state = new FakeState();
        var binding = new Binding { Rows = Rows("a", "b", "a") };
        var m = Mount(body, binding, state: state);
        Assert.IsType<UiMarkupExpander>(m.Copies[0]).Toggle();
        Assert.IsType<UiMarkupExpander>(m.Copies[1]).Toggle();

        binding.Rows = Rows("a", "a");
        m.Frame();
        Assert.Contains(("section#a", "expanded"), state.Values.Keys);
        Assert.Contains(("section#b", "expanded"), state.Values.Keys);

        binding.Rows = Rows("a");
        m.Frame();
        Assert.Contains(("section#a", "expanded"), state.Values.Keys);
    }

    // 11
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_null_or_empty_list_removes_every_copy_and_swapping_back_builds_again(bool isNull)
    {
        var binding = new Binding { Rows = Rows("a", "b") };
        var m = Mount(Keyed, binding, themed: true);
        int themedEmpty = ThemeActions(Mount(Keyed, new Binding { Rows = [] }, themed: true).Window.Frame);

        binding.Rows = isNull ? null : [];
        m.Frame();
        Assert.Empty(m.Copies);
        Assert.Empty(m.Items);
        Assert.Empty(m.Marker.Copies);
        Assert.Equal(themedEmpty, ThemeActions(m.Window.Frame));

        binding.Rows = Rows("c", "d");
        m.Frame();
        Assert.Equal(new[] { "c", "d" }, Labels(m.Copies));
        Assert.Equal(m.Copies, m.Items);
    }

    [Fact]
    public void Removing_a_copy_disposes_what_its_subtree_holds()
    {
        const string body = "<repeat items=\"{Sections}\" key=\"{Id}\"><group layout=\"column\"><label text=\"{Title}\" h=\"20\"/>"
            + "<repeat items=\"{Rows}\" key=\"{Id}\"><group layout=\"row\" h=\"22\"><label text=\"{Label}\" h=\"20\"/></group>"
            + "</repeat></group></repeat>";
        var binding = new Binding { Sections = [Sec("a", "1"), Sec("b", "1")] };
        var m = Mount(body, binding);
        var outer = new DisposableProbe();
        var inner = new DisposableProbe();
        var kept = new DisposableProbe();
        m.Copies[0].AddChild(outer);
        m.Copies[0].Children[2].AddChild(inner);   // inside section a's first row
        m.Copies[1].AddChild(kept);

        binding.Sections = [binding.Sections[1]];
        m.Frame();

        Assert.Equal(1, outer.Disposed);
        Assert.Equal(1, inner.Disposed);
        Assert.Equal(0, kept.Disposed);
    }

    [Fact]
    public void Removing_a_copy_with_an_open_colour_popup_releases_its_textures()
    {
        var textures = new FakeTextures();
        var binding = new Binding { Rows = Rows("a", "b") };
        var m = Mount("<repeat items=\"{Rows}\" key=\"{Id}\"><color value=\"{Color}\" onchange=\"{SetColor}\" w=\"40\" h=\"18\"/></repeat>",
            binding, textures: textures);
        var swatch = Assert.IsType<UiMarkupColor>(m.Copies[1]);
        Assert.True(swatch.Open());
        Assert.NotEmpty(textures.Uploads);

        binding.Rows = [binding.Rows[0]];
        m.Frame();

        Assert.False(swatch.IsOpen);
        Assert.Equal(textures.Uploads, textures.Released);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_copy_that_fails_to_build_in_a_tick_leaves_nothing_behind(bool brokenFirst)
    {
        var binding = new Binding { Sections = [Sec("a", "1")] };
        var m = Mount(NestedInGroup, binding, themed: true);
        var kept = m.Copies[0];
        var broken = Sec("b", "1");
        broken.Broken = true;
        int actions = ThemeActions(m.Window.Frame);

        binding.Sections = brokenFirst ? [broken, binding.Sections[0]] : [binding.Sections[0], broken];
        Assert.ThrowsAny<Exception>(m.Frame);

        Assert.Equal(new[] { kept }, m.Copies);
        Assert.Equal(new[] { kept }, m.Items);
        Assert.Single(m.Marker.Copies);
        Assert.Equal(actions, ThemeActions(m.Window.Frame));

        // The next tick tries again.
        broken.Broken = false;
        m.Frame();
        Assert.Equal(2, m.Marker.Copies.Count);
        Assert.Contains(kept, m.Copies);
        Assert.Equal(new[] { "B", "b1" }, Labels(m.Copies[brokenFirst ? 0 : 1].Children));
    }

    [Fact]
    public void A_template_that_throws_part_way_takes_back_what_it_built()
    {
        var binding = new Binding { Rows = Rows("a") };
        var m = Mount("<repeat items=\"{Rows}\" key=\"{Id}\"><label text=\"{Label}\" h=\"20\"/>"
            + "<label text=\"x\" h=\"20\"/></repeat>", binding, themed: true);
        var before = m.Copies;
        int actions = ThemeActions(m.Window.Frame);
        var field = typeof(UiMarkupRepeat).GetField("_add",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var add = (MarkupRepeat.AddElement)field.GetValue(m.Marker)!;
        int calls = 0;
        field.SetValue(m.Marker, (MarkupRepeat.AddElement)((parent, el, source, state, flex) =>
        {
            add(parent, el, source, state, flex);
            if (++calls == 1) throw new InvalidOperationException("second element fails");
        }));

        binding.Rows = Rows("a", "b");
        Assert.Throws<InvalidOperationException>(m.Frame);

        Assert.Equal(before, m.Copies);
        Assert.Equal(before, m.Items);
        Assert.Single(m.Marker.Copies);
        Assert.Equal(actions, ThemeActions(m.Window.Frame));
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MarkupRepeatConsoleCollection
{
    public const string Name = "Markup repeat console capture";
}
