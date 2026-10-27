# Chat Deck Strip and Fly-out Implementation Plan (plan 3 of 5)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The Chat Deck can collapse to a 34 px strip on the nearer screen edge (one icon stud per tab with badges, ⇥ at the bottom), shows the newest peek lines beside the strip, flies the window out on hover, on any chat-entry action or on a stud click, closes it again by the spec's rules, and pins it back; pinned or collapsed, edge and last tab survive a relog.

**Architecture:** A pure `ChatDeckDrawer` state machine (Pinned / Collapsed / Open, with hover, close and slide timers) and a pure `ChatDeckPeekFeed` (newest 3 peek lines, 6 s life) sit in `src/AcDream.App/UI/Hud`. `UiChatDeck` owns both and two new elements: the strip (a plain root child, not a window) and the peek (a click-through child of an overlay layer). The deck window itself stays the one registered window: while collapsed its element is hidden (not through the window manager, so the Modern HUD switch, the chat-entry target and the visibility hold are untouched), while flown out it sits beside the strip, and its layout capture gate is closed whenever it is not pinned, so the saved window geometry stays the pinned one. The runtime ticks the drawer every frame (a hidden element gets no `OnTick`).

**Tech Stack:** C# / .NET 10, xUnit, the retained UI in `src/AcDream.App/UI` (no new libraries).

**Spec:** `docs/superpowers/specs/2026-10-26-chat-deck-design.md` (branch `docs/chat-deck-spec`). Read "Window → Pinned layout" (the stud row), "Size and placement", "Collapsed strip, peek and fly-out", the *Read state* paragraph under "ChatDeckModel (Core)", "Settings and persistence" (the `layout` part) and "Testing" before starting. Plans 1 and 2 (`2026-10-27-chat-deck-model.md`, `2026-10-27-chat-deck-window.md`) built the model and the pinned window; read `src/AcDream.App/UI/Hud/UiChatDeck.cs`, `UiChatDeck.Draw.cs`, `UiChatDeck.Input.cs`, `ChatDeckGeometry.cs` and `src/AcDream.Core/Chat/Deck/ChatDeckModel.cs`, `ChatDeckSettings.cs` first.

**Worktree:** implement in `~/code/OpenAC/OpenAC-chat-deck-strip` on branch `feat/chat-deck-strip`, created from `main` (plan 2 is merged there, main `ff48c31a` or later).

**Running tests:** always through the Mac runner, narrowed with `MAC_TEST_FILTER` (never a second `--filter`):

```bash
MAC_TEST_FILTER='FullyQualifiedName~ChatDeck' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-strip tests/AcDream.App.Tests/AcDream.App.Tests.csproj
```

Test projects used: `tests/AcDream.Core.Tests` and `tests/AcDream.App.Tests` (each `<name>/<name>.csproj`). `TreatWarningsAsErrors` is on.

## Global Constraints

- Chat Deck UI code lives in `src/AcDream.App/UI/Hud/` (namespace `AcDream.App.UI.Hud`), tests in `tests/AcDream.App.Tests/UI/Hud/` (namespace `AcDream.App.Tests.UI.Hud`). Core additions stay in `src/AcDream.Core/Chat/Deck/` with no UI references.
- Timings (spec): hover to open **0.3 s**; slide out **0.12 s**; close **0.6 s** after the pointer leaves; peek lines live **6 s** and fade over their last **1 s**; at most **3** peek lines.
- Sizes: strip **34 px** wide on the edge, at the window's top and height (taller when the studs need it, never taller than the screen); studs **24×22**, **5 px** apart, from **10 px** below the top; the ⇥ stud **8 px** above the bottom. Peek block at most **420 px** wide, **8 px** from the strip, aligned to the strip's bottom.
- The fly-out never closes while: the pointer is over the strip or the window, the input or search field has focus, the input holds text, the transcript is scrolled up, a deck menu is open, the transcript is being drag-selected, or a press inside the deck holds the pointer. A fly-out a chat-entry action opened closes at once after a send or Esc. Pinning stops it closing.
- Collapse goes to whichever screen edge the window's centre is nearer. Pinning (the fly-out's pin stud or the strip's ⇥) leaves the window where it flew out, beside that edge, and the layout records that place.
- The window's saved geometry (`UiWindowLayout`) changes only while pinned (capture gate). Pinned and edge are saved in `chatDeck.layout` through `ChatDeckModel.SetLayout`.
- Collapsing never goes through `RetailWindowHandle.Hide` / `UiRoot.HideWindow`: the deck sets its own element's `Visible`. `ModernHudSwitch`, `ChatEntryTarget` and the retail visibility hold are not changed.
- Read state (spec): a collapsed deck is not visible, so lines arriving count as unread; a fly-out is visible.
- Peek: a line peeks when it is an alert, or any tab it went to has peek on. Lines you sent yourself never peek (they would echo straight back after a send from the fly-out). Peek lines are not clickable. Peek shows only while collapsed with the fly-out closed; opening the fly-out clears it.
- Icons are bundled SVGs in `assets/hud/` (white on transparent, `viewBox="0 0 24 24"`, only shapes `PluginSvgIcon` accepts, no `fill-rule="evenodd"`; every stroked path carries `fill="none"`).
- Studs on the pinned top row, right to left: collapse ⇤, pin, search ⌕. Plans 4 and 5 add Settings ⚙ and Filters ⧩ to the left of search.
- Pin stud: lit while pinned; pressing it while pinned collapses (same as ⇤); pressing it on a fly-out pins. Collapse stud on a fly-out closes the fly-out.
- No plugin contract change; `ForkContractVersion` stays as it is. Retail chat untouched.
- Commit messages are short, in the existing style (`Chat Deck: ...`), and end with the line `Claude-Session: https://claude.ai/code/session_017hCEwjbupaVz5o7Zx5rwfZ`.
- Never launch the game client for a live check until the user says they are available; live checks use the local ACE server only (10.10.20.20 via `~/OpenAC-dev/dev-client.env`), never Dreamweave.

**User decisions (already made):**
- "A · Icon studs (recommended)" for the strip (mockup c2): each tab picks an icon, the name is the tooltip, about 34 px wide, gold/red badges, ⇥ at the bottom "flies it out and pins it".
- Collapse to edge strip + peek + fly-out (hover/Enter/stud) + pin, as written in the approved spec.
- Five plans, one per series; plan 3 is the strip and fly-out ("Plan 3: collapsing the deck to an edge strip, with peek, fly-out (on hover, Enter or a strip icon) and pin. - Let's write this one next.").

---

## File map

| File | Responsibility |
|---|---|
| `src/AcDream.Core/Chat/Deck/ChatDeckModel.cs` (modify) | `SetLayout(pinned, edge)` saves the layout part |
| `assets/hud/chat-*.svg`, `pin.svg`, `collapse.svg`, `expand.svg` (new) | tab icons and the three drawer icons |
| `src/AcDream.App/UI/Hud/EmbeddedSvgSpriteCache.cs` (modify) | `HudIcons` names for the new icons |
| `src/AcDream.App/UI/Hud/ChatDeckIcons.cs` (new) | `ChatDeckIcon` → icon name |
| `src/AcDream.App/UI/Hud/ChatDeckDrawer.cs` (new) | pinned / collapsed / open state, timers, close rules |
| `src/AcDream.App/UI/Hud/ChatDeckPeekFeed.cs` (new) | which lines peek, newest 3, ageing and fade |
| `src/AcDream.App/UI/Hud/ChatDeckGeometry.cs` (modify) | pin and collapse studs on the top row; new zones |
| `src/AcDream.App/UI/Hud/ChatDeckStripGeometry.cs` (new) | strip studs, hit test, strip and fly-out placement |
| `src/AcDream.App/UI/Hud/UiChatDeck.Strip.cs` (new) | strip and peek elements and their drawing |
| `src/AcDream.App/UI/Hud/UiChatDeck.Drawer.cs` (new) | drawer wiring: studs, placement, ticking, entry reveal, peek feed |
| `src/AcDream.App/UI/Hud/UiChatDeck.cs`, `UiChatDeck.Draw.cs`, `UiChatDeck.Input.cs` (modify) | hooks into the above |
| `src/AcDream.App/UI/UiText.cs` (modify) | `IsSelecting` |
| `src/AcDream.App/UI/RetailUiRuntime.cs` (modify) | mount strip and peek, tick the drawer, capture gate |
| `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckModelTests.cs`, `ChatDeckSettingsJsonTests.cs` (modify) | layout setter and round trip |
| `tests/AcDream.App.Tests/UI/Hud/ChatDeckIconsTests.cs`, `ChatDeckDrawerTests.cs`, `ChatDeckPeekFeedTests.cs`, `ChatDeckStripGeometryTests.cs`, `UiChatDeckStripTests.cs`, `UiChatDeckDrawerTests.cs`, `UiChatDeckFlyoutTests.cs` (new) | |
| `tests/AcDream.App.Tests/UI/Hud/ChatDeckGeometryTests.cs`, `ChatDeckWiringTests.cs` (modify) | |

---

### Task 1: Model saves pinned and edge

**Goal:** `ChatDeckModel.SetLayout(pinned, edge)` updates `Settings.Layout` and raises `SettingsChanged` once per real change; the layout round-trips through the JSON.

**Files:**
- Modify: `src/AcDream.Core/Chat/Deck/ChatDeckModel.cs` (after `SelectTab`)
- Test: `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckModelTests.cs`, `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckSettingsJsonTests.cs`

**Acceptance Criteria:**
- [ ] `SetLayout(false, Right)` sets `Settings.Layout.Pinned == false`, `Edge == Right`, raises `SettingsChanged` once; repeating it raises nothing.
- [ ] `SetLayout` keeps `LastTabId`.
- [ ] Serialize then Parse keeps `Pinned`, `Edge` and `LastTabId`.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ChatDeckModelTests|FullyQualifiedName~ChatDeckSettingsJsonTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-strip tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** (add to `ChatDeckModelTests`)

```csharp
[Fact]
public void SetLayout_SavesPinnedAndEdge_OncePerChange()
{
    var model = new ChatDeckModel(new ChatLog(), ChatDeckDefaults.Create(), "Davian");
    var saved = new List<ChatDeckSettings>();
    model.SettingsChanged += saved.Add;

    model.SetLayout(pinned: false, ChatDeckEdge.Right);
    model.SetLayout(pinned: false, ChatDeckEdge.Right);

    Assert.Single(saved);
    Assert.False(model.Settings.Layout.Pinned);
    Assert.Equal(ChatDeckEdge.Right, model.Settings.Layout.Edge);
}

[Fact]
public void SetLayout_KeepsTheLastTab()
{
    var model = new ChatDeckModel(new ChatLog(), ChatDeckDefaults.Create(), "Davian");
    model.SelectTab("combat");
    model.SetLayout(pinned: false, ChatDeckEdge.Left);
    Assert.Equal("combat", model.Settings.Layout.LastTabId);
}
```

and to `ChatDeckSettingsJsonTests`:

```csharp
[Fact]
public void Layout_RoundTrips()
{
    ChatDeckSettings settings = ChatDeckDefaults.Create() with
    {
        Layout = new ChatDeckLayoutState { Pinned = false, Edge = ChatDeckEdge.Right, LastTabId = "group" },
    };
    ChatDeckSettings back = ChatDeckSettingsJson.Parse(ChatDeckSettingsJson.Serialize(settings, null)).Settings;
    Assert.Equal(settings.Layout, back.Layout);
}
```

- [ ] **Step 2: Run, expect the model tests to fail** (`SetLayout` missing; compile error). The JSON test may already pass (plan 1 wrote the layout part); keep it as the guard.

- [ ] **Step 3: Implement** in `ChatDeckModel`, after `SelectTab`:

```csharp
/// <summary>Pinned or collapsed, and the edge the strip sits on; saved with the settings.</summary>
public void SetLayout(bool pinned, ChatDeckEdge edge)
{
    if (Settings.Layout.Pinned == pinned && Settings.Layout.Edge == edge) return;
    Settings = Settings with { Layout = Settings.Layout with { Pinned = pinned, Edge = edge } };
    SettingsChanged?.Invoke(Settings);
}
```

- [ ] **Step 4: Run the Verify command, expect PASS.**

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Core/Chat/Deck/ChatDeckModel.cs tests/AcDream.Core.Tests/Chat/Deck/ChatDeckModelTests.cs tests/AcDream.Core.Tests/Chat/Deck/ChatDeckSettingsJsonTests.cs
git commit -m "Chat Deck: model saves pinned and edge

Claude-Session: https://claude.ai/code/session_017hCEwjbupaVz5o7Zx5rwfZ"
```

---

### Task 2: Tab and drawer icons

**Goal:** Fifteen bundled SVG icons (one per `ChatDeckIcon`, plus pin, collapse ⇤ and expand ⇥), named in `HudIcons`, and `ChatDeckIcons.Name(icon)` mapping every `ChatDeckIcon` to one.

**Files:**
- Create: `assets/hud/chat-all.svg`, `chat-local.svg`, `chat-group.svg`, `chat-social.svg`, `chat-combat.svg`, `chat-system.svg`, `chat-loot.svg`, `chat-star.svg`, `chat-tell.svg`, `chat-shield.svg`, `chat-scroll.svg`, `chat-coin.svg`, `pin.svg`, `collapse.svg`, `expand.svg`
- Modify: `src/AcDream.App/UI/Hud/EmbeddedSvgSpriteCache.cs` (`HudIcons`)
- Create: `src/AcDream.App/UI/Hud/ChatDeckIcons.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/ChatDeckIconsTests.cs` (the existing `EmbeddedSvgSpriteCacheTests.EveryHudIconIsEmbeddedAndParses` covers embedding and parsing)

**Acceptance Criteria:**
- [ ] Every name in `HudIcons.All` (old and new) is embedded and parses (`EveryHudIconIsEmbeddedAndParses` green).
- [ ] `ChatDeckIcons.Name` returns a distinct name from `HudIcons.All` for every `ChatDeckIcon` value.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ChatDeckIconsTests|FullyQualifiedName~EmbeddedSvgSpriteCacheTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-strip tests/AcDream.App.Tests/AcDream.App.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing test** `ChatDeckIconsTests.cs`

```csharp
using AcDream.App.UI.Hud;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckIconsTests
{
    [Fact]
    public void EveryTabIcon_HasItsOwnBundledIcon()
    {
        var names = Enum.GetValues<ChatDeckIcon>().Select(ChatDeckIcons.Name).ToList();
        Assert.All(names, n => Assert.Contains(n, HudIcons.All));
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void DrawerIcons_AreListed()
    {
        Assert.Contains(HudIcons.Pin, HudIcons.All);
        Assert.Contains(HudIcons.Collapse, HudIcons.All);
        Assert.Contains(HudIcons.Expand, HudIcons.All);
    }
}
```

- [ ] **Step 2: Run, expect a compile failure** (`ChatDeckIcons`, `HudIcons.Pin` missing).

- [ ] **Step 3: Add the SVGs.** Each file is one line, exactly:

`assets/hud/chat-all.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M4 6 L20 6 M4 12 L20 12 M4 18 L20 18" fill="none" stroke="#fff" stroke-width="2.5" stroke-linecap="round"/></svg>
```
`assets/hud/chat-local.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M3 4 L21 4 L21 16 L11 16 L6 21 L6 16 L3 16 Z" fill="#fff"/></svg>
```
`assets/hud/chat-group.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><circle cx="8" cy="8" r="3.5" fill="#fff"/><circle cx="16.5" cy="8" r="3.5" fill="#fff"/><ellipse cx="8" cy="18" rx="6" ry="3.5" fill="#fff"/><ellipse cx="16.5" cy="18" rx="6" ry="3.5" fill="#fff"/></svg>
```
`assets/hud/chat-social.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><circle cx="12" cy="12" r="9" fill="none" stroke="#fff" stroke-width="2"/><ellipse cx="12" cy="12" rx="4" ry="9" fill="none" stroke="#fff" stroke-width="2"/><path d="M3 12 L21 12" fill="none" stroke="#fff" stroke-width="2"/></svg>
```
`assets/hud/chat-combat.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M5 3 L19 19 M19 3 L5 19 M14 18 L18 14 M6 14 L10 18" fill="none" stroke="#fff" stroke-width="2.5" stroke-linecap="round"/></svg>
```
`assets/hud/chat-system.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><circle cx="12" cy="12" r="9" fill="none" stroke="#fff" stroke-width="2"/><rect x="10.75" y="10" width="2.5" height="8" fill="#fff"/><circle cx="12" cy="7" r="1.6" fill="#fff"/></svg>
```
`assets/hud/chat-loot.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M9 3 L15 3 L13.5 7 L10.5 7 Z" fill="#fff"/><path d="M10 8 L14 8 L20 15 L18 21 L6 21 L4 15 Z" fill="#fff"/></svg>
```
`assets/hud/chat-star.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><polygon points="12,2.5 14.9,8.6 21.5,9.3 16.5,13.8 17.9,20.4 12,17 6.1,20.4 7.5,13.8 2.5,9.3 9.1,8.6" fill="#fff"/></svg>
```
`assets/hud/chat-tell.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><rect x="3" y="5.5" width="18" height="13" fill="none" stroke="#fff" stroke-width="2"/><polyline points="3.5,6.5 12,13 20.5,6.5" fill="none" stroke="#fff" stroke-width="2" stroke-linejoin="round"/></svg>
```
`assets/hud/chat-shield.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M12 2.5 L20 5.5 L20 11 L12 21.5 L4 11 L4 5.5 Z" fill="#fff"/></svg>
```
`assets/hud/chat-scroll.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><rect x="5" y="3" width="14" height="18" fill="none" stroke="#fff" stroke-width="2"/><path d="M8.5 8 L15.5 8 M8.5 12 L15.5 12 M8.5 16 L13 16" fill="none" stroke="#fff" stroke-width="2" stroke-linecap="round"/></svg>
```
`assets/hud/chat-coin.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><circle cx="12" cy="12" r="8.5" fill="none" stroke="#fff" stroke-width="2.5"/><circle cx="12" cy="12" r="4" fill="#fff"/></svg>
```
`assets/hud/pin.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M8.5 3 L15.5 3 L14.5 9.5 L18 13.5 L6 13.5 L9.5 9.5 Z" fill="#fff"/><path d="M12 13.5 L12 21" fill="none" stroke="#fff" stroke-width="2.2" stroke-linecap="round"/></svg>
```
`assets/hud/collapse.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M5 4 L5 20 M19 12 L9 12 M13 7.5 L8.5 12 L13 16.5" fill="none" stroke="#fff" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"/></svg>
```
`assets/hud/expand.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M19 4 L19 20 M5 12 L15 12 M11 7.5 L15.5 12 L11 16.5" fill="none" stroke="#fff" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"/></svg>
```

(`assets/hud/*.svg` is already embedded by `AcDream.App.csproj`; nothing to add there.)

- [ ] **Step 4: Name them in `HudIcons`** (`EmbeddedSvgSpriteCache.cs`): add the constants after `ChevronDown` and append them to `All`.

```csharp
    public const string ChatAll = "chat-all";
    public const string ChatLocal = "chat-local";
    public const string ChatGroup = "chat-group";
    public const string ChatSocial = "chat-social";
    public const string ChatCombat = "chat-combat";
    public const string ChatSystem = "chat-system";
    public const string ChatLoot = "chat-loot";
    public const string ChatStar = "chat-star";
    public const string ChatTell = "chat-tell";
    public const string ChatShield = "chat-shield";
    public const string ChatScroll = "chat-scroll";
    public const string ChatCoin = "chat-coin";
    public const string Pin = "pin";
    public const string Collapse = "collapse";
    public const string Expand = "expand";

    public static IReadOnlyList<string> All { get; } =
    [
        Peace, Melee, Missile, Magic, Repeat, AutoTarget, KeepInView, Cast,
        RecallLifestone, RecallMarketplace, RecallHouse, RecallMansion, RecallAllegiance,
        Search, ChevronDown,
        ChatAll, ChatLocal, ChatGroup, ChatSocial, ChatCombat, ChatSystem,
        ChatLoot, ChatStar, ChatTell, ChatShield, ChatScroll, ChatCoin,
        Pin, Collapse, Expand,
    ];
```

- [ ] **Step 5: Create `ChatDeckIcons.cs`**

```csharp
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

/// <summary>The bundled icon each tab icon choice draws with.</summary>
internal static class ChatDeckIcons
{
    public static string Name(ChatDeckIcon icon) => icon switch
    {
        ChatDeckIcon.Local => HudIcons.ChatLocal,
        ChatDeckIcon.Group => HudIcons.ChatGroup,
        ChatDeckIcon.Social => HudIcons.ChatSocial,
        ChatDeckIcon.Combat => HudIcons.ChatCombat,
        ChatDeckIcon.System => HudIcons.ChatSystem,
        ChatDeckIcon.Loot => HudIcons.ChatLoot,
        ChatDeckIcon.Star => HudIcons.ChatStar,
        ChatDeckIcon.Tell => HudIcons.ChatTell,
        ChatDeckIcon.Shield => HudIcons.ChatShield,
        ChatDeckIcon.Scroll => HudIcons.ChatScroll,
        ChatDeckIcon.Coin => HudIcons.ChatCoin,
        _ => HudIcons.ChatAll,
    };
}
```

- [ ] **Step 6: Run the Verify command, expect PASS.** If `EveryHudIconIsEmbeddedAndParses` reports a parse reason for one file, fix that file's SVG (the reason names the attribute or element) without changing its shape's meaning.

- [ ] **Step 7: Commit**

```bash
git add assets/hud/*.svg src/AcDream.App/UI/Hud/EmbeddedSvgSpriteCache.cs src/AcDream.App/UI/Hud/ChatDeckIcons.cs tests/AcDream.App.Tests/UI/Hud/ChatDeckIconsTests.cs
git commit -m "Chat Deck: tab and drawer icons

Claude-Session: https://claude.ai/code/session_017hCEwjbupaVz5o7Zx5rwfZ"
```

---

### Task 3: Drawer state machine

**Goal:** A pure `ChatDeckDrawer` that holds pinned / collapsed / open, the edge, what opened the fly-out and the slide, and applies the spec's open and close timings from `Tick(dt, input)`.

**Files:**
- Create: `src/AcDream.App/UI/Hud/ChatDeckDrawer.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/ChatDeckDrawerTests.cs`

**Acceptance Criteria:**
- [ ] Starts pinned; `Collapse(edge)` → Collapsed with that edge, `LayoutChanged` once; `Pin()` from Collapsed or Open → Pinned, `LayoutChanged` once; `Pin()` while pinned raises nothing; `Restore` raises nothing.
- [ ] Collapsed: pointer over the strip for 0.3 s opens (reason Hover, Slide 0); a break in hover restarts the 0.3 s.
- [ ] Open: Slide reaches 1 after 0.12 s; with nothing holding it, it closes 0.6 s after the pointer left and not before; each hold (strip, deck, focus, draft, scrolled up, busy) keeps it open.
- [ ] `EntryDone()` closes a fly-out opened by Entry at once, and does nothing to one opened by Hover or Stud.
- [ ] `Open` does nothing unless collapsed; `NearestEdge` picks by the window centre.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ChatDeckDrawerTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-strip tests/AcDream.App.Tests/AcDream.App.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** `ChatDeckDrawerTests.cs`

```csharp
using AcDream.App.UI.Hud;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckDrawerTests
{
    private static readonly ChatDeckDrawerInput Away = new(false, false, false, true, false, false);
    private static readonly ChatDeckDrawerInput OnStrip = Away with { PointerOverStrip = true };

    private static (ChatDeckDrawer Drawer, List<int> Changes) Collapsed()
    {
        var d = new ChatDeckDrawer();
        var changes = new List<int>();
        d.LayoutChanged += () => changes.Add(1);
        d.Collapse(ChatDeckEdge.Left);
        changes.Clear();
        return (d, changes);
    }

    [Fact]
    public void Collapse_And_Pin_ReportTheLayoutOnce()
    {
        var d = new ChatDeckDrawer();
        int changes = 0;
        d.LayoutChanged += () => changes++;
        Assert.True(d.Pinned);

        d.Collapse(ChatDeckEdge.Right);
        Assert.Equal(ChatDeckDrawerState.Collapsed, d.State);
        Assert.Equal(ChatDeckEdge.Right, d.Edge);
        Assert.True(d.StripVisible);
        Assert.False(d.DeckVisible);
        Assert.Equal(1, changes);

        d.Pin();
        d.Pin();
        Assert.True(d.Pinned);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Restore_RaisesNothing()
    {
        var d = new ChatDeckDrawer();
        int changes = 0;
        d.LayoutChanged += () => changes++;
        d.Restore(pinned: false, ChatDeckEdge.Right);
        Assert.Equal(ChatDeckDrawerState.Collapsed, d.State);
        Assert.Equal(ChatDeckEdge.Right, d.Edge);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Hover_OpensAfterTheDelay_AndSlidesOut()
    {
        var (d, _) = Collapsed();
        d.Tick(0.2, OnStrip);
        Assert.Equal(ChatDeckDrawerState.Collapsed, d.State);
        d.Tick(0.11, OnStrip);
        Assert.Equal(ChatDeckDrawerState.Open, d.State);
        Assert.Equal(ChatDeckOpenReason.Hover, d.OpenedBy);
        Assert.Equal(0, d.Slide);

        d.Tick(0.06, OnStrip);
        Assert.InRange(d.Slide, 0.4, 0.6);
        d.Tick(0.07, OnStrip);
        Assert.Equal(1, d.Slide);
    }

    [Fact]
    public void Hover_ThatBreaks_StartsAgain()
    {
        var (d, _) = Collapsed();
        d.Tick(0.25, OnStrip);
        d.Tick(0.01, Away);
        d.Tick(0.25, OnStrip);
        Assert.Equal(ChatDeckDrawerState.Collapsed, d.State);
    }

    [Fact]
    public void Open_ClosesAfterThePointerHasBeenAwayLongEnough()
    {
        var (d, _) = Collapsed();
        d.Open(ChatDeckOpenReason.Hover);
        d.Tick(0.5, Away);
        Assert.Equal(ChatDeckDrawerState.Open, d.State);
        d.Tick(0.11, Away);
        Assert.Equal(ChatDeckDrawerState.Collapsed, d.State);
        Assert.Equal(ChatDeckOpenReason.None, d.OpenedBy);
    }

    public static TheoryData<ChatDeckDrawerInput> Holds() =>
    [
        Away with { PointerOverStrip = true },
        Away with { PointerOverDeck = true },
        Away with { InputFocused = true },
        Away with { InputEmpty = false },
        Away with { ScrolledUp = true },
        Away with { Busy = true },
    ];

    [Theory]
    [MemberData(nameof(Holds))]
    public void Open_StaysWhileSomethingHoldsIt(ChatDeckDrawerInput hold)
    {
        var (d, _) = Collapsed();
        d.Open(ChatDeckOpenReason.Hover);
        d.Tick(5.0, hold);
        Assert.Equal(ChatDeckDrawerState.Open, d.State);
    }

    [Fact]
    public void EntryDone_ClosesOnlyAnEntryFlyout()
    {
        var (d, _) = Collapsed();
        d.Open(ChatDeckOpenReason.Hover);
        d.EntryDone();
        Assert.Equal(ChatDeckDrawerState.Open, d.State);

        d.Close();
        d.Open(ChatDeckOpenReason.Entry);
        d.EntryDone();
        Assert.Equal(ChatDeckDrawerState.Collapsed, d.State);
    }

    [Fact]
    public void Pin_FromAFlyout_StopsItClosing()
    {
        var (d, changes) = Collapsed();
        d.Open(ChatDeckOpenReason.Stud);
        d.Pin();
        d.Tick(5.0, Away);
        Assert.True(d.Pinned);
        Assert.Equal(1, d.Slide);
        Assert.Single(changes);
    }

    [Fact]
    public void Open_OnlyFromCollapsed()
    {
        var d = new ChatDeckDrawer();
        d.Open(ChatDeckOpenReason.Entry);
        Assert.True(d.Pinned);
    }

    [Theory]
    [InlineData(10f, ChatDeckEdge.Left)]
    [InlineData(1100f, ChatDeckEdge.Right)]
    [InlineData(570f, ChatDeckEdge.Left)]   // centre exactly at the middle goes left
    public void NearestEdge_ByTheWindowCentre(float left, ChatDeckEdge edge) =>
        Assert.Equal(edge, ChatDeckDrawer.NearestEdge(left, 460f, 1600f));
}
```

- [ ] **Step 2: Run, expect a compile failure.**

- [ ] **Step 3: Implement** `ChatDeckDrawer.cs`

```csharp
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

internal enum ChatDeckDrawerState { Pinned, Collapsed, Open }

/// <summary>What opened the fly-out: it decides whether a send or Esc closes it again.</summary>
internal enum ChatDeckOpenReason { None, Hover, Entry, Stud }

/// <summary>One frame's view of what keeps a fly-out open.</summary>
/// <param name="Busy">A deck menu is open, the transcript is being drag-selected, or a press inside the deck holds the pointer.</param>
internal readonly record struct ChatDeckDrawerInput(
    bool PointerOverStrip,
    bool PointerOverDeck,
    bool InputFocused,
    bool InputEmpty,
    bool ScrolledUp,
    bool Busy);

/// <summary>
/// Whether the Chat Deck is pinned, collapsed to its edge strip, or flown out from it.
/// Pure: time comes in through <see cref="Tick"/>, the pointer and input through
/// <see cref="ChatDeckDrawerInput"/>; the window does the placing.
/// </summary>
internal sealed class ChatDeckDrawer
{
    public const double HoverDelay = 0.3;
    public const double CloseDelay = 0.6;
    public const double SlideTime = 0.12;

    private double _hover;
    private double _away;

    public ChatDeckDrawerState State { get; private set; } = ChatDeckDrawerState.Pinned;
    public ChatDeckEdge Edge { get; private set; } = ChatDeckEdge.Left;
    public ChatDeckOpenReason OpenedBy { get; private set; }
    /// <summary>0 when a fly-out starts, 1 once it is all the way out (and always 1 otherwise).</summary>
    public double Slide { get; private set; } = 1;

    public bool Pinned => State == ChatDeckDrawerState.Pinned;
    public bool DeckVisible => State != ChatDeckDrawerState.Collapsed;
    public bool StripVisible => State != ChatDeckDrawerState.Pinned;

    /// <summary>Pinned or the edge changed: the owner saves the layout.</summary>
    public event Action? LayoutChanged;

    /// <summary>The edge a window collapses to: whichever its centre is nearer (left on a tie).</summary>
    public static ChatDeckEdge NearestEdge(float left, float width, float screenWidth) =>
        left + width / 2f <= screenWidth / 2f ? ChatDeckEdge.Left : ChatDeckEdge.Right;

    /// <summary>Takes a saved layout (at login); raises nothing.</summary>
    public void Restore(bool pinned, ChatDeckEdge edge)
    {
        Edge = edge;
        State = pinned ? ChatDeckDrawerState.Pinned : ChatDeckDrawerState.Collapsed;
        Reset();
    }

    public void Collapse(ChatDeckEdge edge)
    {
        bool changed = State == ChatDeckDrawerState.Pinned || Edge != edge;
        Edge = edge;
        State = ChatDeckDrawerState.Collapsed;
        Reset();
        if (changed) LayoutChanged?.Invoke();
    }

    public void Pin()
    {
        if (State == ChatDeckDrawerState.Pinned) return;
        State = ChatDeckDrawerState.Pinned;
        Reset();
        LayoutChanged?.Invoke();
    }

    public void Open(ChatDeckOpenReason reason)
    {
        if (State != ChatDeckDrawerState.Collapsed) return;
        State = ChatDeckDrawerState.Open;
        OpenedBy = reason;
        Slide = 0;
        _hover = 0;
        _away = 0;
    }

    public void Close()
    {
        if (State != ChatDeckDrawerState.Open) return;
        State = ChatDeckDrawerState.Collapsed;
        Reset();
    }

    /// <summary>A line was sent, or Esc left the input: a fly-out a chat-entry action opened closes at once.</summary>
    public void EntryDone()
    {
        if (State == ChatDeckDrawerState.Open && OpenedBy == ChatDeckOpenReason.Entry) Close();
    }

    public void Tick(double deltaSeconds, in ChatDeckDrawerInput input)
    {
        switch (State)
        {
            case ChatDeckDrawerState.Collapsed:
                _hover = input.PointerOverStrip ? _hover + deltaSeconds : 0;
                if (_hover >= HoverDelay) Open(ChatDeckOpenReason.Hover);
                break;
            case ChatDeckDrawerState.Open:
                Slide = Math.Min(1, Slide + deltaSeconds / SlideTime);
                bool held = input.PointerOverStrip || input.PointerOverDeck || input.InputFocused
                    || !input.InputEmpty || input.ScrolledUp || input.Busy;
                _away = held ? 0 : _away + deltaSeconds;
                if (_away >= CloseDelay) Close();
                break;
        }
    }

    private void Reset()
    {
        OpenedBy = ChatDeckOpenReason.None;
        Slide = 1;
        _hover = 0;
        _away = 0;
    }
}
```

- [ ] **Step 4: Run the Verify command, expect PASS.** (`Hover_OpensAfterTheDelay_AndSlidesOut` ticks 0.2 + 0.11 = 0.31 s; floating-point sums are why the second tick is not 0.1.)

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Hud/ChatDeckDrawer.cs tests/AcDream.App.Tests/UI/Hud/ChatDeckDrawerTests.cs
git commit -m "Chat Deck: drawer state (pinned, collapsed, fly-out)

Claude-Session: https://claude.ai/code/session_017hCEwjbupaVz5o7Zx5rwfZ"
```

---

### Task 4: Peek feed

**Goal:** A pure `ChatDeckPeekFeed` that keeps the newest 3 peekable lines with their text, ages them, fades them over their last second and drops them after 6 s.

**Files:**
- Create: `src/AcDream.App/UI/Hud/ChatDeckPeekFeed.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/ChatDeckPeekFeedTests.cs`

**Acceptance Criteria:**
- [ ] A line to a peek-on tab (Local) peeks; a line only to peek-off tabs (System) does not; an incoming tell (alert) peeks; a line from the character's own name does not.
- [ ] Only the newest 3 are kept, oldest first.
- [ ] `Alpha` is 1 up to 5 s, 0.5 at 5.5 s; `Advance` drops a line at 6 s.
- [ ] `Clear` empties it.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ChatDeckPeekFeedTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-strip tests/AcDream.App.Tests/AcDream.App.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** `ChatDeckPeekFeedTests.cs` (lines go through a real model, so tabs and verdicts are the real ones)

```csharp
using AcDream.App.UI.Hud;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckPeekFeedTests
{
    private sealed class Rig
    {
        public readonly ChatLog Log = new();
        public readonly ChatDeckModel Model;
        public readonly ChatDeckPeekFeed Feed = new(l => l.Facts.Text);

        public Rig()
        {
            Model = new ChatDeckModel(Log, ChatDeckDefaults.Create(), "Davian");
            Model.LinesDelivered += d => Feed.Add(d, Model.Settings.Tabs);
        }

        public void Say(string text, string sender = "Bob")
        {
            Log.OnLocalSpeech(sender, text, 0x50000002u, isRanged: false, logTextType: (uint)RetailLogTextType.Speech);
            Model.Tick();
        }
    }

    [Fact]
    public void PeekTabsAndAlertsPeek_OthersAndOwnLinesDoNot()
    {
        var r = new Rig();
        r.Say("hello");                                                          // Local: peek on
        r.Log.OnSystemMessage("You feel refreshed.", (uint)RetailLogTextType.System); // System: peek off
        r.Model.Tick();
        r.Say("me again", sender: "Davian");                                     // own line
        r.Log.OnTellReceived("Bo", "got a sec?", 0x50000003u, (uint)RetailLogTextType.Tell); // alert
        r.Model.Tick();

        Assert.Equal(["hello", "got a sec?"], r.Feed.Lines.Select(l => l.Text));
    }

    [Fact]
    public void KeepsTheNewestThree_OldestFirst()
    {
        var r = new Rig();
        foreach (string s in new[] { "1", "2", "3", "4" }) r.Say(s);
        Assert.Equal(["2", "3", "4"], r.Feed.Lines.Select(l => l.Text));
    }

    [Fact]
    public void FadesOverTheLastSecond_ThenGoes()
    {
        var r = new Rig();
        r.Say("hello");
        r.Feed.Advance(5.0);
        Assert.Equal(1f, ChatDeckPeekFeed.Alpha(r.Feed.Lines[0].Age));
        r.Feed.Advance(0.5);
        Assert.Equal(0.5f, ChatDeckPeekFeed.Alpha(r.Feed.Lines[0].Age), 3);
        r.Feed.Advance(0.5);
        Assert.Empty(r.Feed.Lines);
    }

    [Fact]
    public void Clear_EmptiesIt()
    {
        var r = new Rig();
        r.Say("hello");
        r.Feed.Clear();
        Assert.Empty(r.Feed.Lines);
    }
}
```

Before running, check `RetailLogTextType` (`src/AcDream.Core/Chat/RetailLogTextType.cs`) for the exact member names of plain speech and tells (`Speech`, `Tell` above) and that `ChatDeckChannels` puts them in the Local group; use the names the enum has.

- [ ] **Step 2: Run, expect a compile failure.**

- [ ] **Step 3: Implement** `ChatDeckPeekFeed.cs`

```csharp
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

/// <summary>
/// The lines shown beside the collapsed strip: the newest <see cref="MaxLines"/> that went to a
/// tab with peek on, or are alerts, never your own. Each lives <see cref="LifeSeconds"/> and
/// fades over its last <see cref="FadeSeconds"/>. Oldest first.
/// </summary>
internal sealed class ChatDeckPeekFeed(Func<ChatDeckLine, string> text)
{
    public const int MaxLines = 3;
    public const double LifeSeconds = 6.0;
    public const double FadeSeconds = 1.0;

    public readonly record struct Entry(ChatDeckLine Line, string Text, double Age);

    private readonly List<Entry> _lines = [];

    public IReadOnlyList<Entry> Lines => _lines;

    public static bool Peeks(ChatDeckDelivery delivery, IReadOnlyList<ChatDeckTab> tabs) =>
        !delivery.Line.Facts.IsOwn
        && (delivery.Line.Alert || delivery.TabIds.Any(id => tabs.Any(t => t.Id == id && t.Peek)));

    public void Add(IReadOnlyList<ChatDeckDelivery> deliveries, IReadOnlyList<ChatDeckTab> tabs)
    {
        foreach (ChatDeckDelivery delivery in deliveries)
        {
            if (!Peeks(delivery, tabs)) continue;
            _lines.Add(new Entry(delivery.Line, text(delivery.Line), 0));
            if (_lines.Count > MaxLines) _lines.RemoveAt(0);
        }
    }

    public void Advance(double deltaSeconds)
    {
        for (int i = _lines.Count - 1; i >= 0; i--)
        {
            Entry aged = _lines[i] with { Age = _lines[i].Age + deltaSeconds };
            if (aged.Age >= LifeSeconds) _lines.RemoveAt(i);
            else _lines[i] = aged;
        }
    }

    /// <summary>1 until the last second of a line's life, then down to 0.</summary>
    public static float Alpha(double age) =>
        age <= LifeSeconds - FadeSeconds ? 1f : (float)Math.Max(0, (LifeSeconds - age) / FadeSeconds);

    public void Clear() => _lines.Clear();
}
```

- [ ] **Step 4: Run the Verify command, expect PASS.**

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Hud/ChatDeckPeekFeed.cs tests/AcDream.App.Tests/UI/Hud/ChatDeckPeekFeedTests.cs
git commit -m "Chat Deck: peek feed

Claude-Session: https://claude.ai/code/session_017hCEwjbupaVz5o7Zx5rwfZ"
```

---

### Task 5: Pin and collapse studs, strip geometry

**Goal:** The pinned top row gains pin and collapse studs (right to left: collapse, pin, search), and a new `ChatDeckStripGeometry` lays out and hit-tests the strip and computes where the strip and the fly-out sit on screen.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/ChatDeckGeometry.cs`
- Create: `src/AcDream.App/UI/Hud/ChatDeckStripGeometry.cs`
- Modify: `tests/AcDream.App.Tests/UI/Hud/ChatDeckGeometryTests.cs` (line 41 expects the search stud at the far right; it moves two studs left)
- Test: `tests/AcDream.App.Tests/UI/Hud/ChatDeckStripGeometryTests.cs`

**Acceptance Criteria:**
- [ ] `CollapseStud.X == width - Pad - StudSize`; `PinStud` one stud and `StudGap` to its left; `SearchStud` one more to the left; tabs never reach `SearchStud.X`; `HitAt` finds `PinStud` and `CollapseStud`.
- [ ] Strip: studs from y = 10, 22 high, 5 apart, centred in 34; ⇥ stud 8 px above the bottom; a tab stud with no room above the ⇥ stud has zero size; `HitAt` finds tab i and `PinStud`.
- [ ] `Place`: left edge x = 0, right edge x = screen − 34; height = max(window height, `FullHeight(tabs)`) capped at the screen; top clamped so the strip stays on screen.
- [ ] `FlyoutLeft`: at slide 1 the window touches the strip (left: 34; right: screen − 34 − width); at slide 0 it sits `SlideDistance` (16) further in toward the strip.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ChatDeckGeometryTests|FullyQualifiedName~ChatDeckStripGeometryTests|FullyQualifiedName~UiChatDeck' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-strip tests/AcDream.App.Tests/AcDream.App.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** In `ChatDeckGeometryTests`, replace the assertion at line 41 with:

```csharp
        Assert.Equal(460 - ChatDeckGeometry.Pad - ChatDeckGeometry.StudSize, g.CollapseStud.X, 2);
        Assert.Equal(g.CollapseStud.X - ChatDeckGeometry.StudGap - ChatDeckGeometry.StudSize, g.PinStud.X, 2);
        Assert.Equal(g.PinStud.X - ChatDeckGeometry.StudGap - ChatDeckGeometry.StudSize, g.SearchStud.X, 2);
```

and in `HitAt_FindsEachZone` add:

```csharp
        Assert.Equal(ChatDeckZone.PinStud, g.HitAt(g.PinStud.X + 2, g.PinStud.Y + 2).Zone);
        Assert.Equal(ChatDeckZone.CollapseStud, g.HitAt(g.CollapseStud.X + 2, g.CollapseStud.Y + 2).Zone);
```

Create `ChatDeckStripGeometryTests.cs`:

```csharp
using AcDream.App.UI.Hud;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckStripGeometryTests
{
    [Fact]
    public void Studs_FromTheTop_PinStudAtTheBottom()
    {
        var g = ChatDeckStripGeometry.Create(220f, 6);
        Assert.Equal(6, g.Studs.Count);
        Assert.Equal(new DeckRect(5f, 10f, 24f, 22f), g.Studs[0]);
        Assert.Equal(10f + 27f, g.Studs[1].Y);
        Assert.Equal(new DeckRect(5f, 220f - 8f - 22f, 24f, 22f), g.PinStud);
        Assert.Equal(new ChatDeckHit(ChatDeckZone.Tab, 1), g.HitAt(7f, g.Studs[1].Y + 2f));
        Assert.Equal(ChatDeckZone.PinStud, g.HitAt(7f, g.PinStud.Y + 2f).Zone);
        Assert.Equal(ChatDeckZone.Frame, g.HitAt(1f, 1f).Zone);
    }

    [Fact]
    public void StudsWithNoRoom_AreDropped()
    {
        var g = ChatDeckStripGeometry.Create(140f, 6);
        Assert.All(g.Studs.Where(s => s.W > 0), s => Assert.True(s.Y + s.H <= g.PinStud.Y - ChatDeckStripGeometry.StudGap + 0.01f));
        Assert.Contains(g.Studs, s => s.W == 0);
    }

    [Fact]
    public void Place_OnTheEdge_TallEnoughForEveryStud_OnScreen()
    {
        DeckRect left = ChatDeckStripGeometry.Place(ChatDeckEdge.Left, 670f, 140f, 6, 1600f, 900f);
        Assert.Equal(0f, left.X);
        Assert.Equal(ChatDeckStripGeometry.FullHeight(6), left.H);
        Assert.Equal(900f - left.H, left.Y);           // pulled up to stay on screen

        DeckRect right = ChatDeckStripGeometry.Place(ChatDeckEdge.Right, 100f, 300f, 6, 1600f, 900f);
        Assert.Equal(1600f - 34f, right.X);
        Assert.Equal(100f, right.Y);
        Assert.Equal(300f, right.H);

        DeckRect tall = ChatDeckStripGeometry.Place(ChatDeckEdge.Left, 0f, 2000f, 6, 1600f, 900f);
        Assert.Equal(900f, tall.H);
    }

    [Fact]
    public void FlyoutLeft_TouchesTheStripOnceOut()
    {
        Assert.Equal(34f, ChatDeckStripGeometry.FlyoutLeft(ChatDeckEdge.Left, 460f, 1600f, 1));
        Assert.Equal(34f - 16f, ChatDeckStripGeometry.FlyoutLeft(ChatDeckEdge.Left, 460f, 1600f, 0));
        Assert.Equal(1600f - 34f - 460f, ChatDeckStripGeometry.FlyoutLeft(ChatDeckEdge.Right, 460f, 1600f, 1));
        Assert.Equal(1600f - 34f - 460f + 16f, ChatDeckStripGeometry.FlyoutLeft(ChatDeckEdge.Right, 460f, 1600f, 0));
    }
}
```

(`DeckRect` is a record struct with `X, Y, W, H`; if its constructor parameter order differs, use named arguments.)

- [ ] **Step 2: Run, expect a compile failure.**

- [ ] **Step 3: Change `ChatDeckGeometry`.** Append the zones (keep existing values):

```csharp
internal enum ChatDeckZone { None, Frame, Tab, SearchStud, Chip, PinStud, CollapseStud }
```

Add `public const float StudGap = 4f;`, the two properties after `SearchStud`:

```csharp
    public required DeckRect PinStud { get; init; }
    public required DeckRect CollapseStud { get; init; }
```

In `Create`, replace the first line (`var stud = ...`) with:

```csharp
        var collapse = new DeckRect(width - Pad - StudSize, Pad, StudSize, StudSize);
        var pin = collapse with { X = collapse.X - StudGap - StudSize };
        var stud = pin with { X = pin.X - StudGap - StudSize };
```

set `PinStud = pin, CollapseStud = collapse,` in the returned object, and in `HitAt` after the search stud line:

```csharp
        if (PinStud.Contains(x, y)) return new(ChatDeckZone.PinStud);
        if (CollapseStud.Contains(x, y)) return new(ChatDeckZone.CollapseStud);
```

- [ ] **Step 4: Create `ChatDeckStripGeometry.cs`**

```csharp
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

/// <summary>
/// The collapsed Chat Deck's strip: one stud per tab from the top, the ⇥ stud at the bottom,
/// and where the strip and the flown-out window sit on screen.
/// </summary>
internal sealed class ChatDeckStripGeometry
{
    public const float Width = 34f;
    public const float StudWidth = 24f;
    public const float StudHeight = 22f;
    public const float StudGap = 5f;
    public const float PadTop = 10f;
    public const float PadBottom = 8f;
    /// <summary>How far short of the strip a fly-out starts its slide.</summary>
    public const float SlideDistance = 16f;

    public required DeckRect Frame { get; init; }
    /// <summary>One per tab; zero size for a tab with no room above the ⇥ stud.</summary>
    public required IReadOnlyList<DeckRect> Studs { get; init; }
    public required DeckRect PinStud { get; init; }

    /// <summary>The height that shows every tab's stud and the ⇥ stud.</summary>
    public static float FullHeight(int tabs) => PadTop + tabs * (StudHeight + StudGap) + StudHeight + PadBottom;

    public static ChatDeckStripGeometry Create(float height, int tabCount)
    {
        float x = (Width - StudWidth) / 2f;
        var pin = new DeckRect(x, height - PadBottom - StudHeight, StudWidth, StudHeight);
        var studs = new List<DeckRect>(tabCount);
        float y = PadTop;
        for (int i = 0; i < tabCount; i++)
        {
            bool fits = y + StudHeight <= pin.Y - StudGap + 0.01f;
            studs.Add(fits ? new DeckRect(x, y, StudWidth, StudHeight) : new DeckRect(x, y, 0f, 0f));
            y += StudHeight + StudGap;
        }
        return new ChatDeckStripGeometry { Frame = new DeckRect(0f, 0f, Width, height), Studs = studs, PinStud = pin };
    }

    public ChatDeckHit HitAt(float x, float y)
    {
        for (int i = 0; i < Studs.Count; i++)
            if (Studs[i].W > 0 && Studs[i].Contains(x, y)) return new(ChatDeckZone.Tab, i);
        if (PinStud.Contains(x, y)) return new(ChatDeckZone.PinStud);
        return Frame.Contains(x, y) ? new(ChatDeckZone.Frame) : new(ChatDeckZone.None);
    }

    /// <summary>The strip on screen: on <paramref name="edge"/>, from the window's top, tall enough for every stud where the screen allows.</summary>
    public static DeckRect Place(
        ChatDeckEdge edge, float windowTop, float windowHeight, int tabCount, float screenWidth, float screenHeight)
    {
        float h = MathF.Min(screenHeight, MathF.Max(windowHeight, FullHeight(tabCount)));
        float top = Math.Clamp(windowTop, 0f, MathF.Max(0f, screenHeight - h));
        float left = edge == ChatDeckEdge.Left ? 0f : screenWidth - Width;
        return new DeckRect(left, top, Width, h);
    }

    /// <summary>The flown-out window's left edge beside the strip; <paramref name="slide"/> 0..1 eases it the last <see cref="SlideDistance"/> out.</summary>
    public static float FlyoutLeft(ChatDeckEdge edge, float windowWidth, float screenWidth, double slide)
    {
        float back = (float)(1 - Math.Clamp(slide, 0, 1)) * SlideDistance;
        return edge == ChatDeckEdge.Left ? Width - back : screenWidth - Width - windowWidth + back;
    }
}
```

- [ ] **Step 5: Make the new studs controls** in `UiChatDeck.cs`:

```csharp
    private static bool IsControl(ChatDeckHit hit) =>
        hit.Zone is ChatDeckZone.Tab or ChatDeckZone.SearchStud or ChatDeckZone.Chip
            or ChatDeckZone.PinStud or ChatDeckZone.CollapseStud;
```

(Pressing them does nothing until Task 7.) Draw them in `UiChatDeck.Draw.cs` `OnDraw`, after the search stud:

```csharp
        DrawStud(ctx, _geometry.PinStud, HudIcons.Pin, on: true);
        DrawStud(ctx, _geometry.CollapseStud, HudIcons.Collapse, on: false);
```

(Task 7 makes the pin stud's `on` follow the drawer.)

- [ ] **Step 6: Run the Verify command, expect PASS** (includes every existing `UiChatDeck*` test: the search stud tests press `Geometry.SearchStud` wherever it now is).

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.App/UI/Hud/ChatDeckGeometry.cs src/AcDream.App/UI/Hud/ChatDeckStripGeometry.cs src/AcDream.App/UI/Hud/UiChatDeck.cs src/AcDream.App/UI/Hud/UiChatDeck.Draw.cs tests/AcDream.App.Tests/UI/Hud/ChatDeckGeometryTests.cs tests/AcDream.App.Tests/UI/Hud/ChatDeckStripGeometryTests.cs
git commit -m "Chat Deck: pin and collapse studs, strip geometry

Claude-Session: https://claude.ai/code/session_017hCEwjbupaVz5o7Zx5rwfZ"
```

---

### Task 6: Strip and peek elements

**Goal:** `UiChatDeck` creates two elements it owns but does not parent: `Strip` (draws the strip, takes presses and shows tab-name tooltips) and `Peek` (draws the peek lines, never takes input), plus the drawer and peek feed fields the next tasks wire up.

**Files:**
- Create: `src/AcDream.App/UI/Hud/UiChatDeck.Strip.cs`
- Modify: `src/AcDream.App/UI/Hud/UiChatDeck.cs` (constructor)
- Test: `tests/AcDream.App.Tests/UI/Hud/UiChatDeckStripTests.cs`

**Acceptance Criteria:**
- [ ] `Strip` hit-tests true inside its frame and handles clicks; `Peek` never hit-tests.
- [ ] A press on a strip tab stud goes to `PressStripTab(i)`, on the ⇥ stud to `PressStripPin()` (empty stubs here; Task 7 implements and tests them).
- [ ] The strip's tooltip over tab stud i is that tab's name; over ⇥ it is "Pin the chat"; elsewhere null.
- [ ] Drawing the strip and the peek with no fonts and no icons does not throw.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~UiChatDeckStripTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-strip tests/AcDream.App.Tests/AcDream.App.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** `UiChatDeckStripTests.cs`

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiChatDeckStripTests
{
    private static UiChatDeckTests.Harness Laid()
    {
        var h = new UiChatDeckTests.Harness();
        h.Deck.Strip.Width = ChatDeckStripGeometry.Width;
        h.Deck.Strip.Height = 220f;
        h.Deck.LayOutStrip();
        return h;
    }

    [Fact]
    public void Strip_TakesPresses_PeekNever()
    {
        var h = Laid();
        h.Deck.Strip.Visible = true;
        h.Deck.Peek.Visible = true;
        h.Deck.Peek.Width = h.Deck.Peek.Height = 50f;
        Assert.Same(h.Deck.Strip, h.Deck.Strip.HitTest(10f, 20f));
        Assert.True(h.Deck.Strip.HandlesClick);
        Assert.Null(h.Deck.Peek.HitTest(5f, 5f));
    }

    [Fact]
    public void Tooltip_NamesTheTab_OrThePin()
    {
        var h = Laid();
        DeckRect group = h.Deck.StripGeometry.Studs[h.TabIndex("group")];
        Assert.Equal("Group", h.Deck.StripTooltipAt(group.X + 2f, group.Y + 2f));
        DeckRect pin = h.Deck.StripGeometry.PinStud;
        Assert.Equal("Pin the chat", h.Deck.StripTooltipAt(pin.X + 2f, pin.Y + 2f));
        Assert.Null(h.Deck.StripTooltipAt(1f, 1f));
    }
}
```

`UiElement.HitTest(x, y)` returns the element hit (or null) and skips hidden elements, hence the `Visible = true` above. Drawing is covered by the runtime's headless draw in Task 9's suite and the live check; there is no render-context fake to assert against here.

- [ ] **Step 2: Run, expect a compile failure.**

- [ ] **Step 3: Create `UiChatDeck.Strip.cs`**

```csharp
using System.Numerics;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiChatDeck
{
    private const float StripBadgePixels = 10f;
    private const float StripBadgeHeight = 13f;
    private const float PeekMaxWidth = 420f;
    private const float PeekGap = 8f;
    private const float PeekPadX = 6f;

    private readonly ChatDeckDrawer _drawer = new();
    private readonly ChatDeckPeekFeed _peek = new(line => AcDream.Runtime.Chat.ChatLineProjector.Project(line.Entry).Text);
    private ChatDeckStripGeometry _strip = ChatDeckStripGeometry.Create(ChatDeckGeometry.MinHeight, 0);

    /// <summary>The collapsed strip: a plain child of the interface root, placed by the deck.</summary>
    internal UiElement Strip { get; private set; } = null!;
    /// <summary>The peek lines: a click-through child of an overlay layer, placed by the deck.</summary>
    internal UiElement Peek { get; private set; } = null!;
    internal ChatDeckStripGeometry StripGeometry => _strip;
    internal ChatDeckDrawer Drawer => _drawer;
    internal bool Pinned => _drawer.Pinned;
    internal IReadOnlyList<ChatDeckPeekFeed.Entry> PeekLines => _peek.Lines;

    private void CreateStrip()
    {
        Strip = new StripElement(this) { Visible = false };
        Peek = new PeekElement(this) { Visible = false };
    }

    /// <summary>Lays the strip's studs out for its current height and the current tabs.</summary>
    internal void LayOutStrip() => _strip = ChatDeckStripGeometry.Create(Strip.Height, _labels.Count);

    internal string? StripTooltipAt(float x, float y)
    {
        ChatDeckHit hit = _strip.HitAt(x, y);
        if (hit.Zone == ChatDeckZone.PinStud) return "Pin the chat";
        if (hit.Zone != ChatDeckZone.Tab || _model is null || hit.Tab >= _model.Settings.Tabs.Count) return null;
        return _model.Settings.Tabs[hit.Tab].Name;
    }

    private void PressStrip(ChatDeckHit hit)
    {
        if (hit.Zone == ChatDeckZone.Tab) PressStripTab(hit.Tab);
        else if (hit.Zone == ChatDeckZone.PinStud) PressStripPin();
    }

    private void DrawStrip(UiRenderContext ctx)
    {
        DeckRect f = _strip.Frame;
        float opacity = _model?.Settings.Appearance.BackgroundOpacity ?? new ChatDeckAppearance().BackgroundOpacity;
        // Square on the screen edge, rounded on the inside.
        var radii = _drawer.Edge == ChatDeckEdge.Left
            ? new CanvasCornerRadii(0f, 8f, 8f, 0f)
            : new CanvasCornerRadii(8f, 0f, 0f, 8f);
        ctx.FillVerticalGradient(f.X, f.Y, f.W, f.H, radii,
            ChatDeckPalette.FrameTop with { W = opacity }, ChatDeckPalette.FrameBottom with { W = opacity });

        if (_model is { } model)
        {
            IReadOnlyList<ChatDeckTab> tabs = model.Settings.Tabs;
            for (int i = 0; i < _strip.Studs.Count && i < tabs.Count; i++)
            {
                DeckRect r = _strip.Studs[i];
                if (r.W <= 0) continue;
                bool selected = tabs[i].Id == model.SelectedTabId;
                ctx.FillRoundedRect(r.X - 1f, r.Y - 1f, r.W + 2f, r.H + 2f, 6f, ChatDeckPalette.FrameEdge);
                ctx.FillRoundedRect(r.X, r.Y, r.W, r.H, 5f, selected ? ChatDeckPalette.TabSelected : ChatDeckPalette.StudOff);
                DrawIcon(ctx, ChatDeckIcons.Name(tabs[i].Icon), r.X + (r.W - 14f) / 2f, r.Y + (r.H - 14f) / 2f, 14f,
                    selected ? ChatDeckPalette.TextInk : ChatDeckPalette.StudOffInk);
                DrawStripBadge(ctx, r, i);
            }
        }
        DrawStud(ctx, _strip.PinStud, HudIcons.Expand, on: false);
    }

    private void DrawStripBadge(UiRenderContext ctx, DeckRect stud, int index)
    {
        string badge = index < _labels.Count ? _labels[index].Badge : "";
        if (badge.Length == 0 || _model is null) return;
        var face = new DeckFace(_fonts?.Face(StripBadgePixels, BundledUiFontWeight.SemiBold));
        float textW = face.Measure(ctx, badge);
        float w = MathF.Max(StripBadgeHeight, textW + 4f);
        var pill = new DeckRect(stud.X + stud.W - w + 6f, stud.Y - 5f, w, StripBadgeHeight);
        bool alert = _model.FindTab(_model.Settings.Tabs[index].Id)?.Alert == true;
        ctx.FillRoundedRect(pill.X, pill.Y, pill.W, pill.H, pill.H / 2f,
            alert ? ChatDeckPalette.BadgeRed : ChatDeckPalette.BadgeGold);
        DrawLabel(ctx, face, badge, pill.X + (pill.W - textW) / 2f, pill,
            alert ? ChatDeckPalette.TextInk : ChatDeckPalette.BadgeInk);
    }

    internal static float PeekRowHeight(ChatDeckAppearance appearance) => appearance.FontSize + 10f;

    /// <summary>Beside the strip, its bottom lined up with the strip's.</summary>
    private void PlacePeek(UiRoot root, DeckRect strip)
    {
        ChatDeckAppearance appearance = _model?.Settings.Appearance ?? new ChatDeckAppearance();
        float w = MathF.Min(PeekMaxWidth, MathF.Max(0f, root.Width - ChatDeckStripGeometry.Width - 2 * PeekGap));
        float h = ChatDeckPeekFeed.MaxLines * PeekRowHeight(appearance);
        Peek.Width = w;
        Peek.Height = h;
        Peek.Left = _drawer.Edge == ChatDeckEdge.Left ? strip.X + strip.W + PeekGap : strip.X - PeekGap - w;
        Peek.Top = MathF.Max(0f, strip.Y + strip.H - h - PeekGap);
    }

    private void DrawPeek(UiRenderContext ctx, float width, float height)
    {
        IReadOnlyList<ChatDeckPeekFeed.Entry> lines = _peek.Lines;
        if (lines.Count == 0) return;
        ChatDeckAppearance appearance = _model?.Settings.Appearance ?? new ChatDeckAppearance();
        var face = new DeckFace(_fonts?.Face(appearance.FontSize, BundledUiFontWeight.Regular));
        float row = PeekRowHeight(appearance);
        float y = height;
        for (int i = lines.Count - 1; i >= 0; i--)   // newest at the bottom
        {
            y -= row;
            float alpha = ChatDeckPeekFeed.Alpha(lines[i].Age);
            string text = Fit(ctx, face, lines[i].Text, width - 2 * PeekPadX);
            float w = face.Measure(ctx, text) + 2 * PeekPadX;
            float x = _drawer.Edge == ChatDeckEdge.Left ? 0f : width - w;
            ctx.FillRoundedRect(x, y + 1f, w, row - 2f, 5f, ChatDeckPalette.FieldFill with { W = 0.75f * alpha });
            Vector4 ink = lines[i].Line.LineColour is uint argb ? ChatDeckTranscript.Argb(argb) : ChannelColour(lines[i].Line, appearance);
            DrawLabel(ctx, face, text, x + PeekPadX, new DeckRect(x, y, w, row), ink with { W = ink.W * alpha });
        }
    }

    /// <summary>The strip: presses on its studs, a tooltip naming the tab under the pointer.</summary>
    private sealed class StripElement : UiElement
    {
        private readonly UiChatDeck _deck;
        private float _hoverX = -1f;
        private float _hoverY = -1f;
        private ChatDeckHit _tipFor;

        public StripElement(UiChatDeck deck)
        {
            _deck = deck;
            RuntimeTooltipTextSource = () => _deck.StripTooltipAt(_hoverX, _hoverY);
        }

        public override bool HandlesClick => true;
        public override bool ReceivesHoverMouseMove => true;

        protected override bool OnHitTest(float localX, float localY)
        {
            // A press anywhere on the strip is the strip's, never a window or world drag.
            CapturesPointerDrag = true;
            return _deck._strip.Frame.Contains(localX, localY);
        }

        public override bool OnEvent(in UiEvent e)
        {
            switch (e.Type)
            {
                case UiEventType.MouseDown:
                    _deck.PressStrip(_deck._strip.HitAt(e.Data1, e.Data2));
                    return true;
                case UiEventType.Click:
                    return true;
                case UiEventType.HoverEnter:
                case UiEventType.MouseMove:
                    _hoverX = e.Data1;
                    _hoverY = e.Data2;
                    ChatDeckHit hit = _deck._strip.HitAt(_hoverX, _hoverY);
                    if (hit != _tipFor)
                    {
                        _tipFor = hit;
                        NotifyTooltipTextChanged();
                    }
                    return false;
                case UiEventType.HoverLeave:
                    _hoverX = _hoverY = -1f;
                    _tipFor = default;
                    return false;
                default:
                    return false;
            }
        }

        protected override void OnDraw(UiRenderContext ctx) => _deck.DrawStrip(ctx);
    }

    /// <summary>The peek lines; never takes input.</summary>
    private sealed class PeekElement(UiChatDeck deck) : UiElement
    {
        protected override bool OnHitTest(float localX, float localY) => false;

        protected override void OnDraw(UiRenderContext ctx) => deck.DrawPeek(ctx, Width, Height);
    }
}
```

In `UiChatDeck.cs`, call `CreateStrip();` at the end of the constructor, and add the two press stubs to `UiChatDeck.Strip.cs` for now (Task 7 replaces them):

```csharp
    private void PressStripTab(int index) { }
    private void PressStripPin() { }
```

If `UiEventType.HoverEnter` / `HoverLeave` / `MouseMove` are `int` constants (they are, in `UiEvent.cs`), the `case` labels above compile as written. If `CanvasCornerRadii` is not visible from `AcDream.App.UI.Hud`, add `using AcDream.App.UI;` (it is `internal` in the same assembly).

- [ ] **Step 4: Run the Verify command, expect PASS.**

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Hud/UiChatDeck.Strip.cs src/AcDream.App/UI/Hud/UiChatDeck.cs tests/AcDream.App.Tests/UI/Hud/UiChatDeckStripTests.cs
git commit -m "Chat Deck: strip and peek elements

Claude-Session: https://claude.ai/code/session_017hCEwjbupaVz5o7Zx5rwfZ"
```

---

### Task 7: Collapse, pin and placement

**Goal:** The pin and collapse studs and the strip's ⇥ work: the deck hides itself and shows the strip on the nearer edge, pins back where it flew out, saves pinned/edge through the model, restores them at login, turns window drag and resize off while not pinned, and counts lines as unread while collapsed.

**Files:**
- Create: `src/AcDream.App/UI/Hud/UiChatDeck.Drawer.cs`
- Modify: `src/AcDream.App/UI/Hud/UiChatDeck.cs` (`OnShown`, `OnHidden`, `Refresh`, `OnEvent`), `UiChatDeck.Draw.cs` (pin stud lit only while pinned), `UiChatDeck.Strip.cs` (remove the two stubs)
- Test: `tests/AcDream.App.Tests/UI/Hud/UiChatDeckDrawerTests.cs`

**Acceptance Criteria:**
- [ ] Collapse stud on a pinned deck at (10, 670): deck `Visible == false`, strip visible at x 0 with the deck's top, `Model.Settings.Layout` = { Pinned false, Edge Left }.
- [ ] Deck whose centre is right of the screen middle collapses to the right edge (strip x = screen − 34).
- [ ] Pin stud on a pinned deck collapses too; the pin stud draws lit only while pinned.
- [ ] Strip ⇥: deck visible at Left 34 and the strip's top, strip hidden, `Layout.Pinned` true, `Resizable` and `Draggable` back on.
- [ ] While not pinned, `Resizable == false` and `Draggable == false`.
- [ ] A new model whose saved layout is { Pinned false, Edge Right } comes up collapsed on the right.
- [ ] `OnHidden` hides strip and peek; `OnShown` brings back the collapsed state (deck hidden again, strip shown).
- [ ] A line to the selected tab while collapsed raises its unread count.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~UiChatDeck' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-strip tests/AcDream.App.Tests/AcDream.App.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** `UiChatDeckDrawerTests.cs`

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

/// <summary>The deck mounted on a 1600×900 root with its strip and peek, shown as the switch shows it.</summary>
internal sealed class DrawerWorld
{
    public readonly UiRoot Root = new() { Width = 1600, Height = 900 };
    public readonly UiChatDeckTests.Harness H;

    public DrawerWorld(float left = 10f, float top = 670f, ChatDeckSettings? settings = null)
    {
        H = new UiChatDeckTests.Harness();
        if (settings is not null)
        {
            H.Model = new ChatDeckModel(H.Log, settings, "Davian");
            H.Deck.Refresh();
        }
        RetailWindowFrame.Mount(Root, H.Deck, _ => (0u, 0, 0), UiChatDeck.MountOptions(left, top) with { Visible = true });
        Root.AddChild(H.Deck.Strip);
        Root.AddChild(H.Deck.Peek);
        H.Deck.OnShown();
    }

    public UiChatDeck Deck => H.Deck;

    public void Press(DeckRect r) => H.Press(r);

    public void PressStrip(DeckRect r) =>
        Deck.Strip.OnEvent(new UiEvent(0, Deck.Strip, UiEventType.MouseDown, Data1: (int)(r.X + 2), Data2: (int)(r.Y + 2)));

    public void Tick(double seconds) => Deck.TickDrawer(seconds);
}

public sealed class UiChatDeckDrawerTests
{
    [Fact]
    public void Collapse_HidesTheWindow_ShowsTheStripOnTheNearerEdge()
    {
        var w = new DrawerWorld();
        w.Press(w.Deck.Geometry.CollapseStud);

        Assert.False(w.Deck.Visible);
        Assert.True(w.Deck.Strip.Visible);
        Assert.Equal(0f, w.Deck.Strip.Left);
        Assert.Equal(w.Deck.Top, w.Deck.Strip.Top, 1);
        Assert.False(w.H.Model!.Settings.Layout.Pinned);
        Assert.Equal(ChatDeckEdge.Left, w.H.Model.Settings.Layout.Edge);
        Assert.False(w.Deck.Resizable);
        Assert.False(w.Deck.Draggable);
    }

    [Fact]
    public void RightHalf_CollapsesRight()
    {
        var w = new DrawerWorld(left: 1100f, top: 100f);
        w.Press(w.Deck.Geometry.CollapseStud);
        Assert.Equal(1600f - ChatDeckStripGeometry.Width, w.Deck.Strip.Left);
        Assert.Equal(ChatDeckEdge.Right, w.H.Model!.Settings.Layout.Edge);
    }

    [Fact]
    public void PinStud_WhilePinned_Collapses()
    {
        var w = new DrawerWorld();
        w.Press(w.Deck.Geometry.PinStud);
        Assert.Equal(ChatDeckDrawerState.Collapsed, w.Deck.Drawer.State);
    }

    [Fact]
    public void StripPin_PinsBesideTheEdge()
    {
        var w = new DrawerWorld();
        w.Press(w.Deck.Geometry.CollapseStud);
        w.PressStrip(w.Deck.StripGeometry.PinStud);

        Assert.True(w.Deck.Visible);
        Assert.False(w.Deck.Strip.Visible);
        Assert.Equal(ChatDeckStripGeometry.Width, w.Deck.Left);
        Assert.Equal(w.Deck.Strip.Top, w.Deck.Top, 1);
        Assert.True(w.H.Model!.Settings.Layout.Pinned);
        Assert.True(w.Deck.Resizable);
        Assert.True(w.Deck.Draggable);
        Assert.Equal(1f, w.Deck.Opacity);
    }

    [Fact]
    public void SavedCollapsedLayout_ComesBackAtLogin()
    {
        ChatDeckSettings saved = ChatDeckDefaults.Create() with
        {
            Layout = new ChatDeckLayoutState { Pinned = false, Edge = ChatDeckEdge.Right },
        };
        var w = new DrawerWorld(settings: saved);
        w.Tick(0.016);
        Assert.Equal(ChatDeckDrawerState.Collapsed, w.Deck.Drawer.State);
        Assert.False(w.Deck.Visible);
        Assert.Equal(1600f - ChatDeckStripGeometry.Width, w.Deck.Strip.Left);
    }

    [Fact]
    public void ModernHudOff_HidesTheStrip_OnBringsTheCollapsedStateBack()
    {
        var w = new DrawerWorld();
        w.Press(w.Deck.Geometry.CollapseStud);

        w.Deck.Visible = false;     // what the switch's hide does
        w.Deck.OnHidden();
        Assert.False(w.Deck.Strip.Visible);
        Assert.False(w.Deck.Peek.Visible);

        w.Deck.Visible = true;      // what the switch's show does
        w.Deck.OnShown();
        Assert.False(w.Deck.Visible);
        Assert.True(w.Deck.Strip.Visible);
    }

    [Fact]
    public void LinesWhileCollapsed_AreUnread()
    {
        var w = new DrawerWorld();
        w.Press(w.Deck.Geometry.CollapseStud);
        w.H.Say("hello");
        w.Tick(0.016);
        Assert.Equal(1, w.H.Model!.FindTab(ChatDeckDefaults.AllTabId)!.Unread);
    }
}
```

(`Harness.Model` is a public field; if it is `readonly`, make it settable in `UiChatDeckTests.Harness`. If `RetailWindowFrame.Options` is not a record that supports `with`, mount with `MountOptions` and then call `Root.ShowWindow(WindowNames.ChatDeck)`.)

- [ ] **Step 2: Run, expect a compile failure** (`TickDrawer` missing).

- [ ] **Step 3: Create `UiChatDeck.Drawer.cs`**

```csharp
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiChatDeck
{
    private bool? _frameDraggable;

    private void CreateDrawer() =>
        _drawer.LayoutChanged += () => _model?.SetLayout(_drawer.Pinned, _drawer.Edge);

    /// <summary>The runtime calls this every frame: a hidden window gets no OnTick.</summary>
    internal void TickDrawer(double deltaSeconds)
    {
        if (!_shown) return;
        if (!Visible) Refresh();   // the strip's badges follow the model while the window is hidden
        ChatDeckDrawerState before = _drawer.State;
        _peek.Advance(deltaSeconds);
        _drawer.Tick(deltaSeconds, ReadDrawerInput());
        AfterDrawerMoved(before);
        ApplyDrawer();
    }

    private void PressPin()
    {
        if (_drawer.Pinned) CollapseToEdge();
        else PinWhereItFliesOut();
    }

    private void PressCollapse()
    {
        if (_drawer.Pinned) CollapseToEdge();
        else CloseFlyout();
    }

    private void PressStripTab(int index)
    {
        SelectTab(index);
        OpenFlyout(ChatDeckOpenReason.Stud);
    }

    private void PressStripPin() => PinWhereItFliesOut();

    internal void CollapseToEdge()
    {
        if (FindRoot() is not { } root) return;
        _drawer.Collapse(ChatDeckDrawer.NearestEdge(Left, Width, root.Width));
        LeaveDeck();
        ApplyDrawer();
    }

    /// <summary>Pins the window where the fly-out puts it, beside the edge, and has the layout record that place.</summary>
    private void PinWhereItFliesOut()
    {
        if (_drawer.Pinned || FindRoot() is not { } root) return;
        DeckRect strip = ChatDeckStripGeometry.Place(_drawer.Edge, Top, Height, _labels.Count, root.Width, root.Height);
        Left = ChatDeckStripGeometry.FlyoutLeft(_drawer.Edge, Width, root.Width, 1);
        Top = strip.Y;
        _drawer.Pin();
        ApplyDrawer();
        ResetAnchorCapture();
        root.NotifyWindowMoved(this);   // after Pin: the capture gate is open again
    }

    private void OpenFlyout(ChatDeckOpenReason reason)
    {
        if (_drawer.State != ChatDeckDrawerState.Collapsed) return;
        _drawer.Open(reason);
        _peek.Clear();
        ApplyDrawer();
    }

    private void CloseFlyout()
    {
        ChatDeckDrawerState before = _drawer.State;
        _drawer.Close();
        AfterDrawerMoved(before);
        ApplyDrawer();
    }

    private void AfterDrawerMoved(ChatDeckDrawerState before)
    {
        if (before == ChatDeckDrawerState.Open && _drawer.State == ChatDeckDrawerState.Collapsed) LeaveDeck();
        if (before == ChatDeckDrawerState.Collapsed && _drawer.State == ChatDeckDrawerState.Open) _peek.Clear();
    }

    /// <summary>The window is leaving the screen: its menus close and its fields give the keyboard back.</summary>
    private void LeaveDeck()
    {
        _tabMenu.Close(restoreFocus: false);
        OnInputHidden();   // UiChatDeck.Input.cs: chip menu, input and search focus
    }

    /// <summary>Puts the window, the strip and the peek where the drawer's state says.</summary>
    internal void ApplyDrawer()
    {
        UiRoot? root = FindRoot();
        if (!_shown || root is null)
        {
            Strip.Visible = false;
            Peek.Visible = false;
            return;
        }

        _frameDraggable ??= Draggable;
        Resizable = _drawer.Pinned;
        Draggable = _drawer.Pinned && _frameDraggable.Value;
        Visible = _drawer.DeckVisible;
        Opacity = _drawer.State == ChatDeckDrawerState.Open ? (float)_drawer.Slide : 1f;
        Strip.Visible = _drawer.StripVisible;
        if (!_drawer.StripVisible)
        {
            Peek.Visible = false;
            return;
        }

        DeckRect s = ChatDeckStripGeometry.Place(_drawer.Edge, Top, Height, _labels.Count, root.Width, root.Height);
        Strip.Left = s.X;
        Strip.Top = s.Y;
        Strip.Width = s.W;
        Strip.Height = s.H;
        LayOutStrip();

        if (_drawer.State == ChatDeckDrawerState.Open)
        {
            Left = ChatDeckStripGeometry.FlyoutLeft(_drawer.Edge, Width, root.Width, _drawer.Slide);
            Top = s.Y;
            ResetAnchorCapture();
        }

        PlacePeek(root, s);
        Peek.Visible = _drawer.State == ChatDeckDrawerState.Collapsed && _peek.Lines.Count > 0;
    }

    private ChatDeckDrawerInput ReadDrawerInput()
    {
        if (FindRoot() is not { } root) return default;
        float x = root.MouseX;
        float y = root.MouseY;
        bool overStrip = Strip.Visible && Inside(Strip.Left, Strip.Top, Strip.Width, Strip.Height, x, y);
        bool overDeck = Visible && Inside(Left, Top, Width, Height, x, y);
        UiElement? focus = root.KeyboardFocus;
        bool focused = ReferenceEquals(focus, Input) || ReferenceEquals(focus, SearchField);
        bool busy = Transcript.IsSelecting || _tabMenu.IsOpen || _chipMenu.IsOpen || IsInside(root.Captured);
        return new ChatDeckDrawerInput(overStrip, overDeck, focused, Input.Text.Length == 0, !Transcript.Scroll.AtEnd, busy);
    }

    private static bool Inside(float left, float top, float width, float height, float x, float y) =>
        x >= left && x < left + width && y >= top && y < top + height;

    private bool IsInside(UiElement? element)
    {
        for (UiElement? e = element; e is not null; e = e.Parent)
            if (ReferenceEquals(e, this)) return true;
        return false;
    }
}
```

Remove the two press stubs from `UiChatDeck.Strip.cs`.

- [ ] **Step 4: Hook it into `UiChatDeck.cs`.**

Constructor, after `CreateStrip();`: `CreateDrawer();`

`OnShown`:

```csharp
    public void OnShown()
    {
        _shown = true;
        ApplyDrawer();
        _model?.SetView(_drawer.DeckVisible, Transcript.Scroll.AtEnd);
    }
```

`OnHidden` — close a fly-out first, and hide the strip and peek at the end:

```csharp
    public void OnHidden()
    {
        _shown = false;
        _drawer.Close();
        // A hidden element gets no ticks, so Refresh will not tell the model until it is shown again.
        _model?.SetView(false, Transcript.Scroll.AtEnd);
        _tabMenu.Close(restoreFocus: false);
        OnInputHidden();           // UiChatDeck.Input.cs
        ApplyDrawer();
    }
```

`Refresh` — when the model changes, follow its `LinesDelivered` (used in Task 8), drop the peek and take its saved layout; and report visibility through the drawer. Replace the model-change block and the last line:

```csharp
        if (!ReferenceEquals(model, _model))
        {
            if (_model is not null)
            {
                _model.Cleared -= ResetView;
                _model.LinesDelivered -= OnLinesDelivered;
            }
            _model = model;
            if (_model is not null)
            {
                _model.Cleared += ResetView;
                _model.LinesDelivered += OnLinesDelivered;
                _drawer.Restore(_model.Settings.Layout.Pinned, _model.Settings.Layout.Edge);
            }
            _peek.Clear();
            ResetView();
        }
        ...
        _model?.SetView(_shown && _drawer.DeckVisible, Transcript.Scroll.AtEnd);
        if (_shown && model is not null && !ReferenceEquals(_appliedFor, model))
        {
            _appliedFor = model;
            ApplyDrawer();
        }
```

with a field `private ChatDeckModel? _appliedFor;` (placing once per model, so a login lands collapsed without waiting for the next drawer tick). Add a no-op handler for now in `UiChatDeck.Drawer.cs` (Task 8 fills it):

```csharp
    private void OnLinesDelivered(IReadOnlyList<ChatDeckDelivery> deliveries) { }
```

`ResetView` also clears the peek: add `_peek.Clear();`.

`Detach` (in `UiChatDeck.Input.cs`): also `if (_model is not null) _model.LinesDelivered -= OnLinesDelivered;`.

`OnEvent` `MouseDown` switch:

```csharp
                    case ChatDeckZone.PinStud: PressPin(); return true;
                    case ChatDeckZone.CollapseStud: PressCollapse(); return true;
```

`UiChatDeck.Draw.cs`: `DrawStud(ctx, _geometry.PinStud, HudIcons.Pin, on: _drawer.Pinned);`

- [ ] **Step 5: Add `IsSelecting` to `UiText`** (`src/AcDream.App/UI/UiText.cs`, next to `_selecting`):

```csharp
    /// <summary>A drag-select is under way.</summary>
    internal bool IsSelecting => _selecting;
```

- [ ] **Step 6: Run the Verify command, expect PASS** (all `UiChatDeck*` tests, old and new).

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.App/UI/Hud/UiChatDeck*.cs src/AcDream.App/UI/UiText.cs tests/AcDream.App.Tests/UI/Hud/UiChatDeckDrawerTests.cs tests/AcDream.App.Tests/UI/Hud/UiChatDeckTests.cs
git commit -m "Chat Deck: collapse to the edge strip and pin back

Claude-Session: https://claude.ai/code/session_017hCEwjbupaVz5o7Zx5rwfZ"
```

---

### Task 8: Fly-out triggers, closing and peek

**Goal:** The fly-out opens on hover, on every chat-entry action and on a strip tab stud, closes by the spec's rules (and at once after a send or Esc when a chat-entry action opened it), and the peek shows peekable lines while collapsed.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/UiChatDeck.Input.cs` (entry methods, `Send`, `OnEscape`), `UiChatDeck.Drawer.cs` (`OnLinesDelivered`, `RevealForEntry`, `EndEntry`)
- Test: `tests/AcDream.App.Tests/UI/Hud/UiChatDeckFlyoutTests.cs`

**Acceptance Criteria:**
- [ ] Pointer on the strip: still collapsed at 0.2 s, open by 0.31 s; after another 0.12 s the window sits at Left 34 with `Opacity` 1.
- [ ] Pointer away from an open fly-out: still open at 0.5 s, collapsed by 0.61 s, window hidden.
- [ ] Text in the input keeps a fly-out open with the pointer away.
- [ ] `EnterChatMode`, `ToggleChatEntry`, `StartCommand`, `StartTell`, `StartReply`, `Focus` on a collapsed deck each open it (reason Entry) and focus the input.
- [ ] After `EnterChatMode`, submitting a line sends it and closes the fly-out at once with focus dropped; `Input.OnEscape` does the same; a hover-opened fly-out stays open after Esc.
- [ ] A strip tab stud opens the fly-out on that tab.
- [ ] Collapse stud on a fly-out closes it; pin stud on a fly-out pins it in place (Left stays 34).
- [ ] While collapsed, a Local line shows in the peek (`Peek.Visible`), a System line does not; after 6 s it is gone and the peek hidden; opening the fly-out empties the peek; a line arriving while the fly-out is open does not peek.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~UiChatDeck' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-strip tests/AcDream.App.Tests/AcDream.App.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** `UiChatDeckFlyoutTests.cs`

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiChatDeckFlyoutTests
{
    private static DrawerWorld Collapsed()
    {
        var w = new DrawerWorld();
        w.Press(w.Deck.Geometry.CollapseStud);
        w.Root.OnMouseMove(800, 100);   // away from everything
        return w;
    }

    private static void PointAtStrip(DrawerWorld w) =>
        w.Root.OnMouseMove((int)(w.Deck.Strip.Left + 2), (int)(w.Deck.Strip.Top + 4));

    [Fact]
    public void Hover_OpensAfterTheDelay_AndSlidesBesideTheStrip()
    {
        var w = Collapsed();
        PointAtStrip(w);
        w.Tick(0.2);
        Assert.Equal(ChatDeckDrawerState.Collapsed, w.Deck.Drawer.State);
        w.Tick(0.11);
        Assert.Equal(ChatDeckDrawerState.Open, w.Deck.Drawer.State);
        Assert.True(w.Deck.Visible);
        w.Tick(0.13);
        Assert.Equal(ChatDeckStripGeometry.Width, w.Deck.Left);
        Assert.Equal(1f, w.Deck.Opacity);
    }

    [Fact]
    public void PointerAway_ClosesAfterTheDelay()
    {
        var w = Collapsed();
        PointAtStrip(w);
        w.Tick(0.31);
        w.Root.OnMouseMove(800, 100);
        w.Tick(0.5);
        Assert.Equal(ChatDeckDrawerState.Open, w.Deck.Drawer.State);
        w.Tick(0.11);
        Assert.Equal(ChatDeckDrawerState.Collapsed, w.Deck.Drawer.State);
        Assert.False(w.Deck.Visible);
    }

    [Fact]
    public void ADraft_KeepsItOpen()
    {
        var w = Collapsed();
        PointAtStrip(w);
        w.Tick(0.31);
        w.Deck.Input.SetText("half a line");
        w.Root.OnMouseMove(800, 100);
        w.Tick(2.0);
        Assert.Equal(ChatDeckDrawerState.Open, w.Deck.Drawer.State);
    }

    public static TheoryData<string> EntryActions() =>
        ["EnterChatMode", "ToggleChatEntry", "StartCommand", "StartTell", "StartReply", "Focus"];

    [Theory]
    [MemberData(nameof(EntryActions))]
    public void EveryChatEntryAction_FliesOutAndFocusesTheInput(string action)
    {
        var w = Collapsed();
        switch (action)
        {
            case "EnterChatMode": w.Deck.EnterChatMode(null); break;
            case "ToggleChatEntry": w.Deck.ToggleChatEntry(null); break;
            case "StartCommand": w.Deck.StartCommand(); break;
            case "StartTell": w.Deck.StartTell("Bo"); break;
            case "StartReply": w.Deck.StartReply("Bo"); break;
            case "Focus": Assert.True(w.Deck.Focus()); break;
        }
        Assert.Equal(ChatDeckDrawerState.Open, w.Deck.Drawer.State);
        Assert.Equal(ChatDeckOpenReason.Entry, w.Deck.Drawer.OpenedBy);
        Assert.Same(w.Deck.Input, w.Root.KeyboardFocus);
    }

    [Fact]
    public void Send_ClosesAnEntryFlyoutAtOnce()
    {
        var w = Collapsed();
        w.Deck.EnterChatMode(null);
        w.Deck.Input.SetText("hello");
        w.Deck.Input.OnSubmit!("hello");
        Assert.Single(w.H.Bus.Published);
        Assert.Equal(ChatDeckDrawerState.Collapsed, w.Deck.Drawer.State);
        Assert.Null(w.Root.KeyboardFocus);
    }

    [Fact]
    public void Escape_ClosesAnEntryFlyout_NotAHoverOne()
    {
        var w = Collapsed();
        w.Deck.EnterChatMode(null);
        w.Deck.Input.OnEscape!();
        Assert.Equal(ChatDeckDrawerState.Collapsed, w.Deck.Drawer.State);

        PointAtStrip(w);
        w.Tick(0.31);
        w.Deck.Input.OnEscape!();
        Assert.Equal(ChatDeckDrawerState.Open, w.Deck.Drawer.State);
    }

    [Fact]
    public void StripStud_OpensOnThatTab()
    {
        var w = Collapsed();
        w.PressStrip(w.Deck.StripGeometry.Studs[w.H.TabIndex("group")]);
        Assert.Equal(ChatDeckDrawerState.Open, w.Deck.Drawer.State);
        Assert.Equal("group", w.H.Model!.SelectedTabId);
    }

    [Fact]
    public void FlyoutStuds_CollapseCloses_PinPinsInPlace()
    {
        var w = Collapsed();
        w.Deck.EnterChatMode(null);
        w.Press(w.Deck.Geometry.CollapseStud);
        Assert.Equal(ChatDeckDrawerState.Collapsed, w.Deck.Drawer.State);

        w.PressStrip(w.Deck.StripGeometry.Studs[0]);
        w.Tick(0.2);
        w.Press(w.Deck.Geometry.PinStud);
        Assert.True(w.Deck.Pinned);
        Assert.Equal(ChatDeckStripGeometry.Width, w.Deck.Left);
    }

    [Fact]
    public void Peek_ShowsPeekLinesWhileCollapsed_ThenFades()
    {
        var w = Collapsed();
        w.H.Say("hello");                                     // Local tab: peek on
        w.H.Log.OnSystemMessage("You feel refreshed.", (uint)RetailLogTextType.System);
        w.H.Model!.Tick();
        w.Tick(0.016);
        Assert.Single(w.Deck.PeekLines);
        Assert.True(w.Deck.Peek.Visible);

        w.Tick(6.0);
        Assert.Empty(w.Deck.PeekLines);
        Assert.False(w.Deck.Peek.Visible);
    }

    [Fact]
    public void Flyout_ClearsThePeek_AndLinesWhileOpenDoNotPeek()
    {
        var w = Collapsed();
        w.H.Say("hello");
        w.Deck.EnterChatMode(null);
        Assert.Empty(w.Deck.PeekLines);
        w.H.Say("while open");
        Assert.Empty(w.Deck.PeekLines);
    }
}
```

(`Harness.Say` ticks the model and refreshes the deck; `Harness.Bus.Published` records each send.)

- [ ] **Step 2: Run, expect the new tests to fail** (entry actions focus a hidden input, no peek).

- [ ] **Step 3: Fill in `UiChatDeck.Drawer.cs`**

```csharp
    private void OnLinesDelivered(IReadOnlyList<ChatDeckDelivery> deliveries)
    {
        if (_drawer.State == ChatDeckDrawerState.Collapsed && _model is { } model)
            _peek.Add(deliveries, model.Settings.Tabs);
    }

    /// <summary>A chat-entry action on a collapsed deck: fly out first, so the input it focuses is on screen.</summary>
    private void RevealForEntry()
    {
        if (_shown) OpenFlyout(ChatDeckOpenReason.Entry);
    }

    /// <summary>A line was sent or Esc left the input: a fly-out a chat-entry action opened closes now.</summary>
    private void EndEntry()
    {
        ChatDeckDrawerState before = _drawer.State;
        _drawer.EntryDone();
        AfterDrawerMoved(before);
        ApplyDrawer();
    }
```

- [ ] **Step 4: Wire the input** in `UiChatDeck.Input.cs`.

In `CreateInputRow`, after `Input.OnSubmit = Send;`:

```csharp
        Input.OnEscape = EndEntry;
```

`Send` ends with `EndEntry();` after `SubmitTo`.

Each `IChatEntryTarget` method starts with `RevealForEntry();`:

```csharp
    public void EnterChatMode(KeyChord? physicalChord)
    {
        RevealForEntry();
        UiRoot? root = FindRoot();
        ...
    }

    public void ToggleChatEntry(KeyChord? physicalChord)
    {
        if (FindRoot() is not { } root) return;
        bool focused = ReferenceEquals(root.KeyboardFocus, Input);
        if (!focused) RevealForEntry();
        root.SetKeyboardFocus(focused ? null : Input);
        if (physicalChord is { Device: 0 } chord)
            root.SuppressPhysicalKeyUntilRelease(chord.Key);
        if (focused) EndEntry();
    }

    public void StartCommand()
    {
        RevealForEntry();
        Input.SetText("/");
        FindRoot()?.SetKeyboardFocus(Input);
    }

    public void StartTell(string name)
    {
        RevealForEntry();
        Input.SetText($"@tell {name}, ");
        FindRoot()?.SetKeyboardFocus(Input);
    }

    public bool Focus()
    {
        if (FindRoot() is not { } root) return false;
        RevealForEntry();
        root.SetKeyboardFocus(Input);
        return true;
    }
```

(`StartReply` goes through `StartTell`.)

- [ ] **Step 5: Run the Verify command, expect PASS.** (With *Stay in chat mode* on, `UiField` only skips its own focus drop after `Submit()`; it never re-focuses, so the focus `LeaveDeck` dropped stays dropped and the closed fly-out does not hold the keyboard.)

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/UI/Hud/UiChatDeck*.cs tests/AcDream.App.Tests/UI/Hud/UiChatDeckFlyoutTests.cs
git commit -m "Chat Deck: fly-out on hover, chat entry and studs; peek lines

Claude-Session: https://claude.ai/code/session_017hCEwjbupaVz5o7Zx5rwfZ"
```

---

### Task 9: Runtime mount and layout gate

**Goal:** `RetailUiRuntime` puts the strip on the interface root and the peek on its own overlay layer, ticks the drawer every frame after the model, and closes the Chat Deck's layout capture while it is not pinned, so fly-out moves never reach the saved layout and a pin records the new place.

**Files:**
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs` (`MountChatDeck`, `Tick`, persistence setup)
- Modify: `tests/AcDream.App.Tests/UI/Hud/ChatDeckWiringTests.cs`

**Acceptance Criteria:**
- [ ] After mount, `deck.Strip.Parent` is the root and `deck.Peek.Parent` is a `UiOverlayLayer` named `ChatDeckPeek` that is visible.
- [ ] A failed mount removes the strip and the peek as well as the window.
- [ ] `Tick` calls `_chatDeck?.TickDrawer(deltaSeconds)` right after `_chatDeckSession?.Tick()`.
- [ ] Wiring test: collapse, fly out (Left 34), `SaveAll` → saved Chat Deck X is still 10; pin from the strip, `SaveAll` → saved X is 34.
- [ ] Wiring test: while collapsed, `Switch.Tick()` leaves the deck hidden, the strip shown and the retail chat hidden; `Switch.Apply(false)` hides the strip and brings the retail chat back.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ChatDeck|FullyQualifiedName~ModernHudSwitch' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-strip tests/AcDream.App.Tests/AcDream.App.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing wiring tests.** In `ChatDeckWiringTests.World`, after `RetailWindowFrame.Mount(...)` add `Root.AddChild(Deck.Strip); Root.AddChild(Deck.Peek);`, and after `Persistence.RestoreAll();` add the gate exactly as the runtime will:

```csharp
            Persistence.SetCaptureGate(WindowNames.ChatDeck, () => Deck.Pinned);
```

Add the tests:

```csharp
    private UiWindowLayout SavedDeck() =>
        new SettingsStore(_path).LoadWindowLayout("Alice", "1600x900", WindowNames.ChatDeck, default)!.Value;

    private static void PressDeck(UiChatDeck deck, DeckRect r) =>
        deck.OnEvent(new UiEvent(0, deck, UiEventType.MouseDown, Data1: (int)(r.X + 2), Data2: (int)(r.Y + 2)));

    [Fact]
    public void FlyoutMoves_StayOutOfTheLayout_PinRecordsTheNewPlace()
    {
        var w = new World(_path);
        w.Switch.Apply(true);
        w.Persistence.SaveAll();
        Assert.Equal(10f, SavedDeck().X);

        PressDeck(w.Deck, w.Deck.Geometry.CollapseStud);
        w.Deck.EnterChatMode(null);
        w.Deck.TickDrawer(0.2);
        Assert.Equal(ChatDeckStripGeometry.Width, w.Deck.Left);
        w.Persistence.SaveAll();
        Assert.Equal(10f, SavedDeck().X);

        w.Deck.Strip.OnEvent(new UiEvent(0, w.Deck.Strip, UiEventType.MouseDown,
            Data1: (int)(w.Deck.StripGeometry.PinStud.X + 2), Data2: (int)(w.Deck.StripGeometry.PinStud.Y + 2)));
        w.Persistence.SaveAll();
        Assert.Equal(ChatDeckStripGeometry.Width, SavedDeck().X);
    }

    [Fact]
    public void Collapsed_SurvivesTheSwitchTick_AndOffBringsTheRetailChatBack()
    {
        var w = new World(_path);
        w.Switch.Apply(true);
        PressDeck(w.Deck, w.Deck.Geometry.CollapseStud);

        w.Switch.Tick();
        Assert.False(w.Deck.Visible);
        Assert.True(w.Deck.Strip.Visible);
        Assert.False(w.Chat.Visible);

        w.Switch.Apply(false);
        Assert.False(w.Deck.Strip.Visible);
        Assert.True(w.Chat.Visible);
    }
```

If the first `SaveAll` does not record X = 10 because the window has not been captured yet, call `w.Root.NotifyWindowMoved(w.Deck)` once before it (as a player's first move would); keep the rest unchanged.

- [ ] **Step 2: Run, expect `FlyoutMoves_...` to fail only if the gate or the pin notification is wrong; both tests should compile.** (The World now mirrors the runtime wiring; the runtime change follows.)

- [ ] **Step 3: Mount the strip and peek** in `MountChatDeck`, right after `RetailWindowFrame.Mount(...)`:

```csharp
            Host.Root.AddChild(deck.Strip);
            Layout.UiOverlayLayer peekLayer = OverlayHost.AddLayer("ChatDeckPeek");
            peekLayer.Visible = true;   // click-through; the peek element shows itself when it has lines
            peekLayer.AddChild(deck.Peek);
```

and in the `catch` block, before `deck.Detach();`:

```csharp
                deck.Strip.Parent?.RemoveChild(deck.Strip);
                deck.Peek.Parent?.RemoveChild(deck.Peek);
```

(If `UiElement.Parent` is not typed with a `RemoveChild` method, cast: `(deck.Strip.Parent as UiElement)?.RemoveChild(deck.Strip)`; `Host.Root.RemoveChild` is what the window removal above already uses.)

- [ ] **Step 4: Tick the drawer** in `Tick`, right after `_chatDeckSession?.Tick();`:

```csharp
        // After the model, so a line delivered this frame can peek this frame.
        _chatDeck?.TickDrawer(deltaSeconds);
```

- [ ] **Step 5: Close the layout capture while not pinned**, next to the Spell Deck's gate:

```csharp
            // The saved window geometry is the pinned window's; a fly-out's place is the strip's.
            _persistence.SetCaptureGate(WindowNames.ChatDeck, () => _chatDeck?.Pinned ?? true);
```

- [ ] **Step 6: Run the Verify command, expect PASS.**

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.App/UI/RetailUiRuntime.cs tests/AcDream.App.Tests/UI/Hud/ChatDeckWiringTests.cs
git commit -m "Chat Deck: mount the strip and peek, tick the drawer, gate the layout

Claude-Session: https://claude.ai/code/session_017hCEwjbupaVz5o7Zx5rwfZ"
```

---

### Task 10: Full suite and live check

**Goal:** The whole Mac suite is green on the branch, then the user checks the strip, peek, fly-out and pin on the local ACE server.

**Files:**
- Modify: `docs/superpowers/plans/2026-10-27-chat-deck-strip.md` (task status, on `docs/chat-deck-spec`)

**Acceptance Criteria:**
- [ ] `run-mac-tests.sh` on the worktree reports 0 failures (paste the summary line).
- [ ] Live, after the user says they are available, on 10.10.20.20: collapse left and right; studs and badges match the tabs; tooltips name the tabs; hover opens after a short pause and closes after leaving; Enter opens, sending closes at once; Esc closes; a stud opens on its tab; ⇥ and the pin stud pin beside the edge; peek shows Local and Group lines and tells, fades in about 6 s; collapsed state, edge and window place survive a relog; Modern HUD off/on with the deck collapsed.
- [ ] Anything the live check finds is fixed on the branch with a test where one can be written, and the suite rerun.

**Verify:** `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-strip` → 0 failed

**Steps:**

- [ ] **Step 1: Run the full suite** (no filter). On a failure, fix it (systematic-debugging) and rerun until green.
- [ ] **Step 2: Ask the user whether they are available for the live check. Do not launch the client before they say yes.** Build and launch per `~/OpenAC-dev/dev-client.env` (local ACE only), Modern HUD on.
- [ ] **Step 3: Walk the acceptance list with the user;** record each result in the plan's task status.
- [ ] **Step 4: Commit the status** on `docs/chat-deck-spec` (`docs: Chat Deck plan 3 task status ...`). Merging into main and rynth is the finishing step (finishing-a-development-branch), after the user's choice.
