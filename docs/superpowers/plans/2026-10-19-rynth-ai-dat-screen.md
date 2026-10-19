# RynthAi Dat Geometry and Screen Slots (RynthAi sub-project #6) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** RynthAi's dat-backed features (line of sight, indoor detours, dungeon map, terrain overlay, hazards, component database) find OpenAC's dats, and its radar walls and 2D nav path draw through three new screen slots.

**Architecture:**
- Task 1 adds a managed `--redirect-type From=To` mode to `tools/RynthInteropRedirect` and a registry stand-in compiled into the RynthAi wrapper, so their `Registry.LocalMachine…InstallPath` read answers a path the host sets.
- Task 2 carries the client's dat directory to the stand-in (`ClientExtensionContext.DatDirectory`).
- Task 3 adds `IClientScreen` (viewport, projection, radar disc, all in logical points) to `AcDream.ClientExtensions`, implemented in the App.
- Task 4 binds `WorldToScreenFn`, `GetViewportSizeFn`, `GetRadarRectFn` in `AcDream.Rynth.Host` over `IClientScreen`.
- Task 5 is the live check, run with the user.

**Tech Stack:** .NET 10 (`~/.dotnet/dotnet`), Mono.Cecil (already used by the redirect tool), xunit v3 (VSTest runner: `--filter "FullyQualifiedName~X"`), RynthCore/RynthSuite submodules (read only).

**Spec:** `docs/superpowers/specs/2026-10-19-rynth-ai-dat-screen-design.md` (approved 2026-10-19). RynthAi split: #1–#5 done, **#6 this plan**; outdoor detours (RynthNav) are a later sub-project.

## Global Constraints

- Repo `~/code/OpenAC/OpenAC` (the fork, `davidc-smith/OpenAC`). Work on branch `feat/rynth-ai-dat-screen` (already created from `main`, holds the spec commit). Do not push.
- Use `~/.dotnet/dotnet` (the system `dotnet` lacks SDK 10.0.300).
- **Never edit, copy or move anything inside `external/RynthCore` or `external/RynthSuite`.** `git status --porcelain external` must stay empty.
- `AcDream.App`, `AcDream.ClientExtensions`, `AcDream.Runtime` and `AcDream.Rynth.Host` build with warnings as errors and analyzers on. New code there must build clean. The RynthAi wrapper (`src/AcDream.Rynth.Plugin.RynthAi`) has warnings-as-errors and analyzers off; the redirect tool builds with the repo defaults.
- **Table rule:** every `[UnmanagedCallersOnly]` table function null-checks its pointers, catches every exception, logs through `Fail`, and returns 0. An exception crossing that boundary ends the process. Follow `src/AcDream.Rynth.Host/RynthApiTable.State.cs` exactly (`RynthApiTable? table = _current; if (table is null || p is null) return 0; try { … } catch (Exception error) { table.Fail(nameof(X), error); return 0; }`).
- `RynthApiTable.ApiVersion` stays **86**. **No plugin contract change:** `AcDream.Plugin.Abstractions` is not touched; `ForkContractVersion` stays `0.1.25-fork.9`.
- **Slots set by this plan:** `WorldToScreenFn`, `GetViewportSizeFn`, `GetRadarRectFn`. **Still unset after it:** `NativeAttackFn`, `StopMovementFn`, `DoMovementFn`, `SetAutonomyLevelFn`, `SetRadarSuppressedFn`, `GetPluginInterfaceFn`, `IsPlayerReadyFn`, `GetScreenModeFn`.
- **Units:** all screen values are logical points (`window.Size`), the space ImGui's `DisplaySize` and the retained UI use. No framebuffer/Retina scaling anywhere in this plan.
- **World frame for WorldToScreen:** RynthCore's — metres from the player's landblock, X east, Y up, Z north — converted exactly as `RynthNav3D` does (`RuntimeNavigationPositions.FromCellLocal(cell, new Vector3(x, z, y))`).
- All table functions run on the game thread (RynthAi renders from the overlay callback on that thread too). No locking.
- Rynth host tests run without parallelism (assembly attribute in `tests/AcDream.Rynth.Host.Tests/FakeGameView.cs`). Only one `RynthApiTable` can be live at a time, so each test disposes its table or host. `RegistryStandIn.InstallPath` is static: every test that sets it restores `null` in a `finally`/`Dispose`.
- Known unrelated failures exist in `AcDream.App.Tests` (installed dats, offscreen Vulkan). Run App tests with each task's filter. `AcDream.Rynth.Host.Tests` must be fully green.
- Commit messages end with the line `Claude-Session: https://claude.ai/code/session_01X3ryhqAVUCdtME4PCQLPJi`.
- Never launch a client without asking the user first. Only the local ACE server 10.10.20.20, never Dreamweave. Never print passwords (`~/OpenAC-dev/dev-client.env`).

**User decisions (already made):**
- Scope: "B" — dat path + `GetRadarRect` / `WorldToScreen` / `GetViewportSize`; outdoor detours (GetPluginInterface + RynthNav + nav tiles) later.
- Dat path: "Yeah A" — registry stand-in through RynthInteropRedirect, not ProcessPath redirection, not symlinks.
- Section 1 approved ("Yes - please proceed to implementation plan"); spec incl. section 2 approved ("Looks good, proceed").
- Standing rule: their code is compiled unchanged; stand-ins, never rewrites; we must be able to pull their daily changes.

---

### Task 1: Registry stand-in and the `--redirect-type` mode

**Goal:** RynthAi's two registry reads (`GeometryLoader.FindACFolder`, `ComponentDatabase.FindPortalDat`) call a stand-in that answers `InstallPath` from a static the host sets, on every OS.

**Files:**
- Create: `src/AcDream.Rynth.Plugin.RynthAi/StandIns/RegistryStandIn.cs`
- Modify: `src/AcDream.Rynth.Plugin.RynthAi/AcDream.Rynth.Plugin.RynthAi.csproj` (compile `StandIns\**`; pass `--redirect-type` twice in `MakeWin32Inert`; update the comment's "advapi32" sentence only if wrong — `RynthInstallPaths`' advapi32 P/Invoke stays)
- Modify: `tools/RynthInteropRedirect/Program.cs`
- Create: `tests/AcDream.Rynth.Host.Tests/RegistryStandInTests.cs`
- Modify: `tests/AcDream.Rynth.Host.Tests/RynthAiInteropTests.cs`

**Acceptance Criteria:**
- [ ] `RegistryKeyStandIn.OpenSubKey` answers a key only for `SOFTWARE\WOW6432Node\Turbine\Asheron's Call` (ordinal-ignore-case) while `InstallPath` is set; `GetValue("InstallPath")` (ignore case) answers it; anything else null.
- [ ] The built `AcDream.Rynth.Plugin.RynthAi.dll` has no type or member reference to `Microsoft.Win32.Registry` or `Microsoft.Win32.RegistryKey`.
- [ ] `ComponentDatabase.FindPortalDat()` (invoked by reflection) returns `<InstallPath>/client_portal.dat` for a temp dir holding an empty `client_portal.dat`.
- [ ] With `ACDREAM_DAT_DIR` (or `~/AsheronsCall`) present, `new GeometryLoader().Initialize()` with no argument returns true; the test is skipped (returns early with a message) when no dats exist.
- [ ] The tool fails (exit 1, message names the member) when the `<to>` type lacks a member the assembly calls; a rerun on the rewritten assembly reports 0 types redirected and does not rewrite.
- [ ] Existing `RynthAiInteropTests` still pass.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RegistryStandIn|FullyQualifiedName~RynthAiInteropTests"` → all pass; build output shows `… 2 type(s) redirected` for the RynthAi wrapper.

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.Rynth.Host.Tests/RegistryStandInTests.cs`:

```csharp
using AcDream.Rynth.Plugin.RynthAi.StandIns;

namespace AcDream.Rynth.Host.Tests;

public sealed class RegistryStandInTests : IDisposable
{
    private const string AcKey = @"SOFTWARE\WOW6432Node\Turbine\Asheron's Call";

    public void Dispose() => RegistryStandIn.InstallPath = null;

    [Fact]
    public void NoKeyWhileThePathIsUnset()
    {
        RegistryStandIn.InstallPath = null;
        Assert.Null(RegistryStandIn.LocalMachine.OpenSubKey(AcKey));
    }

    [Fact]
    public void TheAcKeyAnswersInstallPath()
    {
        RegistryStandIn.InstallPath = "/dats";
        using RegistryKeyStandIn? key = RegistryStandIn.LocalMachine.OpenSubKey(AcKey.ToUpperInvariant());
        Assert.NotNull(key);
        Assert.Equal("/dats", key!.GetValue("installpath"));
        Assert.Null(key.GetValue("Version"));
    }

    [Fact]
    public void OtherKeysAreMissing()
    {
        RegistryStandIn.InstallPath = "/dats";
        Assert.Null(RegistryStandIn.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft"));
    }
}
```

Append to `RynthAiInteropTests` (keep the existing tests; add `using Mono.Cecil;` is already there):

```csharp
    [Fact]
    public void NoRegistryReferenceIsLeft()
    {
        using ModuleDefinition module = ModuleDefinition.ReadModule(Theirs.Location);
        string[] left = module.GetTypeReferences().Select(t => t.FullName)
            .Concat(module.GetMemberReferences().Select(m => m.DeclaringType.FullName))
            .Where(name => name is "Microsoft.Win32.Registry" or "Microsoft.Win32.RegistryKey")
            .Distinct()
            .ToArray();
        Assert.Empty(left);
    }

    [Fact]
    public void ComponentDatabaseFindsThePortalDatThroughTheStandIn()
    {
        string dir = Directory.CreateTempSubdirectory("rynth-reg").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "client_portal.dat"), []);
            AcDream.Rynth.Plugin.RynthAi.StandIns.RegistryStandIn.InstallPath = dir;
            Type db = Theirs.GetType("RynthCore.Plugin.RynthAi.ComponentDatabase", throwOnError: true)!;
            MethodInfo find = db.GetMethod("FindPortalDat", BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.Equal(Path.Combine(dir, "client_portal.dat"), find.Invoke(null, null));
        }
        finally
        {
            AcDream.Rynth.Plugin.RynthAi.StandIns.RegistryStandIn.InstallPath = null;
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GeometryLoaderOpensTheInstalledDatsThroughTheStandIn()
    {
        string? dats = Environment.GetEnvironmentVariable("ACDREAM_DAT_DIR");
        if (string.IsNullOrWhiteSpace(dats))
            dats = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AsheronsCall");
        if (!File.Exists(Path.Combine(dats, "client_cell_1.dat")))
        {
            Console.WriteLine($"skipped: no dats at {dats}");
            return;
        }

        AcDream.Rynth.Plugin.RynthAi.StandIns.RegistryStandIn.InstallPath = dats;
        try
        {
            using var loader = new RynthCore.Plugin.RynthAi.Raycasting.GeometryLoader();
            Assert.True(loader.Initialize());
        }
        finally
        {
            AcDream.Rynth.Plugin.RynthAi.StandIns.RegistryStandIn.InstallPath = null;
        }
    }
```

If `GeometryLoader`/`ComponentDatabase.FindPortalDat` reach a `C:\` path first on macOS they simply skip it (`Directory.Exists` false), so the stand-in is what answers.

- [ ] **Step 2: Run the tests to see them fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RegistryStandIn|FullyQualifiedName~RynthAiInteropTests"`
Expected: build error — `AcDream.Rynth.Plugin.RynthAi.StandIns` does not exist.

- [ ] **Step 3: Write the stand-in**

`src/AcDream.Rynth.Plugin.RynthAi/StandIns/RegistryStandIn.cs`:

```csharp
namespace AcDream.Rynth.Plugin.RynthAi.StandIns;

/// <summary>
/// Stands in for <c>Microsoft.Win32.Registry</c> in RynthAi (tools/RynthInteropRedirect
/// --redirect-type, on every OS). RynthAi reads one value, the Asheron's Call
/// InstallPath, to find the dats; the host sets <see cref="InstallPath"/> to the
/// client's dat directory before RynthAi starts. Every other key is missing.
/// </summary>
public static class RegistryStandIn
{
    internal const string AcKey = @"SOFTWARE\WOW6432Node\Turbine\Asheron's Call";

    /// <summary>The root RynthAi opens its key from (their <c>Registry.LocalMachine</c>).</summary>
    public static readonly RegistryKeyStandIn LocalMachine = new(isRoot: true);

    /// <summary>The dat directory; null answers no key, as a machine without AC installed.</summary>
    public static string? InstallPath { get; set; }
}

/// <summary>Stands in for <c>Microsoft.Win32.RegistryKey</c>: only the members RynthAi calls.</summary>
public sealed class RegistryKeyStandIn : IDisposable
{
    private readonly bool _isRoot;

    internal RegistryKeyStandIn(bool isRoot) => _isRoot = isRoot;

    public RegistryKeyStandIn? OpenSubKey(string name) =>
        _isRoot && RegistryStandIn.InstallPath is not null
            && string.Equals(name, RegistryStandIn.AcKey, StringComparison.OrdinalIgnoreCase)
            ? new RegistryKeyStandIn(isRoot: false)
            : null;

    public object? GetValue(string name) =>
        !_isRoot && string.Equals(name, "InstallPath", StringComparison.OrdinalIgnoreCase)
            ? RegistryStandIn.InstallPath
            : null;

    public void Dispose()
    {
    }
}
```

In the wrapper csproj add to the first `<ItemGroup>` of compile items:

```xml
    <Compile Include="StandIns\**\*.cs" />
```

- [ ] **Step 4: Add `--redirect-type` to the tool**

In `tools/RynthInteropRedirect/Program.cs`:

1. Usage/doc: `[--inert <module,...>] [--plain-threadstatic-swaps] [--redirect-type <from>=<to>]...`. Add to the summary: "With `--redirect-type`, every use of type `<from>` (a referenced type, by full name) becomes `<to>` (a type in the assembly): its members by name, signature and static-ness, and every field, local, parameter, return and type operand of that type. A member `<to>` lacks fails the run."
2. Replace the argument parsing with a loop over `args[3..]` that accepts `--inert <list>` (at most once), `--plain-threadstatic-swaps` (at most once) and `--redirect-type <from>=<to>` (repeatable, both sides non-empty); anything else prints the usage and returns 2.
3. Pass the pairs to `Redirect(...)`, which calls `int types = RedirectTypes(module, typeMaps)` after the swaps, includes it in the write condition (`redirected + inerted + swaps + types > 0`) and returns it; the success line appends `, {types} type(s) redirected` when any `--redirect-type` was given.
4. Add:

```csharp
    /// <summary>
    /// Rebinds every use of each <c>from</c> type to its <c>to</c> type in the module.
    /// Returns how many <c>from</c> types were found in use.
    /// </summary>
    private static int RedirectTypes(ModuleDefinition module, IReadOnlyList<(string From, string To)> maps)
    {
        if (maps.Count == 0)
            return 0;
        var targets = maps.ToDictionary(
            m => m.From,
            m => module.GetType(m.To) ?? throw new InvalidOperationException($"{m.To} is not in {module.Name}"));
        var used = new HashSet<string>();
        var standIns = targets.Values.ToHashSet();

        TypeReference Map(TypeReference type)
        {
            switch (type)
            {
                case ByReferenceType byRef:
                    return new ByReferenceType(Map(byRef.ElementType));
                case ArrayType array:
                    return new ArrayType(Map(array.ElementType), array.Rank);
                case GenericInstanceType generic:
                    var copy = new GenericInstanceType(generic.ElementType);
                    foreach (TypeReference argument in generic.GenericArguments)
                        copy.GenericArguments.Add(Map(argument));
                    return copy;
            }

            if (!targets.TryGetValue(type.FullName, out TypeDefinition? target))
                return type;
            used.Add(type.FullName);
            return target;
        }

        foreach (TypeDefinition type in module.GetTypes().Where(t => !standIns.Contains(t)).ToList())
        {
            foreach (FieldDefinition field in type.Fields)
                field.FieldType = Map(field.FieldType);
            foreach (PropertyDefinition property in type.Properties)
                property.PropertyType = Map(property.PropertyType);
            foreach (MethodDefinition method in type.Methods)
            {
                method.ReturnType = Map(method.ReturnType);
                foreach (ParameterDefinition parameter in method.Parameters)
                    parameter.ParameterType = Map(parameter.ParameterType);
                if (!method.HasBody)
                    continue;
                foreach (VariableDefinition variable in method.Body.Variables)
                    variable.VariableType = Map(variable.VariableType);
                foreach (Instruction instruction in method.Body.Instructions)
                {
                    switch (instruction.Operand)
                    {
                        case MethodReference callee when targets.TryGetValue(callee.DeclaringType.FullName, out TypeDefinition? to):
                            used.Add(callee.DeclaringType.FullName);
                            instruction.Operand = FindMember(to, callee, Map);
                            break;
                        case FieldReference field when targets.TryGetValue(field.DeclaringType.FullName, out TypeDefinition? to):
                            used.Add(field.DeclaringType.FullName);
                            instruction.Operand = to.Fields.FirstOrDefault(f => f.Name == field.Name && f.IsStatic == field.Resolve()?.IsStatic)
                                ?? throw new InvalidOperationException($"{to.FullName} has no field {field.Name}");
                            break;
                        case TypeReference operand:
                            instruction.Operand = Map(operand);
                            break;
                    }
                }
            }
        }

        return used.Count;
    }

    private static MethodDefinition FindMember(TypeDefinition to, MethodReference callee, Func<TypeReference, TypeReference> map)
    {
        string[] parameters = callee.Parameters.Select(p => map(p.ParameterType).FullName).ToArray();
        return to.Methods.FirstOrDefault(candidate =>
                   candidate.IsPublic && candidate.Name == callee.Name && candidate.HasThis == callee.HasThis
                   && candidate.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters))
               ?? throw new InvalidOperationException(
                   $"{to.FullName} has no {(callee.HasThis ? "instance" : "static")} {callee.Name}({string.Join(", ", parameters)})");
    }
```

Notes for the implementer:
- `Registry.LocalMachine` is a `public static readonly` **field**, so it arrives as a `FieldReference` (`ldsfld`); the stand-in's `LocalMachine` is a field for that reason. `field.Resolve()` may need the reference assemblies; the resolver is already set up from the references file. If it cannot resolve, compare static-ness by opcode instead (`ldsfld`/`stsfld`/`ldsflda` = static).
- `callvirt` on the stand-in's non-virtual instance methods is valid IL (null check + call); leave the opcode.
- `using (var key = …)` compiles to `callvirt IDisposable::Dispose()` on the retyped local, which the stand-in implements.
- Keep `MakeThreadStaticSwapsPlain`, `--inert` and the P/Invoke redirect untouched.

- [ ] **Step 5: Wire it into the wrapper build**

In `src/AcDream.Rynth.Plugin.RynthAi/AcDream.Rynth.Plugin.RynthAi.csproj`, `MakeWin32Inert`'s `Exec` gains, after `--plain-threadstatic-swaps`:

```
--redirect-type Microsoft.Win32.Registry=AcDream.Rynth.Plugin.RynthAi.StandIns.RegistryStandIn --redirect-type Microsoft.Win32.RegistryKey=AcDream.Rynth.Plugin.RynthAi.StandIns.RegistryKeyStandIn
```

Update the comment above the target: their two `Registry` install-path reads go to `StandIns/RegistryStandIn` (the host sets the dat directory); `RynthInstallPaths`' advapi32 call stays. Delete `src/AcDream.Rynth.Plugin.RynthAi/obj/*/win32-inert.stamp` is **not** needed: the target's input is the freshly compiled assembly.

- [ ] **Step 6: Run the tests**

Run: `~/.dotnet/dotnet build src/AcDream.Rynth.Plugin.RynthAi 2>&1 | grep RynthInteropRedirect`
Expected: `… 13 made inert, 2 thread-static swap(s) made plain, 2 type(s) redirected`

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RegistryStandIn|FullyQualifiedName~RynthAiInteropTests"`
Expected: all pass (the GeometryLoader test prints nothing and passes on this Mac, where `~/AsheronsCall` exists).

Check the failure path by hand: temporarily rename `GetValue` in the stand-in to `GetValue2`, build the wrapper, expect `error: AcDream.Rynth.Plugin.RynthAi.StandIns.RegistryKeyStandIn has no instance GetValue(System.String)` and a failed build; restore it.

- [ ] **Step 7: Full host suite, then commit**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass. `git status --porcelain external` → empty.

```bash
git add tools/RynthInteropRedirect/Program.cs src/AcDream.Rynth.Plugin.RynthAi tests/AcDream.Rynth.Host.Tests/RegistryStandInTests.cs tests/AcDream.Rynth.Host.Tests/RynthAiInteropTests.cs
git commit -m "feat(rynth): registry stand-in for RynthAi's dat folder lookup (--redirect-type)

Claude-Session: https://claude.ai/code/session_01X3ryhqAVUCdtME4PCQLPJi"
```

---

### Task 2: Carry the client's dat directory to RynthAi

**Goal:** The App's dat directory reaches `RegistryStandIn.InstallPath` before RynthAi starts, so its raycast init runs on real dats.

**Files:**
- Modify: `src/AcDream.ClientExtensions/ClientExtensionContext.cs` (add `DatDirectory`)
- Modify: `src/AcDream.App/Extensions/ClientExtensionHost.cs` (`Attach` parameter `string? datDirectory = null` → context)
- Modify: `src/AcDream.App/Rendering/GameWindow.cs:540` (pass `datDirectory: _datDir`)
- Modify: `src/AcDream.Rynth.Host/RynthClientExtension.cs` (set the stand-in before `new RynthHost`; clear it in `Dispose`)
- Test: `tests/AcDream.Rynth.Host.Tests/RynthHostTests.cs` (or the existing test that attaches `RynthClientExtension`; add there)

**Acceptance Criteria:**
- [ ] `ClientExtensionContext.DatDirectory` exists (`string?`, init-only, XML doc: "The directory the client read its dats from; null in a host without dats.").
- [ ] After `RynthClientExtension.Attach` with `DatDirectory = "/x"`, `RegistryStandIn.InstallPath == "/x"`; after `Dispose`, it is null.
- [ ] `GameWindow` passes `_datDir`.
- [ ] App and host build clean (warnings as errors).

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass; `~/.dotnet/dotnet build src/AcDream.App` → 0 errors.

**Steps:**

- [ ] **Step 1: Failing test.** Find the existing test that attaches a `RynthClientExtension` to a `ClientExtensionContext` (`grep -rn "new RynthClientExtension" tests/AcDream.Rynth.Host.Tests`). Next to it add a test that builds the same context with `DatDirectory = "/x"`, attaches, asserts `RegistryStandIn.InstallPath == "/x"`, disposes the extension, asserts null. Restore `InstallPath = null` in `finally`. Run it: fails to compile (`DatDirectory` missing).

- [ ] **Step 2: Context property.** In `ClientExtensionContext` after `WorldTriangles`:

```csharp
    /// <summary>The directory the client read its dats from; null in a host without dats.</summary>
    public string? DatDirectory { get; init; }
```

- [ ] **Step 3: App.** `ClientExtensionHost.Attach` gains a trailing `string? datDirectory = null` parameter and sets `DatDirectory = datDirectory` in the initializer. `GameWindow.cs:540` adds `datDirectory: _datDir` to the call.

- [ ] **Step 4: Extension.** In `RynthClientExtension.Attach`, right after `RynthInstallFolders.Apply();`:

```csharp
        // RynthAi finds the dats through its registry read (StandIns/RegistryStandIn); it
        // starts loading them on a pool thread in Startup, so this is set first.
        AcDream.Rynth.Plugin.RynthAi.StandIns.RegistryStandIn.InstallPath = context.DatDirectory;
```

In `Dispose`, after the host is disposed: `AcDream.Rynth.Plugin.RynthAi.StandIns.RegistryStandIn.InstallPath = null;`.

- [ ] **Step 5: Run, then commit.**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass. `~/.dotnet/dotnet build src/AcDream.App` → 0 errors.

```bash
git add src/AcDream.ClientExtensions/ClientExtensionContext.cs src/AcDream.App/Extensions/ClientExtensionHost.cs src/AcDream.App/Rendering/GameWindow.cs src/AcDream.Rynth.Host/RynthClientExtension.cs tests/AcDream.Rynth.Host.Tests
git commit -m "feat(rynth): pass the client's dat directory to RynthAi's registry stand-in

Claude-Session: https://claude.ai/code/session_01X3ryhqAVUCdtME4PCQLPJi"
```

---

### Task 3: `IClientScreen` — viewport, projection and radar disc from the App

**Goal:** Extensions can ask the client for the viewport size, the screen point of a navigation position, and the radar disc, all in logical points.

**Files:**
- Create: `src/AcDream.ClientExtensions/IClientScreen.cs`
- Modify: `src/AcDream.ClientExtensions/ClientExtensionContext.cs` (add `Screen`)
- Create: `src/AcDream.App/Extensions/ClientScreen.cs`
- Modify: `src/AcDream.App/Extensions/ClientExtensionHost.cs` (`Attach` parameter `IClientScreen? screen = null` → context)
- Modify: `src/AcDream.App/Rendering/GameWindow.cs` (create the `ClientScreen`, pass it at line 540, bind its camera where `SelectionCameraSource` is created (~line 1403), bind the UI runtime where `_retailUiRuntime` is assigned (~line 1005), unbind on teardown where `_retailUiRuntime` is cleared)
- Test: `tests/AcDream.App.Tests/Extensions/ClientScreenTests.cs`

**Acceptance Criteria:**
- [ ] `TryGetViewportSize` returns the camera snapshot's viewport (`window.Size`); false before a camera is bound or when either side is 0.
- [ ] `TryProject` is false before the camera is bound or while `LiveWorldOriginState.IsKnown` is false; otherwise projects `PluginNavigationProjection.ToWorld(position, CenterX, CenterY)` with `View * Projection`: false when clip W ≤ 0.001, else `x = (ndcX * 0.5 + 0.5) * vw`, `y = (1 − (ndcY * 0.5 + 0.5)) * vh` (same as `ScreenProjection`), off-screen points included.
- [ ] `TryGetRadarDisc` is false before the UI runtime is bound, when `WindowNames.Radar` is not registered or not visible; else centre = radar `ContentRoot.ScreenPosition + ((UiRadar)ContentRoot).Center`, radius = `RadarController.RadarPixelRadius`.
- [ ] Tests cover: a known camera (e.g. `Matrix4x4.CreateLookAt` + `CreatePerspectiveFieldOfView`, viewport 800×600) projects a point straight ahead to (400, 300) and a point behind the camera to false; unknown origin → false; unbound → all false.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ClientScreenTests"` → all pass.

**Steps:**

- [ ] **Step 1: The interface.** `src/AcDream.ClientExtensions/IClientScreen.cs`:

```csharp
using System.Numerics;
using AcDream.Plugin.Abstractions;

namespace AcDream.ClientExtensions;

/// <summary>
/// The client's 3D view and radar as an extension sees them, in logical points (the
/// space the overlay and the retained UI draw in; not framebuffer pixels).
/// </summary>
public interface IClientScreen
{
    /// <summary>The window size; false before the world view exists.</summary>
    bool TryGetViewportSize(out Vector2 size);

    /// <summary>
    /// Where a world position falls on screen, Y down. False when it is behind the
    /// camera or the view is not ready; a point beside the screen still answers true.
    /// </summary>
    bool TryProject(PluginNavigationPosition position, out Vector2 screen);

    /// <summary>The radar disc's centre and radius on screen; false while the radar is hidden.</summary>
    bool TryGetRadarDisc(out Vector2 centre, out float radius);
}
```

`ClientExtensionContext` gains:

```csharp
    /// <summary>The client's view and radar on screen; null in a host without a window.</summary>
    public IClientScreen? Screen { get; init; }
```

- [ ] **Step 2: Failing tests.** `tests/AcDream.App.Tests/Extensions/ClientScreenTests.cs` drives `ClientScreen` through its test seam (Step 3): a `Func<(Matrix4x4 View, Matrix4x4 Projection, Vector2 Viewport)>` camera and a `LiveWorldOriginState`. Build a position whose `ToWorld` lands at a known world point: with `origin.TryInitialize(127, 127)` the conversion is `EW*240 + 84`, `NS*240 + 84`, `Elevation*240`; pick camera eye/target in that world space (Z up — check `PluginNavigationProjection.ToWorld`: X = east, Y = north, Z = elevation). Cases:
  - unbound camera → `TryGetViewportSize`/`TryProject` false;
  - origin not known → `TryProject` false;
  - eye at world (84, 84, 10) looking at (84, 84 + 100, 10), up (0,0,1), 60° FOV, 800×600: a position 50 m north of the eye projects to (400, 300) within 0.5; a position 50 m south → false;
  - `TryGetRadarDisc` with no UI runtime → false.
  Run them: compile failure (`ClientScreen` missing).

- [ ] **Step 3: Implement `ClientScreen`.** `src/AcDream.App/Extensions/ClientScreen.cs`:

```csharp
using System.Numerics;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.App.World;
using AcDream.ClientExtensions;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Extensions;

/// <summary>
/// <see cref="IClientScreen"/> over the world camera, the live world origin and the
/// retained radar window. Its sources arrive later than the extensions attach, so
/// each answers false until bound.
/// </summary>
internal sealed class ClientScreen(LiveWorldOriginState origin) : IClientScreen
{
    private Func<(Matrix4x4 View, Matrix4x4 Projection, Vector2 Viewport)>? _camera;
    private RetailWindowManager? _windows;

    public void BindCamera(Func<(Matrix4x4 View, Matrix4x4 Projection, Vector2 Viewport)>? camera) => _camera = camera;

    public void BindWindows(RetailWindowManager? windows) => _windows = windows;

    public bool TryGetViewportSize(out Vector2 size)
    {
        size = default;
        if (_camera is null)
            return false;
        Vector2 viewport = _camera().Viewport;
        if (!(viewport.X > 0 && viewport.Y > 0))
            return false;
        size = viewport;
        return true;
    }

    public bool TryProject(PluginNavigationPosition position, out Vector2 screen)
    {
        screen = default;
        if (_camera is null || !origin.IsKnown)
            return false;
        (Matrix4x4 view, Matrix4x4 projection, Vector2 viewport) = _camera();
        if (viewport.X <= 0 || viewport.Y <= 0)
            return false;
        Vector3 world = PluginNavigationProjection.ToWorld(position, origin.CenterX, origin.CenterY);
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), view * projection);
        if (clip.W <= 0.001f)
            return false;
        float ndcX = clip.X / clip.W, ndcY = clip.Y / clip.W;
        screen = new Vector2((ndcX * 0.5f + 0.5f) * viewport.X, (1f - (ndcY * 0.5f + 0.5f)) * viewport.Y);
        return true;
    }

    public bool TryGetRadarDisc(out Vector2 centre, out float radius)
    {
        centre = default;
        radius = 0f;
        if (_windows is not { } windows
            || !windows.IsVisible(WindowNames.Radar)
            || !windows.TryGet(WindowNames.Radar, out RetailWindowHandle handle)
            || handle.ContentRoot is not UiRadar radar)
            return false;
        centre = radar.ScreenPosition + radar.Center;
        radius = RadarController.RadarPixelRadius;
        return true;
    }
}
```

Check while implementing: the exact namespace of `WindowNames`, `RetailWindowHandle`, `UiRadar` (`grep -rn "class WindowNames\|class RetailWindowHandle\|class UiRadar" src/AcDream.App`), and that `RetailUiRuntime.Host.WindowManager` is the manager GameWindow passes to `BindWindows` (`RetailUiRuntime.cs:590` exposes `Host`; the retained `UiHost` has `WindowManager`, as `RetailUiRuntime.cs:532` uses). `UiRadar.Center` is in the radar root's local space (set from the disc element by `RadarController.ResolveCenter`). If the test project cannot see `internal` App types, it already has `InternalsVisibleTo` for `AcDream.App.Tests` — confirm with `grep -n InternalsVisibleTo src/AcDream.App/AcDream.App.csproj`.

- [ ] **Step 4: Wire it in `GameWindow`.**
  - Field: `private readonly AcDream.App.Extensions.ClientScreen _clientScreen;`, assigned in the constructor body as `_clientScreen = new(_liveWorldOrigin);` (field initializers cannot reference the instance field `_liveWorldOrigin`).
  - `ClientExtensionHost.Attach` gains `IClientScreen? screen = null` → `Screen = screen`; line 540 passes `screen: _clientScreen`.
  - Where `new SelectionCameraSource(hostInputCamera.CameraController, _window!, viewPlane)` is created (~line 1403), keep the instance in a local, call `_clientScreen.BindCamera(source.UiSnapshot)`, and return it.
  - Where `_retailUiRuntime = retained.Runtime;` (~line 1005): `_clientScreen.BindWindows(_retailUiRuntime.Host.WindowManager);`. Wherever `_retailUiRuntime` is set back to null or disposed: `_clientScreen.BindWindows(null);` and `_clientScreen.BindCamera(null);` beside it (`grep -n "_retailUiRuntime = null\|_retailUiRuntime?.Dispose" src/AcDream.App/Rendering/GameWindow.cs`).

- [ ] **Step 5: Run, then commit.**

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ClientScreenTests"` → all pass. `~/.dotnet/dotnet build src/AcDream.App` → 0 warnings as errors.

```bash
git add src/AcDream.ClientExtensions src/AcDream.App/Extensions src/AcDream.App/Rendering/GameWindow.cs tests/AcDream.App.Tests/Extensions/ClientScreenTests.cs
git commit -m "feat(extensions): IClientScreen — viewport, projection and radar disc in points

Claude-Session: https://claude.ai/code/session_01X3ryhqAVUCdtME4PCQLPJi"
```

---

### Task 4: Rynth screen slots — `WorldToScreenFn`, `GetViewportSizeFn`, `GetRadarRectFn`

**Goal:** RynthAi's `HasWorldToScreen`, `HasGetViewportSize` and `HasGetRadarRect` are true and answer from `IClientScreen`.

**Files:**
- Modify: `src/AcDream.Rynth.Host/IRynthGameView.cs` (three members)
- Modify: `src/AcDream.Rynth.Host/RuntimeRynthGameView.cs` (ctor parameter `IClientScreen? screen = null`; implementations)
- Modify: `src/AcDream.Rynth.Host/RynthClientExtension.cs` (pass `context.Screen`)
- Create: `src/AcDream.Rynth.Host/RynthApiTable.Screen.cs`
- Modify: `src/AcDream.Rynth.Host/RynthApiTable.cs` (call `BindScreenSlots()` in the bind list, ~lines 88-96)
- Modify: `tests/AcDream.Rynth.Host.Tests/FakeGameView.cs`
- Create: `tests/AcDream.Rynth.Host.Tests/RynthScreenSlotsTests.cs`

**Acceptance Criteria:**
- [ ] `IRynthGameView` gains `bool TryWorldToScreen(float x, float y, float z, out float screenX, out float screenY)`, `bool TryGetViewportSize(out uint width, out uint height)`, `bool TryGetRadarRect(out int x0, out int y0, out int x1, out int y1)`.
- [ ] `RuntimeRynthGameView.TryWorldToScreen`: false without a screen or without a player pose; else `screen.TryProject(RuntimeNavigationPositions.FromCellLocal(cell, new Vector3(x, z, y)), out p)` → `(p.X, p.Y)`.
- [ ] `TryGetViewportSize`: points rounded (`(uint)MathF.Round`), false when the screen says false.
- [ ] `TryGetRadarRect`: from `TryGetRadarDisc(c, r)`: `x0 = round(c.X − (r + 2))`, `y0 = round(c.Y − (r + 2))`, `x1 = round(c.X + r + 2)`, `y1 = round(c.Y + r + 2)`.
- [ ] Each table slot answers 0 with a null pointer, when the view says false, or when the view throws (one "threw" line through `Fail`); 1 with the values written otherwise.
- [ ] The table's `HasWorldToScreen`/`HasGetViewportSize`/`HasGetRadarRect` as seen by a `RynthCoreHost` built over it are true (assert through the SDK, as other slot tests do).
- [ ] `RuntimeRynthGameView` unit test: `TryWorldToScreen` passes a position whose cell-local origin is `(x, z, y)` to the screen (use a recording fake `IClientScreen`).

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass.

**Steps:**

- [ ] **Step 1: Failing tests.** In `RynthScreenSlotsTests.cs`, follow the pattern of the existing slot tests in `RynthApiTableTests.cs` (create the table over a `FakeGameView`, read the function pointers from `table.Native`, call them through `delegate* unmanaged[Cdecl]<…>`, dispose the table). Fake state: `FakeGameView.Screen` = `(bool Ok, float X, float Y)`, `Viewport` = `(bool Ok, uint W, uint H)`, `Radar` = `(bool Ok, int X0, int Y0, int X1, int Y1)`, each honouring `Throw`. Tests:
  - `WorldToScreen` writes `(sx, sy)` and returns 1; returns 0 when `Screen.Ok` is false, when a pointer is null, and when `Throw` (then the log has one line containing `WorldToScreen` and `threw`).
  - `GetViewportSize` and `GetRadarRect` the same way.
  - `HasWorldToScreen`, `HasGetViewportSize`, `HasGetRadarRect` true on `new RynthCoreHost(table.Native)` (or however the existing tests construct the SDK host — `grep -n "new RynthCoreHost" tests/AcDream.Rynth.Host.Tests`).
  - For `RuntimeRynthGameView`: if existing tests build one over a `GameRuntime` (`grep -rn "new RuntimeRynthGameView" tests`), add a test with a recording `IClientScreen` (records the `PluginNavigationPosition` it was asked for and answers `(10, 20)`): with the player at cell `0xA9B40001` origin (0,0,0), `TryWorldToScreen(1, 2, 3)` asks for `RuntimeNavigationPositions.FromCellLocal(0xA9B40001, new Vector3(1, 3, 2))` and answers (10, 20); radar disc centre (100, 50) radius 50 gives rect (48, -2, 152, 102). If no such runtime fixture exists, test the two pure conversions as `internal static` helpers on `RuntimeRynthGameView` (`ToNavigationPosition(cell, x, y, z)` and `RadarRect(centre, radius)`) instead.
  Run: compile failure.

- [ ] **Step 2: View members.** Add to `IRynthGameView`:

```csharp
    /// <summary>
    /// Where a point in metres from the player's landblock (X east, Y up, Z north) falls
    /// on screen, in points, Y down. False when behind the camera or the view is not ready.
    /// </summary>
    bool TryWorldToScreen(float x, float y, float z, out float screenX, out float screenY);

    /// <summary>The window size in points.</summary>
    bool TryGetViewportSize(out uint width, out uint height);

    /// <summary>
    /// The radar's box on screen in points, as RynthCore's surface box: RynthAi draws its
    /// walls at min(width, height) / 2 − 2 from the centre. False while the radar is hidden.
    /// </summary>
    bool TryGetRadarRect(out int x0, out int y0, out int x1, out int y1);
```

`RuntimeRynthGameView`: new ctor parameter `IClientScreen? screen = null` (last), stored in `_screen`:

```csharp
    public bool TryWorldToScreen(float x, float y, float z, out float screenX, out float screenY)
    {
        screenX = screenY = 0f;
        if (_screen is null || !TryGetPlayerPose(out uint cell, out _, out _))
            return false;
        if (!_screen.TryProject(ToNavigationPosition(cell, x, y, z), out Vector2 point))
            return false;
        (screenX, screenY) = (point.X, point.Y);
        return true;
    }

    // RynthCore's frame is Y up; the client's cell frame is Z up (as RynthNav3D converts).
    internal static PluginNavigationPosition ToNavigationPosition(uint cell, float x, float y, float z) =>
        RuntimeNavigationPositions.FromCellLocal(cell, new Vector3(x, z, y));

    public bool TryGetViewportSize(out uint width, out uint height)
    {
        width = height = 0;
        if (_screen is null || !_screen.TryGetViewportSize(out Vector2 size))
            return false;
        (width, height) = ((uint)MathF.Round(size.X), (uint)MathF.Round(size.Y));
        return width > 0 && height > 0;
    }

    public bool TryGetRadarRect(out int x0, out int y0, out int x1, out int y1)
    {
        x0 = y0 = x1 = y1 = 0;
        if (_screen is null || !_screen.TryGetRadarDisc(out Vector2 centre, out float radius))
            return false;
        (x0, y0, x1, y1) = RadarRect(centre, radius);
        return true;
    }

    // RynthAi draws at min(w, h) / 2 - 2, so the box is the disc plus 2 on every side.
    internal static (int X0, int Y0, int X1, int Y1) RadarRect(Vector2 centre, float radius)
    {
        float half = radius + 2f;
        return ((int)MathF.Round(centre.X - half), (int)MathF.Round(centre.Y - half),
            (int)MathF.Round(centre.X + half), (int)MathF.Round(centre.Y + half));
    }
```

(Add `using AcDream.ClientExtensions;` and `using AcDream.Runtime.Navigation;` as needed; `AcDream.Rynth.Host` already references `AcDream.ClientExtensions` through `RynthClientExtension`.)

`RynthClientExtension`: `new RuntimeRynthGameView(context.Runtime, context.SubmitChatCommand, context.Automation, context.Screen)`.

`FakeGameView`: the three members over the fake state from Step 1.

- [ ] **Step 3: Table slots.** `src/AcDream.Rynth.Host/RynthApiTable.Screen.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AcDream.Rynth.Host;

// The table's screen slots (RynthAi sub-project #6): radar walls and the 2D nav path.
public sealed unsafe partial class RynthApiTable
{
    private void BindScreenSlots()
    {
        Native->WorldToScreenFn = (IntPtr)(delegate* unmanaged[Cdecl]<float, float, float, float*, float*, int>)&WorldToScreen;
        Native->GetViewportSizeFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint*, uint*, int>)&GetViewportSize;
        Native->GetRadarRectFn = (IntPtr)(delegate* unmanaged[Cdecl]<int*, int*, int*, int*, int>)&GetRadarRect;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int WorldToScreen(float x, float y, float z, float* screenX, float* screenY)
    {
        RynthApiTable? table = _current;
        if (table is null || screenX is null || screenY is null)
            return 0;
        try
        {
            if (!table._view.TryWorldToScreen(x, y, z, out float sx, out float sy))
                return 0;
            *screenX = sx;
            *screenY = sy;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(WorldToScreen), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetViewportSize(uint* width, uint* height)
    {
        RynthApiTable? table = _current;
        if (table is null || width is null || height is null)
            return 0;
        try
        {
            if (!table._view.TryGetViewportSize(out uint w, out uint h))
                return 0;
            *width = w;
            *height = h;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetViewportSize), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetRadarRect(int* x0, int* y0, int* x1, int* y1)
    {
        RynthApiTable? table = _current;
        if (table is null || x0 is null || y0 is null || x1 is null || y1 is null)
            return 0;
        try
        {
            if (!table._view.TryGetRadarRect(out int left, out int top, out int right, out int bottom))
                return 0;
            *x0 = left;
            *y0 = top;
            *x1 = right;
            *y1 = bottom;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetRadarRect), error);
            return 0;
        }
    }
}
```

Check the exact field names on the native struct (`external/RynthCore/src/RynthCore.PluginSdk/RynthCoreApiNative.cs:64,65,106`) and the SDK's delegate shapes (`RynthCoreHost.cs:372-386`) match before building. Add `BindScreenSlots();` to the list in `RynthApiTable.cs`.

- [ ] **Step 4: Run, then commit.**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass. `~/.dotnet/dotnet build src/AcDream.App` → 0 errors. `git status --porcelain external` → empty.

```bash
git add src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests
git commit -m "feat(rynth): WorldToScreen, GetViewportSize and GetRadarRect slots over IClientScreen

Claude-Session: https://claude.ai/code/session_01X3ryhqAVUCdtME4PCQLPJi"
```

---

### Task 5: Live check (with the user)

> **USER-ORDERED GATE — NON-SKIPPABLE.** Live check with the user; ask before launching.

**Goal:** On the local ACE server, RynthAi's dat features and screen overlays work in the real client.

**Files:** none (findings go in the progress ledger; fixes get their own commits).

**Acceptance Criteria:**
- [ ] Ask the user before launching; they confirm they are available.
- [ ] Log shows `RynthAi: raycast init=True … acDir=(auto)` after login (dev root log or `~/.config/RynthCore` RynthAi log).
- [ ] Combat: with RynthAi on, a monster behind a wall is not chosen while one in sight is.
- [ ] Dungeon: enabling the dungeon map shows the baked map; radar walls (dashboard setting `ShowRadarWalls`) draw on the radar disc, rotate with the player, and follow the radar window when it is moved.
- [ ] Nav: an indoor route that gets stuck logs an indoor detour plan instead of only jumping; the 2D route overlay sits on the 3D Nav3D markers.
- [ ] Repeat the radar and 2D overlay checks with the window on the Retina built-in display.

**Verify:** user confirmation of each item above, with the log line captured.

**Steps:**

- [ ] **Step 1:** Ask the user whether they are available for a live check. Do not launch before they say yes.
- [ ] **Step 2:** Build the dev root and launch per `[[live-testing-rules]]` (local ACE 10.10.20.20 via `~/OpenAC-dev/dev-client.env`, never Dreamweave; `DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib` for Vulkan).
- [ ] **Step 3:** Walk the acceptance items with the user; capture the `raycast init` line.
- [ ] **Step 4:** Record results in the progress ledger; fix anything found under a new commit with tests, and repeat the affected item.
