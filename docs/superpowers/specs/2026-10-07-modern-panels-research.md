# Modern game panels — research notes (pre-PoC)

Status: research only, no code. Fork-only; not for upstream.
Date: 2026-10-07. Baseline: fork `main` at `a0c1408e`.

## Goal

An option to replace the retail game panels (Inventory, Character,
Spellbook, Options, …) with custom panels that look modern and are laid out
quite differently from retail. Must be optional: with the option off, the
client behaves exactly as today.

## Decision so far

**Client-only.** The modern panels are built inside `AcDream.App`, not via
the plugin infrastructure. No plugin-contract changes, no `-fork.n` bump.

(Considered first: doing it through plugins. Rejected — plugins cannot
take over a retail window's open path, markup has no item-slot element, and
item drag is internal to the client. Those gaps vanish client-side.)

## How the retail panels work today

- **Panel catalog** — `src/AcDream.App/UI/RetailPanelCatalog.cs` maps retail
  panel ids to window names (`WindowNames.*`): CharacterInformation (3),
  PositiveEffects (4), NegativeEffects (5), Inventory (7), LinkStatus (8),
  MiniGame (9), Options (10), Character (11), SocialPanel (12), Magic/
  Spellbook (13), Vitae (15), MapHouse (16), Journal (25), Book (2).
  `ToolbarPanels` is the subset with toolbar buttons.
- **Controllers bind to DAT layouts by element id.** Each panel controller
  in `src/AcDream.App/UI/Layout/` looks up its widgets from the retail
  LayoutDesc by hard-coded id, e.g. `InventoryController.cs:13`
  (`ContentsGridId = 0x100001C6`, burden meter, container list, …). Look
  and behaviour are entangled in the constructors — reskinning retail
  panels into something "quite different" is not realistic; a modern panel
  is a new view.
- **Mounting** — panels are built and registered in
  `src/AcDream.App/UI/RetailUiRuntime.cs` (`_panelUi.RegisterMainPanel(...)`
  around lines 2057, 2255, 2477).
- **Switching** — `src/AcDream.App/UI/Layout/RetailPanelUiController.cs`
  owns the "one main panel at a time" behaviour (`TogglePanel`,
  `SetPanelVisibility`, restore-previous), keyed by **window name**.
- **Shared frame size** — `ConfigureMainPanelFrame` forces every main panel
  to the retail frame's min/max width/height.
- **Single open seam** — toolbar buttons, keybinds (`InputAction`) and the
  plugin `Show/Hide/ToggleClientWindow` API all go through
  `RetailUiRuntime.ToggleWindow(name)` / `ShowWindow` / `HideWindow`
  (`RetailUiRuntime.cs` ~1290–1325; `BindToolbarPanelButtons`,
  `OnWindowVisibilityChanged` keeps toolbar lamps in sync).

## Why client-only is feasible

1. **Windows are DAT-agnostic.** `RetailWindowHandle` is just
   `OuterFrame` + `ContentRoot` (`UiElement`s) + optional
   `IRetainedPanelController` / `IRetainedWindowStateController`, registered
   through `RetailWindowManager.Register`. Markup windows already register
   this way (`MarkupWindow.cs:27`). A modern window registered under the
   **same window name** as the retail one inherits: panel switching,
   toolbar button + lamp, keybinds, layout persistence
   (`RetailWindowLayoutPersistence`), and plugin show/hide — for free.
2. **Data and actions are injected, not DAT-derived.** E.g.
   `InventoryController` receives `ClientObjectTable`, `Spellbook`,
   `SelectionState`, `ShortcutStore`, `RuntimeItemInteraction`,
   `StackSplitQuantityState`, and send delegates (use, put-in-container,
   split, merge). Character side has `CharacterSheetProvider`,
   `CharacterStatController`, etc. A modern view can consume the same
   sources.
3. **Widgets exist and are constructible in code:** `UiItemList` (with
   `IItemListDragHandler` drag/drop), `UiItemSlot`, `UiMeter`,
   `UiNineSlicePanel`, `UiScrollArea`/`UiScrollablePanel`, `UiTabPanel`,
   `UiViewport` (paperdoll 3D), `UiFlexBox` + `Layout/Flex/*`, plus the
   modern plugin theme (`PluginUiStyle`, `PluginMarkupTheme`), SVG icons
   (`SvgIconRasterizer`, `PluginSvgIconCache`) and canvas fonts.

## The option

- A client setting, e.g. `ModernPanels` (global first; per-panel later if
  wanted).
- At mount time in `RetailUiRuntime`, choose retail vs modern factory per
  panel and register the result under the retail window name. Off = no
  change in code path.
- First cut: change applies on restart / UI rebuild (no hot swap).

## The real work

1. **Logic/view split per panel (main cost).** Retail controllers mix
   behaviour with DAT lookups. Per panel, either:
   - extract behaviour (sorting, burden calc, container switching, drag
     rules, …) into a view-agnostic model used by both retail and modern
     views — **preferred**; or
   - duplicate it into the modern view — faster, but drifts.
2. **Shared frame geometry.** Modern panels that want their own size or
   free resize must bypass or replace the `ConfigureMainPanelFrame`
   constraint for their entries.
3. **Per-panel difficulty:**

   | Tier | Panels | Why |
   |---|---|---|
   | Easy | Character, CharacterInformation, Positive/Negative Effects, Vitae, LinkStatus | mostly read-only display |
   | Medium | Spellbook, Journal, Social | lists + a few actions |
   | Hard | Inventory + Paperdoll, Options, MapHouse | drag/container rules, many settings pages, map surface |

## Suggested PoC

1. Add the `ModernPanels` option and the retail-vs-modern factory switch in
   `RetailUiRuntime`.
2. Build a modern **Character** panel (light logic extraction), registered
   under `WindowNames.Character`.
3. Verify: toolbar button + lamp, keybind, main-panel switching with other
   retail panels, layout persistence, option off = byte-identical retail
   behaviour.
4. Only then tackle Inventory, using the logic-extraction pattern proven on
   Character.

## Open questions for the PoC

- Global switch or per-panel choice?
- Do modern panels keep the retail "one main panel at a time" docking, or
  become free-floating windows?
- Visual direction: reuse the modern plugin theme tokens as-is, or a
  separate panel theme?
- Hot-swap the option at runtime, or restart-only?
