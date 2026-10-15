# The host remembers a canvas's position (Aethrix api-gap #52)

Date: 2026-10-15. Scope: fork client only (no Aethrix migration, no upstream PR).
Contract: `0.1.25-fork.7`.

## Problem

A plugin canvas starts at the anchor and offset its descriptor gives. The host saves and
restores every window's placement per character (`RetailWindowLayoutPersistence`), but never a
canvas's, so a canvas the player moved is back at its registered place after a logout and login,
a client restart or a hot reload. Every plugin with a movable canvas has to store the position
itself and set it again at login (Aethrix does this for four HUDs).

A plugin moves its canvas by setting `IPluginCanvas.Offset` (and possibly `Anchor`) on each
pointer move, so the host never sees a drag "end". Each `SettingsStore` save rewrites the whole
settings file, so the host can't save on every set.

## Decisions

- **Opt-in.** Only canvases registered with `PluginCanvasDescriptor.RemembersPosition = true`
  are saved and restored. A popup or menu the plugin places itself is never touched, and existing
  plugins don't change.
- **No new event.** At login the host sets `Anchor` and `Offset`; a plugin reads them when it
  needs them. The docs say so.
- **Anchor-relative, not per resolution.** The saved value is the anchor and the offset. Unlike
  window placements there is no per-resolution copy; the restore clamps the canvas onto the screen.

## Contract (`AcDream.Plugin.Abstractions`)

`PluginCanvasDescriptor.RemembersPosition` (`bool`, `init`, default `false`), documented as:

- On, the host saves the canvas's `Anchor` and `Offset` for the logged-in character and puts them
  back when that character next enters the world (and at registration, for a canvas registered
  while the character is already in the world). It is kept on screen when put back.
- A move is saved once the canvas has stayed put briefly and no press is held on it, so a drag is
  saved once, after the release.
- The plugin should read `Anchor` and `Offset` rather than cache them: the host may change them
  when a character enters the world.
- Nothing is saved or restored before a character has logged in, for an `InMarkup` canvas (its
  element places it), or on a host without a window (the flag is kept).
- A host before `0.1.25-fork.7` ignores the flag.

`ForkContractVersion` in `AcDream.Plugin.Abstractions.csproj` goes to `0.1.25-fork.7`.

## Storage (`SettingsStore`)

New section, beside `windowPlacements`:

```json
"pluginCanvasPositions": {
  "<characterKey>": {
    "<pluginId>/<canvasId>": { "anchor": "BottomRight", "x": -12, "y": -40 }
  }
}
```

- `PluginCanvasPosition? LoadPluginCanvasPosition(string toonKey, string canvasKey)` and
  `void SavePluginCanvasPosition(string toonKey, string canvasKey, PluginCanvasPosition position)`.
  `PluginCanvasPosition` is a small record (`string Anchor`, `double X`, `double Y`) in the
  settings namespace. The anchor is stored by name, so the UI abstractions don't need the plugin
  contract; an unknown name, a missing field or a non-finite number loads as null.
- Errors are logged and swallowed, as the window methods do.

## Host (`AcDream.App/UI/PluginCanvasPositionPersistence`)

One instance, owned by `RetailUiRuntime`, built beside `RetailWindowLayoutPersistence` from the
same `SettingsStore`, character key and screen size. It sees canvases through a narrow interface,
so its tests need no renderer:

```csharp
internal interface IRememberedCanvas
{
    string Key { get; }            // "<pluginId>/<canvasId>"
    PluginCanvasAnchor Anchor { get; set; }
    PluginPoint Offset { get; set; }
    (int Width, int Height) Size { get; }
    bool IsHoldingPointer { get; }
}
```

`PluginCanvasRegistration` provides it. `IsHoldingPointer` is new; the mounted element sets it
while it holds a press (`Press` until `Release` or a cancel).

Behaviour:

- **Track.** `Add(canvas)` when an opted-in, non-markup canvas mounts; `Remove(canvas)` when it
  unmounts or is disposed. Each tracked canvas keeps a baseline (the position last saved or
  restored), a pending flag and the time the position last changed.
- **Restore.** `RestoreAll()` runs from `RetailUiRuntime.RestoreLayout()`. It first flushes any
  pending saves under the previous character, then, for the current character (skipped for an
  empty or `"default"` key), loads each canvas's entry. When there is one, it sets `Anchor`, then
  sets `Offset` clamped so the whole canvas is on screen at its current size; when there isn't,
  the canvas stays where it is. Either way the result becomes the baseline. The restored
  character is remembered, and `Add` of a canvas while that character is still current restores
  that canvas the same way.
- **Save.** `Tick(now)` runs once a frame from `RetailUiRuntime`. For each tracked canvas whose
  `Anchor`/`Offset` differs from its baseline, it records the change time. When the position has
  not changed for 0.5 s and `IsHoldingPointer` is false, it saves under the restored character and
  makes the position the baseline. A canvas moved every frame is saved once it stops.
- **Flush.** `Remove` and `Dispose` save a pending change immediately (still under the restored
  character). Nothing is saved when no character has been restored.
- **Clamp.** Positions are in interface pixels. The canvas's top-left on screen is computed from
  anchor, offset and size as `PluginCanvasElement` places it (shared helper), clamped into
  `[0, screen - size]` (0 when the canvas is larger than the screen), and turned back into an
  offset for the same anchor.

Threading: `Anchor` and `Offset` are written by the plugin on its Tick thread and read by the
interface each frame already. The persistence reads and writes them from the interface thread, as
the element does; no new locking.

## Docs

- `docs/plugin-api.md`, Canvases: a "Remembered position" subsection with the rules above and a
  short example.
- Aethrix `docs/api-gaps.md` #52: "(fixed in the fork, <commit>)" with the minimum contract.

## Tests

- `SettingsStore`: round trip, other sections preserved, a bad entry loads as null.
- `PluginCanvasPositionPersistence` with fake canvases and a fake clock: restore sets anchor and
  offset; the restore is clamped for every anchor kind; no entry keeps the registered position;
  the restore itself is not saved; a move saves once after 0.5 s of stillness; a held press delays
  the save until release; a canvas moving every frame is not saved until it stops; the
  `"default"` key never saves; `Remove`/`Dispose` flush; a character switch flushes to the old
  character and restores the new one; a canvas added after a restore is restored.
- `RetailUiRuntime` or element-level: an opted-out canvas is never tracked; a markup canvas is
  never tracked; `IsHoldingPointer` follows press, release and cancel.
- The host-parity allow-list if the new descriptor member needs an entry.
- Optional live check on the local ACE server with a demo canvas.
