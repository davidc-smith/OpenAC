using System.Collections;
using AcDream.App.UI;
using AcDream.App.UI.Markup;

namespace AcDream.App.Tests.UI;

/// <summary>
/// <c>&lt;repeat&gt;</c> at build time: one copy of the template per list
/// item, each bound to its item, straight into the surrounding flex container
/// after a zero-size marker; the build errors (R3, R6, R8, attributes, the
/// <c>items</c> type); nesting; the first measurement; theme and context menus
/// inside copies.
/// </summary>
public sealed class MarkupRepeatBuildTests
{
    private class Row
    {
        public string Label { get; set; } = "";
        public int Id { get; set; }
        public bool On { get; set; }
        public int Clicks { get; private set; }
        public List<bool> Changes { get; } = new();
        public List<int> Picks { get; } = new();
        public Action Click => () => Clicks++;
        public Action<bool> SetOn => Changes.Add;
        public Action<int> Pick => Picks.Add;
    }

    private sealed class FancyRow : Row
    {
        public bool Fancy { get; set; }
    }

    private sealed class Section
    {
        public string Title { get; set; } = "";
        public int Id { get; set; }
        public IReadOnlyList<Row> Rows { get; set; } = [];
    }

    private sealed class Binding
    {
        public IReadOnlyList<Row>? Rows { get; set; } = [];
        public IReadOnlyList<Section> Sections { get; set; } = [];
        public List<Row> RowList { get; set; } = [];
        public IEnumerable<Row> RowSeq { get; set; } = [];
        public int Count => 3;
        public IEnumerable Plain => new ArrayList();
        public string Text => "abc";
    }

    private static Row[] Rows(params string[] labels) =>
        labels.Select((label, i) => new Row { Label = label, Id = i + 1 }).ToArray();

    private static MarkupWindow Window(string body, Binding binding, string attrs = "layout=\"column\"",
        PluginUiThemeSettings? themes = null) =>
        MarkupDocument.BuildWindow(
            $"<panel x=\"0\" y=\"0\" w=\"300\" {attrs}>{body}</panel>", binding, _ => (0u, 0, 0),
            themes: themes, fallbackTitle: "Demo");

    private static UiPluginContentHost Content(MarkupWindow window) =>
        Assert.IsType<UiPluginContentHost>(window.ContentRoot);

    private static string[] Labels(IEnumerable<UiElement> elements) =>
        elements.OfType<UiLabel>().Select(label => label.TextSource!() ?? "").ToArray();

    private const string LabelTemplate = "<repeat items=\"{Rows}\"><label text=\"{Label}\"/></repeat>";

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void One_copy_per_item_in_list_order_after_the_marker(int count)
    {
        string[] labels = new[] { "a", "b", "c" }[..count];
        var window = Window(LabelTemplate, new Binding { Rows = Rows(labels) });
        var content = Content(window);

        Assert.IsType<UiMarkupRepeat>(content.Children[0]);
        Assert.Equal(labels.Length + 1, content.Children.Count);
        Assert.Equal(labels, Labels(content.Children.Skip(1)));
        Assert.Equal(labels, Labels(content.Flex!.Items.Select(item => item.Element)));
        Assert.Equal(labels.Length, content.Flex.Items.Count);
        var marker = content.Children[0];
        Assert.Equal((0f, 0f), (marker.Width, marker.Height));
        Assert.True(marker.ClickThrough);
    }

    [Fact]
    public void A_copy_reads_its_item_and_calls_back_on_its_item()
    {
        var rows = Rows("a", "b", "c");
        var window = Window(
            "<repeat items=\"{Rows}\"><button text=\"{Label}\" onclick=\"{Click}\"/>"
            + "<checkbox checked=\"{On}\" onchange=\"{SetOn}\"/></repeat>",
            new Binding { Rows = rows });
        var content = Content(window);
        var buttons = content.Children.OfType<UiSimpleButton>().ToArray();
        var boxes = content.Children.OfType<UiMarkupCheckbox>().ToArray();

        Assert.Equal(new[] { "a", "b", "c" }, buttons.Select(b => b.TextSource!()));
        buttons[1].OnEvent(new UiEvent { Type = UiEventType.Click });
        rows[2].On = true;
        boxes[2].Changed!(false);

        Assert.Equal(new[] { 0, 1, 0 }, rows.Select(r => r.Clicks));
        Assert.Equal(new bool?[] { false, false, true }, boxes.Select(b => b.CheckedSource!()));
        Assert.Empty(rows[1].Changes);
        Assert.Equal(new[] { false }, rows[2].Changes);
    }

    [Fact]
    public void A_misspelt_required_binding_fails_with_an_empty_or_null_list()
    {
        const string body = "<repeat items=\"{Rows}\"><checkbox checked=\"{Onn}\"/></repeat>";

        Assert.Throws<FormatException>(() => Window(body, new Binding { Rows = [] }));
        Assert.Throws<FormatException>(() => Window(body, new Binding { Rows = null }));
    }

    [Fact]
    public void A_member_only_on_a_subtype_is_invisible()
    {
        var binding = new Binding { Rows = [new FancyRow { Label = "a", Fancy = true }] };

        Assert.Throws<FormatException>(() =>
            Window("<repeat items=\"{Rows}\"><checkbox checked=\"{Fancy}\"/></repeat>", binding));
    }

    [Theory]
    [InlineData("")]
    [InlineData("items=\"Rows\"")]
    [InlineData("items=\"(Rows)\"")]
    [InlineData("items=\"{Count}\"")]
    [InlineData("items=\"{Plain}\"")]
    [InlineData("items=\"{Text}\"")]
    [InlineData("items=\"{Missing}\"")]
    public void Items_must_bind_a_typed_list(string items)
    {
        Assert.Throws<FormatException>(() =>
            Window($"<repeat {items}><label text=\"x\"/></repeat>", new Binding()));
    }

    [Theory]
    [InlineData("RowList")]
    [InlineData("RowSeq")]
    public void Items_may_bind_an_IEnumerable_of_T_or_a_list_that_implements_it(string property)
    {
        var window = Window($"<repeat items=\"{{{property}}}\"><label text=\"{{Label}}\"/></repeat>",
            new Binding { RowList = [.. Rows("a", "b")], RowSeq = Rows("a", "b") });

        Assert.Equal(new[] { "a", "b" }, Labels(Content(window).Children));
    }

    [Fact]
    public void A_repeat_goes_in_a_flex_container()
    {
        var binding = new Binding { Rows = Rows("a") };

        Assert.Throws<FormatException>(() => Window(LabelTemplate, binding, attrs: "h=\"200\""));
        Assert.Throws<FormatException>(() =>
            Window($"<group w=\"100\" h=\"100\">{LabelTemplate}</group>", binding));
        Assert.Throws<FormatException>(() =>
            Window($"<group layout=\"column\"><group w=\"100\" h=\"100\">{LabelTemplate}</group></group>", binding));
    }

    [Theory]
    [InlineData("x=\"0\"")]
    [InlineData("w=\"100\"")]
    [InlineData("h=\"20\"")]
    [InlineData("grow=\"1\"")]
    [InlineData("anchor=\"left\"")]
    [InlineData("name=\"rows\"")]
    [InlineData("visible=\"false\"")]
    public void A_repeat_takes_only_items_and_key(string attr)
    {
        Assert.Throws<FormatException>(() =>
            Window($"<repeat items=\"{{Rows}}\" {attr}><label text=\"x\"/></repeat>",
                new Binding { Rows = Rows("a") }));
    }

    [Fact]
    public void A_named_element_needs_a_key_on_every_enclosing_repeat()
    {
        var binding = new Binding
        {
            Rows = Rows("a", "b"),
            Sections = [new Section { Title = "S", Id = 1, Rows = Rows("x") }],
        };

        var keyless = Assert.Throws<FormatException>(() =>
            Window("<repeat items=\"{Rows}\"><label name=\"n\" text=\"{Label}\"/></repeat>", binding));
        Assert.Contains("needs key=", keyless.Message);
        var keylessId = Assert.Throws<FormatException>(() =>
            Window("<repeat items=\"{Rows}\"><label id=\"n\" text=\"{Label}\"/></repeat>", binding));
        Assert.Contains("id=\"n\"", keylessId.Message);
        Assert.Throws<FormatException>(() => Window(
            "<repeat items=\"{Sections}\"><group layout=\"column\">"
            + "<repeat items=\"{Rows}\" key=\"{Id}\"><label name=\"n\" text=\"{Label}\"/></repeat>"
            + "</group></repeat>", binding));

        var keyed = Window("<repeat items=\"{Rows}\" key=\"{Id}\"><label name=\"n\" text=\"{Label}\"/></repeat>", binding);
        Assert.Equal(new[] { "a", "b" }, Labels(Content(keyed).Children));
    }

    [Fact]
    public void A_key_must_bind_a_member_of_the_item_type()
    {
        Assert.Throws<FormatException>(() => Window(
            "<repeat items=\"{Rows}\" key=\"{Nope}\"><label text=\"x\"/></repeat>", new Binding()));
        Assert.Throws<FormatException>(() => Window(
            "<repeat items=\"{Rows}\" key=\"(Id)\"><label text=\"x\"/></repeat>", new Binding()));
    }

    [Fact]
    public void A_context_menu_goes_on_a_template_element_not_on_the_repeat()
    {
        Assert.Throws<FormatException>(() => Window(
            "<repeat items=\"{Rows}\"><label text=\"x\"/><contextmenu items=\"A|B\" onselect=\"{Pick}\"/></repeat>",
            new Binding { Rows = Rows("a") }));
    }

    [Fact]
    public void Repeats_nest_and_inner_bindings_read_the_inner_item()
    {
        var binding = new Binding
        {
            Sections =
            [
                new Section { Title = "A", Id = 1, Rows = Rows("a1", "a2") },
                new Section { Title = "B", Id = 2, Rows = Rows("b1") },
            ],
        };
        var window = Window(
            "<repeat items=\"{Sections}\" key=\"{Id}\"><group layout=\"column\"><label text=\"{Title}\"/>"
            + "<repeat items=\"{Rows}\" key=\"{Id}\"><label text=\"{Label}\"/></repeat></group></repeat>",
            binding);
        var content = Content(window);

        var groups = content.Children.Skip(1).ToArray();
        Assert.Equal(2, groups.Length);
        Assert.Equal(new[] { "A", "a1", "a2" }, Labels(groups[0].Children));
        Assert.Equal(new[] { "B", "b1" }, Labels(groups[1].Children));
        Assert.IsType<UiMarkupRepeat>(groups[0].Children[1]);
        var inner = ((UiFlexGroup)groups[0]).Flex.Items.Select(item => item.Element);
        Assert.Equal(new[] { "A", "a1", "a2" }, Labels(inner));
    }

    [Theory]
    [InlineData("", new[] { "A", "a1", "a2", "B", "b1", "end" })]
    [InlineData("<label text=\".\"/>", new[] { "A", "a1", "a2", ".", "B", "b1", ".", "end" })]
    public void A_repeat_directly_inside_a_repeat_keeps_each_section_together(string tail, string[] order)
    {
        var binding = new Binding
        {
            Sections =
            [
                new Section { Title = "A", Id = 1, Rows = Rows("a1", "a2") },
                new Section { Title = "B", Id = 2, Rows = Rows("b1") },
            ],
        };
        var window = Window(
            "<repeat items=\"{Sections}\"><label text=\"{Title}\"/>"
            + $"<repeat items=\"{{Rows}}\"><label text=\"{{Label}}\"/></repeat>{tail}</repeat>"
            + "<label text=\"end\"/>",
            binding);
        var content = Content(window);

        Assert.Equal(order, Labels(content.Children));
        Assert.Equal(order, Labels(content.Flex!.Items.Select(item => item.Element)));
        // The inner copies belong to the inner repeat, not to the outer copy.
        var outer = Assert.IsType<UiMarkupRepeat>(content.Children[0]);
        Assert.Equal(tail.Length == 0 ? 2 : 3, outer.Copies[0].Elements.Count);
    }

    [Fact]
    public void Nested_repeats_in_a_themed_window_follow_the_theme()
    {
        var settings = new PluginUiThemeSettings();
        var binding = new Binding
        {
            Sections =
            [
                new Section { Title = "A", Id = 1, Rows = Rows("a1", "a2") },
                new Section { Title = "B", Id = 2, Rows = [] },
            ],
        };
        var window = Window(
            "<repeat items=\"{Sections}\"><group layout=\"column\"><label text=\"{Title}\"/>"
            + "<repeat items=\"{Rows}\"><label text=\"{Label}\"/></repeat></group></repeat>",
            binding, attrs: "layout=\"column\" theme=\"plugin\"", themes: settings);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(window.Frame);
        var inner = Content(window).Children[1].Children.OfType<UiLabel>().Last();

        settings.Theme = PluginUiTheme.Moss;
        root.Tick(0.016, 10);

        Assert.Equal("a2", inner.TextSource!());
        Assert.Equal(settings.Palette!.Text, inner.TextColor);
    }

    [Fact]
    public void The_first_measurement_includes_the_copies()
    {
        float empty = Window(LabelTemplate, new Binding { Rows = [] }).Frame.Height;
        float three = Window(LabelTemplate, new Binding { Rows = Rows("a", "b", "c") }).Frame.Height;

        Assert.True(three > empty, $"{three} > {empty}");
    }

    [Fact]
    public void A_copy_follows_the_theme()
    {
        var settings = new PluginUiThemeSettings();
        var window = Window(LabelTemplate, new Binding { Rows = Rows("a", "b") },
            attrs: "layout=\"column\" theme=\"plugin\"", themes: settings);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(window.Frame);
        var label = Content(window).Children.OfType<UiLabel>().Last();

        settings.Theme = PluginUiTheme.Moss;
        root.Tick(0.016, 10);

        Assert.Equal(settings.Palette!.Text, label.TextColor);
    }

    [Fact]
    public void A_copys_theme_actions_are_held_by_its_scope()
    {
        var settings = new PluginUiThemeSettings();
        var window = Window(LabelTemplate, new Binding { Rows = Rows("a", "b") },
            attrs: "layout=\"column\" theme=\"plugin\"", themes: settings);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(window.Frame);
        var marker = Assert.IsType<UiMarkupRepeat>(Content(window).Children[0]);
        var label = (UiLabel)marker.Copies[1].Elements.Single();
        var classic = label.TextColor;

        marker.Copies[1].Scope!.Dispose();
        settings.Theme = PluginUiTheme.Moss;
        root.Tick(0.016, 10);

        Assert.Equal(classic, label.TextColor);
        Assert.Equal(settings.Palette!.Text, ((UiLabel)marker.Copies[0].Elements.Single()).TextColor);
    }

    private sealed class RecordingState : IMarkupControlState
    {
        public List<string> Reads { get; } = new();
        public int Generation => 1;
        public string? Get(string control, string kind) { Reads.Add(control); return null; }
        public void Set(string control, string kind, string value) { }
        public void Remove(string control, string kind) { }
    }

    [Fact]
    public void A_keyed_copy_remembers_its_named_controls_under_its_key()
    {
        var state = new RecordingState();
        var rows = Rows("a", "b");
        rows[1].Id = 42;
        var window = MarkupDocument.BuildWindow(
            "<panel x=\"0\" y=\"0\" w=\"300\" layout=\"column\"><repeat items=\"{Rows}\" key=\"{Id}\">"
            + "<expander name=\"sec\" h=\"60\" text=\"{Label}\"/></repeat></panel>",
            new Binding { Rows = rows }, _ => (0u, 0, 0), controlState: state);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(window.Frame);
        root.Tick(0.016, 10);

        Assert.Contains("sec#1", state.Reads);
        Assert.Contains("sec#42", state.Reads);
        Assert.DoesNotContain("sec", state.Reads);
    }

    [Fact]
    public void An_empty_lists_check_build_leaves_no_theme_actions_behind()
    {
        var settings = new PluginUiThemeSettings();
        var with = Window(LabelTemplate, new Binding { Rows = [] },
            attrs: "layout=\"column\" theme=\"plugin\"", themes: settings);
        var without = Window("", new Binding(), attrs: "layout=\"column\" theme=\"plugin\"", themes: settings);

        Assert.Equal(ThemeActions(without.Frame), ThemeActions(with.Frame));
    }

    private sealed class NullableSections
    {
        public IReadOnlyList<Section>? Sections { get; set; }
    }

    private const string NestedSections =
        "<repeat items=\"{Sections}\"><group layout=\"column\"><label text=\"{Title}\"/>"
        + "<repeat items=\"{Rows}\"><checkbox checked=\"{INNER}\"/></repeat></group></repeat>";

    private static MarkupWindow ThemedNested(string inner, IReadOnlyList<Section>? sections) =>
        MarkupDocument.BuildWindow(
            $"<panel x=\"0\" y=\"0\" w=\"300\" layout=\"column\" theme=\"plugin\">"
            + NestedSections.Replace("INNER", inner) + "</panel>",
            new NullableSections { Sections = sections }, _ => (0u, 0, 0),
            themes: new PluginUiThemeSettings(), fallbackTitle: "Demo");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_themed_window_checks_nested_repeats_when_the_outer_list_is_empty(bool isNull)
    {
        IReadOnlyList<Section>? sections = isNull ? null : [];
        var empty = ThemedNested("On", sections);
        var plain = Window("", new Binding(), attrs: "layout=\"column\" theme=\"plugin\"",
            themes: new PluginUiThemeSettings());

        Assert.Single(Content(empty).Children);
        Assert.Equal(ThemeActions(plain.Frame), ThemeActions(empty.Frame));
        Assert.Throws<FormatException>(() => ThemedNested("Onn", sections));
    }

    [Fact]
    public void A_copys_span_runs_through_the_copies_of_a_repeat_directly_inside_it()
    {
        var binding = new Binding
        {
            Sections = [new Section { Title = "A", Id = 1, Rows = Rows("a1", "a2") }],
        };
        var window = Window(
            "<repeat items=\"{Sections}\"><label text=\"{Title}\"/>"
            + "<repeat items=\"{Rows}\"><label text=\"{Label}\"/></repeat><label text=\".\"/></repeat>",
            binding);
        var content = Content(window);
        var outer = Assert.IsType<UiMarkupRepeat>(content.Children[0]);

        Assert.Equal(content.Children.Skip(1), outer.Span(outer.Copies[0]));
    }

    private static int ThemeActions(UiElement frame) =>
        Assert.IsType<UiPluginMarkupPanel>(frame).ThemeActionCount;

    [Fact]
    public void A_context_menu_in_a_template_belongs_to_its_copy()
    {
        var rows = Rows("a", "b");
        var window = Window(
            "<repeat items=\"{Rows}\"><button text=\"{Label}\" w=\"80\" h=\"20\">"
            + "<contextmenu items=\"Edit|Delete\" onselect=\"{Pick}\"/></button></repeat>",
            new Binding { Rows = rows });
        var buttons = Content(window).Children.OfType<UiSimpleButton>().ToArray();

        var menu = Assert.IsType<UiMarkupContextMenu>(buttons[1].ContextMenu);
        Assert.Contains(menu, buttons[1].Children);
        menu.Selected!(1);

        Assert.Empty(rows[0].Picks);
        Assert.Equal(new[] { 1 }, rows[1].Picks);
    }
}
