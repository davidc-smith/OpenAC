# Command Deck resize — design

Date: 2026-10-26. Fork-only follow-up to the Command Deck
(`docs/superpowers/specs/2026-10-26-command-deck-design.md`, archived as
`archive/specs/command-deck-spec`).

## Goal

Let the player size the Modern HUD Command Deck two ways:

- **Scale** the whole deck evenly (text included) by dragging a right-hand corner.
- **Stretch** it wider by dragging the right edge, so the bars, jump strip,
  power track and casting line get longer.

The deck's height still never changes between modes; it changes only when
the player rescales.

## Behaviour

### Grips

| Grip | Effect |
|------|--------|
| Right edge | Width only, at the current scale. |
| Top-right / bottom-right corner | Scale (keeps the stretch ratio; width follows). |
| Left edge, left corners, top edge, bottom edge | No resize. |

The left side stays where it is because the left-hand studs (x 2–28) and the
orb sit on the window's left edge; a left grip would take clicks meant for
the studs. To widen towards the left, the player widens on the right and
moves the deck.

Visuals: no grip marks at rest. When the pointer is over a grip, the existing
resize cursor shows and a faint bronze stroke lights the right edge or corner
of the frame.

### Limits and snapping

- **Scale:** 0.75 to 1.5, in 0.05 steps. While dragging a corner it snaps to
  the nearest step and locks onto 1.00 when within ±0.03.
- **Stretch** (content width ÷ 420 at the current scale): 1.0 to 2.0, continuous.
  A corner drag keeps the stretch; an edge drag keeps the scale.
- Window size = `(420 × stretch × scale, 150 × scale)`.
- The existing screen limits still apply on top: the deck cannot be resized
  or restored past the screen, so on a narrow screen the maximum stretch shrinks.

### What gets longer when the deck is wider

At the same scale:

- **Stays the same size:** the orb, the left-hand studs, the row labels (60 pt),
  the H/M/L buttons and the recast button (both kept against the right edge),
  and the recall studs (kept on the left at their current 30 pt spacing).
- **Gets longer:** the health, stamina and mana bars, the jump track, the power
  track (fills up to the H/M/L buttons), and the casting text (more room
  before a long name ends in "...").
- The frame's right edge follows the content's right edge, with the same
  12 pt margin as today.

At stretch 1.0 and scale 1.0 every rectangle matches today's
`CommandDeckLayout` exactly.

### Saving and reset

No new settings. `RetailWindowLayoutPersistence` already saves each window's
width and height per character (and in named profiles). On restore and after
every resize the deck works out:

- `scale = snap(height ÷ 150)`, limited to 0.75–1.5
- `stretch = width ÷ (420 × scale)`, limited to 1.0–2.0

and corrects its own width and height to match (an invalid or hand-edited
saved size is fixed this way). The existing "reset layout" puts the deck back
to its authored 420 × 150, which is scale 1.0 and stretch 1.0. No
double-click or menu reset.

## Design

### 1. `DeckGeometry` (replaces the static `CommandDeckLayout`)

`src/AcDream.App/UI/Hud/CommandDeckLayout.cs` becomes an immutable
`DeckGeometry(float Scale, float Stretch)`:

- It keeps the public shape of today's class (`Frame`, `Orb`, `HealthBar`, ...,
  `HeightButton(i)`, `RecallStud(i)`, `Stud(i)`, `AllRects()`, `RectFor`,
  `ZoneAt`, `PowerAt`, `InOrb`), as instance members.
- Rectangles are built in unscaled points from `ContentRight = 408 + 420 × (Stretch − 1)`,
  then every rectangle is multiplied by `Scale`.
- The constants and helpers (`BaseWidth = 420`, `BaseHeight = 150`, scale and
  stretch limits, `SnapScale`, `FromSize(w, h)`, `Size`) live on the type, so
  the size rules can be tested without a UI.
- `DeckGeometry.Default` = scale 1, stretch 1.

`UiCommandDeck` holds the current geometry and passes it to drawing, click
detection and tooltip placement in place of the static calls. Nothing in the
geometry depends on combat mode, so mode changes never change the size.

### 2. Fonts per scale

`DeckFonts` today loads 14 px values, 12 px labels and a 14 px SemiBold title
font once. It becomes a small cache keyed by scale step: each face is loaded
lazily at `round(14 × scale)` / `round(12 × scale)` pixels (with the same
Retina handling as today), and kept. At most 16 steps × 3 faces, and only the
sizes actually used are ever loaded. Text is drawn natively at each size; no
bitmap scaling. Deck tooltips keep their normal size; only their anchor uses
the geometry.

Line weights (frame edge, bar outlines, the power handle) scale with the
geometry and are rounded to whole pixels, with a 1 px minimum.

### 3. Resize hooks on `UiElement` / `UiRoot`

Today `UiRoot` moves each dragged edge on its own and always offers both sides
at a corner. Two virtual members are added to `UiElement`; both do nothing by
default, so other windows behave exactly as today:

```csharp
/// Edges a resize may start from, given the edges the pointer is on.
protected internal virtual ResizeEdges FilterResizeEdges(ResizeEdges hit) => hit;

/// The size a resize should use, given the edges being dragged and the size
/// the drag proposes (already within Min/Max and the parent).
protected internal virtual (float Width, float Height) ConstrainResize(
    ResizeEdges edges, float width, float height) => (width, height);
```

- `UiRoot` calls `FilterResizeEdges` wherever it works out hover and grip edges
  (border hit and authored grip), so a refused edge shows no resize cursor and
  starts no resize.
- During a drag `UiRoot` calls `ConstrainResize` after `ResizeRect`, then puts
  the window back against the edges that are not being dragged (for example,
  a top-right drag keeps the bottom and left edges fixed).

`UiCommandDeck`:

- `FilterResizeEdges`: allows exactly `Right`, `Right|Top`, `Right|Bottom`; every
  other edge set becomes `None`.
- `ConstrainResize`: edge only → keep the scale, limit the width to the stretch
  range. Corner → scale from the proposed height (snapped as above), keep the
  stretch, width follows.
- Min/Max width and height are set to the full allowed range (scale 0.75 /
  stretch 1.0 to scale 1.5 / stretch 2.0) so the persistence limits accept any
  valid size; `ConstrainResize` and the restore correction enforce the
  combined rule.
- `Resizable = true`; `ResizeX = ResizeY = true`; `ResizableEdges = Right`.

After a resize (and on restore) the deck rebuilds its geometry with
`DeckGeometry.FromSize(Width, Height)` and corrects its size if that changed it.

### 4. Edge cases

- **Screen too small:** the screen limits apply after the deck's rules. If a
  saved size cannot fit, the restore picks the largest valid size that does.
- **Retina / external screen:** fonts load at deck scale × the existing 2×
  handling; covered in the live check.
- **Modern HUD off and on, relog, combat stance, magic, recall:** no geometry
  change; the saved size is kept.
- **Moving the deck:** unchanged; only grips resize.
- **Default placement:** `WindowPlacementGeometry` centres the authored size at
  the bottom of the screen; unchanged (a reset is always 420 × 150).

## Testing

- `CommandDeckLayoutTests` → over a grid of scales (0.75, 1.0, 1.5) × stretches
  (1.0, 1.5, 2.0): every part sits inside the frame, the frame inside the
  window, no overlapping clickable parts, H/M/L and recast against the right
  edge, the power track and bars longer as the stretch grows, and scale 1 /
  stretch 1 matching today's rectangles exactly.
- Size rule tests: `SnapScale` steps and the 1.00 lock; `FromSize` limits;
  edge drag keeps the height; corner drag keeps the stretch.
- `UiRoot` tests: the default hooks change nothing (existing resize tests stay
  green); a filtered edge shows no hover cursor and starts no drag; a top-right
  constrained drag keeps the bottom-left corner fixed.
- Persistence round-trip: a saved scaled + stretched deck restores to the same
  geometry; an invalid saved size is corrected; reset layout gives 420 × 150.
- Existing deck draw, click, drag and tooltip tests move to a geometry
  instance, plus a scaled case for click detection.
- Tests run through `~/code/OpenAC/test-baseline/run-mac-tests.sh`.
- **Live check** on the local ACE server (10.10.20.20), only once the user
  says they are free: edge and corner drags at both ends of the range,
  snapping to 100%, power drag and H/M/L at 150% and 75%, relog and restart,
  reset layout, Modern HUD off and on, Retina and the external screen.

## Out of scope

- Resizing from the left side, top or bottom.
- Scaling deck tooltips.
- A scale slider in Options, or a reset gesture on the deck.
- Spreading the recall studs across a wider row.
- Upstream PR (the Command Deck is fork-only).
