# Rynth Icons: AC Icons in Script Windows, Phosphor Icons on the Bar

Date: 2026-10-18. Branch: `feat/rynth-icons`. Builds on
`2026-10-18-rynth-bar-and-saved-layout-design.md`, which left icons as
placeholders (the title's first letter).

## Goal

1. Script windows draw real AC icons for their Image / ImageButton ops
   (icon ids, object ids and spell ids), the same pictures the client shows
   in its inventory and spell bar.
2. The launcher bar draws Phosphor icons, as RynthCore's own bar
   (`ImGuiBar.cs`) does: a known title gets its icon, anything else keeps its
   first letter.

## Decisions

- Both pieces, one plan (the user picked "both").
- Bar icons are Phosphor glyphs merged into the overlay's font, not AC icons:
  they match RynthCore and stay readable at bar size. The title-to-icon table
  is RynthCore's `PanelIcons` (Oracle → Sparkle, Tracker → ChartLineUp, ...).
- AC icons come from the client's existing `RetailPluginImageBackend`
  (`IconComposer` + `TextureCache`), so there is no new dat decoder and an icon
  in a script window is the client's own icon.
- The overlay stays safe: it draws only its font atlas and textures the icon
  service handed out. Any other texture id is skipped and logged, as now.

## Architecture

```
GameWindow ── retained UI built ──▶ ImGuiOverlay.BindIcons(RetailUiRuntime.ImageBackend)
                                        │
IClientOverlay.Icons (IClientOverlayIcons) ◀─ OverlayIcons (records handed-out handles)
IClientOverlay.AddMergedFont(ttf, glyphs, scale)
                                        │
ImGuiVulkanRenderer: draws atlas + handed-out handles; merges fonts at each atlas build
                                        ▲
RynthClientExtension.Attach ─ ScriptIcons.Source = overlay.Icons (ScriptWindows stand-in)
                            ─ overlay.AddMergedFont(Phosphor.ttf, PhosphorIcons.Baked, 1.2)
RynthBar ─ RynthBarIcons.For(title) → Phosphor glyph or first letter
```

### Client (`AcDream.ClientExtensions`, `AcDream.App`)

**`IClientOverlayIcons`** (new, `AcDream.ClientExtensions`):

```csharp
public interface IClientOverlayIcons
{
    /// <summary>An icon (0x06 RenderSurface) by id; a bare index gets 0x06000000 added. 0 = none.</summary>
    IntPtr Icon(uint iconId);
    /// <summary>A spell's icon. 0 = none or unknown spell.</summary>
    IntPtr SpellIcon(uint spellId);
    /// <summary>A known object's icon with its underlay, overlay and effects. 0 = none or unknown object.</summary>
    IntPtr ObjectIcon(uint objectId);
}
```

Every returned value is an ImGui texture id for `ImGui.Image`. Game thread only
(the thread that draws the overlay).

**`IClientOverlay`** gains:

- `IClientOverlayIcons Icons { get; }` - always non-null. Before the client's
  UI exists it returns 0 for everything.
- `void AddMergedFont(byte[] ttf, string glyphs, float scale)` - merge the
  font's `glyphs` (BMP code points) into the overlay's font at `scale` times
  its pixel size, from the next atlas build on. Called before the first frame
  (from `Attach`); a later call forces a rebuild on the next frame.

**`OverlayIcons`** (new, `AcDream.App/Extensions/Overlay`): implements
`IClientOverlayIcons` over an `IPluginImageBackend` bound late.
- `Icon` uses `TryGetClientArt(PluginIcons.Normalize(id))`; `SpellIcon` and
  `ObjectIcon` use the backend's methods of the same name.
- A UI texture handle `h` becomes ImGui texture id `h`; the handle-to-slot rule
  (`UiTextureTableHandle.ToSlot`, slot = h - 1) is applied in the renderer.
  The font atlas's id becomes its handle too (slot + 1), so an icon handle can
  never equal the atlas id.
- Each handle it hands out goes into a set the renderer reads (`IsIssued`).
- Exceptions from the backend are caught, logged once per id and kind, and
  give 0 (the replay then draws its placeholder).
- Caching is the backend's (`IconComposer` and `TextureCache` already cache by
  id and by layer tuple), so a lookup each frame costs dictionary hits. An
  object's icon is looked up again every frame, so a changed overlay or
  effect shows at once.

**`ImGuiOverlay`**: owns one `OverlayIcons`; `BindIcons(IPluginImageBackend)`
binds it (once; later calls are ignored and logged). Keeps the merged-font
list and passes it to the renderer.

**`ImGuiVulkanRenderer`**:
- `IsDrawableTexture` becomes `TryGetDrawSlot`: the atlas's handle, or an id
  the icon service issued; either way the slot is `id - 1`. Everything else is still skipped and
  reported once.
- `PrepareFrame` merges each registered font after `AddFontDefault`, at
  `FontSizePoints * scale * font.Scale`, with `MergeMode = true`,
  `PixelSnapH = true`, `GlyphMinAdvanceX` = that size and
  `FontDataOwnedByAtlas = false`. The font bytes and glyph ranges stay pinned
  (native copies) until the renderer is disposed.
- A font failing to merge (bad data) is logged and dropped; the atlas is built
  without it.

**Wiring**: `RetailUiRuntime` keeps the `RetailPluginImageBackend` it creates
(`internal IPluginImageBackend ImageBackend`); `GameWindow` calls
`_overlay.BindIcons(retained.Runtime.ImageBackend)` where it stores the
runtime.

### Extension (`AcDream.Rynth.ScriptWindows`, `AcDream.Rynth.Host`)

- **`ScriptIcons` stand-in**: gains `internal static IClientOverlayIcons? Source`.
  `TryGet(kind, id)` asks `Icon`, `ObjectIcon` or `SpellIcon` by kind;
  `Unknown`, id 0, no source or a 0 result return false. The `Icon` kind keeps
  RynthCore's rule (`AcIconDecoder.NormalizeIconId`, same as `PluginIcons.Normalize`).
- **Phosphor**: the ScriptWindows project links RynthCore's
  `ImGui/PhosphorIcons.cs` and embeds `Assets/Fonts/Phosphor.ttf`. Its MIT
  licence is added to `NOTICE.md`.
- **`RynthBarIcons`** (new, Host): RynthCore's `PanelIcons` table and
  `For(string title, out bool letter)` - the Phosphor glyph, else the first
  letter or digit upper-cased (today's `Letter`), else `?`.
- **`RynthBar`**: buttons draw `RynthBarIcons.For(title)`; the grip draws
  `DotsSixVertical`; menu entries are prefixed with their icon. Tooltips keep
  the title.
- **`RynthClientExtension.Attach`**: with an overlay, sets
  `ScriptIcons.Source = overlay.Icons` and calls
  `overlay.AddMergedFont(phosphorTtf, PhosphorIcons.Baked, 1.2f)`. `Dispose`
  clears `Source` if it is still ours. A missing font resource is logged; the
  bar then shows letters (Phosphor code points draw as nothing, so the bar
  checks the glyph exists via `ImFont.FindGlyphNoFallback` and falls back to
  the letter).

## Error handling

- No overlay: nothing changes (no icons, no bar).
- UI not built yet / icon unknown: 0 → placeholder square (script window) as today.
- Backend throws: logged once per id/kind, 0.
- Font missing or fails to merge: logged; bar falls back to letters.
- A foreign or stale texture id: never drawn, reported once (unchanged).

## Testing

- `AcDream.App.Tests/Extensions`:
  - `ImGuiVulkanRendererTests`: issued ids are drawable (slot = id - 1), others
    still are not; the existing atlas tests keep passing.
  - `OverlayIconsTests` (fake `IPluginImageBackend`): unbound → 0; each kind
    calls the right backend method with the normalised id; a returned handle is
    issued; a throwing backend gives 0 and logs once.
  - `ImGuiOverlayTests`: `BindIcons` binds once; `AddMergedFont` reaches the
    renderer and forces a rebuild.
  - Dat-backed (`[Trait("Lane", "InstalledDat")]`, run with
    `ACDREAM_DAT_DIR=/Users/davidsmith/AsheronsCall`): a known icon id and a
    known spell id resolve to non-zero handles through `RetailPluginImageBackend`
    + `OverlayIcons`.
- `AcDream.Rynth.Host.Tests`:
  - `ScriptIcons` maps each kind to the right `IClientOverlayIcons` call; no
    source / Unknown / 0 → false.
  - `RynthBarIcons`: Oracle → Sparkle, Tracker → ChartLineUp, unknown → letter.
  - Headless ImGui: the Phosphor merge builds an atlas containing `Sparkle`.
  - `RynthBarTests` keep passing.
- Live check (ask first; local ACE only): the bar shows the grip and Oracle's
  Sparkle in the Scripts menu. No current plugin draws an AC icon in a script
  window, so that path is covered by tests, not the live check.

## Out of scope

- Icons for windows whose titles aren't in RynthCore's table (they keep letters).
- Async icon decoding (RynthCore decodes on a worker; our backend decodes on
  first use, and the few icons a window shows are cheap).
- Pop-out windows, bar rows, bar settings.
