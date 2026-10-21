# Rynth drop targets: design

Date: 2026-10-21. Branch `feat/rynth-drop-targets` (from main 0a94aae4).

## Goal

AC items dragged out of OpenAC's packs or main inventory can be dropped on
RynthCore's ImGui panels that take items: Item Count (track the item), the
Loot Editor (rule from the item) and the Mini Remote slots. Their own
`UiDropTargets` is compiled unchanged, so the panels' "drop here" look,
`TryTake` and per-slot targets behave as in RynthCore. Drags out of their
Inventory panel onto those panels start working too (their `InventoryFace`
already calls `TryClaim`; today the stand-in always says no).

It also fixes a hazard: today an item let go over any ImGui window is still
handled by the retail UI underneath, so it can land in the world or on a
retail panel hidden behind the ImGui window.

## Decisions (user, 2026-10-21)

- Sources: packs + main inventory only (`ItemDragSource.Inventory`), as
  RynthCore claims only drags out of an AC item list. Paperdoll, shortcut bar
  and ground drags are never offered to a panel.
- An item let go over an ImGui window that doesn't claim it is cancelled: it
  stays where it was. This applies to every drag source, not only inventory.
- Approach A: their `UiDropTargets` unchanged + a release claim in OpenAC.
  (Rejected: proxy `UiElement` targets in `UiRoot`; swallowing the mouse-up
  over ImGui, which leaves the drag stuck.)

## OpenAC side

- `IClientItemDrops` (new, `AcDream.ClientExtensions`, host-only like
  `IClientScreen`; no contract bump), on `ClientExtensionContext.ItemDrops`:
  - `uint DraggedItem` — the dragged object id while a drag whose payload is an
    `ItemDragPayload` with `SourceKind == Inventory` is active; 0 otherwise.
  - `void SetDropClaimer(Func<uint, Vector2, bool>? claimer)` — item id and the
    release point in overlay (ImGui) coordinates; true = a panel took it.
    One claimer (the Rynth host); null clears it.
- `UiRoot` gains a release interceptor
  (`Func<object, Vector2, bool>? DragReleaseInterceptor`), asked when a drag
  is let go, before `FinishDrag`'s hit-test, with the payload and the
  release point in window points (the point before `UiRoot` maps it to its
  canvas, so the same space as ImGui's). True
  ends the drag there: the source slot stops being dragged, no
  `DropReleased`, no `DragReleasedOutsideUi` (no world placement).
- The App's interceptor (`ClientItemDrops`, `AcDream.App/Extensions`):
  - pointer not over an ImGui window → false (today's behaviour, unchanged);
  - over one → an inventory `ItemDragPayload` with a claimer is offered to it
    (window point passed through unchanged); returns true
    whether claimed or not (cancel rule).
- "Over an ImGui window" is a new `ImGuiOverlay.PointerOverWindow`, set each
  frame after `NewFrame` from
  `IsWindowHovered(AnyWindow | AllowWhenBlockedByActiveItem)`.
  ImGui 1.91 clears its hovered window while a mouse button pressed outside
  ImGui is held (click ownership), so during any retail drag
  (`UiRoot.DragPayload != null`, given to the overlay as
  `Func<bool> ExternalDragActive`) the overlay submits an external drag
  source each frame (`BeginDragDropSource(SourceExtern)` + payload type
  `OPENAC_DRAG`), which keeps hover tracking alive. Checked headless
  2026-10-21: without it hovered=false over a window; with it hovered=true
  over and false off the window, `WantCaptureMouse` stays false (retail still
  gets the mouse-up), and the payload clears after release.
  `WantCaptureMouse` itself can't be used: it is false during such a drag.
  The flag is last frame's, which is fine at a hand's speed.
- Coordinates: `UiRoot`'s mouse handlers and the overlay's input feed both
  get Silk window positions (points); `UiRoot` divides by its `CanvasScale`
  only after, so the interceptor gets the unmapped point. The Retina live
  check confirms.

## Rynth side

- `external/RynthCore/src/RynthCore.Engine/ImGui/UiDropTargets.cs` compiled
  unchanged into `AcDream.Rynth.EngineUi`; the `UiDropTargets` stand-in in
  `StandIns/DashboardStandIns.cs` is deleted.
- What their file links to:
  - `user32!GetCursorPos` — the existing `RynthInteropRedirect` P/Invoke step
    points it at a new `Win32CursorStandIn`. Their `out POINT` is a private
    nested struct the stand-in can't name, so the tool also matches a by-ref
    struct parameter whose type has the same instance fields (names and
    types, in order); the stand-in declares its own `CursorPoint { int X, Y }`.
    It returns a host-set point in
    ImGui coordinates (overlay mouse each frame; the exact release point
    while a claim runs).
  - `ImGuiPopOuts` stand-in gains `ClientOriginOnScreen() => Vector2.Zero`
    and `IsCursorOver(string) => false` (OpenAC has no pop-outs, so
    `InPopOutFrame` stays false and no target is ever popped).
  - `Compatibility.UiElementHooks.CurrentDragObject` stand-in reads a
    host-set source, fed from `IClientItemDrops.DraggedItem`, so
    `AnyDragInProgress` lights the faces' drop look during inventory drags.
- Claimer: `RynthDropClaim.Claim(item, point)` (EngineUi); the Rynth host's
  `RynthClientExtension` registers it with `SetDropClaimer` at attach (when
  the overlay is there) and clears it, and the stand-in sources, at dispose. It sets the cursor stand-in to the release point, calls
  `UiDropTargets.TryClaim(item, 0, out _)`, then restores the cursor source.
  The face takes the item with `TryTake` on its next draw, as in RynthCore.
- Threading: one game thread; their lock and 500 ms staleness stay as is.

## Out of scope

- Spells dropped on panels (RynthCore claims only objects).
- Drags from their Inventory panel onto OpenAC's retail windows.
- RynthAi's own ImGui drag payload (`RYNTH_INV_ITEM`) — already ImGui-only.

## Testing

- Rynth.Host.Tests: claim reaches the published accepting target under the
  release point; stale or not-accepting targets aren't claimed; the cursor is
  restored after a claim; the claimer is cleared at dispose;
  `CurrentDragObject` follows `DraggedItem`.
- App tests: a drop off ImGui is unchanged (hit-test / outside callback run);
  an inventory drag over ImGui reaches the claimer; a paperdoll drag over
  ImGui is cancelled and not offered; an unclaimed drop over ImGui is
  cancelled (no `DropReleased`, no world placement); `DraggedItem` is 0 for
  non-inventory drags and after the drag ends.
- Live check (user): pack item onto Item Count, Loot Editor and a Mini Remote
  slot (drop look shows while dragging); drop on the dashboard cancels; their
  Inventory panel item onto a Remote slot; normal retail drags and world
  drops unchanged; on the Retina display.
