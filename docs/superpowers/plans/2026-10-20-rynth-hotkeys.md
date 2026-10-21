# RynthCore Hotkeys Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Settings > Hotkeys on the RynthAi dashboard works as in RynthCore: bound keys run built-in actions or chat commands in the world, the key never reaching OpenAC, and `/rc hotkey ...` works.

**Architecture:** RynthCore's `HotkeyModel.cs`, `HotkeyService.cs` and `HotkeysSettingsUi.cs` compile unchanged into AcDream.Rynth.EngineUi; their three user32 P/Invokes are redirected to a key-state stand-in. The client gains a key gate subscribed to the keyboard before anything else: it offers every key press to overlay sinks (`IOverlayKeyboardSink.KeyPress`) and, when one takes it, keeps that key (repeats, chars, key-up) from OpenAC's dispatcher, retained UI and movement. The Rynth host's new `RynthHotkeyKeyboard` sink turns each press into the Win32 message their `HotkeyService.HandleKeyMessage` reads.

**Tech Stack:** .NET 10 (`~/.dotnet`), Silk.NET.Input 2.23, ImGui.NET, Mono.Cecil (RynthInteropRedirect), xUnit.

**Spec:** `docs/superpowers/specs/2026-10-20-rynth-hotkeys-design.md`

## Global Constraints

- RynthCore source is compiled unchanged (never edit files under `external/`). Missing engine types get stand-ins under `src/AcDream.Rynth.EngineUi/StandIns/` in the existing style (namespace = RynthCore's, short `<summary>` saying what OpenAC does instead).
- Build/test with `export PATH=$HOME/.dotnet:$PATH`. Never run `tools/update-package-locks.ps1`.
- Virtual-key codes are Win32 values (e.g. `VK_F1 = 0x70`, `VK_CONTROL = 0x11`). lParam bits: 24 = extended, 30 = previous state down (repeat / key-up), 31 = transition (key-up).
- `OverlayModifiers`: `Control = 1` (Ctrl or Command on macOS), `Shift = 2`, `Alt = 4` (Option on macOS).
- No plugin contract change; fork contract stays `0.1.26-fork.2`.
- Commit messages end with `Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz`.
- Work on branch `feat/rynth-hotkeys`.

**User decisions (already made):**
- Route A: compile their HotkeyService unchanged, redirect user32, extend the overlay sink (2026-10-20).
- A Rynth hotkey wins over an OpenAC binding on the same key; their F6/F7/F8 defaults take over OpenAC's F6 (Spell Components) and F8 (Attributes) (2026-10-20).
- Lua slash routing needs no work (test e3aef33c); ServerPick/ReportFace stand-ins stay; drop targets are the next sub-project.

---

## File map

| File | Change |
|---|---|
| `tools/RynthInteropRedirect/Program.cs` | stand-in argument takes a comma-separated list |
| `tests/AcDream.Tools.RynthInteropRedirect.Tests/RedirectPInvokeTests.cs` | new |
| `src/AcDream.ClientExtensions/IOverlayKeyboardSink.cs` | `OverlayModifiers.Alt`, `OverlayKeyEvent`, `KeyPress`, `FocusLost` |
| `src/AcDream.ClientExtensions/IClientInput.cs` | new: `IsTyping`, `TypingDescription` |
| `src/AcDream.ClientExtensions/ClientExtensionContext.cs` | `Input` property |
| `src/AcDream.App/Extensions/Overlay/OverlayVirtualKeys.cs` | new: Silk `Key` → VK |
| `src/AcDream.App/Extensions/Overlay/OverlayKeyGate.cs` | new: first-subscriber gate |
| `src/AcDream.App/Extensions/Overlay/OverlayInputGate.cs` | `KeyTaken`, `CharTaken`, `BindKeys` |
| `src/AcDream.App/Extensions/Overlay/ImGuiOverlay.cs` | `OfferKeyPress`, `SinkFocusLost`, `AttachKeyGate` |
| `src/AcDream.App/Input/SilkKeyboardSource.cs` | skip taken keys; `IsHeld` false for taken |
| `src/AcDream.App/UI/RetainedUiInputBinding.cs` | skip taken keys/chars |
| `src/AcDream.App/Extensions/ClientInputState.cs` | new: `IClientInput` over UI focus + chat entry |
| `src/AcDream.App/Extensions/ClientExtensionHost.cs` | pass `input` |
| `src/AcDream.App/Rendering/GameWindow.cs` | attach the gate at input creation; pass `ClientInputState` |
| `src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj` | compile their 3 files; redirect to 2 stand-in types |
| `src/AcDream.Rynth.EngineUi/StandIns/Win32KeyStateStandIn.cs` | new |
| `src/AcDream.Rynth.EngineUi/StandIns/HotkeyStandIns.cs` | new: `AcKeyboardFocus`, `EngineLifecycle` |
| `src/AcDream.Rynth.EngineUi/StandIns/DashboardStandIns.cs` | drop `HotkeysSettingsUi`; `EntryPoint.NoteManualReload`; `AcMainThreadQueue.EnqueueChatCommand` |
| `src/AcDream.Rynth.EngineUi/StandIns/SectionsStandIns.cs` | `IltInfo.PluginLoaded` |
| `src/AcDream.Rynth.EngineUi/StandIns/RynthCoreChatCommandsStandIn.cs` | `/rc hotkeys`, `/rc hotkey` |
| `src/AcDream.Rynth.Host/RynthInstallFolders.cs` | `RYNTHCORE_HOTKEYS_FILE` |
| `src/AcDream.Rynth.Host/RynthAiHookup.cs` | point `AcMainThreadQueue.ChatCommand` |
| `src/AcDream.Rynth.Host/RynthHotkeyKeyboard.cs` | new: the sink |
| `src/AcDream.Rynth.Host/RynthClientExtension.cs` | register the sink; typing source |
| tests in `tests/AcDream.App.Tests/Extensions/`, `tests/AcDream.Rynth.Host.Tests/` | new |

---

### Task 1: RynthInteropRedirect takes several stand-in types

**Goal:** The tool's stand-in argument accepts `TypeA,TypeB`, each P/Invoke redirected to whichever listed type has a matching public static method.

**Files:**
- Modify: `tools/RynthInteropRedirect/Program.cs` (usage text, `Run` around line 86, `Redirect` around lines 100-145)
- Create: `tests/AcDream.Tools.RynthInteropRedirect.Tests/RedirectPInvokeTests.cs`

**Acceptance Criteria:**
- [ ] With `App.ClipStandIn,App.KeyStandIn`, a user32 `GetKeyState` import redirects to `App.KeyStandIn::GetKeyState` and `OpenClipboard` to `App.ClipStandIn::OpenClipboard`.
- [ ] A single type (today's form) behaves as before; a listed type missing from the assembly fails with exit 1 naming it.
- [ ] Output reads `... N P/Invoke(s) redirected to App.ClipStandIn,App.KeyStandIn ...`.
- [ ] Existing RedirectMember/RedirectType tests still pass.

**Verify:** `dotnet test tests/AcDream.Tools.RynthInteropRedirect.Tests` → all pass (11 old + new).

**Steps:**

- [ ] **Step 1: Write the failing test**

```csharp
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace AcDream.Tools.RynthInteropRedirect.Tests;

/// <summary>
/// P/Invoke redirection to stand-in types over one small assembly built here (App):
/// two user32 imports on App.Native and two stand-in types, one per import.
/// </summary>
public sealed class RedirectPInvokeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rynth-pinvoke-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string AppPath => Path.Combine(_dir, "App.dll");

    [Fact]
    public void EachImportGoesToTheListedTypeThatHasIt()
    {
        Build();

        (int code, string output, string error) = Run("App.ClipStandIn,App.KeyStandIn");

        Assert.True(code == 0, error);
        Assert.Contains("2 P/Invoke(s) redirected to App.ClipStandIn,App.KeyStandIn", output, StringComparison.Ordinal);
        using ModuleDefinition app = Read();
        Assert.Equal(["System.Int16 App.KeyStandIn::GetKeyState(System.Int32)"], Calls(app, "GetKeyState"));
        Assert.Equal(["System.Boolean App.ClipStandIn::OpenClipboard(System.IntPtr)"], Calls(app, "OpenClipboard"));
    }

    [Fact]
    public void OneTypeStillWorks()
    {
        Build();

        (int code, string output, string error) = Run("App.KeyStandIn");

        Assert.True(code == 0, error);
        Assert.Contains("1 P/Invoke(s) redirected to App.KeyStandIn", output, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingListedTypeFailsAndNamesIt()
    {
        Build();

        (int code, _, string error) = Run("App.ClipStandIn,App.Nowhere");

        Assert.Equal(1, code);
        Assert.Contains("App.Nowhere is not in", error, StringComparison.Ordinal);
    }

    private static string[] Calls(ModuleDefinition app, string method) =>
        app.GetType("App.Native").Methods.Single(m => m.Name == method).Body.Instructions
            .Select(i => i.Operand).OfType<MethodReference>().Select(m => m.FullName).ToArray();

    private (int Code, string Output, string Error) Run(string standIns)
    {
        string references = Path.Combine(_dir, "refs.txt");
        File.WriteAllText(references, string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        int code = Program.Run([AppPath, standIns, references], output, error);
        return (code, output.ToString(), error.ToString());
    }

    private ModuleDefinition Read() =>
        ModuleDefinition.ReadModule(new MemoryStream(File.ReadAllBytes(AppPath)));

    private void Build()
    {
        using AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("App", new Version(1, 0)), "App", ModuleKind.Dll);
        ModuleDefinition app = assembly.MainModule;
        var user32 = new ModuleReference("user32.dll");
        app.ModuleReferences.Add(user32);

        var native = new TypeDefinition("App", "Native", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, app.TypeSystem.Object);
        app.Types.Add(native);
        native.Methods.Add(Import(app, user32, "GetKeyState", app.TypeSystem.Int16, app.TypeSystem.Int32));
        native.Methods.Add(Import(app, user32, "OpenClipboard", app.TypeSystem.Boolean, app.TypeSystem.IntPtr));

        app.Types.Add(StandIn(app, "KeyStandIn", "GetKeyState", app.TypeSystem.Int16, app.TypeSystem.Int32));
        app.Types.Add(StandIn(app, "ClipStandIn", "OpenClipboard", app.TypeSystem.Boolean, app.TypeSystem.IntPtr));
        assembly.Write(AppPath);
    }

    private static MethodDefinition Import(ModuleDefinition app, ModuleReference module, string name, TypeReference returns, TypeReference parameter)
    {
        var method = new MethodDefinition(name,
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.PInvokeImpl | MethodAttributes.HideBySig, returns)
        {
            PInvokeInfo = new PInvokeInfo(PInvokeAttributes.CallConvWinapi, name, module),
            ImplAttributes = MethodImplAttributes.PreserveSig,
        };
        method.Parameters.Add(new ParameterDefinition(parameter));
        return method;
    }

    private static TypeDefinition StandIn(ModuleDefinition app, string type, string name, TypeReference returns, TypeReference parameter)
    {
        var standIn = new TypeDefinition("App", type, TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, app.TypeSystem.Object);
        var method = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static, returns);
        method.Parameters.Add(new ParameterDefinition(parameter));
        ILProcessor il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        standIn.Methods.Add(method);
        return standIn;
    }
}
```

- [ ] **Step 2: Run to see it fail**

Run: `dotnet test tests/AcDream.Tools.RynthInteropRedirect.Tests --filter RedirectPInvokeTests`
Expected: `EachImportGoesToTheListedTypeThatHasIt` and `AMissingListedTypeFailsAndNamesIt` FAIL (`App.ClipStandIn,App.KeyStandIn is not in ...`).

- [ ] **Step 3: Implement**

In `Program.cs`, keep `string? standIn = args[1] == "-" ? null : args[1];` (it is printed as given) and change `Redirect` to resolve a list:

```csharp
        TypeDefinition[] standIns = standInTypeName is null
            ? []
            : standInTypeName.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(name => module.GetType(name) ?? throw new InvalidOperationException($"{name} is not in {assemblyPath}"))
                .ToArray();
```

replacing the single `TypeDefinition? standIn = ...` lookup, and in the P/Invoke loop:

```csharp
            if (standIns.Contains(type))
                continue;
            ...
                MethodDefinition? target = Win32Modules.Contains(moduleName, StringComparer.OrdinalIgnoreCase)
                    ? standIns.SelectMany(s => s.Methods).FirstOrDefault(
                        candidate => candidate.IsPublic && candidate.IsStatic && candidate.Name == entryPoint
                            && SameSignature(candidate, method))
                    : null;
```

Update the `<c>` usage doc comment and `Usage` string: `<stand-in type[,type...]|->`, and the summary sentence "the stand-in types (in the same assembly; `-` for none) have a public static method for".

- [ ] **Step 4: Run all tool tests**

Run: `dotnet test tests/AcDream.Tools.RynthInteropRedirect.Tests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add tools/RynthInteropRedirect/Program.cs tests/AcDream.Tools.RynthInteropRedirect.Tests/RedirectPInvokeTests.cs
git commit -m "feat(tools): RynthInteropRedirect redirects P/Invokes to several stand-in types

Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz"
```

---

### Task 2: Overlay key presses: VK table, `KeyPress`, the gate

**Goal:** Every key press reaches overlay sinks as an `OverlayKeyEvent` with its Win32 key code before any other keyboard listener, and a press a sink takes never reaches OpenAC's dispatcher, plugin hotkeys, retained UI or movement.

**Files:**
- Modify: `src/AcDream.ClientExtensions/IOverlayKeyboardSink.cs`
- Create: `src/AcDream.App/Extensions/Overlay/OverlayVirtualKeys.cs`
- Create: `src/AcDream.App/Extensions/Overlay/OverlayKeyGate.cs`
- Modify: `src/AcDream.App/Extensions/Overlay/OverlayInputGate.cs`
- Modify: `src/AcDream.App/Extensions/Overlay/ImGuiOverlay.cs` (beside `SinkKeyDown`, ~line 117-170)
- Modify: `src/AcDream.App/Input/SilkKeyboardSource.cs` (`IsHeld`, `OnKeyDown`, `OnKeyUp`)
- Modify: `src/AcDream.App/UI/RetainedUiInputBinding.cs` (`RetainedKeyboardInputBinding` ctor lambdas + `Invoke`)
- Modify: `src/AcDream.App/Rendering/GameWindow.cs` (`AcquirePlatform`, ~line 1227)
- Create: `tests/AcDream.App.Tests/Extensions/OverlayKeyGateTests.cs`

**Acceptance Criteria:**
- [ ] `OverlayVirtualKeys.Map`: `Key.A`→(0x41,false), `Key.Number1`→(0x31,false), `Key.F1`→(0x70,false), `Key.Keypad5`→(0x65,false), `Key.KeypadEnter`→(0x0D,true), `Key.Up`→(0x26,true), `Key.ControlRight`→(0xA3,true), `Key.Semicolon`→(0xBA,false), an unmapped key → (0,false).
- [ ] A sink returning true from `KeyPress` for Ctrl+F1: `SilkKeyboardSource` raises no `KeyDown`/`KeyUp` for F1, `IsHeld(F1)` is false while held, and `OverlayInputGate.CharTaken` is true until F1's key-up; the up event is still offered to the sink.
- [ ] An untaken key raises `KeyDown`/`KeyUp` exactly as before.
- [ ] A second key-down for a held key reaches the sink with `Repeat = true`.
- [ ] Focus loss clears every taken key and calls each sink's `FocusLost()`.
- [ ] A sink that throws from `KeyPress` is dropped (as `KeyDown` throws are) and the press passes.
- [ ] `ImGuiOverlayTests` still pass (RynthChat's sink path unchanged).

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~OverlayKeyGateTests|FullyQualifiedName~ImGuiOverlayTests|FullyQualifiedName~SilkInputSourceLifetimeTests|FullyQualifiedName~RetainedUiInputBindingTests"` → all pass.

**Steps:**

- [ ] **Step 1: Contract additions** in `IOverlayKeyboardSink.cs`:

```csharp
/// <summary>Modifiers held with a key. Control includes Command on macOS; Alt is Option there.</summary>
[Flags]
public enum OverlayModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
}

/// <summary>
/// One key press or release as Windows numbers it: <paramref name="VirtualKey"/> is the Win32
/// virtual-key code (0 for a key with none), <paramref name="Extended"/> the extended-key flag
/// (arrows, the navigation block, right Ctrl/Alt, numpad Enter and /), <paramref name="Repeat"/>
/// a key-down for a key already down.
/// </summary>
public readonly record struct OverlayKeyEvent(int VirtualKey, OverlayModifiers Modifiers, bool Down, bool Repeat, bool Extended);
```

and in the interface, after `Character`:

```csharp
    /// <summary>
    /// Every key press and release, offered before the client's own keyboard input sees it.
    /// True takes it: the client never sees that key (its repeats, characters and release
    /// included) until it comes back up.
    /// </summary>
    bool KeyPress(OverlayKeyEvent e) => false;

    /// <summary>The client window lost the keyboard; held keys are gone.</summary>
    void FocusLost()
    {
    }
```

- [ ] **Step 2: Write the failing tests** `tests/AcDream.App.Tests/Extensions/OverlayKeyGateTests.cs`:

```csharp
using AcDream.App.Extensions.Overlay;
using AcDream.App.Input;
using AcDream.App.Rendering;
using AcDream.ClientExtensions;
using Silk.NET.Input;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed class OverlayKeyGateTests : IDisposable
{
    private readonly ImGuiOverlay _overlay = new(_ => { });
    private readonly OverlayKeyGate _gate;
    private readonly TakingSink _sink = new();
    private readonly IDisposable _registration;

    public OverlayKeyGateTests()
    {
        _gate = new OverlayKeyGate(_overlay.OfferKeyPress, _overlay.SinkFocusLost);
        OverlayInputGate.BindKeys(_gate);
        _registration = _overlay.AddKeyboardSink(_sink);
    }

    public void Dispose()
    {
        OverlayInputGate.BindKeys(null);
        _registration.Dispose();
        _overlay.Dispose();
    }

    private sealed class TakingSink : IOverlayKeyboardSink
    {
        public int TakeVk { get; set; } = 0x70;
        public bool Throw { get; set; }
        public List<OverlayKeyEvent> Presses { get; } = new();
        public int FocusLosses { get; private set; }
        public bool Capturing => false;
        public void KeyDown(OverlayKey key, OverlayModifiers modifiers) { }
        public void KeyUp(OverlayKey key) { }
        public void Character(char value) { }
        public bool KeyPress(OverlayKeyEvent e)
        {
            if (Throw) throw new InvalidOperationException("sink failed");
            Presses.Add(e);
            return e.VirtualKey == TakeVk && (e.Modifiers & OverlayModifiers.Control) != 0;
        }
        public void FocusLost() => FocusLosses++;
    }

    private sealed class KeyboardSurface : IKeyboardEventSurface
    {
        private Action<Key>? _down, _up;
        public HashSet<Key> Held { get; } = new();
        public void AddKeyDown(Action<Key> callback) => _down = callback;
        public void RemoveKeyDown(Action<Key> callback) => _down = null;
        public void AddKeyUp(Action<Key> callback) => _up = callback;
        public void RemoveKeyUp(Action<Key> callback) => _up = null;
        public bool IsKeyPressed(Key key) => Held.Contains(key);
        public void Down(Key key) { Held.Add(key); _down?.Invoke(key); }
        public void Up(Key key) { Held.Remove(key); _up?.Invoke(key); }
    }

    [Theory]
    [InlineData(Key.A, 0x41, false)]
    [InlineData(Key.Number1, 0x31, false)]
    [InlineData(Key.F1, 0x70, false)]
    [InlineData(Key.F12, 0x7B, false)]
    [InlineData(Key.Keypad5, 0x65, false)]
    [InlineData(Key.KeypadEnter, 0x0D, true)]
    [InlineData(Key.KeypadDivide, 0x6F, true)]
    [InlineData(Key.Up, 0x26, true)]
    [InlineData(Key.Home, 0x24, true)]
    [InlineData(Key.ShiftLeft, 0xA0, false)]
    [InlineData(Key.ControlRight, 0xA3, true)]
    [InlineData(Key.AltLeft, 0xA4, false)]
    [InlineData(Key.Semicolon, 0xBA, false)]
    [InlineData(Key.GraveAccent, 0xC0, false)]
    [InlineData(Key.Unknown, 0, false)]
    public void SilkKeysMapToWindowsKeyCodes(Key key, int vk, bool extended) =>
        Assert.Equal((vk, extended), OverlayVirtualKeys.Map(key));

    [Fact]
    public void ATakenPressNeverReachesTheDispatcherOrMovement()
    {
        var surface = new KeyboardSurface();
        var source = SilkKeyboardSource.CreateDetached(surface, new HostQuiescenceGate());
        var seen = new List<string>();
        source.KeyDown += (key, _) => seen.Add("down " + key);
        source.KeyUp += (key, _) => seen.Add("up " + key);
        source.Attach();

        _gate.KeyDown(Key.F1, OverlayModifiers.Control);
        surface.Down(Key.F1);
        Assert.False(source.IsHeld(Key.F1));
        Assert.True(OverlayInputGate.CharTaken);
        _gate.KeyDown(Key.F1, OverlayModifiers.Control);
        surface.Down(Key.F1);
        _gate.KeyUp(Key.F1, OverlayModifiers.Control);
        surface.Up(Key.F1);

        Assert.Empty(seen);
        Assert.False(OverlayInputGate.CharTaken);
        Assert.Equal([false, true, false], _sink.Presses.Select(p => p.Repeat && p.Down));
        Assert.False(_sink.Presses[^1].Down);
        source.Dispose();
    }

    [Fact]
    public void AnUntakenPressPassesAsBefore()
    {
        var surface = new KeyboardSurface();
        var source = SilkKeyboardSource.CreateDetached(surface, new HostQuiescenceGate());
        var seen = new List<string>();
        source.KeyDown += (key, _) => seen.Add("down " + key);
        source.KeyUp += (key, _) => seen.Add("up " + key);
        source.Attach();

        _gate.KeyDown(Key.F1, OverlayModifiers.None);
        surface.Down(Key.F1);
        Assert.True(source.IsHeld(Key.F1));
        _gate.KeyUp(Key.F1, OverlayModifiers.None);
        surface.Up(Key.F1);

        Assert.Equal(["down F1", "up F1"], seen);
        source.Dispose();
    }

    [Fact]
    public void FocusLossClearsTakenKeysAndReachesTheSinks()
    {
        _gate.KeyDown(Key.F1, OverlayModifiers.Control);
        Assert.True(OverlayInputGate.KeyTaken(Key.F1));

        _gate.FocusLost();

        Assert.False(OverlayInputGate.KeyTaken(Key.F1));
        Assert.Equal(1, _sink.FocusLosses);
    }

    [Fact]
    public void AThrowingSinkIsDroppedAndThePressPasses()
    {
        _sink.Throw = true;

        _gate.KeyDown(Key.F1, OverlayModifiers.Control);

        Assert.False(OverlayInputGate.KeyTaken(Key.F1));
        _sink.Throw = false;
        _gate.KeyUp(Key.F1, OverlayModifiers.Control);
        _gate.KeyDown(Key.F1, OverlayModifiers.Control);
        Assert.Empty(_sink.Presses);   // dropped: never asked again
    }
}
```

- [ ] **Step 3: Run to see them fail**

Run: `dotnet test tests/AcDream.App.Tests --filter OverlayKeyGateTests`
Expected: build errors (`OverlayKeyGate`, `OverlayVirtualKeys`, `OfferKeyPress`, `BindKeys` missing).

- [ ] **Step 4: `OverlayVirtualKeys.cs`**

```csharp
using Silk.NET.Input;

namespace AcDream.App.Extensions.Overlay;

/// <summary>
/// Silk.NET keys → Win32 virtual-key codes and the extended-key flag, as Windows
/// reports them in WM_KEYDOWN (RynthCore's hotkeys read these). 0 for a key with none.
/// </summary>
internal static class OverlayVirtualKeys
{
    public static (int Vk, bool Extended) Map(Key key) => key switch
    {
        >= Key.A and <= Key.Z => (0x41 + (key - Key.A), false),
        >= Key.Number0 and <= Key.Number9 => (0x30 + (key - Key.Number0), false),
        >= Key.F1 and <= Key.F24 => (0x70 + (key - Key.F1), false),
        >= Key.Keypad0 and <= Key.Keypad9 => (0x60 + (key - Key.Keypad0), false),
        Key.KeypadMultiply => (0x6A, false),
        Key.KeypadAdd => (0x6B, false),
        Key.KeypadSubtract => (0x6D, false),
        Key.KeypadDecimal => (0x6E, false),
        Key.KeypadDivide => (0x6F, true),
        Key.KeypadEnter => (0x0D, true),
        Key.Backspace => (0x08, false),
        Key.Tab => (0x09, false),
        Key.Enter => (0x0D, false),
        Key.Pause => (0x13, false),
        Key.CapsLock => (0x14, false),
        Key.Escape => (0x1B, false),
        Key.Space => (0x20, false),
        Key.PageUp => (0x21, true),
        Key.PageDown => (0x22, true),
        Key.End => (0x23, true),
        Key.Home => (0x24, true),
        Key.Left => (0x25, true),
        Key.Up => (0x26, true),
        Key.Right => (0x27, true),
        Key.Down => (0x28, true),
        Key.PrintScreen => (0x2C, true),
        Key.Insert => (0x2D, true),
        Key.Delete => (0x2E, true),
        Key.SuperLeft => (0x5B, true),
        Key.SuperRight => (0x5C, true),
        Key.Menu => (0x5D, true),
        Key.NumLock => (0x90, true),
        Key.ScrollLock => (0x91, false),
        Key.ShiftLeft => (0xA0, false),
        Key.ShiftRight => (0xA1, false),
        Key.ControlLeft => (0xA2, false),
        Key.ControlRight => (0xA3, true),
        Key.AltLeft => (0xA4, false),
        Key.AltRight => (0xA5, true),
        Key.Semicolon => (0xBA, false),
        Key.Equal => (0xBB, false),
        Key.Comma => (0xBC, false),
        Key.Minus => (0xBD, false),
        Key.Period => (0xBE, false),
        Key.Slash => (0xBF, false),
        Key.GraveAccent => (0xC0, false),
        Key.LeftBracket => (0xDB, false),
        Key.BackSlash => (0xDC, false),
        Key.RightBracket => (0xDD, false),
        Key.Apostrophe => (0xDE, false),
        _ => (0, false),
    };
}
```

(Silk orders `A..Z`, `Number0..Number9`, `F1..F25`, `Keypad0..Keypad9` contiguously; the theory test pins it.)

- [ ] **Step 5: `OverlayKeyGate.cs`**

```csharp
using AcDream.ClientExtensions;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace AcDream.App.Extensions.Overlay;

/// <summary>
/// Offers every key press to the overlay's keyboard sinks before the client's own
/// keyboard input sees it, and remembers which keys a sink took: the client's input
/// (<see cref="OverlayInputGate.KeyTaken"/>) skips a taken key, its repeats and
/// characters, until it comes back up. Silk calls keyboard handlers in subscription
/// order, so <see cref="Attach"/> must run before anything else subscribes (GameWindow
/// does it as it creates the input context). Game thread.
/// </summary>
internal sealed class OverlayKeyGate
{
    private readonly Func<OverlayKeyEvent, bool> _offer;
    private readonly Action _focusLost;
    private readonly HashSet<Key> _down = new();
    private readonly HashSet<Key> _taken = new();
    private Key? _upTaken;

    public OverlayKeyGate(Func<OverlayKeyEvent, bool> offer, Action focusLost)
    {
        _offer = offer ?? throw new ArgumentNullException(nameof(offer));
        _focusLost = focusLost ?? throw new ArgumentNullException(nameof(focusLost));
    }

    /// <summary>True while the key's press, repeat or release belongs to a sink.</summary>
    public bool IsTaken(Key key) => _taken.Contains(key) || _upTaken == key;

    /// <summary>True while a taken key is down: its characters are the sink's too.</summary>
    public bool CharTaken => _taken.Count > 0;

    public void KeyDown(Key key, OverlayModifiers modifiers)
    {
        _upTaken = null;
        bool repeat = !_down.Add(key);
        (int vk, bool extended) = OverlayVirtualKeys.Map(key);
        if (_offer(new OverlayKeyEvent(vk, modifiers, Down: true, repeat, extended)))
            _taken.Add(key);
        else
            _taken.Remove(key);
    }

    public void KeyUp(Key key, OverlayModifiers modifiers)
    {
        _upTaken = null;
        _down.Remove(key);
        bool wasTaken = _taken.Remove(key);
        (int vk, bool extended) = OverlayVirtualKeys.Map(key);
        bool took = _offer(new OverlayKeyEvent(vk, modifiers, Down: false, Repeat: false, extended));
        if (wasTaken || took)
            _upTaken = key;
    }

    public void FocusLost()
    {
        _down.Clear();
        _taken.Clear();
        _upTaken = null;
        _focusLost();
    }

    /// <summary>Subscribes to every keyboard and the window's focus. Dispose to unsubscribe.</summary>
    public IDisposable Attach(IInputContext input, IView? window)
    {
        ArgumentNullException.ThrowIfNull(input);
        var keyboards = input.Keyboards.ToArray();
        Action<IKeyboard, Key, int> down = (keyboard, key, _) => KeyDown(key, Modifiers(keyboard));
        Action<IKeyboard, Key, int> up = (keyboard, key, _) => KeyUp(key, Modifiers(keyboard));
        Action<bool> focus = focused => { if (!focused) FocusLost(); };
        foreach (IKeyboard keyboard in keyboards)
        {
            keyboard.KeyDown += down;
            keyboard.KeyUp += up;
        }

        if (window is not null)
            window.FocusChanged += focus;
        return new Subscription(() =>
        {
            foreach (IKeyboard keyboard in keyboards)
            {
                keyboard.KeyDown -= down;
                keyboard.KeyUp -= up;
            }

            if (window is not null)
                window.FocusChanged -= focus;
        });
    }

    private static OverlayModifiers Modifiers(IKeyboard keyboard)
    {
        bool Held(Key a, Key b) => keyboard.IsKeyPressed(a) || keyboard.IsKeyPressed(b);
        return (Held(Key.ControlLeft, Key.ControlRight) || Held(Key.SuperLeft, Key.SuperRight) ? OverlayModifiers.Control : OverlayModifiers.None)
            | (Held(Key.ShiftLeft, Key.ShiftRight) ? OverlayModifiers.Shift : OverlayModifiers.None)
            | (Held(Key.AltLeft, Key.AltRight) ? OverlayModifiers.Alt : OverlayModifiers.None);
    }

    private sealed class Subscription(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
```

- [ ] **Step 6: `OverlayInputGate.cs`** — add below `SuppressKeyboard`:

```csharp
    private static OverlayKeyGate? _keys;

    /// <summary>True when an overlay sink took this key's current press (or its release).</summary>
    public static bool KeyTaken(Silk.NET.Input.Key key) => _keys?.IsTaken(key) == true;

    /// <summary>True while a taken key is down: typed characters belong to the sink.</summary>
    public static bool CharTaken => _keys?.CharTaken == true;

    public static void BindKeys(OverlayKeyGate? keys) => _keys = keys;
```

- [ ] **Step 7: `ImGuiOverlay.cs`** — beside `SinkKeyDown`:

```csharp
    /// <summary>Offers a key press to every sink; true when one took it. A failed overlay takes nothing.</summary>
    internal bool OfferKeyPress(OverlayKeyEvent e)
    {
        if (_failed)
            return false;
        bool taken = false;
        foreach (IOverlayKeyboardSink sink in _sinks.ToArray())
        {
            try { taken |= sink.KeyPress(e); }
            catch (Exception error) { DropSink(sink, error); }
        }

        return taken;
    }

    internal void SinkFocusLost() => ForEachSink(sink => sink.FocusLost());

    /// <summary>
    /// Puts the key gate on the input context's keyboards, ahead of everything else (call it
    /// as the context is created). Dispose to take it off.
    /// </summary>
    internal IDisposable AttachKeyGate(Silk.NET.Input.IInputContext input, Silk.NET.Windowing.IView? window)
    {
        var gate = new OverlayKeyGate(OfferKeyPress, SinkFocusLost);
        IDisposable subscription = gate.Attach(input, window);
        OverlayInputGate.BindKeys(gate);
        return new KeyGateRegistration(gate, subscription);
    }

    private sealed class KeyGateRegistration(OverlayKeyGate gate, IDisposable subscription) : IDisposable
    {
        public void Dispose()
        {
            subscription.Dispose();
            OverlayInputGate.UnbindKeys(gate);
        }
    }
```

and add to `OverlayInputGate`:

```csharp
    /// <summary>Unbinds the gate only when it is still the bound one.</summary>
    public static void UnbindKeys(OverlayKeyGate keys)
    {
        if (ReferenceEquals(_keys, keys))
            _keys = null;
    }
```

- [ ] **Step 8: Consumers.** `SilkKeyboardSource.cs`:

```csharp
    public bool IsHeld(Key key) =>
        _surface.IsKeyPressed(key) && !AcDream.App.Extensions.Overlay.OverlayInputGate.KeyTaken(key);
    ...
    private void OnKeyDown(Key key)
    {
        // Read at event time: the gate's answer belongs to this press.
        bool taken = AcDream.App.Extensions.Overlay.OverlayInputGate.KeyTaken(key);
        _quiescence.Invoke(() =>
        {
            if (Volatile.Read(ref _active) != 0 && !taken)
                KeyDown?.Invoke(key, ReadModifiers());
        });
    }

    private void OnKeyUp(Key key)
    {
        bool taken = AcDream.App.Extensions.Overlay.OverlayInputGate.KeyTaken(key);
        _quiescence.Invoke(() =>
        {
            if (Volatile.Read(ref _active) != 0 && !taken)
                KeyUp?.Invoke(key, ReadModifiers());
        });
    }
```

`RetainedKeyboardInputBinding` ctor:

```csharp
        _down = key => Invoke(() => _root.OnKeyDown((int)key), Overlay.OverlayInputGate.KeyTaken(key));
        _up = key => Invoke(() => _root.OnKeyUp((int)key), Overlay.OverlayInputGate.KeyTaken(key));
        _char = value => Invoke(() => _root.OnChar(value), Overlay.OverlayInputGate.CharTaken);
```

(using the full name `AcDream.App.Extensions.Overlay.OverlayInputGate` as the existing `Invoke` does), and `Invoke`:

```csharp
    private void Invoke(Action callback, bool taken) =>
        _quiescence.Invoke(() =>
        {
            if (Volatile.Read(ref _active) != 0 && !taken && !AcDream.App.Extensions.Overlay.OverlayInputGate.SuppressKeyboard)
                callback();
        });
```

- [ ] **Step 9: GameWindow.** Add a field `private IDisposable? _keyGate;` beside `_overlay` (line ~285). In `AcquirePlatform` (line ~1233) replace

```csharp
                () => _window!.CreateInput(),
                static input => input.Dispose(),
```

with

```csharp
                () => AttachKeyGate(_window!.CreateInput()),
                input =>
                {
                    Interlocked.Exchange(ref _keyGate, null)?.Dispose();
                    input.Dispose();
                },
```

and add the method beside `AcquirePlatform`:

```csharp
    // The key gate must be the keyboard's first subscriber, so it goes on as the context is made.
    private IInputContext AttachKeyGate(IInputContext input)
    {
        _keyGate = _overlay.AttachKeyGate(input, _window);
        return input;
    }
```

(`IInputContext` is `Silk.NET.Input.IInputContext`; use the file's existing spelling.)

- [ ] **Step 10: Run the tests**

Run: the **Verify** command.
Expected: all PASS. Also `dotnet build src/AcDream.App` → 0 errors.

- [ ] **Step 11: Commit**

```bash
git add src/AcDream.ClientExtensions/IOverlayKeyboardSink.cs src/AcDream.App/Extensions/Overlay src/AcDream.App/Input/SilkKeyboardSource.cs src/AcDream.App/UI/RetainedUiInputBinding.cs src/AcDream.App/Rendering/GameWindow.cs tests/AcDream.App.Tests/Extensions/OverlayKeyGateTests.cs
git commit -m "feat(overlay): sinks see every key press first and can take it from the client

Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz"
```

---

### Task 3: `IClientInput.IsTyping` for extensions

**Goal:** Extensions can ask whether the player is typing in one of the client's own text boxes (the chat entry or any retained-UI focus element).

**Files:**
- Create: `src/AcDream.ClientExtensions/IClientInput.cs`
- Modify: `src/AcDream.ClientExtensions/ClientExtensionContext.cs`
- Create: `src/AcDream.App/Extensions/ClientInputState.cs`
- Modify: `src/AcDream.App/Extensions/ClientExtensionHost.cs` (`Attach` parameters + context)
- Modify: `src/AcDream.App/Rendering/GameWindow.cs:545` (pass `input:`)
- Create: `tests/AcDream.App.Tests/Extensions/ClientInputStateTests.cs`

**Acceptance Criteria:**
- [ ] `IsTyping` is true when the UI focus source is true or the chat entry is active, false otherwise.
- [ ] `TypingDescription` is "the chat entry", "a client text box" or "nothing".
- [ ] `ClientExtensionContext.Input` is non-null in the graphical client.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter ClientInputStateTests` → pass; `dotnet build src/AcDream.App` → 0 errors.

**Steps:**

- [ ] **Step 1: Interface**

```csharp
namespace AcDream.ClientExtensions;

/// <summary>The client's own keyboard state as an extension sees it. Game thread.</summary>
public interface IClientInput
{
    /// <summary>True while the player is typing in a client text box (the chat entry, a name field...).</summary>
    bool IsTyping { get; }

    /// <summary>Where the typing goes, for status lines: "the chat entry", "a client text box" or "nothing".</summary>
    string TypingDescription { get; }
}
```

`ClientExtensionContext` gains:

```csharp
    /// <summary>The client's keyboard state; null in a host without a keyboard.</summary>
    public IClientInput? Input { get; init; }
```

- [ ] **Step 2: Failing test**

```csharp
using AcDream.App.Extensions;

namespace AcDream.App.Tests.Extensions;

public sealed class ClientInputStateTests
{
    [Theory]
    [InlineData(false, false, false, "nothing")]
    [InlineData(true, false, true, "the chat entry")]
    [InlineData(false, true, true, "a client text box")]
    [InlineData(true, true, true, "the chat entry")]
    public void TypingFollowsTheChatEntryAndUiFocus(bool chat, bool uiFocus, bool typing, string where)
    {
        var state = new ClientInputState(() => uiFocus, () => chat);

        Assert.Equal(typing, state.IsTyping);
        Assert.Equal(where, state.TypingDescription);
    }
}
```

- [ ] **Step 3: Implement** `ClientInputState.cs`:

```csharp
using AcDream.ClientExtensions;

namespace AcDream.App.Extensions;

/// <summary>
/// Whether the player is typing in the client: the chat entry is active, or the retained
/// UI has a keyboard focus element. Both read live (the UI binds after extensions attach).
/// </summary>
internal sealed class ClientInputState(Func<bool> uiHasKeyboardFocus, Func<bool> chatEntryActive) : IClientInput
{
    public bool IsTyping => chatEntryActive() || uiHasKeyboardFocus();

    public string TypingDescription =>
        chatEntryActive() ? "the chat entry" : uiHasKeyboardFocus() ? "a client text box" : "nothing";
}
```

`ClientExtensionHost.Attach` gains `IClientInput? input = null` (last parameter) and `Input = input,` in the context. GameWindow line 545 appends:

```csharp
, input: new AcDream.App.Extensions.ClientInputState(
    () => _retainedInputCapture is { } capture && capture.HasKeyboardFocus,
    () => _automation?.Chat.IsInputActive == true)
```

(`_retainedInputCapture` is assigned after this line; the lambda reads it later. If it is declared non-nullable, keep the `is { } capture` pattern — it compiles and is null-safe at runtime.)

- [ ] **Step 4: Run** the Verify commands → pass / 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.ClientExtensions/IClientInput.cs src/AcDream.ClientExtensions/ClientExtensionContext.cs src/AcDream.App/Extensions/ClientInputState.cs src/AcDream.App/Extensions/ClientExtensionHost.cs src/AcDream.App/Rendering/GameWindow.cs tests/AcDream.App.Tests/Extensions/ClientInputStateTests.cs
git commit -m "feat(extensions): extensions can ask whether the player is typing

Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz"
```

---

### Task 4: Compile their hotkeys; stand-ins; `/rc hotkey`; the file

**Goal:** RynthCore's `HotkeyModel.cs`, `HotkeyService.cs` and `HotkeysSettingsUi.cs` compile unchanged into EngineUi with stand-ins and redirects, the Settings Hotkeys tab draws, and `/rc hotkey(s)` works.

**Files:**
- Modify: `src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj` (Compile items after the `SettingsFace.cs` line; redirect `Exec` at line ~202; header comment lists Hotkeys)
- Create: `src/AcDream.Rynth.EngineUi/StandIns/Win32KeyStateStandIn.cs`
- Create: `src/AcDream.Rynth.EngineUi/StandIns/HotkeyStandIns.cs`
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/DashboardStandIns.cs` (delete `HotkeysSettingsUi`; `EntryPoint.NoteManualReload`; `AcMainThreadQueue.ChatCommand` + `EnqueueChatCommand`)
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/SectionsStandIns.cs` (`IltInfo.PluginLoaded`)
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/RynthCoreChatCommandsStandIn.cs`
- Modify: `src/AcDream.Rynth.Host/RynthInstallFolders.cs`
- Modify: `src/AcDream.Rynth.Host/RynthAiHookup.cs`
- Create: `tests/AcDream.Rynth.Host.Tests/RynthHotkeysTests.cs`
- Modify: `tests/AcDream.Rynth.Host.Tests/RynthAiPluginTests.cs` (install-folder expectations)

**Acceptance Criteria:**
- [ ] Build prints `RynthInteropRedirect: AcDream.Rynth.EngineUi.dll: 14 P/Invoke(s) redirected to AcDream.Rynth.EngineUi.Win32ClipboardStandIn,AcDream.Rynth.EngineUi.Win32KeyStateStandIn` (11 clipboard + 3 key state).
- [ ] `/rc hotkey add ctrl+f1 /ra help` replies `Hotkey added: Ctrl+F1 - ...`, `/rc hotkey list` lists it, `/rc hotkey remove ctrl+f1` replies `Removed 1 hotkey(s) on Ctrl+F1.`, and the sandbox `hotkeys.txt` holds/loses the line.
- [ ] `/rc hotkeys` opens Settings on the Hotkeys tab; drawing it headless gives > 300 vertices and no exception lines.
- [ ] `EnqueueChatCommand("/ra help")` runs through `AcMainThreadQueue.ChatCommand` on the next `UiDataHub.Drain`, not at once.
- [ ] Off Windows `RynthInstallFolders.Apply` sets `RYNTHCORE_HOTKEYS_FILE=<RYNTHCORE_DIR>/hotkeys.txt` unless already set; on Windows it is untouched.
- [ ] All Rynth.Host.Tests pass (431+).

**Verify:** `dotnet build src/AcDream.Rynth.EngineUi 2>&1 | grep RynthInteropRedirect` → the 14-redirect line; `dotnet test tests/AcDream.Rynth.Host.Tests` → all pass.

**Steps:**

- [ ] **Step 1: csproj.** After the `SettingsFace.cs` Compile line add:

```xml
    <Compile Include="$(RynthEngine)Hotkeys\HotkeyModel.cs" Link="RynthCore\Hotkeys\HotkeyModel.cs" />
    <Compile Include="$(RynthEngine)Hotkeys\HotkeyService.cs" Link="RynthCore\Hotkeys\HotkeyService.cs" />
    <Compile Include="$(RynthEngine)ImGui\Panels\HotkeysSettingsUi.cs" Link="RynthCore\HotkeysSettingsUi.cs" />
```

In the redirect `Exec`, change the stand-in argument `AcDream.Rynth.EngineUi.Win32ClipboardStandIn` to `AcDream.Rynth.EngineUi.Win32ClipboardStandIn,AcDream.Rynth.EngineUi.Win32KeyStateStandIn`, and extend the comment above it (line ~176) with "and HotkeyService's GetKeyState / GetForegroundWindow / GetWindowThreadProcessId (Win32KeyStateStandIn)". In the header comment's panel list add "the Hotkeys settings tab".

Delete the `HotkeysSettingsUi` stand-in class (and its summary) from `DashboardStandIns.cs`.

- [ ] **Step 2: `Win32KeyStateStandIn.cs`**

```csharp
// Stand-in for the user32 calls RynthCore's HotkeyService makes from acclient's WndProc.
// HotkeyService is compiled unchanged; after compile, tools/RynthInteropRedirect points
// its GetKeyState / GetForegroundWindow / GetWindowThreadProcessId at the methods of the
// same name and signature here. The host's hotkey sink sets the state before each key.
using System;

namespace AcDream.Rynth.EngineUi;

/// <summary>
/// The modifier keys and window focus as HotkeyService asks Windows for them, answered from
/// the overlay key event being handled (OpenAC has one window, ours). Game thread.
/// </summary>
internal static class Win32KeyStateStandIn
{
    private const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12;
    private const int VkLShift = 0xA0, VkRShift = 0xA1, VkLControl = 0xA2, VkRControl = 0xA3, VkLMenu = 0xA4, VkRMenu = 0xA5;
    private static readonly IntPtr OurWindow = new(1);

    internal static bool Control { get; set; }

    internal static bool Shift { get; set; }

    internal static bool Alt { get; set; }

    /// <summary>The client window has the keyboard (false after focus loss until the next key).</summary>
    internal static bool Focused { get; set; }

    public static short GetKeyState(int nVirtKey)
    {
        bool down = nVirtKey switch
        {
            VkControl or VkLControl or VkRControl => Control,
            VkShift or VkLShift or VkRShift => Shift,
            VkMenu or VkLMenu or VkRMenu => Alt,
            _ => false,
        };
        return down ? unchecked((short)0x8000) : (short)0;
    }

    public static IntPtr GetForegroundWindow() => Focused ? OurWindow : IntPtr.Zero;

    public static uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid)
    {
        pid = hWnd == OurWindow ? (uint)Environment.ProcessId : 0;
        return pid == 0 ? 0u : 1u;
    }
}
```

- [ ] **Step 3: `HotkeyStandIns.cs`**

```csharp
// Stand-ins for the engine types RynthCore's HotkeyService uses that OpenAC doesn't compile.
using System;

namespace RynthCore.Engine.Compatibility
{
    /// <summary>
    /// "Is the player typing in one of AC's own text boxes?" RynthCore reads acclient's UI focus
    /// element; here the host points <see cref="IsTyping"/> at OpenAC's chat entry and UI focus.
    /// </summary>
    internal static class AcKeyboardFocus
    {
        internal static Func<bool>? IsTyping { get; set; }

        internal static Func<string>? Where { get; set; }

        /// <summary>Null (unknown) until the host points it somewhere, as RynthCore without a UI manager.</summary>
        public static bool? HasTextFocus() => IsTyping?.Invoke();

        public static string Describe() =>
            IsTyping is null ? "OpenAC's keyboard focus: unknown." : $"OpenAC's keyboard focus: {Where?.Invoke() ?? "unknown"}.";
    }
}

namespace RynthCore.Engine
{
    /// <summary>RynthCore reloads its engine (the "reload" hotkey, /rc rl); OpenAC has no engine reload.</summary>
    internal static class EngineLifecycle
    {
        public static void SignalReload() =>
            Compatibility.AcMainThreadQueue.EnqueueWriteToChat("[RynthCore] Reload isn't in OpenAC.", 1);
    }
}
```

In `DashboardStandIns.cs`, `EntryPoint` gains:

```csharp
        /// <summary>RynthCore notes a reload the player asked for; OpenAC doesn't reload, so nothing to note.</summary>
        internal static void NoteManualReload()
        {
        }
```

and `AcMainThreadQueue` gains (update its summary to "only the chat write, chat commands and the selection their code uses"):

```csharp
        /// <summary>Runs a chat command as if typed (the host: the same runner as ServerMenus.Runner).</summary>
        internal static Action<string>? ChatCommand { get; set; }

        /// <summary>
        /// A chat command for the next tick (RynthCore's next main-thread drain), so a hotkey's
        /// command never runs inside its own keystroke. False when nothing runs chat commands.
        /// </summary>
        public static bool EnqueueChatCommand(string text)
        {
            if (ChatCommand is null)
                return false;
            RynthCore.Engine.UI.Data.UiDataHub.Post("hotkey chat command", () => ChatCommand?.Invoke(text));
            return true;
        }
```

In `SectionsStandIns.cs` `IltInfo`, after `IsIlt`:

```csharp
        public static bool PluginLoaded => false;
```

- [ ] **Step 4: `/rc` stand-in.** In `RynthCoreChatCommandsStandIn.TryHandle`, before the `miniremote` check, add their branches (from RynthCore's `RynthCoreChatCommands.cs:434-470`, their texts):

```csharp
            // /rc hotkeys : Settings > Hotkeys. /rc hotkey list|add|remove|enable|disable|actions|status|on|off
            if (IsVerb(sub, "hotkeys", out string hotkeyArgs) && hotkeyArgs.Length == 0)
            {
                if (!ImGuiPanelHost.HasFace(SettingsFace.Title))
                    reply("The Settings panel isn't available (the ImGui layer is off?). /rc hotkey list|add|remove work here in chat.");
                else
                {
                    SettingsFace.RequestTab(HotkeysSettingsUi.TabName);
                    if (!ImGuiPanelHost.IsOpen(SettingsFace.Title))
                        UI.PanelRouter.Toggle(SettingsFace.Title);
                }
                return true;
            }
            if (IsVerb(sub, "hotkey", out hotkeyArgs) || IsVerb(sub, "hotkeys", out hotkeyArgs))
            {
                foreach (string line in Hotkeys.HotkeyService.HandleCommand(hotkeyArgs))
                    reply(line);
                return true;
            }
```

Update the usage line to `"Usage: /rc miniremote [show|hide|toggle|tab <tab>|section <id>] | inventory | hotkeys | hotkey <list|add|remove|...>"`, the "isn't in OpenAC (only ...)" reply to `(only /rc miniremote, /rc inventory, /rc hotkeys and /rc hotkey)`, and the class summary.

- [ ] **Step 5: Host wiring.** `RynthInstallFolders`: add `internal const string HotkeysVariable = "RYNTHCORE_HOTKEYS_FILE";` and at the end of `Apply(...)`:

```csharp
        if (string.IsNullOrWhiteSpace(get(HotkeysVariable)) && get(CoreVariable) is { Length: > 0 } coreDir)
            set(HotkeysVariable, Path.Combine(coreDir, "hotkeys.txt"));
```

(and one sentence in the class summary: "RynthCore's hotkeys file goes there too, through their own RYNTHCORE_HOTKEYS_FILE, instead of .NET's ~/.local/share."). Rename the existing `core` pattern variable if needed to avoid the name clash.

`RynthAiHookup`: `AcMainThreadQueue.ChatCommand = _runner;` beside `ServerMenus.Runner = _runner;` and in `Dispose`: `if (AcMainThreadQueue.ChatCommand == _runner) AcMainThreadQueue.ChatCommand = null;`.

- [ ] **Step 6: Tests** `tests/AcDream.Rynth.Host.Tests/RynthHotkeysTests.cs`:

```csharp
using System.Reflection;
using AcDream.Rynth.Host;
using AcDream.TestSupport;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.Hotkeys;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthHotkeysTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-hotkeys-");
    private readonly FakeGameView _view = new() { PlayerId = 0x50000007u };
    private readonly string _file;
    private readonly string? _oldFile = Environment.GetEnvironmentVariable(HotkeyStore.EnvOverride);

    public RynthHotkeysTests()
    {
        _file = Path.Combine(Directory.CreateTempSubdirectory("rynth-hotkeys-file-").FullName, "hotkeys.txt");
        Environment.SetEnvironmentVariable(HotkeyStore.EnvOverride, _file);
        ResetHotkeyService();
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = _log.Add;
        UiDataHub.Clear();
    }

    public void Dispose()
    {
        AcDream.Rynth.EngineUi.RynthAiDashboard.Stop();
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = null;
        LoginLifecycleHooks.HasObservedLoginComplete = false;
        ResetHotkeyService();
        Environment.SetEnvironmentVariable(HotkeyStore.EnvOverride, _oldFile);
        _sandbox.Dispose();
    }

    /// <summary>HotkeyService keeps one generation's statics; each test starts from an unloaded file.</summary>
    internal static void ResetHotkeyService()
    {
        HotkeyService.Shutdown();
        Type service = typeof(HotkeyService);
        service.GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, false);
        service.GetField("_nextFileCheckMs", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, 0L);
        service.GetField("_file", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, new HotkeyFile(true, new List<HotkeyBinding>()));
    }

    internal static string WaitForFile(string path, Func<string, bool> done)
    {
        for (int i = 0; i < 100; i++)
        {
            string text = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            if (done(text)) return text;
            Thread.Sleep(20);
        }
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    private List<string> Rc(string arguments)
    {
        var lines = new List<string>();
        RynthCoreChatCommands.TryHandle(arguments, lines.Add);
        return lines;
    }

    [Fact]
    public void RcHotkeyAddsListsAndRemovesAndSavesTheFile()
    {
        List<string> added = Rc("hotkey add ctrl+f1 /ra help");
        Assert.Contains(added, line => line.StartsWith("Hotkey added: Ctrl+F1", StringComparison.Ordinal));
        Assert.Contains("Ctrl+F1", WaitForFile(_file, text => text.Contains("Ctrl+F1", StringComparison.Ordinal)), StringComparison.Ordinal);

        Assert.Contains(Rc("hotkey list"), line => line.Contains("Ctrl+F1", StringComparison.Ordinal));

        Assert.Equal(["Removed 1 hotkey(s) on Ctrl+F1."], Rc("hotkey remove ctrl+f1"));
        Assert.DoesNotContain("Ctrl+F1", WaitForFile(_file, text => !text.Contains("Ctrl+F1", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public void AChatCommandRunsOnTheNextDrainNotAtOnce()
    {
        var ran = new List<string>();
        AcMainThreadQueue.ChatCommand = ran.Add;
        try
        {
            Assert.True(AcMainThreadQueue.EnqueueChatCommand("/ra help"));
            Assert.Empty(ran);
            UiDataHub.Drain(_log.Add);
            Assert.Equal(["/ra help"], ran);
        }
        finally { AcMainThreadQueue.ChatCommand = null; }
    }

    [Fact]
    public void RcHotkeysOpensTheSettingsHotkeysTabAndItDraws()
    {
        using var host = new RynthHost(_view, _log.Add, [typeof(RynthCore.Plugin.RynthAi.PluginExports)]);
        using var exports = new RynthChatHookup(host, _view, _log.Add);
        using var ai = new RynthAiHookup(host, _view, _log.Add);
        using var imgui = new HeadlessImGui();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();

        Assert.Empty(Rc("hotkeys"));
        for (int i = 0; i < 3; i++)
        {
            AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
            host.Tick();
            int vertices = imgui.Frame(ImGuiPanelHost.DrawAll);
            Assert.True(vertices > 300, $"frame {i}: only {vertices} vertices");
        }

        Assert.True(ImGuiPanelHost.IsOpen(SettingsFace.Title));
        Assert.DoesNotContain(_log, line => line.Contains("Exception", StringComparison.Ordinal) || line.Contains("threw", StringComparison.Ordinal));
    }
}
```

In `RynthAiPluginTests` add, beside the existing `RynthInstallFolders.Apply` tests:

```csharp
    [Fact]
    public void OffWindowsTheHotkeysFileGoesBesideTheCoreFolder()
    {
        var env = new Dictionary<string, string?>();
        RynthInstallFolders.Apply(name => env.GetValueOrDefault(name), (name, value) => env[name] = value, isWindows: false, "/home/me/.config");
        Assert.Equal(Path.Combine("/home/me/.config", "RynthCore", "hotkeys.txt"), env[RynthInstallFolders.HotkeysVariable]);

        env[RynthInstallFolders.HotkeysVariable] = "/mine.txt";
        RynthInstallFolders.Apply(name => env.GetValueOrDefault(name), (name, value) => env[name] = value, isWindows: false, "/home/me/.config");
        Assert.Equal("/mine.txt", env[RynthInstallFolders.HotkeysVariable]);
    }
```

and extend the Windows test's assertions with `Assert.False(env.ContainsKey(RynthInstallFolders.HotkeysVariable));`.

- [ ] **Step 7: Build, fix compile gaps, run**

Run: `dotnet build src/AcDream.Rynth.EngineUi 2>&1 | grep -E "error|RynthInteropRedirect"`
Expected: no errors; the 14-redirect line. If the compiler names another missing member, add it to the stand-in of that name in the same style (a trial compile on 2026-10-20 found only the five above).

Run: `dotnet test tests/AcDream.Rynth.Host.Tests`
Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.Rynth.EngineUi src/AcDream.Rynth.Host/RynthInstallFolders.cs src/AcDream.Rynth.Host/RynthAiHookup.cs tests/AcDream.Rynth.Host.Tests/RynthHotkeysTests.cs tests/AcDream.Rynth.Host.Tests/RynthAiPluginTests.cs
git commit -m "feat(rynth): compile RynthCore's hotkeys and Settings Hotkeys tab; /rc hotkey

Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz"
```

---

### Task 5: The hotkey sink: key presses reach HotkeyService

**Goal:** In the client, a bound key pressed in the world runs its action once and is taken from OpenAC; Settings "press a key" captures; typing anywhere holds hotkeys back.

**Files:**
- Create: `src/AcDream.Rynth.Host/RynthHotkeyKeyboard.cs`
- Modify: `src/AcDream.Rynth.Host/RynthClientExtension.cs` (fields ~line 42; `Attach` after `StartChat`; `Dispose` ~line 236)
- Modify: `tests/AcDream.Rynth.Host.Tests/RynthHotkeysTests.cs`

**Acceptance Criteria:**
- [ ] Bound Ctrl+F1 → `/ra help` (action `command`): first `KeyPress` down returns true and queues `/ra help` once (after `UiDataHub.Drain`, `FakeGameView.SubmittedCommands == ["/ra help"]`); its repeat, `Character`, and key-up are taken; the command runs only once.
- [ ] An unbound key (Ctrl+F2) returns false.
- [ ] Held back (returns false, nothing queued) when `IsTyping` is true, when the overlay wants the keyboard, and before login; a key after `FocusLost` still fires.
- [ ] `HotkeyService.BeginCapture()` then Ctrl+Shift+F5 down → returns true and `TryTakeCapture` yields `Ctrl+Shift+F5`; Esc during capture cancels.
- [ ] Binding `panel:Dungeon Map` toggles that panel open (with the dashboard started).
- [ ] The sink is registered with the overlay when the extension attaches with an overlay and removed on dispose; `AcKeyboardFocus.IsTyping` points at `context.Input` and is cleared on dispose.

**Verify:** `dotnet test tests/AcDream.Rynth.Host.Tests --filter RynthHotkeysTests` → all pass; then the full `dotnet test tests/AcDream.Rynth.Host.Tests`.

**Steps:**

- [ ] **Step 1: Failing tests** — add to `RynthHotkeysTests`:

```csharp
    private static AcDream.ClientExtensions.OverlayKeyEvent Press(int vk, bool down = true, bool repeat = false,
        AcDream.ClientExtensions.OverlayModifiers mods = AcDream.ClientExtensions.OverlayModifiers.Control) =>
        new(vk, mods, down, repeat, Extended: false);

    private const int VkF1 = 0x70, VkF2 = 0x71, VkF5 = 0x74, VkEscape = 0x1B;

    private (RynthHotkeyKeyboard Keys, List<string> Ran) Bound(bool typing = false, bool overlayWants = false)
    {
        var ran = new List<string>();
        AcMainThreadQueue.ChatCommand = ran.Add;
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        var keys = new RynthHotkeyKeyboard(() => overlayWants);
        AcKeyboardFocus.IsTyping = () => typing;
        HotkeyService.Add(new HotkeyBinding(new KeyCombo(VkF1, HotkeyMods.Ctrl), HotkeyActions.Command, "/ra help"));
        return (keys, ran);
    }

    [Fact]
    public void ABoundKeyFiresOnceAndIsTakenWholly()
    {
        (RynthHotkeyKeyboard keys, List<string> ran) = Bound();
        try
        {
            Assert.True(keys.KeyPress(Press(VkF1)));
            Assert.True(keys.KeyPress(Press(VkF1, repeat: true)));
            keys.Character('\u0001');
            Assert.True(keys.KeyPress(Press(VkF1, down: false)));
            UiDataHub.Drain(_log.Add);

            Assert.Equal(["/ra help"], ran);
            Assert.False(keys.KeyPress(Press(VkF2)));
        }
        finally { AcMainThreadQueue.ChatCommand = null; AcKeyboardFocus.IsTyping = null; }
    }

    [Theory]
    [InlineData(true, false, true)]    // typing in OpenAC
    [InlineData(false, true, true)]    // an ImGui field or Rynth chat has the keyboard
    [InlineData(false, false, false)]  // before login
    public void HeldBackWhileTypingOrOutOfWorld(bool typing, bool overlayWants, bool inWorld)
    {
        (RynthHotkeyKeyboard keys, List<string> ran) = Bound(typing, overlayWants);
        try
        {
            LoginLifecycleHooks.HasObservedLoginComplete = inWorld;

            Assert.False(keys.KeyPress(Press(VkF1)));
            UiDataHub.Drain(_log.Add);
            Assert.Empty(ran);
        }
        finally { AcMainThreadQueue.ChatCommand = null; AcKeyboardFocus.IsTyping = null; }
    }

    [Fact]
    public void AKeyAfterAFocusLossStillFires()
    {
        // OpenAC sends keys only while its window has them, so the key itself is focus:
        // a loss must not leave hotkeys blocked (their tracker resets on WM_KILLFOCUS).
        (RynthHotkeyKeyboard keys, List<string> ran) = Bound();
        try
        {
            keys.FocusLost();
            Assert.False(AcDream.Rynth.EngineUi.Win32KeyStateStandIn.Focused);

            Assert.True(keys.KeyPress(Press(VkF1)));
            UiDataHub.Drain(_log.Add);
            Assert.Equal(["/ra help"], ran);
        }
        finally { AcMainThreadQueue.ChatCommand = null; AcKeyboardFocus.IsTyping = null; }
    }

    [Fact]
    public void SettingsCaptureTakesTheNextComboAndEscCancels()
    {
        var keys = new RynthHotkeyKeyboard(() => false);
        HotkeyService.BeginCapture();
        Assert.True(keys.KeyPress(Press(VkF5, mods: AcDream.ClientExtensions.OverlayModifiers.Control | AcDream.ClientExtensions.OverlayModifiers.Shift)));
        Assert.True(HotkeyService.TryTakeCapture(out KeyCombo? combo));
        Assert.Equal("Ctrl+Shift+F5", combo?.ToString());

        HotkeyService.BeginCapture();
        Assert.True(keys.KeyPress(Press(VkEscape, mods: AcDream.ClientExtensions.OverlayModifiers.None)));
        Assert.False(HotkeyService.Capturing);
    }

    [Fact]
    public void APanelBindingTogglesThePanel()
    {
        using var host = new RynthHost(_view, _log.Add, [typeof(RynthCore.Plugin.RynthAi.PluginExports)]);
        using var exports = new RynthChatHookup(host, _view, _log.Add);
        using var ai = new RynthAiHookup(host, _view, _log.Add);
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        AcKeyboardFocus.IsTyping = () => false;
        try
        {
            var keys = new RynthHotkeyKeyboard(() => false);
            HotkeyService.Add(new HotkeyBinding(new KeyCombo(VkF2, HotkeyMods.Ctrl), HotkeyActions.PanelPrefix + DungeonMapFace.Title));

            Assert.True(keys.KeyPress(Press(VkF2)));

            Assert.True(ImGuiPanelHost.IsOpen(DungeonMapFace.Title));
        }
        finally { AcKeyboardFocus.IsTyping = null; }
    }
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test tests/AcDream.Rynth.Host.Tests --filter RynthHotkeysTests`
Expected: build error, `RynthHotkeyKeyboard` missing.

- [ ] **Step 3: `RynthHotkeyKeyboard.cs`**

```csharp
using AcDream.ClientExtensions;
using AcDream.Rynth.EngineUi;
using RynthCore.Engine.Hotkeys;

namespace AcDream.Rynth.Host;

/// <summary>
/// OpenAC's keyboard for RynthCore's hotkeys: what their Win32Backend hands HotkeyService
/// from acclient's WndProc, made from the overlay's key events. Each press becomes the
/// WM_KEYDOWN / WM_SYSKEYDOWN (Alt held) or key-up message Windows would send, the modifier
/// state their GetKeyState reads is set first (Win32KeyStateStandIn), and their answer
/// decides whether OpenAC sees the key. Characters and focus loss go to them too. Game thread.
/// </summary>
internal sealed class RynthHotkeyKeyboard : IOverlayKeyboardSink
{
    private const uint WmKillFocus = 0x0008, WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmChar = 0x0102,
        WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
    private const long Extended = 0x01000000, PreviousDown = 0x40000000, Transition = 0x80000000;

    private readonly Func<bool> _overlayHasKeyboard;

    /// <param name="overlayHasKeyboard">True while an ImGui text field or Rynth chat has the keyboard (their "Rynth text focus").</param>
    public RynthHotkeyKeyboard(Func<bool> overlayHasKeyboard)
    {
        ArgumentNullException.ThrowIfNull(overlayHasKeyboard);
        _overlayHasKeyboard = overlayHasKeyboard;
    }

    public bool Capturing => false;

    public bool KeyPress(OverlayKeyEvent e)
    {
        if (e.VirtualKey == 0)
            return false;
        Win32KeyStateStandIn.Focused = true;   // OpenAC only sends keys while its window has them
        Win32KeyStateStandIn.Control = (e.Modifiers & OverlayModifiers.Control) != 0;
        Win32KeyStateStandIn.Shift = (e.Modifiers & OverlayModifiers.Shift) != 0;
        Win32KeyStateStandIn.Alt = (e.Modifiers & OverlayModifiers.Alt) != 0;
        bool alt = Win32KeyStateStandIn.Alt;
        uint message = e.Down ? (alt ? WmSysKeyDown : WmKeyDown) : (alt ? WmSysKeyUp : WmKeyUp);
        long lParam = 1
            | (e.Extended ? Extended : 0)
            | (e.Repeat || !e.Down ? PreviousDown : 0)
            | (e.Down ? 0 : Transition);
        return HotkeyService.HandleKeyMessage(message, (IntPtr)e.VirtualKey, (IntPtr)lParam, _overlayHasKeyboard());
    }

    public void Character(char value) =>
        HotkeyService.HandleKeyMessage(WmChar, (IntPtr)value, (IntPtr)1, _overlayHasKeyboard());

    public void FocusLost()
    {
        Win32KeyStateStandIn.Focused = false;
        HotkeyService.HandleKeyMessage(WmKillFocus, IntPtr.Zero, IntPtr.Zero, false);
    }

    public void KeyDown(OverlayKey key, OverlayModifiers modifiers)
    {
    }

    public void KeyUp(OverlayKey key)
    {
    }
}
```

- [ ] **Step 4: Wire it in `RynthClientExtension`.** Fields beside `_keyboardSink`:

```csharp
    private IDisposable? _hotkeySink;
    private Func<bool>? _isTyping;
```

In `Attach`, after `if (context.Overlay is { } chatOverlay) StartChat(context, chatOverlay);`:

```csharp
        if (context.Overlay is { } keyOverlay)
            StartHotkeys(context, keyOverlay);
```

and the method (beside `StartChat`):

```csharp
    /// <summary>RynthCore's hotkeys: every key press offered to their HotkeyService before OpenAC sees it.</summary>
    private void StartHotkeys(ClientExtensionContext context, IClientOverlay overlay)
    {
        if (context.Input is { } input)
        {
            _isTyping = () => input.IsTyping;
            AcKeyboardFocus.IsTyping = _isTyping;
            AcKeyboardFocus.Where = () => input.TypingDescription;
        }

        _hotkeySink = overlay.AddKeyboardSink(new RynthHotkeyKeyboard(() => overlay.WantsKeyboard));
    }
```

In `Dispose`, beside the `_keyboardSink` release:

```csharp
        _hotkeySink?.Dispose();
        _hotkeySink = null;
        if (_isTyping is not null && AcKeyboardFocus.IsTyping == _isTyping)
        {
            AcKeyboardFocus.IsTyping = null;
            AcKeyboardFocus.Where = null;
        }
        _isTyping = null;
        RynthCore.Engine.Hotkeys.HotkeyService.Shutdown();
```

(`using RynthCore.Engine.Compatibility;` is already in the file for `RynthCoreChatCommands`; add it if not.)

`overlay.WantsKeyboard` includes RynthChat's capture and ImGui's keyboard capture — their "Rynth text focus". Our own sink's `Capturing` is false, so it never makes itself typing.

- [ ] **Step 5: Run**

Run: `dotnet test tests/AcDream.Rynth.Host.Tests --filter RynthHotkeysTests`, then `dotnet test tests/AcDream.Rynth.Host.Tests`, then `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~Extensions"`.
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.Rynth.Host/RynthHotkeyKeyboard.cs src/AcDream.Rynth.Host/RynthClientExtension.cs tests/AcDream.Rynth.Host.Tests/RynthHotkeysTests.cs
git commit -m "feat(rynth): key presses reach RynthCore's HotkeyService; bound keys run and are taken

Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz"
```

---

### Task 6: Gate and live check (user)

**Goal:** The full build and test gate show no new failures, and the user confirms hotkeys work in the live client.

**Files:**
- Modify: `docs/superpowers/plans/2026-10-20-rynth-hotkeys.md` (tick tasks, record results)

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → 0 errors.
- [ ] `dotnet test tests/AcDream.Rynth.Host.Tests`, `tests/AcDream.App.Tests`, `tests/AcDream.Tools.RynthInteropRedirect.Tests` → no failures beyond the known Mac baseline (70 failures on main 2026-10-20, none in these Rynth/overlay areas).
- [ ] Live (user runs the client; ask first, local ACE only): Settings > Hotkeys lists F6/F7/F8; Ctrl+1 → `/ra help` prints RynthAi help once and Ctrl+1 does nothing else; with the chat entry open Ctrl+1 does not fire; F8 restores mana and Attributes does not open; a `panel:Dungeon Map` binding toggles the map; an Option(Alt)+key combo fires; `/rc hotkey list` lists the bindings.

**Verify:** the commands above; the live checklist answered by the user.

**Steps:**

- [ ] **Step 1:** Run the build and the three test projects; compare failures to the baseline; record counts in this plan.
- [ ] **Step 2:** Ask the user whether they are available for a live check (never launch a client without asking). Give them the checklist above; record each result here.
- [ ] **Step 3:** Commit the plan update.
