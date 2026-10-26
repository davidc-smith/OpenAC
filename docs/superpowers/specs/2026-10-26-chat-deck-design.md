# Chat Deck — design

Date: 2026-10-26. Status: agreed in brainstorm; revised after Codex review; awaiting spec review.

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
  (`0x2100005B` ×4), but only once the Chat Deck has mounted; if it fails to mount the
  retail chat stays (see Modern HUD switch). Modern HUD off brings them back with their
  own state unchanged. The Chat Deck model is created once per session and keeps
  collecting while hidden, so switching back and forth keeps what each window already
  has; the two windows do not hold identical history (retail keeps 500 shared lines).
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
ChatLog.EntryAppended / Cleared ──► ChatDeckModel (AcDream.Core/Chat/Deck, pure)
                            ├─ ChatRuleEngine   : ordered rules + presets → verdict
                            ├─ ChatDeckTab[]    : name, icon, channels, send channel,
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

No UI types. Receives each `ChatEntry` after `ChatLog`'s global suppression, plugin
filters and language censorship have run. Retail per-window filters (`ChatWindowState`)
do not apply: the Chat Deck has its own subscriptions.

**Lifecycle.** One model per game session, created at login and disposed at logout.
`ChatLog` gains a `Cleared` event, raised by `Clear()`.

| Event | Histories | Unread / alerts | Tabs, rules, settings |
|---|---|---|---|
| `/clear` (`ChatLog.Cleared`) | cleared | cleared | kept |
| Logout / character switch / disconnect | model disposed | gone | kept (saved) |
| Login | new model, empty | none | loaded for that character |
| Modern HUD toggled | kept (model keeps running) | kept | kept |

**Threading.** `EntryAppended` is raised synchronously on whichever thread appended.
The model only enqueues the entry there (a lock-guarded queue); it applies queued
entries on the game thread at the start of each UI tick. All other model state is
touched only on the game thread, and the UI reads it there. `Dispose` unsubscribes from
`ChatLog` and drops the queue; subscribing twice is guarded against.

**Line identity.** Each line is evaluated once into an immutable `ChatDeckLine` (the
entry, its channel key, its spans, its colours, its alert flag). Tabs hold references to
that one object, so copying a line into several tabs costs no extra text.

**Channel key.** Several channels share one `LogTextType` (Patron, Vassals, Monarch
and Covassals all use Social; Allegiance and Olthoi both use `0x12`). So tabs and rules
do not subscribe to raw chat types but to **channels**: a `ChatDeckChannel` is the
`LogTextType` plus, where needed, the channel name or id (`ChannelName`,
`LegacyChannelChatType`, the Turbine chat room). A pure `ChatDeckChannels.Classify(entry)`
maps each entry to exactly one channel; own-sent echoes map to the same channel as
received lines. The editor lists channels, grouped; tests cover every channel and its
send variant.

**Rule fields.** A rule has: enabled, kind (`Contains`, `Regex`, `MyName`,
`Preset:<id>`), match field (`Text` = the message body without sender or channel
prefix, the default; `Sender`; `Line` = the full worded line as shown), the channels it
applies to (empty = any), action (`Hide`, `MoveTo`, `CopyTo`, `ColourLine`,
`ColourMatch`), target tab id (Move/Copy), colour (Colour), alert. `Contains` is
case-insensitive (ordinal ignore-case). `MyName` matches the logged-in character's name
as a whole word in `Text` and never matches lines you sent yourself. It is the built-in
"Highlight my name" rule, on by default with a colour and alert.

**Regex safety.** Regexes compile with `RegexOptions.NonBacktracking |
CultureInvariant | IgnoreCase` and a 20 ms match timeout. A pattern that fails to
compile marks the rule invalid; a timeout marks it faulted. Either way the rule is
disabled until edited and the editor shows it red. Patterns are capped at 256
characters; matching reads at most the first 2,000 characters of a line.

**Verdict.** For each line, every enabled, valid rule whose channels and pattern match
is collected once, in rule order. Then:

1. If any matching rule is **Hide**, the line is dropped: no tab stores it, no unread,
   no alert, no peek. Hide wins wherever it sits in the list.
2. Otherwise the destinations start from subscriptions: every tab that subscribes to
   the line's channel, plus *All*.
3. The first matching **Move to** replaces the subscribed tabs with its target (plus
   *All*). A Move whose target tab no longer exists is ignored.
4. Every matching **Copy to** adds its target (missing targets ignored).
5. The destination set is de-duplicated; *All* is always in it, once.
6. The first matching **Colour line** sets the line colour; the first matching
   **Colour match** colours the matched text. Without either, the channel colour applies.
7. If any matching rule has **alert**, the line is an alert in every destination tab
   (red badge, peeks even on tabs with peek off, optional beep). Incoming tells are
   always alerts.

**Limits.** At most 16 tabs, 200 rules, tab names 24 characters, a saved `chatDeck`
object of 256 KB. History is 1,000 line references per tab.

**Presets** are rules that ship in code (`ChatDeckPresets`), about 80, grouped
Combat / Magic / Pets / Items / Spam / Notices, matching the set RynthChat offers
(damage dealt / taken, evades, resists, crits, procs, periodic ticks, fizzles, buffs
others cast on you, spell expiry, component use, pets, salvage results, kill-task
progress, trade/buff-bot spam, Master Arbitrator spam and so on). Almost all of them
match on **server wording** (regexes over ACE's line text) limited to the right channels;
`CombatKind` only says Info/Warning/Error, so it is not a classifier. Each preset in the
code says which it is. Each preset carries a version number and fixtures: sample lines
it must match and near-misses it must not, taken from ACE source formats and from real
lines captured on the local ACE server. A saved preset rule stores only its id, its
enabled state, its action and any colour or target, so an update can improve the
pattern. All presets start off.

Tabs: id (stable), name, icon (from a fixed icon set), channels (a set of
`ChatDeckChannel`), send channel, peek on/off, history ring of 1,000 line references,
unread count, alert flag. *All* is a fixed tab: it cannot be deleted, subscribes to
every channel, and receives every line that is not hidden.

Default tabs: **All** (send Say), **Local** (speech, tells, emotes; send Say; peek on),
**Social** (General, Trade, LFG, Roleplay, Society, Olthoi; send General), **Group**
(Fellowship, Allegiance, Patron, Vassals, Covassals, Monarch; send Fellowship; peek on),
**Combat** (combat and magic types; send Say), **System** (system, notices, errors;
send Say).

Read state, decided when a line arrives: a line added to the tab that is selected
and visible (pinned, or flown out) while the view is at the bottom is read at once.
Otherwise it raises that tab's unread count, and its alert flag if the line is an alert.
Selecting a tab, or scrolling a scrolled-up view back to the bottom, clears that tab's
unread count and alert flag. A line in several tabs is unread in each until seen there.
The gold badge is the unread count; the red badge shows while the alert flag is set.
*Mark all read* clears every tab.

Send channel: with no `/` prefix, typed text is sent as the tab's channel through
`ChatCommandRouter`, exactly as if the matching channel command were typed (Say sends
plain). The channel chip on the input can override it for the current line only. If the
channel is not available (no fellowship, not in an allegiance) the router's own error
line shows; there is no silent fallback. `/r`, `/rt` and every `/command` go to the
router unchanged.

**Chat entry ownership.** Today every chat-entry action (`EnterChatMode`,
`ToggleChatEntry`, `FocusChatEntry`, `ChatStartCommand`, reply, tell-to-selected) calls
the retail chat controller directly in `RetailUiRuntime`, and `FocusChatEntry` focuses
`DefaultTextInput`. These go through one `IChatEntryTarget` instead: the retail chat
window by default, the Chat Deck's input while the Chat Deck is shown. Both front ends
share the existing `RuntimeChatEntryOwner` (draft, send target and recall history), so
Up/Down recall the same 100 lines in either window and a half-typed line survives a
Modern HUD switch.

### Settings and persistence

Per character, in the settings file under `character[toonKey].chatDeck`.
`SaveCharacter` today rebuilds the whole character object, so `SettingsStore` gains
`LoadChatDeck(toonKey)` / `SaveChatDeck(toonKey, json)` that read and replace only that
key, and `SaveCharacter` is changed to keep sibling keys it does not own (including
`chatDeck`) instead of dropping them. Both write through the store's existing
write path. The `chatDeck` object carries a `version` (starting at 1) and holds tabs
(without history), rules, enabled presets and appearance. Unknown fields and unknown
preset ids are kept and ignored, so an older or newer build does not lose them. A
malformed or over-size object loads defaults and logs once; the bad object is left
unchanged on disk until the next save.

Window geometry (the expanded window's position and size) goes in the existing
window layout (`UiWindowLayout`, per character and resolution, as the Command Deck's
does). `UiWindowLayout` has no room for the rest, so `chatDeck` also holds a `layout`
part: pinned or collapsed, edge (left/right), last selected tab id. Defaults: pinned,
left edge, *All*.

A deleted tab's Move/Copy rules are kept, disabled, and labelled "(tab deleted)".

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
  coordinate links and tag links work as in retail chat. The wording, tag parsing and
  coordinate linking that `RuntimeChatFeed.BuildLine` does today are pulled out into a
  timestamp-free projector (`ChatLineProjector`) that returns the line's text spans and
  link metadata; `RuntimeChatFeed` uses it unchanged in behaviour (still adding the
  retail timestamp), and the Chat Deck adds its own timestamp, prefix and colours; highlighted lines get a coloured left bar. Scrolling up stops the view
  following new lines and shows a "▼ N new" marker; clicking it or scrolling to the
  bottom resumes. Drag-select copies several lines; right-click a line: Copy line, Move
  these to ▸, Also show in ▸, Hide these, Colour these…, each opening the editor's Rules
  page with a new rule filled in from the line: its channel, and a `Contains` pattern of
  the message text. The editor shows a preview of the pattern with any part you choose
  to generalise (sender, numbers) marked, and a count plus sample of the lines in the
  current session's history it would match. The rule is only added when you press Add.
- **Search:** ⌕ opens a field over the transcript top; it narrows the current tab to
  lines containing the text (case-insensitive); Esc or clearing it restores the view.
- **Input:** the send channel chip (e.g. **Fellowship ▾**) then the text field.
  Up/Down recall sent lines (the shared 100-line history); Ctrl+Tab / Ctrl+Shift+Tab cycle tabs; Enter
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
  send channel, peek checkbox, channels as grouped checkboxes; ▲ ▼ reorder, Delete
  tab (not for All), Reset to defaults (asks first).
- **Rules:** ordered table (enabled, when, from, do, ⋮ menu: edit, duplicate, delete);
  *+ Rule*, *+ Standard filters…* (grouped preset checklist), ▲ ▼; a **Test a line**
  box: pick a channel, optionally a sender, and paste the message text; it shows every
  rule that matches, the verdict (destinations, colour, alert, or hidden) and which rule
  decided each part. A line can also be sent to the test box from the transcript's
  right-click menu, which fills in its real channel and sender. Invalid or faulted rules
  show red.

## Modern HUD switch

Today the switch treats Modern HUD as all-or-nothing: one deck, one list of retail
windows (`RetailWindows`), all hidden once the deck is mounted. It is generalised to a
list of **replacements**, each pairing a Modern HUD element with the retail windows it
replaces and its own mounted state:

- Command Deck → vitals, side vitals, combat, jump power bar (unchanged).
- Chat Deck → main chat (`WindowNames.Chat`) and the four floaties.

A replacement's retail windows are hidden only while that element is mounted and shown.
If the Chat Deck fails to mount, the retail chat and floaties stay, the failure is logged
once, and the Command Deck is unaffected (and the other way round). The chat-entry
target follows the same state: it points at the Chat Deck only while the Chat Deck is
mounted and shown. Hiding is not the player's choice: the retail windows' saved
visibility is unchanged.

## Testing

- **Core unit tests:**
  - Verdict: every action; Hide after Move or Copy still hides; Copy to the Move
    target and Move to *All* give one destination each; missing targets ignored;
    alert with Hide gives no alert; colour precedence; My-name ignores own lines.
  - Channels: `Classify` for every channel and its send echo; Allegiance vs Olthoi and
    Patron/Vassals/Monarch/Covassals land in the right default tab.
  - Regex: invalid pattern, a pathological pattern times out and faults the rule,
    length caps.
  - Presets: each matches its fixtures and misses its near-misses.
  - History cap per tab; shared line references; limits on tabs, rules, names.
  - Read state: selected and visible at bottom is read; scrolled up is unread;
    switching tabs and scrolling down clear; alert vs unread badges.
  - Lifecycle: `ChatLog.Cleared` empties histories and unread; dispose unsubscribes;
    no double subscription; entries appended from another thread are applied on the
    next tick.
  - Send channel choice and chip override.
  - Settings: round trip; `SaveCharacter` keeps `chatDeck` and `SaveChatDeck` keeps
    the other character keys; deleted-tab rules; unknown fields and preset ids kept;
    malformed or over-size object loads defaults.
  - `ChatLineProjector`: `RuntimeChatFeed` output unchanged for existing tests.
- **App headless tests (like the deck's):** Modern HUD on hides retail chat and
  floaties, off restores them; Chat Deck mount failure keeps retail chat and leaves the
  Command Deck running; every chat-entry action reaches the Chat Deck input while it is
  shown and the retail input otherwise, sharing the draft and recall history; fly-out
  opens and closes for each trigger; Esc returns keys to the game; collapse, pin, edge
  and last tab survive a relog; logout then a different character starts empty with that
  character's tabs; resize limits; rule-from-line opens the editor with a preview.
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
