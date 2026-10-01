using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>
/// The modern theme must never leak into Classic: a themed window switched
/// back to Classic draws exactly what a window that never opted in draws.
/// </summary>
public sealed class PluginThemeClassicIdentityTests
{
    private sealed class Binding
    {
        public Action Click => () => { };
        public bool TabSelected => true;
        public bool Checked => true;
        public Action Toggle => () => { };
        public float Value => 0.6f;
        public Action<float> Changed => _ => { };
        public string Text => "Asheron";
        public IReadOnlyList<string> Items => ["Strength Self VII", "Invulnerability Self VII", "Impregnability Self VII"];
        public string Selected => "Strength Self VII";
        public int Index => 1;
        public IReadOnlyList<bool> Flags => [true, false, true];
        public Action<int> Check => _ => { };
        public IReadOnlyList<string> Log => ["one", "two", "three"];
        public float Fill => 0.4f;
    }

    internal const string Body = """
          <group x="8" y="28" w="300" h="20" background="#FF202020" border="#FF404040" />
          <label x="12" y="52" text="Target" />
          <button x="12" y="70" w="80" h="24" text="Start" onclick="{Click}" />
          <tab x="100" y="70" w="80" h="24" text="Status" selected="{TabSelected}" onclick="{Click}" />
          <toggle x="12" y="100" w="160" h="20" text="Auto-rebuff" checked="{Checked}" onclick="{Toggle}" />
          <slider x="12" y="126" w="200" h="16" value="{Value}" onchange="{Changed}" />
          <field x="12" y="148" w="200" h="24" text="{Text}" />
          <menu x="12" y="178" w="200" h="24" items="{Items}" selected="{Selected}" />
          <list x="12" y="208" w="200" h="60" items="{Items}" selected="{Index}" selectionband="true" />
          <list x="220" y="208" w="160" h="60" selected="{Index}">
            <column type="check" width="20" values="{Flags}" onchange="{Check}" />
            <column type="text" width="*" items="{Items}" />
          </list>
          <log x="12" y="274" w="200" h="60" items="{Log}" />
          <meter x="12" y="340" w="200" h="12" fill="{Fill}" color="#FFCC3333" />
        """;

    private static UiRoot Root(string themeAttribute, PluginUiThemeSettings settings, UiDatFont font)
    {
        var panel = MarkupDocument.Build(
            $"<panel x=\"10\" y=\"10\" w=\"400\" h=\"360\" title=\"Gallery\" {themeAttribute}>{Body}</panel>",
            new Binding(), _ => (0u, 0, 0), datFont: font, themes: settings);
        var root = new UiRoot { Width = 600, Height = 600 };
        root.AddChild(panel);
        return root;
    }

    [Fact]
    public void ThemedWindowSwitchedBackToClassicDrawsExactlyLikeAClassicWindow()
    {
        var classicFont = BundledUiFont.Bake(12).CreateFont(7);
        var modernFont = BundledUiFont.Bake().CreateFont(8);

        var plainSettings = new PluginUiThemeSettings(modernFont: modernFont);
        UiRoot plain = Root("", plainSettings, classicFont);
        plain.Tick(0.016, 1);
        plain.Tick(0.016, 2);

        var settings = new PluginUiThemeSettings(modernFont: modernFont);
        UiRoot themed = Root("theme=\"plugin\"", settings, classicFont);
        settings.Theme = PluginUiTheme.Moss;
        themed.Tick(0.016, 1);
        float[] moss = ThemeDrawCapture.Draw(themed);
        settings.Theme = PluginUiTheme.Classic;
        themed.Tick(0.016, 2);

        float[] classic = ThemeDrawCapture.Draw(plain);
        float[] back = ThemeDrawCapture.Draw(themed);

        // The capture must actually record the gallery, and see theme changes.
        Assert.True(classic.Length > 500, $"expected a real draw, got {classic.Length} floats");
        Assert.NotEqual(classic, moss);

        Assert.Equal(classic, back);
    }
}
