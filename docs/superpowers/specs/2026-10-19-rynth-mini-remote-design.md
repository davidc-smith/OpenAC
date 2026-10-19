# RynthCore Mini Remote in OpenAC — design

Date: 2026-10-19. Branch `feat/rynth-mini-remote`. Next dashboard panel after the Dungeon Map.

## Goal

The RynthAi dashboard's Char menu item "Mini Remote" (and `/rc miniremote`) opens RynthCore's own
Mini Remote window: the sections rail down the left, and on Remote the engine's quick-use slot grid.

## What the user sees

Our pinned RynthAi (RynthSuite e6fffb0, 2026-10-09) exports neither `RynthPluginRenderEmbed` nor
`RynthPluginGetSectionsJson`, so:

- **Remote**: RynthCore's own slot grid (their fallback when RynthAi doesn't draw the remote). Each
  slot shows the item's icon and carried count; click uses it; on an empty slot click puts the selected
  inventory item there; right-click: Set to selected, Clear, More / Fewer slots, Bigger / Smaller boxes;
  Ctrl+wheel resizes boxes; a status row shows the use waiting for the server or how the last one ended.
  Slots and box size are saved per character (their `ItemHudStore`, already compiled for Item Count).
  Dragging items onto slots does nothing (`UiDropTargets` stand-in, as today).
- **Rail**, in RynthCore's order, each section filled by the engine panels that exist here:
  Session Stats (their "needs RynthAi" overview text), Map (Dungeon Map), Skills (Skills),
  Combat (Damage), Nav (Nav), Inventory (Item Count, Items as sub-tabs), Chat (Chat, when RynthChat runs).
  Crafting, Spells, Friends, Fellowship, Pets show faded with RynthCore's own empty notes.
- Right-click menu: sections, "Show the sections rail" toggle; no server items.

## Compiled unchanged (from `external/RynthCore/src/RynthCore.Engine`)

- `ImGui/Panels/MiniRemoteFace.cs`, `ImGui/Panels/MiniRemoteFace.Sections.cs`
- `UI/Sections/SectionCatalog.cs`, `SectionManifest.cs`, `SectionRegistry.cs`, `ServerForce.cs`,
  `ServerForceStore.cs`, `NotLoadedServers.cs`
- `Compatibility/IltDetect.cs`
- Extracted by `tools/RynthTypeExtract` from `Compatibility/InventoryModel.cs`: `InventoryActionKind`,
  `InventoryRequest`, `InventoryPending`, `InventoryResult` (added to the existing `=...` item list).

`ServerSelection*.cs`, `ServerStateJson.cs` and the real `ServerMenus.cs` are not compiled.

## Stand-ins / host work

1. **Panel host embedding** (`src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiPanelHost.cs`), as
   RynthCore's host: `CanEmbed(title)` (registered and not "Mini Remote"), `TryGetDefaultSize`,
   `DrawEmbedded(hostKey, title, size)` drawing a separate copy of the face (created with `OnShown` on
   first draw) in a child window, with RynthCore's padding/flags rules, error line and 3-failure stop;
   `DrawAll` sweeps copies not drawn for 750 ms (`OnHidden`). While a copy draws, `RequestSize`,
   `RequestPos` and `DragWindowWithLastItem` are ignored (`[ThreadStatic] _inEmbed`). `Reset` drops copies.
2. **`InventoryModel` item use** (`DashboardStandIns.cs`): `Request`, `Pending`, `LastResult` and a
   game-thread `MainThreadTick` (called from `RynthAiDashboard.Tick`). Only `Use` is supported (the only
   kind the Mini Remote sends); other kinds finish "Not available in OpenAC.". Behaviour follows
   RynthCore's `InventoryModel`: one action at a time, refusal texts identical; held while the client is
   busy (`BusyCount > 0`) up to 10 s ("Not sent: the game stayed busy with another item action. Try
   again."); sent through the same `UseObject` path RynthAi's slot uses (wieldables go through Equip);
   watched at each tick: a WeenieError since the send → "Refused: Using X (error 0x..)"; a UseDone →
   "Used X." / "Couldn't use X (error 0x..)."; item gone or its container/wielder/stack changed →
   "Used X."; 6 s with nothing → "No answer seen for: Using X. You can act again.". Results carry
   `Stopwatch` ticks as theirs. Host hooks (set by `RynthChatHookup`, unset on Dispose):
   `InventoryModel.UseSender` (`Func<uint,bool>` → `IRynthGameView.UseObject`), `Busy` (`Func<bool>` →
   `BusyCount > 0`), `Replies` (→ `ServerReplies` as a tuple), `ReadItem` (container, wielder, stack;
   false when gone), `PlayerId`.
3. **Server/plugin stand-ins**: `PluginManager.CollectSectionManifests()` (empty),
   `SkippedServerPlugins` (empty list of a `SkippedServerPlugin` stand-in), `RequestServerPluginLoad`
   (no-op); `IltInfo` (`ServerId` "ilt", `IsIlt` false, `Detected` = not ILT "OpenAC: no ILT plugins",
   `Invalidate` no-op) with `IltVerdict`; `ServerInfo` gains `ServerId` "aelrynth", `Detected`
   (`ServerVerdict` not Aelrynth), `Invalidate`, `LaunchHost` ""; `AccountHooks.AnnouncedWorldName`
   (host-set delegate → `IRynthGameView.WorldName`); `RynthLog.Milestone`/`Compat` if missing.
   Both built-in servers come out inactive; no server groups show. `ServerMenus` stays the stand-in.
4. **Registration and ticking**: `RynthAiDashboard.Start` registers `MiniRemoteFace`;
   `RynthAiDashboard.Tick` runs `SectionRegistry.Tick()` and `InventoryModel.MainThreadTick()`
   (game thread = the overlay draw thread in OpenAC).
5. **`/rc miniremote`**: the host registers chat verb `rc`. A stand-in `RynthCoreChatCommands` handles
   `miniremote|remote [show|hide|toggle|tab <tab>|section <id>]` exactly as RynthCore's branch does
   (their `MovedTabCommand` "character" → `/ro character`); any other `/rc` replies
   "/rc <verb> isn't in OpenAC (only /rc miniremote)." The dashboard's Char → "Session Stats" without
   RynthOracle (`ServerMenus.Run("/rc miniremote section char")`) then works.

No new host slots, no `IRynthGameView` changes, no contract bump.

## Testing

`tests/AcDream.Rynth.Host.Tests` (headless ImGui):
- Embedding: copy drawn inside the host window, separate instance from the open window, `OnShown`
  once, swept with `OnHidden` after 750 ms idle, size/pos requests from inside ignored, Mini Remote not
  embeddable, a throwing face shows its error after 3 failures.
- InventoryModel: refusal while pending; held while busy, sent when free, dropped after 10 s; UseDone
  ok / error; WeenieError refusal; item gone; 6 s timeout; non-Use kinds; host hookup set/unset.
- Sections: with the dashboard started, `SectionRegistry` lists the 12 core sections; Map holds
  Dungeon Map, Inventory holds Item Count then Items; no active servers.
- Mini Remote: registered by `Start`; draws the grid with no throw; rail selection embeds Skills;
  `/rc miniremote section map` opens it on Map; bad section prints their usage line.

Live check (user, later): open from Char menu, set/use slots (a healing kit, a wand → wields), rail
sections draw their panels, rail hide/show, relog keeps slots.

## Out of scope

RynthAi's own remote (needs a RynthSuite bump with `RenderEmbed`), plugin section surfaces, server
groups/ILT tabs, item drag-and-drop onto slots, pop-outs.
