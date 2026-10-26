# Chat Deck — design

Date: 2026-10-26. Status: agreed in brainstorm, awaiting spec review.

## Goal

A Modern HUD replacement for the retail chat window and its four floaties: one
game-style window with your own tabs, text rules and standard filters, a per-tab send
channel, and a collapse-to-the-edge strip that flies out when needed. Client-only and
fork-only, like the Command Deck.

Mockups: `.superpowers/brainstorm/11278-1793048092/content/` (c1 window layout A,
c2 collapsed strip A, c3 Filters & Tabs editor).

## Decisions

- **New window, not the retail one.** The Chat Deck shows only while Modern HUD is on
  (`MiscSettings.ModernHud`). It hides the retail chat (`0x2100006F`) and floaties
  (`0x2100005B` ×4). Modern HUD off brings them back with their own state unchanged.
  Both read the same `ChatLog`, so switching loses no lines.
- **Hybrid build.** The chat itself (tab bar, transcript, input, strip, peek, fly-out)
  is a native `UiChatDeck` element in `src/AcDream.App/UI/Hud`, drawn like
  `UiCommandDeck` with the bundled sans fonts (`DeckFonts`; never the monospace debug
  font). The Filters & Tabs editor and the Settings popup are markup dialogs built from
  the plugin markup controls (tabs, list, checkbox, number, color, contextmenu,
  dialogs), using the Modern theme.
- **RynthChat is independent.** It stays a plugin with its own on/off switch. No
  special case either way; their source stays unchanged.
- **Scrollback per tab**, 1,000 lines each, so Combat spam cannot push tells out. It
  clears on logout. No history on disk (`/log` already writes transcripts).
- **Retail chat is untouched.** No changes to `ChatWindowController`,
  `FloatingChatWindowController` or `ChatWindowState` filters.
- **No plugin contract change**; `ForkContractVersion` stays as it is.

## Architecture

```
ChatLog.EntryAppended ──► ChatDeckModel (AcDream.Core/Chat/Deck, pure)
                            ├─ ChatRuleEngine   : ordered rules + presets → verdict
                            ├─ ChatDeckTab[]    : name, icon, chat types, send channel,
                            │                     peek, history (1,000), unread, mention
                            └─ ChatDeckSettings : appearance + tabs + rules (per character)
                                     ▼
                         UiChatDeck (AcDream.App/UI/Hud, native)
                            ├─ tab bar · transcript · input · icon studs
                            ├─ edge strip · peek · fly-out
                            └─ opens ► markup dialogs: Filters & Tabs, Settings
                                     ▼
                         send ► existing ChatCommandRouter
```

### ChatDeckModel (Core)

No UI types. Receives each `ChatEntry` after `ChatLog`'s own suppression, language
and plugin filters have run, so nothing the retail chat hides shows here. It matches on
`LogTextType`, `CombatKind`, `ChannelName`, the sender, and the text.

A line's verdict, evaluated against the enabled rules top to bottom:

1. A **Hide** rule that matches drops the line everywhere: no tab stores it and it
   counts towards no unread. Evaluation stops.
2. **Move to** *tab*: the line goes to that tab and to *All*, and not to tabs that only
   subscribe to its chat type. The first matching Move wins.
3. **Copy to** *tab*: the line also goes to that tab. Several can match.
4. **Colour line** / **Colour match** set the line's colour or the matched text's
   colour. The first matching colour of each kind wins.
5. Otherwise a tab shows the line if it subscribes to the line's chat type.

Each rule has an optional **alert** flag: a match marks the line as a mention (red
badge, peeks even on tabs with peek off, optional beep). "Highlight my name" is a
built-in rule whose pattern is the logged-in character's name; it is on by default
with a colour and alert.

A rule has: enabled, kind (`Contains` text, `Regex`, `MyName`, `Preset:<id>`), the
chat types it applies to (empty = any), action (`Hide`, `MoveTo`, `CopyTo`,
`ColourLine`, `ColourMatch`), target tab id (for Move/Copy), colour (for Colour), alert.
`Contains` is case-insensitive. An invalid regex marks the rule invalid and disables it;
the editor shows it in red.

**Presets** are rules that ship in code (`ChatDeckPresets`), about 80, grouped
Combat / Magic / Pets / Items / Spam / Notices, matching the set RynthChat offers
(damage dealt / taken, evades, resists, crits, procs, periodic ticks, fizzles, buffs
others cast on you, spell expiry, component use, pets, salvage results, kill-task
progress, trade/buff-bot spam, Master Arbitrator spam and so on). Patterns are written
against ACE's line formats; `CombatKind` is used where `CombatChatTranslator` already
classifies a line. A saved preset rule stores only its id, its enabled state, its action
and any colour or target, so an update can improve the pattern. All presets start off.

Tabs: id (stable), name, icon (from a fixed icon set), chat types (a set of
`LogTextType` values, grouped for the editor), send channel, peek on/off, history ring
of 1,000 entries, unread count, mention flag. *All* is a fixed tab: it cannot be deleted,
subscribes to every type, and receives every line that is not hidden.

Default tabs: **All** (send Say), **Local** (speech, tells, emotes; send Say; peek on),
**Social** (General, Trade, LFG, Roleplay, Society, Olthoi; send General), **Group**
(Fellowship, Allegiance, Patron, Vassals, Covassals, Monarch; send Fellowship; peek on),
**Combat** (combat and magic types; send Say), **System** (system, notices, errors;
send Say).

Read state: viewing a tab clears its unread count and mention flag. A line shown in
several tabs is unread in each until viewed there. *Mark all read* clears every tab.

Send channel: with no `/` prefix, typed text is sent as the tab's channel through
`ChatCommandRouter`, exactly as if the matching channel command were typed (Say sends
plain). The channel chip on the input can override it for the current line only. If the
channel is not available (no fellowship, not in an allegiance) the router's own error
line shows; there is no silent fallback. `/r`, `/rt` and every `/command` go to the
router unchanged.

### Settings and persistence

Per character, through the existing `SettingsStore` character section (`LoadCharacter` / `SaveCharacter`, a new `chatDeck` object): tabs (without history), rules,
enabled presets, appearance. Window position, size, pinned/collapsed and edge go in the
existing per-install layout store, as the Command Deck's do. A deleted tab's Move/Copy
rules are kept, disabled, and labelled "(tab deleted)". Unknown preset ids from a newer
or older build are kept and ignored. Logout clears histories and unread counts only.

Appearance (Settings popup, from the ⚙ stud):

- Font size 10–20 px (bundled sans), input font size 10–20 px.
- Line spacing (1.0–1.6).
- Wrap indent for continuation lines (on/off).
- Background opacity (text stays opaque).
- Timestamps: off, `HH:MM`, `HH:MM:SS`.
- Channel prefix (`[General]`, `[Fellowship]`) shown or hidden.
- Channel colours: retail colours by default, a colour picker per chat type to
  override, *reset all colours*.
- Fade when idle (pinned only): background fades after N seconds without activity.
- Beep on alert (on/off).

## Window

### Pinned layout (mockup c1, option A)

- **Top row:** tabs left to right (`+` adds one; right-click a tab: Rename, Edit tab…,
  Mark as read, Mark all read, Delete). Unread badge: gold count; red for a mention or
  tell. Icon studs on the right: search ⌕, Filters & Tabs ⧩, Settings ⚙, pin, collapse ⇤.
- **Transcript:** coloured, wrapped lines with optional timestamp and channel prefix;
  coordinate links and tag links work as in retail chat (reuse `ChatCoordinateLinks` and
  the tag markup); highlighted lines get a coloured left bar. Scrolling up stops the view
  following new lines and shows a "▼ N new" marker; clicking it or scrolling to the
  bottom resumes. Drag-select copies several lines; right-click a line: Copy line, Move
  these to ▸, Also show in ▸, Hide these, Colour these…, each opening the editor's Rules
  page with a rule filled in from the line (its chat type plus its text with the sender
  and numbers generalised).
- **Search:** ⌕ opens a field over the transcript top; it narrows the current tab to
  lines containing the text (case-insensitive); Esc or clearing it restores the view.
- **Input:** the send channel chip (e.g. **Fellowship ▾**) then the text field.
  Up/Down recall the last 50 sent lines; Ctrl+Tab / Ctrl+Shift+Tab cycle tabs; Enter
  with empty text closes the input as in retail; Esc leaves it. While it has focus the
  game receives no keys (the existing text-field rule).

### Size and placement

Drag the frame to move. Resize from edges and corners freely, minimum about 280×140,
clamped on screen. Collapse goes to whichever of the left or right screen edge the
window's centre is nearer.

### Collapsed strip, peek and fly-out (mockup c2, option A)

- **Strip:** about 34 px wide on that edge at the window's height, one icon stud per
  tab with the same badges, tab name as tooltip, ⇥ at the bottom pins the window.
- **Peek:** for tabs with peek on (and any alert line), the newest lines show beside
  the strip, at most 3, each fading after about 6 s. Peek lines are not clickable.
- **Fly-out opens** on: hover over the strip for about 300 ms; Enter or any action that
  focuses chat entry (`EnterChatMode`, `ToggleChatEntry`, `FocusChatEntry`,
  `ChatStartCommand`, reply, tell-to-selected), respecting key bindings; a click on a
  tab stud (opens on that tab). It slides out in about 120 ms on the last used tab
  unless a stud chose one.
- **Fly-out closes:** about 600 ms after the mouse leaves, only when the input is empty
  and unfocused and the view is not scrolled up or selecting; straight away after send
  or Esc when Enter opened it. Pinning stops it closing.

## Filters & Tabs editor (mockup c3)

A markup dialog with two pages.

- **Tabs:** tab list (with icons, *+ New tab*); for the selected tab: name, icon picker,
  send channel, peek checkbox, chat types as grouped checkboxes; ▲ ▼ reorder, Delete
  tab (not for All), Reset to defaults (asks first).
- **Rules:** ordered table (enabled, when, from, do, ⋮ menu: edit, duplicate, delete);
  *+ Rule*, *+ Standard filters…* (grouped preset checklist), ▲ ▼; a **Test a line**
  box showing which rule matches a pasted line and what it does. Invalid rules show
  red.

## Modern HUD switch

`ModernHudSwitch.RetailWindows` gains the main chat (`WindowNames.Chat`) and the four
floaty windows, so the existing gate hides and restores them. The switch today shows and
hides one deck; it is generalised to a list of Modern HUD elements so the Chat Deck is
shown while Modern HUD and gameplay are active, as the Command Deck is, and a Chat Deck
mount failure is reported on its own without affecting the Command Deck. Hiding is not the player's
choice: the retail windows' saved visibility is unchanged.

## Testing

- **Core unit tests:** rule order and every action; Move vs Copy vs subscription;
  Hide drops everywhere; colour precedence; alert and My-name; invalid regex; each
  preset matches its sample lines and misses near-misses (samples taken from ACE line
  formats); history cap per tab; unread and mention counting and clearing; send-channel
  choice and override; settings round trip, deleted-tab rules, unknown preset ids.
- **App headless tests (like the deck's):** Modern HUD on hides retail chat and
  floaties, off restores them; fly-out opens and closes for each trigger; Enter focuses
  input and Esc returns keys to the game; collapse, pin and edge survive the layout store
  and a relog; resize limits; rule-from-line opens the editor prefilled.
- **Live check on local ACE** (10.10.20.20, only after the user says they are
  available): tabs fill from real traffic (fellowship, General, tells, combat); sending
  from each tab; strip, peek and fly-out feel right; font size and opacity; a rule and a
  preset survive a restart.
- All suites through `~/code/OpenAC/test-baseline/run-mac-tests.sh`.

## Delivery

Five plans, one per series, each on its own feature branch and worktree, merged into
main and rynth after its checks pass:

1. Model and rules engine with presets (Core only).
2. Window: tabs, transcript, input, per-tab send, Modern HUD switch.
3. Collapsed strip, peek, fly-out, pin.
4. Appearance settings popup.
5. Filters & Tabs editor and rule-from-line.

## Out of scope

History on disk across relogs, rule import/export, log to file, side-drawer tab layout,
any change to retail chat, RynthChat interplay, upstream PR.
