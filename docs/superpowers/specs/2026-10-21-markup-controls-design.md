# Plugin markup controls: design

Date: 2026-10-21. Status: agreed with the user in brainstorming, awaiting plan.

## Goal

Give plugin authors the standard controls a UI front end needs, as plugin
markup elements and `IUiRegistry` calls, so Aethrix, the Rynth stand-ins and
any upstream plugin stop hand-building them out of fields, toggles and lists.

Thirteen pieces of work:

| # | Control | Form |
|---|---|---|
| 1 | Checkbox | `<checkbox>` and `<column type="checkbox">` |
| 2 | Numeric field | `<number>` |
| 3 | Radio group | `<radio>` |
| 4 | Separator | `<separator>` |
| 5 | Expander | `<expander>` |
| 6 | Splitter | `<split>` |
| 7 | Table header, sorting, resizable columns | `<list header="true">` |
| 8 | Multi-select | `<list selectmode="multi">` |
| 9 | Tree | `<list tree="true">` |
| 10 | Context menu | `<contextmenu>` child |
| 11 | Hotkey capture | `<hotkey>` |
| 12 | Colour picker | `<color>` |
| 13 | Dialogs | `IUiRegistry.Confirm`, `IUiRegistry.ShowModal` |

Out of scope (decided): toast notifications, date/time pickers, rich text,
charts, submenus, a screen-modal dialog for plugins, a node-object tree API.

## Delivery

- **One branch, `feat/markup-controls`, from `upstream/main`** (v0.1.26,
  391e709a), so it can be offered to Erik as one PR or split into several.
  Each control lands as its own self-contained commit series (builder, widget,
  theme, tests, docs) in the order of the table above, after a shared
  infrastructure series (section 1). Nothing in a later series is required by
  an earlier one, except the shared infrastructure and the `<list>` series
  7 → 8 → 9, and 10's list rows using 8's selection rules.
- The branch is built against upstream's markup: absolute layout, upstream's
  painter and theme. No fork-only code (flex, fonts, markup canvas,
  `PluginUiSettingsStore`) is touched or required.
- **Fork merge** afterwards: merge the branch into fork `main`, then add the
  fork-only integration (section 15) in commits on `main`.
- Specs and plans stay on the fork branch `docs/markup-controls-spec`, never
  in the upstream branch (docfx publishes `docs/**.md`). User-facing docs
  (`docs/plugin-ui-markup.md`, `docs/plugin-api.md`) do go in the branch.
- **Contract:** only #13 changes `AcDream.Plugin.Abstractions`, additively
  (default interface members plus one record). Upstream's version number is
  Erik's call; the PR description says so. On the fork merge,
  `<Version>` goes `0.1.26-fork.2` → `0.1.26-fork.3`.

## Shared rules

These apply to every control below unless it says otherwise.

- **Builders live in their own files.** `MarkupDocument.AddElement` is already
  1,300 lines. Each new element's builder is a static method in its own file
  under `src/AcDream.App/UI/Markup/` (`MarkupCheckbox.cs`, `MarkupNumber.cs`,
  …), and `AddElement` gains one `case` per element that calls it. The `Bind*`
  helpers they need move from `private` to `internal` in place; no other
  refactoring of `MarkupDocument`.
- **Widgets** are `UiMarkup*` classes beside the existing ones
  (`UiMarkupCheckbox`, `UiMarkupNumber`, …), each in its own file.
- **Both looks.** Every control draws in Classic (dat art where the retail
  client has it, otherwise primitives in the Classic colours) and under
  `theme="plugin"` (palette tokens via a new `case` in
  `PluginMarkupTheme.Register`). The retail options screens draw their
  checkboxes as lamps (`UiCheckLamp` reproduces them), so there is no
  square-tick sprite: the Classic `<checkbox>` and `<radio>` are drawn with
  primitives in the lamp's colours.
- **Common attributes.** Every new element accepts `name`/`id`, `visible`,
  `enabled`, `tooltip` and `anchor` through `ApplyCommon`, and `x y w h`.
  A disabled control draws faded and fires nothing.
- **Keyboard.** Interactive controls are `TabStop`s with the existing gold
  focus outline and `UiKeyboardActivation` (Enter/Space) where a press makes
  sense. Clicking does not take keyboard focus from the game, as `<toggle>`
  does, except for controls that edit text (`<number>`, the colour popup's
  hex field) and `<hotkey>`.
- **Binding errors.** A binding that names nothing or has the wrong type
  throws `FormatException` at build, naming the element and attribute, except
  where marked silent. Callbacks fire on the thread that calls `Tick`.
- **Remembered state (the "both" rule).** Sort order, column widths, tree
  expansion, expander state and split position are *state a control can
  remember*. For each:
  - **Unbound:** the host owns it and remembers it per character, per
    window, per control `name`. A control without a `name` is not remembered
    (it starts from its markup default each session). A change also fires
    the control's notification callback if one is bound.
  - **Bound:** the plugin owns it. The host reads the binding every frame,
    never stores it, and a user action calls the callback with the new value
    for the plugin to apply (the controlled-control pattern; `<toggle>`
    already works this way).
  - **Store:** a new `MarkupControlStateStore` beside
    `RetailWindowLayoutPersistence`, saved through the same `SettingsStore`
    and character key. It is written on the frame after a change (and on
    shutdown and layout save), so a control remembers only when the player
    commits a change (drag end, a click), never on every drag frame. Keys are
    `<window name>/<control name>/<kind>`; values are short strings. Unknown
    or malformed stored values are ignored.

## 1. Shared infrastructure (series 0)

Each piece lands with the first series that uses it, not all up front:
the builder folder, `MarkupBuildContext` and `MarkupBindings` with series 1;
`MarkupControlStateStore` with series 5 (expander); `IMarkupTextureService`
with series 12 (colour); further bind helpers with whichever series needs
them.

- `src/AcDream.App/UI/Markup/` folder and the `internal` `Bind*` helpers.
- `MarkupControlStateStore` and its wiring into the markup build: the builder
  gets an `IMarkupControlState` (get/set by key, scoped to the window) the way
  it gets `IMarkupIconResolver`; `null` in tests means "remember nothing".
- `IMarkupTextureService`: upload an RGBA8 buffer, return a releasable
  sprite handle, free it on dispose, backed by
  `TextureCache.UploadReleasableRgba8`/`ReleaseUiTexture`. Only `<color>`
  uses it. Tests use a recording fake.
- New bind helpers as needed: `BindNullableBoolReader`, numeric readers and
  actions for `int`/`long`/`float`/`double`/`decimal`, `BindIntListReader`,
  `BindIntListAction`, `BindIntBoolAction`, `BindFloatListReader`,
  `BindStringSetReader`.

## 2. `<checkbox>` (series 1)

```xml
<checkbox x="8" y="8" w="120" h="16" text="Loot rares"
          checked="{LootRares}" onchange="{SetLootRares}"/>
```

- `checked`: `bool`, or `bool?` where `null` is indeterminate (drawn as a
  dash). Required.
- `onchange`: `Action<bool>`, the *new* value. Unchecked → true,
  checked → false, indeterminate → true. Optional; without it the box is
  display-only.
- `text`, `color` as `<toggle>`. Draws a square box with a tick, not the lamp.
- **Column:** `<column type="checkbox" values="{…}" onchange="{…}">` takes the
  same bindings as `type="check"` (`IReadOnlyList<bool>`, `Action<int>` row)
  and draws checkboxes. `type="check"` stays the lamp.
- **Select-all header** (with series 7): `header="all"` on a checkbox column
  draws a header checkbox, indeterminate while some but not all rows are
  ticked. It needs `onall="{…}"`, an `Action<bool>`, called once with the new
  value for every row. Without `onall` the header cell is blank.

## 3. `<number>` (series 2)

```xml
<number x="8" y="8" w="80" h="20" value="{HealAt}" min="0" max="100" step="5"
        decimals="0" onchange="{SetHealAt}"/>
```

- `value`: `int`, `long`, `float`, `double` or `decimal`. Required.
  `onchange`: `Action<T>` of the *same* type; a mismatch throws at build.
- `min`, `max` (default the type's range), `step` (default 1), `decimals`
  (default 0 for integral types, 2 otherwise). `max < min` throws.
- Built on `UiField` (one-line) plus two spin buttons at the right edge.
- Typing accepts digits, `-` (only when `min < 0`) and `.` (only when
  `decimals > 0`), in the invariant culture. Position and count are not
  filtered: while the text does not parse or is outside the range it draws
  in the error colour, and nothing is reported.
- **Commit** on Enter or focus loss: parse, clamp, round to `decimals`,
  show the result, and call `onchange` if the value changed. Escape restores
  the last committed value. Unparseable text on commit restores it too.
- **Step:** spin buttons (auto-repeat while held, 400 ms delay then every
  60 ms), Up/Down keys while focused, mouse wheel *only while focused*.
  Shift steps ten times. A step commits immediately and clamps.
- While the field is not being edited it follows the bound value, as
  `<field>` follows `text`.

## 4. `<radio>` (series 3)

```xml
<radio x="8" y="8" w="200" h="54" items="Melee|Missile|Magic"
       selected="{Mode}" onchange="{SetMode}" orientation="vertical" style="radio"/>
```

- One element for the whole group. `items`: a literal list separated by `|`,
  or a bound `IEnumerable<string>`. `selected`: `int` (−1 or out of range =
  none). `onchange`: `Action<int>`, the clicked index.
- `orientation`: `vertical` (default) or `horizontal`; options share the box
  equally along that axis.
- `style`: `radio` (default) draws round dots with captions; `segmented`
  draws a joined button bar, with the selected segment in the selected face.
- One tab stop for the group. Arrow keys move the selection and fire
  `onchange`; they wrap.

## 5. `<separator>` (series 4)

```xml
<separator x="8" y="40" w="200" h="1"/>
```

- A 1px line in the theme's border colour (Classic: the unlit lamp's
  muted colour, #FF61573B). Vertical when `h > w`. `color` overrides. Not
  focusable, no events; clicks pass through.

## 6. `<expander>` (series 5)

```xml
<expander name="adv" x="8" y="60" w="240" h="120" text="Advanced"
          expanded="{AdvOpen}" onchange="{SetAdvOpen}"> …children… </expander>
```

- A container. The top 18px is a header row with ▸/▾ and `text`; children
  sit below it in local coordinates and show only when expanded. Clicking
  the header or pressing Enter/Space on it toggles.
- `expanded` (`bool`) and `onchange` (`Action<bool>`, the new state) follow
  the remembered-state rule. Unbound, it starts from `open="true|false"`
  (default `true`).
- **Collapse in absolute layout:** collapsing sets the expander's height to
  the header height. Every sibling in the same parent whose top is at or
  below the expander's authored bottom moves up by the difference, and back
  down on expanding, like an accordion. Siblings above or beside it don't
  move. Anchored siblings keep their anchors; the shift applies to their
  authored position, so a sibling anchored top and bottom grows into the
  space instead of moving.

## 7. `<split>` (series 6)

```xml
<split name="main" x="0" y="0" w="400" h="300" orientation="horizontal"
       position="{SplitAt}" onchange="{SetSplitAt}" min="80" max="320">
  <group>…left…</group>
  <group>…right…</group>
</split>
```

- Exactly two `<group>` children, else build throws. `horizontal` places
  them side by side (the bar is vertical); `vertical` stacks them.
- A 5px bar between them, with a resize cursor and hover highlight. Dragging
  moves it; the two groups resize to fill. `position` is the first pane's
  size in pixels, clamped to `min`/`max` (default 0 and the full size).
- `position` (`float`) and `onchange` (`Action<float>`) follow the
  remembered-state rule; unbound, it starts from `at="…"` (default half).
- When the split itself resizes (anchors), the first pane keeps its size
  and the second takes the rest, still clamped.

## 8. `<list>` header, sorting and resizable columns (series 7)

```xml
<list name="mobs" x="8" y="24" w="320" h="200" header="true" resizable="true"
      selected="{Sel}" onchange="{Pick}" sortcolumn="{SortCol}"
      sortdescending="{SortDesc}" onsort="{SetSort}">
  <column type="text" header="Name" items="{Names}" sort="text"/>
  <column type="text" header="Dist" items="{Dists}" sort="number" sortkeys="{DistKeys}"/>
  <column type="icon" header="" values="{Icons}" onclick="{Act}"/>
</list>
```

- **Header row.** `header="true"` draws an 18px header row above the rows,
  in the header face (Classic: the scrollbar track art; theme: the `Field`
  token with a `Border` underline). Each column's `header` is a literal or a
  bound `string`. The scrollbar starts below the header.
- **Sorting.** A column with `sort="text"`, `sort="number"` or `sort="bool"`
  is sortable; default `none`. Clicking a sortable header sorts ascending,
  clicking it again descending, a third time clears the sort. The sorted
  column shows ▲/▼.
  - Keys: `text` compares the cell strings case-insensitively (ordinal
    ignore case); `number` parses the cell strings (invariant culture,
    unparseable sorts last); `bool` uses a check or checkbox column's
    values. `sortkeys` (`IReadOnlyList<double>` or `IReadOnlyList<string>`)
    replaces the cell values as keys, e.g. a distance shown as "12.3 m".
  - The sort is stable: ties keep the plugin's row order.
- **The view map.** The host draws rows through a permutation
  (view row → plugin row), rebuilt when the sort changes or any sorted
  column's data changes. **Every callback, every bound `selected`, and every
  row index the plugin sees is a plugin row.** Selection follows the row,
  not the position. Button-cell press cancelling (existing rule) also
  cancels when the permutation changes under the press.
- **Sort state** follows the remembered-state rule as one value:
  `sortcolumn` (`int`, the column's index among `<column>` children, −1 none)
  plus `sortdescending` (`bool`), bound as a pair or not at all.
  `onsort` is `Action<int, bool>`.
- **Resizable columns.** With `resizable="true"` and a header, dragging the
  boundary between two header cells resizes the column to its left, at least
  16px. Double-clicking a boundary resets that column to its markup width.
  Widths follow the remembered-state rule: `widths` (`IReadOnlyList<float>`,
  one per column; the last column's entry is ignored, as it is always
  automatic) and `onresize` (`Action<IReadOnlyList<float>>`).
  A remembered width set that doesn't match the column count is ignored.
- `header`, `sort` and `resizable` on a list without `<column>` children
  throw at build.

## 9. `<list selectmode="multi">` (series 8)

- `selectmode`: `single` (default, today's behaviour) or `multi`.
- In `multi`: `selected` binds `IReadOnlyList<int>` (plugin rows), `onchange`
  is `Action<IReadOnlyList<int>>` with the new selection. The selection
  band draws for every selected row (`selectionband` is implied).
- Click: selects only that row. Ctrl-click: adds or removes it. Shift-click:
  selects the range from the anchor (the last plain or Ctrl click) to this
  row, in *view* order. Ctrl+A while focused selects all. Up/Down move a
  single selection; Shift+Up/Down extend it.
- The list is the plugin's: the host never holds a selection of its own
  across frames, it calls `onchange` and draws whatever `selected` returns.

## 10. `<list tree="true">` (series 9)

```xml
<list name="rules" tree="true" levels="{Depths}" keys="{Ids}"
      header="true" selected="{Sel}" onchange="{Pick}">
  <column type="checkbox" values="{On}" onchange="{ToggleRule}"/>
  <column type="text" header="Rule" items="{Names}" sort="text"/>
  <column type="text" header="Value" items="{Values}"/>
</list>
```

- The plugin's rows are the tree flattened in pre-order. `levels`
  (`IReadOnlyList<int>`, 0 = top level) gives each row's depth; a row whose
  level is more than one deeper than the row before it is clamped to one
  deeper. `keys` (`IReadOnlyList<string>`) gives each row a stable id. Both
  required at build. Data can't be checked against the row count at build,
  so at draw time a row past the end of `levels` is level 0 and a row past
  the end of `keys` gets the key `"#<row>"`.
- The **first text column** draws the indent (12px per level) and a ▸/▾
  arrow for rows that have children. Clicking the arrow, or Left/Right on a
  selected row, collapses or expands; Right on an expanded row and Left on a
  collapsed one move to the first child and the parent.
- Collapsed rows' descendants are left out of the view map; row indices the
  plugin sees are still plugin rows.
- **Expansion** follows the remembered-state rule: `expanded`
  (`IReadOnlySet<string>` or `IEnumerable<string>` of keys) and `onexpand`
  (`Action<string, bool>`, key and new state). Unbound, keys default to
  expanded unless `collapsed="true"` on the list (all start collapsed), and
  the host remembers the keys that differ from the default.
- **Sorting** in a tree sorts each set of siblings and keeps children under
  their parent.
- Multi-select works on visible rows; collapsing a parent deselects its
  hidden descendants (calls `onchange`).

## 11. `<contextmenu>` (series 10)

```xml
<button x="8" y="8" w="80" h="20" text="Profile">
  <contextmenu items="{ProfileMenu}" enabled="{ProfileMenuEnabled}" onselect="{OnProfileMenu}"/>
</button>
<list …>
  <contextmenu items="{RowMenu}" onopen="{OnRowMenuOpen}" onselect="{OnRowMenu}"/>
</list>
```

- A child of the element it belongs to; any element may have at most one.
  It is not drawn in place.
- `items`: literal `a|b|-|c` or bound `IEnumerable<string>`. `-` is a
  separator. `enabled`: optional bound `IEnumerable<bool>` per item.
  `onselect`: `Action<int>`, the item index (separators count). Required.
- Opens on `RightClick` over the parent element, at the cursor, flipped or
  shifted to stay on screen. Uses the root's active-popup slot and overlay
  pass, as `<menu>` does, so opening it closes any other popup. Escape, a
  click outside, or the parent hiding closes it. Up/Down/Enter navigate it.
- **On a list:** right-clicking a row first selects it, unless it is
  already in a multi-selection; then `onopen` (`Action<int>`, the plugin
  row, or −1 for empty space) fires; then the menu reads `items` and opens.
  An empty item list doesn't open.
- On a disabled parent it doesn't open.

## 12. `<hotkey>` (series 11)

```xml
<hotkey x="8" y="8" w="140" h="20" value="{ToggleNavKey}" onchange="{SetToggleNavKey}"/>
```

- `value`: `string`, canonical form `Ctrl+Alt+Shift+Key` (modifiers in that
  order, key named as `Silk.NET.Input.Key`'s name), empty = unbound.
  Displayed with `RetailKeyNames.Describe`. An unparseable value shows as
  unbound.
- `onchange`: `Action<string>`, the new canonical string. Required.
- Click (or Enter/Space when focused): shows "Press a key…" and starts
  `InputDispatcher.BeginCapture`. The next chord is written in canonical
  form and reported. Escape cancels, Backspace/Delete reports empty.
  Losing focus or the window hiding cancels the capture.
- A modifier pressed alone is not a chord; capture keeps waiting.
- Keyboard input only (no mouse buttons).

- **Bridge to `PluginKeyChord`** (amendment 2026-10-22, plan 11b). The string's key names are Silk's, which differ from `PluginKey` for the keypad keys and Grave. `PluginKeyChord.TryParseHotkeyText(string, out PluginKeyChord)` reads a box string (either spelling; false for empty and for keys `PluginKey` lacks) and `PluginKeyChord.ToHotkeyText()` writes one. Additive public contract members; the box itself is unchanged.

## 13. `<color>` (series 12)

```xml
<color x="8" y="8" w="40" h="18" value="{BarColor}" onchange="{SetBarColor}" alpha="true"/>
```

- `value` and `onchange` take the bound-colour types: `uint`, `int` or a
  `#AARRGGBB` `string`, with `onchange` of the same type (`Action<uint>`,
  `Action<int>`, `Action<string>`). Mismatch throws at build.
- The control is a swatch (checkerboard behind it when alpha < 1).
  Clicking opens a popup (active-popup slot) containing:
  - a 128×128 saturation/value square and a 16px hue strip,
  - an alpha strip when `alpha="true"` (default `false`: alpha forced to FF),
  - a hex field (`#RRGGBB` or `#AARRGGBB`), applied on Enter or focus loss,
  - a row of swatches: the theme palette tokens, then up to 8 recently
    picked colours (remembered per character, shared by all `<color>`s).
- Live: `onchange` fires while dragging (at most once per frame). A click
  outside the popup closes it and keeps the current colour. Escape closes it
  and restores the colour the popup opened with, reporting it if it differs.
- The square and strips are textures from `IMarkupTextureService`, rebuilt
  when the hue changes and freed when the popup closes.

## 14. Dialogs (series 13)

New members on `IUiRegistry` (and the owner-scoped forms on
`IScopedUiRegistry`), defaulting to `false` on hosts that draw nothing:

```csharp
public sealed record PluginDialog(
    string Title, string Message,
    IReadOnlyList<string>? Buttons = null,   // null = ["OK", "Cancel"]
    string? OwnerView = null);

bool Confirm(PluginDialog dialog, Action<int> onResult);
bool ShowModal(string viewName, string? ownerView = null);
```

- **Window-modal.** A dialog blocks only its owner window: the owner draws
  dimmed, and presses on it are swallowed and flash the dialog. Every other
  window, chat, movement and game hotkeys work as usual. Plugins never get
  `UiRoot.Modal`. No owner (or an unknown one) blocks nothing.
- **`Confirm`** opens a host-built window centred on the owner (or the
  screen). It uses the retail confirmation dialog look (layout 0x15 art)
  unless the owner window is `theme="plugin"`, which gets a themed box. Buttons in a row;
  the last is Cancel. It takes keyboard focus when it opens: Enter picks
  the first button, Escape the last. `onResult` gets the button index; ×,
  the owner window closing, and the plugin unloading all report the last
  index. Exactly one result per dialog. Several confirms queue per owner.
- **`ShowModal`** shows one of the plugin's registered panels (as
  `ShowPanel` does) and makes it modal over `ownerView`. The modal ends when
  the panel hides, by `HidePanel` or its ×. Returns false for an unknown
  view.
- Docs: `docs/plugin-api.md` (members and the window-modal rule).

## 15. Fork merge (after the branch)

On fork `main`, after merging `feat/markup-controls`:

- Flex: every new element takes the flex item attributes; `<expander>`
  collapse reflows inside a flex container instead of shifting siblings;
  `<split>` panes may be flex containers.
- Modern theme / fonts: new controls use the fork's SemiBold and metrics
  where the existing ones do.
- `<Version>` → `0.1.26-fork.3`; note in `docs/plugin-api.md`.
- Expected conflicts: `MarkupDocument.cs` (cases next to fork-only ones),
  `PluginMarkupTheme.cs`, `UiMarkupList.cs` (fork button column work). The
  plan lists resolutions.

## 16. Testing

- **Unit (per series), headless**, in the existing style: build from an XML
  string with a binding object, mount on a `UiRoot`, drive
  `OnMouseDown/Up/OnScroll/OnKeyDown`, assert on the binding; draw tests
  with `RecordingGpuDevice`. New files under `tests/AcDream.App.Tests/UI/`
  named `Markup<Control>Tests.cs`. Each series covers: build errors for
  every required/mistyped binding, the callback contract, keyboard,
  disabled, both looks draw without throwing, and remembered state (fake
  `IMarkupControlState`) where it applies.
- `<list>` series also cover: callbacks receive plugin rows under sort,
  selection follows the row across a re-sort, tree view map with collapsed
  parents, multi-select range in view order.
- Dialogs: `ScopedUiRegistry` tests for one result per dialog, owner
  blocking, unload → cancel.
- **Gate (branch):** solution Release build green; App/Plugin test
  failures on macOS equal upstream's baseline by name.
- **Sample plugin:** `samples/AcDream.Plugins.ControlsDemo`, one window per
  control group in both looks, used for the live check and as reference
  markup.
- **Live check:** on the local ACE server with the demo plugin, Classic and
  plugin theme, at 2x. Ask the user before launching the client.
