using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

public sealed partial class ChatDeckEditorBinding
{
    private IReadOnlyList<PresetGroup>? _presetGroups;

    public IReadOnlyList<PresetGroup> PresetGroups => _presetGroups ??= ChatDeckPresets.GroupOrder
        .Select(g => new PresetGroup(g, ChatDeckPresets.All.Where(p => p.Group == g).Select(p => new PresetRow(this, p)).ToList()))
        .ToList();

    public bool PresetsOpen
    {
        get
        {
            _ = M;                 // a model swap resets the panes; read it before trusting the pane
            return _pane == Pane.Presets;
        }
    }

    public Action ShowPresets => () => { _ = M; _pane = Pane.Presets; _page = ChatDeckEditorPage.Rules; };
    public Action ClosePane => () => { _ = M; _pane = Pane.None; };

    private bool PresetOn(string id) => S.Rules.Any(r => r.Kind == ChatDeckMatchKind.Preset && r.PresetId == id && r.Enabled);

    public sealed record PresetGroup(string Name, IReadOnlyList<PresetRow> Presets);

    public sealed class PresetRow(ChatDeckEditorBinding owner, ChatDeckPreset preset)
    {
        public string Id => preset.Id;
        public string Label => preset.Label;
        public string Description => preset.Description;
        public bool On => owner.PresetOn(preset.Id);
        public Action<bool> SetOn => on => owner.Edit(s => ChatDeckSettingsEdits.SetPresetEnabled(s, preset.Id, on));
    }

    private const string PresetsMarkup = """
            <group layout="column" gap="4" h="270" visible="{PresetsOpen}">
              <separator h="1" />
              <group layout="row" gap="8" align="center" h="22">
                <label text="Standard filters (a ticked filter hides its lines; edit its rule to colour or move them instead)" grow="1" />
                <button text="Done" onclick="{ClosePane}" w="60" h="22" />
              </group>
              <group layout="column" gap="2" scroll="y" grow="1">
                <repeat items="{PresetGroups}" key="{Name}">
                  <label text="{Name}" h="16" />
                  <repeat items="{Presets}" key="{Id}">
                    <group layout="row" h="16">
                      <group w="14" />
                      <checkbox text="{Label}" tooltip="{Description}" checked="{On}" onchange="{SetOn}" grow="1" h="16" />
                    </group>
                  </repeat>
                </repeat>
              </group>
            </group>
        """;
}
