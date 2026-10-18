# RynthInventory in OpenAC — design

Date: 2026-10-18. Status: approved (architecture section; the user asked for the plan
before the remaining sections were reviewed — they are recorded here as decided).

## Goal

Host RynthSuite's RynthInventory plugin in OpenAC, as RynthTracker and RynthOracle are
hosted: `/ginv` opens a "Global Inventory" script window that searches every character's
items — packs, worn and wielded items, and storage the character opens (house storage,
banks, vaults, lockers).

## Decisions

- **Carried items and storage** are both in scope.
- **No auto-identify.** RynthCore auto-appraises inventory; OpenAC does not, and will not
  for this (it is server traffic, i.e. automation). Item spells show for items the player
  has identified; scrolls, gems and potions still show their single spell (DID 28).
- **Save location is the plugin's default**, `ApplicationData/RynthCore/inventory`
  (`~/.config/RynthCore/inventory` on macOS). The submodule stays untouched, and the plugin
  has no public way to take another root.
- **Vault feed** (`/mule-data`, an Aelrynth server mod) is left as is: it needs the
  chat-parser slot, which is filled, and does nothing on a server without the mod.
- The chat-window eat flag stays ignored (existing host behaviour); only the vault feed
  would eat lines.

## Architecture

1. **Wrapper project `src/AcDream.Rynth.Plugin.RynthInventory`** — plain `net10.0`,
   compiles the submodule's sources unchanged (like the RynthOracle wrapper), embeds
   `RynthCore.Plugin.RynthAi/Combat/SpellData.txt` as `RynthInventory.SpellData.txt`.
   Referenced by `AcDream.Rynth.Host` and listed in `AcDream.slnx`.
2. **`IRynthGameView` grows** by a few members, all read from `GameRuntime` in
   `RuntimeRynthGameView`:
   - `bool TryGetObject(uint id, out RynthObjectInfo info)` — one record of the typed
     `ClientObject` fields the slots need (container, wielder, equip location, item type,
     wcid, bitfield, icon/underlay/overlay, spell id, house owner, stack, stack max,
     value, burden, capacity, structure, max structure, workmanship, material, identified,
     appraised spell ids) plus read-only views of its Int/DataId/InstanceId property
     dictionaries. The player's own record takes its property dictionaries from
     `CharacterOwner.LocalPlayer` first (as `TryGetInt64Property` does).
   - `IReadOnlyList<uint> GetContainerContents(uint containerId)` — contained items
     (`Objects.GetContents`) **plus** items it wields (`Objects.GetEquippedBy`), sorted by
     id, matching RynthCore (the scanner finds equipped items only this way).
   - `string AccountName`, `string WorldName` — from `CharacterSelection.Snapshot`.
   - `bool TryGetObjectPosition(uint id, out uint cell, out Vector3 origin)` — the
     entity's physics body cell position.
3. **`RynthApiTable` fills 13 more slots**: `GetObjectIntProperty`,
   `GetObjectDataIdProperty`, `GetObjectInstanceIdProperty`, `GetContainerContents`,
   `GetObjectOwnershipInfo`, `GetObjectWielderInfo`, `GetItemType`, `GetObjectWcid`,
   `GetObjectBitfield`, `HasAppraisalData`, `GetObjectSpellIds`, `GetObjectPosition`,
   `GetCurCoords`, `GetAccountName`, `GetWorldName`. Stat-number → field mapping lives in
   a small pure class (`RynthObjectProperties`) so it is unit-tested without the table:
   - Int: 1 Type, 5 Burden, 6 ItemsCapacity, 10 CurrentlyEquippedLocation, 11 StackSizeMax,
     12 StackSize, 19 Value, 91 MaxStructure, 92 Structure, 105 Workmanship (rounded),
     131 MaterialType (when known); else the Ints dictionary.
   - DataId: 8 Icon, 50 IconOverlay, 52 IconUnderlay (true with 0 = none), 28 SpellId
     (when known); else the DataIds dictionary.
   - InstanceId: 2 Container, 3 Wielder, 32 HouseOwner (when known); else the dictionary;
     0 is "not set" (false).
   - Unknown objects answer false (ownership info especially), never zeros.
   - `GetCurCoords` uses RynthCore's formula on the player's cell position:
     EW = ((cell>>24)*8 + x/24 − 1019.5)/10, NS = (((cell>>16)&0xFF)*8 + y/24 − 1019.5)/10.
   - Strings returned to the plugin use the table's existing ANSI buffer approach (one
     buffer per string slot, valid until the next call).
   - Every function catches everything, logs, and returns 0/false, like the existing ones.
4. **Three new plugin callbacks**, `RynthPlugin` methods calling the exports by name
   (skipped when absent, inside a dispatch scope):
   - `OnViewObjectContents(id)` ← `InventoryOwner.ExternalContainers.Changed` with kind
     `Opened`.
   - `OnStopViewingObjectContents(id)` ← the same event with kind `Closed`.
   - `OnUpdateObjectInventory(id)` ← `Objects.ContainerContentsReplaced(id)`, and
     `Objects.ObjectMoved` for the previous and current container ids (non-zero, distinct).
   `RynthEventBridge` gets matching methods; `RynthClientExtension` subscribes and
   unsubscribes.
5. **`RynthClientExtension`** adds `RynthInventory.PluginExports` to the plugin list and
   registers `/ginv`, offering `"/ginv " + arguments` to the plugins as `/ro` does.

## Data flow

Login → `OnLoginComplete` → the plugin's scanner reads the player, walks
`GetContainerContents` (player → packs → nested packs) and the per-item slots, and writes
`<root>/<world>/<account>/<character>.json` from worker threads. Opening a chest →
`ViewContents` → `ExternalContainers.Changed(Opened)` → `OnViewObjectContents` → the
plugin decides whether it is storage and scans it on its own schedule; closing it →
`OnStopViewingObjectContents` → final scan. `/ginv` → `OnChatBarEnter` → window toggled on
the next tick and drawn through the existing script-window slots.

## Testing

- `RynthObjectProperties` unit tests: each mapped stat, the dictionary fallback, unknown
  object → false.
- `RynthApiTable` tests through `RynthCoreHost` with `FakeGameView` (which grows the new
  members): contents include wielded items, ownership for unknown objects is false,
  world/account names, coords formula.
- `RuntimeRynthGameView` tests over a real `GameRuntime` (contents + equipped union,
  identity names, player properties first).
- RynthInventory host tests: loads; `/ginv status` prints a line; a login with a player,
  a pack and an item writes a character file under a temporary `XDG_CONFIG_HOME`
  (tests are not parallel); opening/closing a storage container reaches the plugin.
- Extension test: `/ginv` registered; container events subscribed and released.
- Live check on local ACE (ask first): `/ginv` lists carried items; opening a storage
  chest adds it; a second character's items appear after logging it in.

## Out of scope

Auto-identify, a host-chosen save path, honouring the chat eat flag, RynthChat and
RynthVision.
