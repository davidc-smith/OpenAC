# HUD text size — design

Date: 2026-10-29. Status: approved in brainstorming (three sections), ready for a plan.

## Goal

Let the player make Modern HUD text bigger or smaller without changing the size of any deck. Each deck already has its own scale (window, boxes and text together); text size is a second, separate factor that changes text only.

## Decisions

| Question | Decision |
|---|---|
| Scope | One HUD-wide text size, and any deck can override it |
| Range | 80% to 150% in 10% steps; default 100% |
| Stored | Per client (not per character), with the Modern HUD switch's setting |
| Global control | "HUD text size" stepper row in the Config options page, under the Modern HUD switch |
| Per-deck control | Right-click a deck's empty frame or title → "Text size" submenu: "Same as HUD (N%)", then 80%…150%, the current one ticked |
| When text no longer fits | Rows in lists grow (the deck window never does); fixed boxes keep their size and their text is cut with "…" and capped to the box height |
| Chat Deck transcript and input | Keep their own pixel sizes from the appearance popup; the text factor applies to the rest of the Chat Deck |

## Model

- `HudTextSettings`: `Global` (percent, int) and `Overrides` (deck → percent). `FactorFor(deck)` returns the override or the global, as a float (1.0 = 100%). Values clamp to 80–150 and snap to 10s on load and on set. "Same as HUD" removes the override.
- Decks are named by a small enum: Command, Spell, Chat, Target, Gear.
- Stored per client next to the Modern HUD switch; a missing or bad file reads as defaults. A change raises an event that every mounted deck listens to.

## Drawing

- A deck has its **scale** (box sizes, from its size as today) and its **text factor**. Faces come from `DeckFonts.At(scale × factor)`; geometry uses `scale` only. Pixel heights round to whole pixels as today, and the font cache bakes each size once.
- **Rows that hold a run of text grow**: list rows, suggestion rows, popup menu rows, chat strip lines, tooltip lines. Height = max(normal row height at scale, the face's line height + the row's padding at scale). Lists and transcripts scroll further; the deck window keeps its size.
- **Fixed boxes keep their size**: vital bars, buttons, studs, chips, tabs, grid cells, side-pack cells, the target plate rows, the footer. Their text is fitted with "…" and, when the face is taller than the box allows, drops to the largest face that fits. No text draws outside its box.
- Popup menu (`UiMarkupContextMenu`) and tooltip (`HudTooltip`) take the text factor for their faces; padding and spacing follow the deck scale.
- A text factor change does what a scale change does today: drop fitted text caches and rebuild geometry. No relog.

## Per deck

- **Command Deck**: vital numbers, labels and CAST are fixed boxes (fit then cap). Jump bar and peace/combat studs unchanged.
- **Spell Deck**: icons unchanged; any labels and the tooltip follow the factor.
- **Target Deck**: plate rows fixed; name and health text fit then cap. Plate size unchanged.
- **Chat Deck**: tabs fixed (names fitted); strip preview lines grow (fewer lines show); chip menus and tooltips follow the factor; transcript and input keep their own sizes.
- **Gear Deck**: list rows and suggestion rows grow; grid captions, chips, studs, side cells, footer and salvage button fit then cap; item, sort, hidden-chip and saved-filter menus and the tooltip follow the factor.

## Controls

- Config page: a stepper row "HUD text size" (80–150, step 10, "%"), shown with the Modern HUD row.
- Each deck: a right-click on its empty frame or title opens a menu with a "Text size" submenu (or the flat list if submenus are not available in the deck menu). Right-clicks on items, chips, tabs and other controls keep their current menus.

## Testing

- Settings: defaults, override set and cleared ("Same as HUD"), clamp and snap, save/load round trip, missing and bad file.
- Fonts: face pixel height = round(base × scale × factor).
- Each deck at 80%, 100% and 150%: window size unchanged; growing rows grow; fixed boxes keep their size and no face is taller than its box.
- One right-click menu test per deck through the real root input path; a Config row test.
- Live check: global size, one deck's override, "Same as HUD", a small Command Deck beside a Gear Deck with large text.

## Out of scope

- Retail (non-Modern HUD) windows and plugin windows.
- Per-character text size.
- Changing the Chat Deck's existing transcript and input size settings.
