# Rynth script windows on an ImGui overlay: design

Status: approved design, not built. Date: 2026-10-17.
Repository: `davidc-smith/OpenAC-Rynth`, branch `feat/rynth-imgui`, which
builds on `feat/rynth-host-poc`. That branch's spec,
`2026-10-17-rynth-host-poc-design.md`, describes the host this builds on.

## Goal

Show RynthSuite plugin windows inside the OpenAC client with real Dear
ImGui, the way RynthCore shows them on Windows, and keep pulling their
updates unchanged. The test plugin is **RynthOracle**.

## Background

Most RynthSuite plugins don't call ImGui themselves. They use RynthCore
**script windows** (engine API v71):

- The plugin records an ImGui-style display list (format 1, about 36 ops)
  and sends it with `UiSubmit`.
- It reads clicks and values back with `UiPollEvents`.
- It asks what the engine supports with `UiGetInfo`.
- The engine parses the list (`DisplayListParser.cs`), keeps the newest list
  per window (`ScriptWindowRegistry.cs`) and replays it with ImGui every frame
  (`ScriptWindowReplay.cs`).
- These three files live in the RynthCore submodule under
  `src/RynthCore.Engine/`. They depend on ImGui.NET and on about eight small
  engine types.

OpenAC facts this relies on (upstream `6db05a2d`):

- **Rendering:**
  - Frames are drawn through an internal GPU layer (`src/AcDream.App/Rendering/Gpu`):
    `IGpuDevice`, `IGpuFrame.AllocateRing`, `BeginPass` and `IGpuPassEncoder`.
  - Textures are bindless (`RegisterTexture` gives a slot index for the
    shared push-constant block).
  - A pipeline accepts embedded SPIR-V (the `GpuShaderSet` name + bytes overload).
  - The UI draws a single-sample pass onto the backbuffer with `Load`. The
    swapchain is `B8G8R8A8_UNORM`.
  - Scissor and viewport are GL-style, with Y from the bottom.
- **Frame hook:** `IDevToolsFrameLifecycle` (`BeginFrame` before the
  world pass, `Render` after the retained UI, `AbortFrame`) exists and is
  wired to `null` in `FrameRootComposition` (about lines 777 and 780).
- **Input capture:** `IInputCaptureSource` already gates movement, hotkeys
  and mouse-look. `DevToolsInputCaptureSource` is a stub that returns false.
  The retained UI's Silk bindings (`RetainedUiInputBinding`) don't consult it.
- **Scale:** UI coordinates are window points. The swapchain is framebuffer
  pixels. `IWindow.Size` and `IWindow.FramebufferSize` give the scale.

## Approach

The overlay is a service inside AcDream.App (new files), offered to
extensions through the contract. The Rynth host compiles RynthCore's
script-window files unchanged, with small stand-ins for the engine types
they use.

### 1. The ImGui overlay in the client

**Contract** (`AcDream.ClientExtensions`, new):

```csharp
public interface IClientOverlay
{
    /// Called once per rendered frame between ImGui.NewFrame and ImGui.Render,
    /// on the render thread. Dispose to remove.
    IDisposable AddFrameCallback(Action draw);
    bool WantsMouse { get; }
    bool WantsKeyboard { get; }
}
```

`ClientExtensionContext` gains:

- `IClientOverlay? Overlay`: null when the overlay can't start.
- `Action<string> SubmitChatCommand`: runs text as if typed in the chat bar.
- (Settled while planning: no selection delegate is needed. The Rynth view
  reads `GameRuntime.ActionOwner.Selection.SelectedObjectId` from the
  runtime it already holds.)

**New App files** (`src/AcDream.App/Extensions/Overlay/`):

- `ImGuiOverlay`
  - Implements `IClientOverlay` and `IDevToolsFrameLifecycle`.
  - Owns the ImGui context.
  - Per frame it sets the display size (points), the framebuffer scale and
    the delta time, feeds input, runs the callbacks and calls `ImGui.Render`.
- `ImGuiVulkanRenderer`
  - Builds the font atlas once, at framebuffer scale with
    `FontGlobalScale = 1/scale`. It uploads the atlas in `BeginFrame`,
    before any pass opens.
  - Registers the atlas as a bindless texture.
  - Per frame it copies vertices (20 bytes: pos float2, uv float2, colour
    `UByte4Normalized`) and 16-bit indices into the frame ring.
  - It opens one pass on the backbuffer (`Target: null`, `Load`, sample
    count 1) and issues one `DrawIndexed` per ImGui command, with a scissor
    converted from the clip rect: pixels, Y from the bottom, clamped, empty
    rects skipped.
  - Embedded GLSL-compiled SPIR-V sits in our own files. It uses the
    shared push-constant block: an ortho transform via `uParamA/B`, texture
    via `uTextureIndexA`.
- `ImGuiInputFeed`
  - Subscribes to the Silk mouse and keyboard, including `KeyChar`, through
    the client's quiescence gate.
  - Queues events for the next frame.
  - Maps Silk keys to `ImGuiKey` and modifiers.

**Upstream edits (target: about 10 lines):**

- `FrameRootComposition`: pass the overlay as the dev-tools frame hook
  instead of `null`.
- `DevToolsInputCaptureSource` (or its construction in `GameWindow`):
  report the overlay's `WantsMouse` / `WantsKeyboard`.
- `RetainedUiInputBinding`: don't forward a mouse or keyboard event to
  `UiRoot` while the overlay wants it.
- `ClientExtensionHost` / `GameWindow`: build the overlay and pass it, the
  chat submitter and the selection into the context.
- `AcDream.App.csproj` + `Directory.Packages.props`: the ImGui.NET package,
  pinned to an exact version at least two weeks old. Regenerate the lock files.

**Fail-safe:** if the ImGui native library can't load or the renderer fails
to start, the overlay logs once and stays inert. (Settled while planning:
extensions attach before the GPU exists, so the overlay object is always
passed. When it never starts, its callbacks never run and script windows
simply don't draw. Plugins still see `HasUi`, because the table is built at
attach.) An exception inside a
frame callback is caught and logged, the callback is removed, and the frame
still ends balanced.

### 2. Script windows in the Rynth host

**New wrapper project `AcDream.Rynth.ScriptWindows`:**

- It compiles these files unchanged from `external/RynthCore/src/RynthCore.Engine/`:
  `UI/ScriptWindows/DisplayListParser.cs`,
  `UI/ScriptWindows/ScriptWindowRegistry.cs` and `ImGui/ScriptWindowReplay.cs`.
- It adds our stand-ins, in their namespaces:

| Their type | Our stand-in |
|---|---|
| `ImGuiPanelHost` (RegisterDynamic, Unregister, Open, Close, IsOpen, IsTransitioning, RequestSize, RequestPos, SetDisplayTitle, DrawBody) | Draws each open script window with `ImGui.Begin`/`End` around their body and keeps the open state; `End` always runs |
| `EngineFrameController.FontScale` | `1f` |
| `RynthLog.UI` | the host log |
| `ScriptIcons.TryGet` | returns false (their placeholder square draws) |
| `UiBackgroundWriter.Enqueue` | runs the work inline |
| `ImGuiTextFocus.NoteActive` | no-op |
| `PluginManager.CurrentDispatch` | the owner currently inside a UI call |
| `LoadedPlugin` | our owner record (plugin name) |
| `PanelStateStore` | in-memory only (positions aren't saved) |

Settled while planning: `ImGui/Panels/ScriptWindowFace.cs` (the window body
that calls the replay) also compiles unchanged, with one more tiny stand-in
(`ImGuiFonts.Get` returns the current font). The full stand-in list is
`RynthLog`, `UiBackgroundWriter`, `ImGuiTextFocus`, `ScriptIcons`,
`EngineFrameController`, `ImGuiFonts`/`UiFont`, `LoadedPlugin`,
`EngineSettings`, `LoginLifecycleHooks`, `UiInfoNative` (layout copied from
the engine), `PanelChrome`, `PanelSpec`, `IImGuiPanel` and
`ImGuiPanelHost`. The owner of a UI call is tracked by the host's own
dispatch scope (RynthCore's `PluginManager.CurrentDispatch` is only
mentioned in comments in these files).

If their files need more than this, the plan adds stand-ins in the same
pattern. A stand-in that would need real logic stops the work for a decision.

**New table slots** (`RynthApiTable`). They match RynthCore's
`Plugins/PluginContract.cs` behaviour and return codes:

- `UiSubmitFn`, `UiPollEventsFn`, `UiGetInfoFn`: call their registry for the
  calling plugin. They are set only when the context has an overlay, so
  `HasUi` is false otherwise.
- `InvokeChatParserFn`: `SubmitChatCommand`.
- `GetSelectedItemIdFn`: `GetSelectedObjectId`.

**Drawing:** the Rynth extension adds one overlay frame callback that
replays every visible script window.

**Chat commands into plugins:** the host claims each plugin's command word
(for this PoC, `ro`) and forwards `/ro …` to `RynthPluginOnChatBarEnter`.

**Plugins:** RynthTracker (existing) and RynthOracle. RynthOracle gets a new
wrapper project, `AcDream.Rynth.Plugin.RynthOracle`, in RynthTracker's pattern.

**Threading:** OpenAC updates and renders on one thread, so plugin calls and
replay don't overlap. Their registry is thread-safe regardless.

## Testing

Headless, with no GPU:

1. **Pipeline through ImGui:**
   - Create a headless ImGui context (fonts built, display size set).
   - Start RynthOracle on our table and check that `HasUi` is true.
   - Send `/ro` through `OnChatBarEnter` and tick until their registry
     holds a visible window with a list.
   - Run one ImGui frame with their replay and check the draw data has vertices.
   - Check that events flow back from ImGui to the plugin through
     `UiPollEvents`. The headless test does this by closing the window as a
     player would (the plugin then hides it, and it stays closed) and by
     checking that ImGui's geometry events reach the plugin. Clicking a
     specific button is left to the user's visual check, because locating
     a button's screen position headlessly would make the test brittle.
2. **Table:** the UI slots appear only with an overlay. Return codes match
   RynthCore's (`-100` without UI). `InvokeChatParser` and
   `GetSelectedItemId` reach the context.
3. **Renderer maths:** the clip rect maps to an OpenAC scissor at scale 1
   and 2, clamped to the framebuffer, and empty rects are skipped.
4. **Stand-ins:** the window host keeps `Begin`/`End` balanced when the body throws.
5. **Fail-safe:** with no overlay, `/ro status` still answers in chat.

Runs without the user (offline client, probe script):

- Type `/ro`, wait, close.
- The log shows the overlay starting (ImGui version, atlas size, scale) and
  Oracle loading.
- A frame summary appears about once a second while a window is open (draw
  lists, vertices).
- No exceptions. Screenshots are unavailable: upstream has the Retina
  capture bug.

User visual check:

- The Oracle window draws correctly, drags, and responds to clicks.
- Clicks and typing on it don't reach the game or retail panels.

## Done

- The headless tests pass.
- The offline run logs a running overlay and a drawn Oracle window with no errors.
- The user has seen the window work.
- The upstream diff stays at about 10 lines plus the package.

## Out of scope

AC icons in script windows, saved window positions, RynthCore's window bar,
RynthAi's direct-ImGui UI, and plugin-declared command words.
