# RynthChat in OpenAC — design

Date: 2026-10-18. Status: approved.

## Goal

Host RynthSuite's RynthChat plugin in OpenAC together with RynthCore's own chat window
(the ImGui `ChatFace`): channel and custom tabs with unread counts, the filter rule
editor, line colours, search, drag-to-copy, and a working input line. The window opens
from the Rynth bar's "Chat" button and sits beside OpenAC's own chat window.

## Decisions

- **Their code, unchanged.** Both the plugin and the window are compiled straight from
  the submodules (`external/RynthSuite`, `external/RynthCore`). We never edit or copy
  their files; a submodule bump brings their daily changes. When a bump starts using an
  engine type we don't have, the build breaks in our wrapper project and the fix is one
  more stand-in member.
- **Full input now.** Typing into their input line works (Enter, characters, Esc,
  arrows, Home/End, Up/Down history, Ctrl+V/C/X, Tell, `/r` and `/rt`).
- **OpenAC's chat window stays.** `ChatHooks.RynthChatOwnsChat` is false, so nothing is
  hidden or suppressed.
- **Off in OpenAC:** Discord bridge, RynthNav "Arrow to" / "Go to" links (no RynthNav
  host), the RynthAi dashboard drawer state, Avalonia click-through.

## No Windows backend, on any OS

RynthCore's real `ImGui/Win32Backend.cs` and `Compatibility/ChatHooks.cs` exist to run
injected into retail `acclient.exe`: they subclass AC's window procedure and hook the
client's memory. OpenAC is not acclient — on Windows, macOS and Linux it is our own
Silk.NET/GLFW window. Neither file is compiled anywhere. Their chat code references
`Win32Backend.ChatCaptureActive` and the `Win32Backend.OnChat*` hooks by name, so our
stand-in carries that name, but it is a plain class of those fields with no P/Invoke and
no window procedure, fed by OpenAC's own keyboard input on every platform. Its doc
comment says so.

## Architecture

1. **`src/AcDream.Rynth.Plugin.RynthChat`** — plain `net10.0` wrapper compiling
   `RynthCore.Plugin.RynthChat/**/*.cs` unchanged, like the Tracker and Inventory
   wrappers. Referenced by `AcDream.Rynth.Host`, listed in `AcDream.slnx`. The host
   already delivers chat lines (`RynthEventBridge.OnChat` → `RynthPluginOnChatWindowText`)
   and runs its `/` lines (`InvokeChatParser`); its two extra exports,
   `RynthChatGetScrollbackJson` and `RynthChatSendLine`, are found by name like every
   other export.
2. **`src/AcDream.Rynth.ChatWindow`** (new) — compiles RynthCore's chat window
   unchanged: `ImGui/Panels/ChatFace.cs`, `ChatFiltersFace.cs`, `FaceKit.cs`,
   `DashboardDrawers.cs`, `UI/Data/Chat*.cs`, `UI/Data/UiSource.cs`,
   `UI/Data/UiSources.cs`, `UI/Panels/RynthChatPresets.cs`, `Compatibility/TellMemory.cs`,
   plus self-contained engine helpers the compiler asks for (`RynthTheme`, `SafeImGui`,
   and `PanelRouter`/`ImGuiPopOuts` if they are self-contained; otherwise stand-ins).
   Warnings-as-errors and analyzers off for this project only, as in ScriptWindows.
3. **Shared stand-ins.** Engine types used by both ScriptWindows and the chat window
   (`IImGuiPanel`, `PanelSpec`, `UiFont`, `ImGuiFonts`, `ImGuiPanelHost`, `RynthLog`, …)
   live in one place so there is one definition of each. They grow to fit their usage
   (`UiFont.Ui9/Ui10/Ui14/Dash9-11`, `ImGuiFonts.Chat`, `PanelSpec.EdgeToEdge`,
   `ImGuiPanelHost.Register/HasFace/BodyGripReserve`). The chat window project's own
   `StandIns/` holds the chat-only ones.
4. **Stand-ins that reach our host** (set by `AcDream.Rynth.Host` at start-up):
   - `PluginExportBinder.Resolve` → the host's export lookup over loaded plugins.
   - `ClientHelperHooks.GetPlayerId`, `SelectedTargetHooks.ReadCurrentSelectedId`,
     `ClientObjectHooks.TryGetObjectName/TryGetItemType`,
     `SessionStateRegistry.LastCharacterName` → `IRynthGameView`.
   - `PanelRouter.Toggle` → the panel host's toggle (Chat ↔ Filters).
5. **Stand-ins that are off:** `ChatHooks` (`SuppressOriginalChat` false,
   `RynthChatOwnsChat` false), `Discord.ChatDiscord` (disabled; `ChatDiscordFace` draws
   nothing), `RynthNavCommands`/`RynthNavFace` (no-ops), `RynthAiDashboardState`,
   `AvaloniaOverlay.ChatCtrlGatedClickThrough` (false).
6. **Window and layout.** The chat panel registers with `ImGuiPanelHost` so the bar's
   "Chat" button (Phosphor icon already mapped) opens it; position and open state go in
   the existing per-install layout store. Settings and history save where their code
   saves them, under the RynthCore application-data folder like the other plugins.

## Keyboard input

Their `ChatModel` fills `Win32Backend.OnChatChar/Backspace/Delete/Send/Cancel/Left/
Right/Home/End/Up/Down/Paste/Copy/Cut/TellSelected/CaptureActivated` and reads
`Win32Backend.ChatCaptureActive`. Our side:

- **Overlay seam.** `IClientOverlay` gains `IDisposable AddKeyboardSink(sink)`. A sink
  reports whether it is capturing and receives key-downs (with modifiers) and
  characters. While any sink captures, the overlay reports `WantsKeyboard`, so the game
  ignores the keys (the same path ImGui text fields already use).
- **Host sink.** While `ChatCaptureActive`, keys map to the `OnChat*` hooks exactly as
  `Win32Backend` maps them: Enter clears capture and runs `OnChatSend` on the next
  frame (their deferred dispatch), Esc clears capture and runs `OnChatCancel`, Ctrl+V /
  Ctrl+C / Ctrl+X, Shift+Insert / Ctrl+Insert, Backspace, Delete, arrows, Home, End.
- **Starting capture.** With the chat window open, Enter starts capture
  (`ChatCaptureActive = true`, `OnChatCaptureActivated`) instead of reaching OpenAC —
  only when no OpenAC text field has focus and no ImGui text input is active. Clicking
  their input box starts it too (their own code).
- **Clipboard** goes through ImGui's clipboard, which works on all three platforms.

## Testing

- Plugin wrapper: builds from the submodule; lines reach it; scrollback export returns
  them.
- Chat window: `ChatData` picks up lines through the `PluginExportBinder` stand-in and
  sends through `RynthChatSendLine`; stand-ins never throw.
- Keyboard: the sink maps each key to its `OnChat*` hook; the game sees no keys while
  capture is active; Enter activates only with the window open and nothing else
  focused; Esc returns keys to the game; send runs on the following frame.
- Source check: `AcDream.Rynth.ChatWindow` compiles the listed submodule files and has
  no local copies of them.
- Tests sandbox HOME, XDG and `CFFIXED_USER_HOME` like the other plugin tests.
- Live check on local ACE (ask first): tabs fill; a filter rule and a colour persist
  across a restart; typing and sending a line and a `/` command work; Esc leaves the
  game keys working; OpenAC's chat window is unaffected.

## Out of scope

Discord bridge, RynthNav links, hiding OpenAC's chat window, the Avalonia chat panel.
