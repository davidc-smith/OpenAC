# RynthCore Hotkeys in OpenAC — design

Date: 2026-10-20. Branch: `feat/rynth-hotkeys` (from main 035a7867).

## Goal

Settings > Hotkeys on the RynthAi dashboard works as in RynthCore: the player binds a
key with Ctrl/Alt/Shift to a built-in action (macro, subsystem toggles, a panel, VTank-style
actions) or any chat command, and pressing it in the world runs it. `/rc hotkey ...` works.

## Approach

Compile RynthCore's own hotkey code unchanged (the no-rewrite rule):
`Hotkeys/HotkeyModel.cs`, `Hotkeys/HotkeyService.cs`, `ImGui/Panels/HotkeysSettingsUi.cs`.
Their `HotkeyService` reads Win32 key messages from AC's WndProc and can eat one keystroke;
OpenAC feeds it the same messages from the overlay keyboard, and the client lets an overlay
sink take one key press so OpenAC's own bindings never see it.

Rejected: a stand-in `HotkeyService` over the plugin `IHotkeyRegistry` — no client change,
but OpenAC-clashing chords are refused, only `PluginKey` keys exist, and their capture and
action code would be rewritten.

## 1. Client (AcDream.App, AcDream.ClientExtensions)

### Sink key presses

`IOverlayKeyboardSink` gains a default method, so RynthChat's sink is untouched:

```csharp
/// <summary>A key press or release; true when this sink takes it (the client then never sees it).</summary>
bool KeyPress(OverlayKeyEvent e) => false;
```

`OverlayKeyEvent(int VirtualKey, OverlayModifiers Modifiers, bool Down, bool Repeat, bool Extended)`.
`IOverlayKeyboardSink` also gains `void FocusLost() { }`. `OverlayModifiers` gains `Alt = 4` (Option on macOS). Control still includes Command on macOS.
The client maps Silk `Key` to a Win32 virtual-key code in one table (`OverlayVirtualKeys`):
letters, digits, F1-F24, numpad digits/operators (`VK_NUMPAD0..`, `VK_ADD`...), punctuation
(`VK_OEM_*`), arrows/Home/End/PageUp/PageDown/Insert/Delete (Extended = true), left/right
Shift/Control/Alt as `VK_LSHIFT`... (their `HotkeyMatch.NormalizeVk` folds them). Keys with no
VK map to 0 (their code ignores 0). The existing `KeyDown(OverlayKey, ...)`, `KeyUp`,
`Character` calls stay as they are.

Repeat: Silk raises no repeat flag, so the overlay marks a key-down as a repeat when that key
is already down in its own held-key set.

### One decision per key press

Silk's keyboard events are multicast to the input dispatcher (`SilkKeyboardSource`, which
also feeds `AppHotkeyRegistry`), the retained-UI binding (`RetainedKeyboardInputBinding`) and
the overlay feed (`ImGuiInputFeed`). Silk calls handlers in subscription order, so the
decision is made by one handler subscribed before all of them:

- `OverlayKeyGate` (App/Extensions/Overlay) subscribes to every keyboard's KeyDown/KeyUp and
  the window's focus right after GameWindow creates the input context (before any other
  keyboard subscriber exists). The overlay owns it (`ImGuiOverlay.AttachKeyGate`) and binds
  it to `OverlayInputGate`; the overlay's own start (later, with the GPU) does not affect it.
- On each key-down the gate offers the press to every sink's `KeyPress` (modifiers read from
  the keyboard; a repeat is a key-down for a key the gate already holds down) and latches
  "taken" for that key; on its key-up it offers the release, and that key-up is taken when
  the press was or a sink takes it.
- `OverlayInputGate.KeyTaken(Key)` / `CharTaken` are checked by `SilkKeyboardSource`
  (KeyDown/KeyUp, and `IsHeld`, which movement polls) and `RetainedKeyboardInputBinding`
  (down/up/char). `CharTaken` is true while any taken key is down. `ImGuiInputFeed` is
  unchanged (sinks still get `KeyDown`/`Character` as now).
- Window focus loss clears every latch and calls each sink's new `FocusLost()` (default no-op).

An untaken key passes exactly as today.

### Is the player typing

`ClientExtensionContext.Input` (new `IClientInput`, null-object default) with
`bool IsTyping` — true while the chat entry is active (`chatEntry.IsInputActive`, the same
source as `BindChatInputActive`) or the retained UI has a keyboard focus element
(`UiRoot.KeyboardFocus is not null`), and `string TypingDescription` for `/rc hotkey status`.

### Conflicts

As in RynthCore, a Rynth hotkey wins over a client binding on the same key. Their defaults
F6/F7/F8 (heal/stamina/mana top-off), seeded once into the file, take over OpenAC's F6
(Spell Components) and F8 (Attributes) as they do AC's in retail. Their Settings tab already
warns when a key is one of AC's defaults (`HotkeyConflicts`). Agreed 2026-10-20.

## 2. Host (AcDream.Rynth.Host, AcDream.Rynth.EngineUi)

### Compiled unchanged

`HotkeyModel.cs`, `HotkeyService.cs`, `HotkeysSettingsUi.cs` are added to
`AcDream.Rynth.EngineUi.csproj`; the `HotkeysSettingsUi` stand-in in
`StandIns/DashboardStandIns.cs` is deleted.

### Redirects

`HotkeyService`'s user32 P/Invokes are redirected at build time by RynthInteropRedirect (as
the clipboard is) to `StandIns/Win32KeyStateStandIn`; the tool learns to take several stand-in
types, comma-separated, instead of one:

- `GetKeyState(vk)`: 0x8000 for Control/Menu/Shift (and their L/R codes) when the host's last
  key event had that modifier, else 0.
- `GetForegroundWindow()`: a non-zero handle while the client window has focus, else zero.
- `GetWindowThreadProcessId(hwnd, out pid)`: our process id for that handle.

The key state and focus are set by the hotkey sink below (game thread, same as their WndProc).

### Stand-ins (only those the compile needs and are missing)

- `AcKeyboardFocus.HasTextFocus()` → `IClientInput.IsTyping`; `Describe()` →
  `TypingDescription`.
- `EngineLifecycle.SignalReload()` / `EntryPoint.NoteManualReload()` (the "reload" action):
  chat line "[RynthCore] Reload isn't in OpenAC."; no reload.
- `AcMainThreadQueue.EnqueueChatCommand(text)`: posted to the UI hub pump (next tick, not
  inside the keystroke) and run through `AcMainThreadQueue.ChatCommand`, which the host points
  at the same runner as `ServerMenus.Runner`.
- `IltInfo.PluginLoaded` => false. (A trial compile on 2026-10-20 showed these five are all
  that is missing; `RynthLog` and `UiBackgroundWriter` already exist.)

### Key feed

New `src/AcDream.Rynth.Host/RynthHotkeyKeyboard.cs` (an `IOverlayKeyboardSink`, added beside
`RynthChatKeyboard`) turns each `OverlayKeyEvent` into their message and calls
`HotkeyService.HandleKeyMessage(msg, (IntPtr)vk, lParam, rynthTextFocus)`:

- msg: `WM_KEYDOWN`/`WM_KEYUP`, or `WM_SYSKEYDOWN`/`WM_SYSKEYUP` while Alt is held.
- lParam: repeat count 1; bit 24 for Extended; bit 30 (previous state down) for a repeat;
  bits 30-31 for a key-up.
- `Character(c)` → `WM_CHAR`; window focus loss → `WM_KILLFOCUS`.
- `rynthTextFocus`: the overlay's `WantsKeyboard` (ImGui text input, or RynthChat capturing).
- Its return value is `KeyPress`'s.

### Commands

The `RynthCoreChatCommands` stand-in routes `/rc hotkey <args>` to
`HotkeyService.HandleCommand(args)` (each line replied) and `/rc hotkeys` to Settings opened
on the Hotkeys tab, as RynthCore does. Usage text lists the new verbs.

### File

At start (off Windows, in `RynthInstallFolders.Apply`, beside RYNTHCORE_DIR) the host sets `RYNTHCORE_HOTKEYS_FILE=<RYNTHCORE_DIR>/hotkeys.txt` (their own
override variable) unless already set, so the file sits with the other Rynth files.

### Actions

Their `HotkeyService.Run`: RynthAi toggles via `RynthAiCommands` (UI hub pump), panels via
`PanelRouter`, chat commands via `AcMainThreadQueue.EnqueueChatCommand` (reaches plugins and
the client parser as now). Hotkeys fire only after login (their gate reads
`LoginLifecycleHooks.HasObservedLoginComplete`).

## 3. Testing

Client (AcDream.App.Tests):
- Silk → VK table: letters, digits, F-keys, numpad, L/R modifiers, extended keys, unmapped → 0.
- Gate: a taken press withholds the dispatcher (and so plugin hotkeys), the retained UI and
  movement polling for that key, its repeats, chars and key-up; an untaken press passes;
  focus loss clears and reaches the sinks.
- `IsTyping` follows chat entry and UI focus.
- RynthChat's sink still gets `KeyDown`/`Character` unchanged.

Host (Rynth.Host.Tests; every test sets `RYNTHCORE_HOTKEYS_FILE` to a sandbox file):
- Ctrl+F1 → `/ra help`: fires once on first press, the press, its repeats, char and key-up are
  taken; an unbound key passes.
- Not fired while typing, while ImGui wants text, or before login.
- Capture: `BeginCapture` + a key → `TryTakeCapture` returns the combo; Esc cancels.
- A panel action toggles that panel.
- `/rc hotkey add|list|remove` replies and updates the file.
- The Hotkeys settings tab draws headless (vertices, no exceptions).

RynthInteropRedirect.Tests: the three user32 imports point at the stand-in.

Live check (user): Settings > Hotkeys lists F6/F7/F8; bind Ctrl+1 → `/ra help`, it fires in
world and Ctrl+1 does nothing else; with the chat entry open the key types instead; F8 does
mana, not Attributes; a panel toggle binding; Alt (Option) combo on macOS; `/rc hotkey list`.

## Out of scope

Drop targets (the next sub-project), engine reload, ILT panels (no Infinite Leaftide).
No plugin contract change: `IOverlayKeyboardSink` and `ClientExtensionContext` are
client-extension interfaces, so the fork contract version stays 0.1.26-fork.2.
