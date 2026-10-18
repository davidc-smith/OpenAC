# Rynth Icons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Script windows draw real AC icons (icon, object and spell ids) and the launcher bar draws RynthCore's Phosphor icons instead of first letters.

**Architecture:** The client's ImGui overlay gains an icon service (`IClientOverlayIcons`, backed by the existing `RetailPluginImageBackend`) and a merged-font hook (`AddMergedFont`). The Vulkan ImGui renderer draws the textures that service handed out and merges registered fonts into its atlas. The Rynth extension points the ScriptWindows `ScriptIcons` stand-in at the service, registers RynthCore's Phosphor font, and the bar uses RynthCore's title-to-icon table.

**Tech Stack:** .NET 10 (`~/.dotnet/dotnet`), ImGui.NET 1.91.6.1, xunit v3 (VSTest runner: `--filter "FullyQualifiedName~X"`).

**Spec:** `docs/superpowers/specs/2026-10-18-rynth-icons-design.md`

## Global Constraints

- Repo `~/code/OpenAC/OpenAC-Rynth`, branch `feat/rynth-icons`. Do not push.
- Use `~/.dotnet/dotnet`. No new projects.
- Never edit anything inside `external/RynthCore` or `external/RynthSuite` (link files from there; don't copy or change them).
- `AcDream.App`, `AcDream.ClientExtensions` and `AcDream.Rynth.Host` build with warnings as errors and analyzers on: new code there must build clean. `AcDream.Rynth.ScriptWindows` has warnings off, but write it to the same standard.
- Every ImGui texture id the overlay draws is a UI texture handle: slot index + 1 (`UiTextureTableHandle.FromSlot` / `ToSlot`). That includes the font atlas after Task 4.
- The overlay draws only its atlas and handles the icon service handed out. Any other id is skipped and reported once, as today.
- Icon lookups and font registration run on the game thread (the overlay's draw thread). No locks are added beyond what `RetailPluginImageBackend` already takes.
- Every `ImGui.Begin` has its `End`, even when the body throws. Never pass script- or plugin-supplied text as an ImGui format string.
- Known unrelated test failures exist in `AcDream.Core.Tests`, `AcDream.Core.Net.Tests` and `AcDream.App.Tests` (missing dat files, live-server tests). Run App tests with a filter, as each task's Verify shows. `AcDream.Rynth.Host.Tests` must be fully green.
- Dat-backed tests carry `[Trait("Lane", "InstalledDat")]` and are run with `ACDREAM_DAT_DIR=/Users/davidsmith/AsheronsCall`.
- Never launch a client against a server without asking the user first; only the local ACE server 10.10.20.20, never Dreamweave; never print passwords.

**User decisions (already made):**
- Cover both: AC icons in script windows and icons on the bar.
- Bar icons are Phosphor glyphs, as RynthCore's bar draws them (not AC icons).

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/AcDream.Rynth.ScriptWindows/AcDream.Rynth.ScriptWindows.csproj` | modify | link `PhosphorIcons.cs`, embed `Phosphor.ttf` |
| `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiStandIns.cs` | modify | `ScriptIcons.Source` lookup |
| `src/AcDream.Rynth.ScriptWindows/StandIns/PhosphorFont.cs` | create | load the embedded font bytes |
| `NOTICE.md` | modify | Phosphor MIT notice |
| `src/AcDream.ClientExtensions/IClientOverlayIcons.cs` | create | the icon service contract |
| `src/AcDream.ClientExtensions/IClientOverlay.cs` | modify | `Icons`, `AddMergedFont` (default members) |
| `src/AcDream.App/Extensions/Overlay/OverlayIcons.cs` | create | icon service over `IPluginImageBackend`, issued-handle set |
| `src/AcDream.App/Extensions/Overlay/OverlayFonts.cs` | create | registered merged fonts, native copies, merge into an atlas |
| `src/AcDream.App/Extensions/Overlay/ImGuiVulkanRenderer.cs` | modify | draw issued handles; atlas id as handle; merge fonts; rebuild on font change |
| `src/AcDream.App/Extensions/Overlay/ImGuiOverlay.cs` | modify | own `OverlayIcons`/`OverlayFonts`; `BindIcons`; pass both to the renderer |
| `src/AcDream.App/UI/RetailUiRuntime.cs` | modify | keep the image backend (`ImageBackend`) |
| `src/AcDream.App/Rendering/GameWindow.cs` | modify | one line: bind icons when the retained UI exists |
| `src/AcDream.Rynth.Host/RynthBarIcons.cs` | create | RynthCore's title → Phosphor table, glyph-or-letter label |
| `src/AcDream.Rynth.Host/RynthBar.cs` | modify | draw icons |
| `src/AcDream.Rynth.Host/RynthClientExtension.cs` | modify | wire `ScriptIcons.Source` and the Phosphor font |
| tests (see each task) | create/modify | |

---

### Task 1: Phosphor and the ScriptIcons lookup in the ScriptWindows wrapper

**Goal:** The ScriptWindows project compiles RynthCore's `PhosphorIcons`, carries `Phosphor.ttf` as a resource, and its `ScriptIcons` stand-in answers through a settable lookup.

**Files:**
- Modify: `src/AcDream.Rynth.ScriptWindows/AcDream.Rynth.ScriptWindows.csproj`
- Modify: `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiStandIns.cs:22-30`
- Create: `src/AcDream.Rynth.ScriptWindows/StandIns/PhosphorFont.cs`
- Modify: `NOTICE.md` (append a section)
- Test: `tests/AcDream.Rynth.Host.Tests/ScriptIconsTests.cs`

**Acceptance Criteria:**
- [ ] `ScriptIcons.TryGet` returns false with no `Source`, for `ScriptIconKind.Unknown`, for id 0, and when `Source` returns `IntPtr.Zero`.
- [ ] With a `Source`, `TryGet(kind, id)` passes `kind` and `id` through and returns the source's non-zero texture.
- [ ] `PhosphorFont.Load()` returns the TTF bytes (starts with `00 01 00 00`) and `PhosphorIcons.Sparkle` compiles in the wrapper.
- [ ] `NOTICE.md` has a Phosphor section with the MIT licence text copied from `external/RynthCore/src/RynthCore.Engine/Assets/Fonts/Phosphor.LICENSE`.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo --filter "FullyQualifiedName~ScriptIconsTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** — `tests/AcDream.Rynth.Host.Tests/ScriptIconsTests.cs`:

```csharp
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.UI.ScriptWindows;

namespace AcDream.Rynth.Host.Tests;

public sealed class ScriptIconsTests : IDisposable
{
    public ScriptIconsTests() => ScriptIcons.Source = null;

    public void Dispose() => ScriptIcons.Source = null;

    [Fact]
    public void WithoutASourceThereIsNoIcon()
    {
        Assert.False(ScriptIcons.TryGet(ScriptIconKind.Icon, 0x06001234u, out IntPtr texture));
        Assert.Equal(IntPtr.Zero, texture);
    }

    [Theory]
    [InlineData(ScriptIconKind.Icon, 0x06001234u)]
    [InlineData(ScriptIconKind.Object, 0x80001234u)]
    [InlineData(ScriptIconKind.Spell, 1234u)]
    public void EachKindReachesTheSourceAsIs(ScriptIconKind kind, uint id)
    {
        var asked = new List<(ScriptIconKind, uint)>();
        ScriptIcons.Source = (k, i) => { asked.Add((k, i)); return (IntPtr)42; };

        Assert.True(ScriptIcons.TryGet(kind, id, out IntPtr texture));
        Assert.Equal((IntPtr)42, texture);
        Assert.Equal(new[] { (kind, id) }, asked);
    }

    [Fact]
    public void UnknownKindsZeroIdsAndEmptyAnswersAreNoIcon()
    {
        int calls = 0;
        ScriptIcons.Source = (_, id) => { calls++; return id == 7 ? IntPtr.Zero : (IntPtr)9; };

        Assert.False(ScriptIcons.TryGet(ScriptIconKind.Unknown, 5, out _));
        Assert.False(ScriptIcons.TryGet(ScriptIconKind.Icon, 0, out _));
        Assert.Equal(0, calls);
        Assert.False(ScriptIcons.TryGet(ScriptIconKind.Icon, 7, out IntPtr texture));
        Assert.Equal(IntPtr.Zero, texture);
    }

    [Fact]
    public void ThePhosphorFontIsEmbedded()
    {
        byte[]? ttf = PhosphorFont.Load();
        Assert.NotNull(ttf);
        Assert.True(ttf!.Length > 10_000);
        Assert.Equal(new byte[] { 0, 1, 0, 0 }, ttf[..4]);
        Assert.Contains(PhosphorIcons.Sparkle, PhosphorIcons.Baked, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run to see it fail.** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo --filter "FullyQualifiedName~ScriptIconsTests"` → build error: `ScriptIcons.Source`, `PhosphorFont`, `PhosphorIcons` don't exist.

- [ ] **Step 3: Link the file and embed the font.** In `AcDream.Rynth.ScriptWindows.csproj`, add to the `Compile` item group and a new item group:

```xml
    <Compile Include="$(RynthEngine)ImGui\PhosphorIcons.cs" Link="RynthCore\PhosphorIcons.cs" />
```

```xml
  <ItemGroup>
    <EmbeddedResource Include="$(RynthEngine)Assets\Fonts\Phosphor.ttf" LogicalName="AcDream.Rynth.ScriptWindows.Phosphor.ttf" />
  </ItemGroup>
```

- [ ] **Step 4: The lookup.** Replace the `ScriptIcons` class in `StandIns/ImGuiStandIns.cs` with:

```csharp
    internal static class ScriptIcons
    {
        /// <summary>
        /// The client's icons: a texture id for an icon, object or spell id, or
        /// zero for none. Set by the host while it has an overlay; null draws
        /// the replay's placeholder square.
        /// </summary>
        public static Func<ScriptIconKind, uint, IntPtr>? Source { get; set; }

        public static bool TryGet(ScriptIconKind kind, uint id, out IntPtr texture)
        {
            texture = IntPtr.Zero;
            if (kind > ScriptIconKind.Spell || id == 0 || Source is not { } source)
                return false;
            texture = source(kind, id);
            return texture != IntPtr.Zero;
        }
    }
```

- [ ] **Step 5: The font loader.** Create `src/AcDream.Rynth.ScriptWindows/StandIns/PhosphorFont.cs`:

```csharp
// RynthCore's Phosphor icon font (MIT; see NOTICE.md), embedded from the submodule.
using System.IO;

namespace RynthCore.Engine.ImGuiBackend
{
    internal static class PhosphorFont
    {
        private const string Resource = "AcDream.Rynth.ScriptWindows.Phosphor.ttf";

        /// <summary>The font file's bytes, or null if the resource is missing.</summary>
        public static byte[]? Load()
        {
            using Stream? stream = typeof(PhosphorFont).Assembly.GetManifestResourceStream(Resource);
            if (stream is null)
                return null;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
    }
}
```

- [ ] **Step 6: Notice.** Append to `NOTICE.md` a `---` separator, a `## Phosphor Icons` section saying "The launcher bar's icons are the Phosphor icon font (https://phosphoricons.com), MIT-licensed, embedded from `external/RynthCore/src/RynthCore.Engine/Assets/Fonts/Phosphor.ttf`.", then the full text of `external/RynthCore/src/RynthCore.Engine/Assets/Fonts/Phosphor.LICENSE` copied verbatim.

- [ ] **Step 7: Run to see it pass.** Same command → all pass. Then the whole project: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo` → all pass.

- [ ] **Step 8: Commit.**

```bash
git add src/AcDream.Rynth.ScriptWindows NOTICE.md tests/AcDream.Rynth.Host.Tests/ScriptIconsTests.cs
git commit -m "feat(rynth): script-window icons ask a settable source; Phosphor font embedded"
```

---

### Task 2: The overlay icon service

**Goal:** `IClientOverlay` offers `Icons` (an `IClientOverlayIcons`) and `AddMergedFont`, and the App has `OverlayIcons`, which answers icon/spell/object lookups through an `IPluginImageBackend` bound late and records every handle it hands out.

**Files:**
- Create: `src/AcDream.ClientExtensions/IClientOverlayIcons.cs`
- Modify: `src/AcDream.ClientExtensions/IClientOverlay.cs`
- Create: `src/AcDream.App/Extensions/Overlay/OverlayIcons.cs`
- Test: `tests/AcDream.App.Tests/Extensions/OverlayIconsTests.cs`

**Acceptance Criteria:**
- [ ] Unbound, `Icon`, `SpellIcon` and `ObjectIcon` return `IntPtr.Zero` and nothing is issued.
- [ ] `Icon(0x1234)` asks the backend's `TryGetClientArt` for `0x06001234`; `Icon(0x06001234)` asks for the same; `Icon(0)` asks nothing.
- [ ] `SpellIcon`/`ObjectIcon` call `TryGetSpellIcon`/`TryGetObjectIcon` with the id unchanged.
- [ ] A handle returned is `IsIssued`; `IntPtr.Zero`, negative values, values above `uint.MaxValue` and handles never returned are not.
- [ ] A throwing backend gives `IntPtr.Zero` and one log line per (kind, id), however many times it is asked.
- [ ] `Bind` a second time leaves the first backend in place and logs once.
- [ ] Existing `IClientOverlay` implementers (test fakes) still compile unchanged: the new members have default implementations.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo --filter "FullyQualifiedName~OverlayIconsTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** — `tests/AcDream.App.Tests/Extensions/OverlayIconsTests.cs`:

```csharp
using AcDream.App.Extensions.Overlay;
using AcDream.App.Plugins;

namespace AcDream.App.Tests.Extensions;

public sealed class OverlayIconsTests
{
    private readonly List<string> _log = new();
    private readonly FakeBackend _backend = new();
    private readonly OverlayIcons _icons;

    public OverlayIconsTests() => _icons = new OverlayIcons(_log.Add);

    [Fact]
    public void UnboundEverythingIsZero()
    {
        Assert.Equal(IntPtr.Zero, _icons.Icon(0x06001234u));
        Assert.Equal(IntPtr.Zero, _icons.SpellIcon(1));
        Assert.Equal(IntPtr.Zero, _icons.ObjectIcon(0x80000001u));
        Assert.False(_icons.IsIssued((IntPtr)5));
    }

    [Fact]
    public void IconIdsAreNormalisedAndZeroAsksNothing()
    {
        _icons.Bind(_backend);
        _backend.Answer = 11;

        Assert.Equal((IntPtr)11, _icons.Icon(0x1234u));
        Assert.Equal((IntPtr)11, _icons.Icon(0x06001234u));
        Assert.Equal(IntPtr.Zero, _icons.Icon(0));
        Assert.Equal(new[] { "art:06001234", "art:06001234" }, _backend.Asked);
    }

    [Fact]
    public void SpellsAndObjectsGoToTheirOwnLookups()
    {
        _icons.Bind(_backend);
        _backend.Answer = 12;

        Assert.Equal((IntPtr)12, _icons.SpellIcon(1234));
        Assert.Equal((IntPtr)12, _icons.ObjectIcon(0x80000001u));
        Assert.Equal(new[] { "spell:000004D2", "object:80000001" }, _backend.Asked);
    }

    [Fact]
    public void OnlyHandedOutHandlesAreIssued()
    {
        _icons.Bind(_backend);
        _backend.Answer = 13;
        _icons.SpellIcon(1);

        Assert.True(_icons.IsIssued((IntPtr)13));
        Assert.False(_icons.IsIssued((IntPtr)14));
        Assert.False(_icons.IsIssued(IntPtr.Zero));
        Assert.False(_icons.IsIssued((IntPtr)(-13)));
        Assert.False(_icons.IsIssued((IntPtr)(0x1_0000_000DL)));
    }

    [Fact]
    public void ANotFoundAnswerIsZeroAndNotIssued()
    {
        _icons.Bind(_backend);
        _backend.Answer = 0;
        Assert.Equal(IntPtr.Zero, _icons.SpellIcon(1));
        Assert.False(_icons.IsIssued(IntPtr.Zero));
    }

    [Fact]
    public void AThrowingBackendIsZeroAndLoggedOncePerLookup()
    {
        _icons.Bind(_backend);
        _backend.Throw = true;

        Assert.Equal(IntPtr.Zero, _icons.SpellIcon(1));
        Assert.Equal(IntPtr.Zero, _icons.SpellIcon(1));
        Assert.Equal(IntPtr.Zero, _icons.SpellIcon(2));
        Assert.Equal(2, _log.Count(line => line.Contains("icon lookup failed", StringComparison.Ordinal)));
    }

    [Fact]
    public void BindingTwiceKeepsTheFirstBackend()
    {
        _icons.Bind(_backend);
        var second = new FakeBackend { Answer = 99 };
        _icons.Bind(second);
        _icons.Bind(second);
        _backend.Answer = 15;

        Assert.Equal((IntPtr)15, _icons.SpellIcon(1));
        Assert.Single(_log, line => line.Contains("already bound", StringComparison.Ordinal));
    }

    private sealed class FakeBackend : IPluginImageBackend
    {
        public uint Answer { get; set; }
        public bool Throw { get; set; }
        public List<string> Asked { get; } = new();

        public bool TryGetClientArt(uint surfaceId, out uint texture, out int width, out int height) =>
            Answer2($"art:{surfaceId:X8}", out texture, out width, out height);

        public bool TryGetSpellIcon(uint spellId, out uint texture, out int width, out int height) =>
            Answer2($"spell:{spellId:X8}", out texture, out width, out height);

        public bool TryGetObjectIcon(uint objectId, out uint texture, out int width, out int height) =>
            Answer2($"object:{objectId:X8}", out texture, out width, out height);

        public uint UploadOwned(byte[] rgba, int width, int height, string debugName) => throw new NotSupportedException();

        public bool ReleaseOwned(uint texture) => throw new NotSupportedException();

        private bool Answer2(string what, out uint texture, out int width, out int height)
        {
            if (Throw)
                throw new InvalidOperationException("dat read failed");
            Asked.Add(what);
            texture = Answer;
            width = height = Answer == 0 ? 0 : 32;
            return Answer != 0;
        }
    }
}
```

- [ ] **Step 2: Run to see it fail.** Verify command → build error: `OverlayIcons` doesn't exist.

- [ ] **Step 3: The contract.** Create `src/AcDream.ClientExtensions/IClientOverlayIcons.cs`:

```csharp
namespace AcDream.ClientExtensions;

/// <summary>
/// The client's icons as ImGui texture ids, for <c>ImGui.Image</c> on the
/// overlay. Zero means no icon (unknown id, or the client's interface isn't
/// up yet). Call from the overlay's draw thread.
/// </summary>
public interface IClientOverlayIcons
{
    /// <summary>An icon image by id; a bare index below 0x01000000 gets 0x06000000 added.</summary>
    IntPtr Icon(uint iconId);

    /// <summary>A spell's icon, as the spell bar draws it.</summary>
    IntPtr SpellIcon(uint spellId);

    /// <summary>A known object's icon with its underlay, overlay and effects, as the inventory draws it.</summary>
    IntPtr ObjectIcon(uint objectId);
}
```

In `src/AcDream.ClientExtensions/IClientOverlay.cs`, add inside the interface after `WantsKeyboard`:

```csharp

    /// <summary>The client's icons as ImGui texture ids. Never null; all zero until the interface is up.</summary>
    IClientOverlayIcons Icons => NoOverlayIcons.Instance;

    /// <summary>
    /// Merges <paramref name="glyphs"/> (BMP code points) from a TrueType
    /// font into the overlay's font at <paramref name="scale"/> times its
    /// size, from the next frame on. The overlay keeps a copy of the bytes.
    /// </summary>
    void AddMergedFont(byte[] ttf, string glyphs, float scale)
    {
    }
```

and below the interface, in the same file:

```csharp

/// <summary>An overlay without icons: every lookup is zero.</summary>
internal sealed class NoOverlayIcons : IClientOverlayIcons
{
    public static readonly NoOverlayIcons Instance = new();

    public IntPtr Icon(uint iconId) => IntPtr.Zero;

    public IntPtr SpellIcon(uint spellId) => IntPtr.Zero;

    public IntPtr ObjectIcon(uint objectId) => IntPtr.Zero;
}
```

- [ ] **Step 4: The service.** Create `src/AcDream.App/Extensions/Overlay/OverlayIcons.cs`:

```csharp
using AcDream.App.Plugins;
using AcDream.ClientExtensions;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Extensions.Overlay;

/// <summary>
/// The overlay's icons: the client interface's own icon services (bound once
/// the interface exists), answered as ImGui texture ids. Every handle handed
/// out is remembered so the renderer draws it and nothing else. Caching is the
/// backend's. Game thread.
/// </summary>
internal sealed class OverlayIcons : IClientOverlayIcons
{
    private readonly Action<string> _log;
    private readonly HashSet<uint> _issued = new();
    private readonly HashSet<(char Kind, uint Id)> _reported = new();
    private IPluginImageBackend? _backend;
    private bool _reportedRebind;

    public OverlayIcons(Action<string> log) => _log = log ?? throw new ArgumentNullException(nameof(log));

    /// <summary>Binds the interface's icon services; only the first call counts.</summary>
    public void Bind(IPluginImageBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (_backend is null)
        {
            _backend = backend;
            return;
        }

        if (!_reportedRebind)
        {
            _reportedRebind = true;
            _log("[ImGui] overlay icons are already bound; keeping the first.");
        }
    }

    public IntPtr Icon(uint iconId)
    {
        uint id = PluginIcons.Normalize(iconId);
        return id == 0 ? IntPtr.Zero : Look('i', id, (IPluginImageBackend b, out uint t) => b.TryGetClientArt(id, out t, out _, out _));
    }

    public IntPtr SpellIcon(uint spellId) =>
        Look('s', spellId, (IPluginImageBackend b, out uint t) => b.TryGetSpellIcon(spellId, out t, out _, out _));

    public IntPtr ObjectIcon(uint objectId) =>
        Look('o', objectId, (IPluginImageBackend b, out uint t) => b.TryGetObjectIcon(objectId, out t, out _, out _));

    /// <summary>True for a texture id this service handed out.</summary>
    public bool IsIssued(IntPtr textureId)
    {
        long value = textureId.ToInt64();
        return value > 0 && value <= uint.MaxValue && _issued.Contains((uint)value);
    }

    private delegate bool Lookup(IPluginImageBackend backend, out uint texture);

    private IntPtr Look(char kind, uint id, Lookup lookup)
    {
        if (_backend is not { } backend)
            return IntPtr.Zero;
        try
        {
            if (!lookup(backend, out uint texture) || texture == 0)
                return IntPtr.Zero;
            _issued.Add(texture);
            return (IntPtr)texture;
        }
        catch (Exception error)
        {
            if (_reported.Add((kind, id)))
                _log($"[ImGui] icon lookup failed ({kind} 0x{id:X8}): {error.GetType().Name}: {error.Message}");
            return IntPtr.Zero;
        }
    }
}
```

- [ ] **Step 5: Run to see it pass.** Verify command → all pass. Also `~/.dotnet/dotnet build src/AcDream.App -nologo -v q` → `0 Error(s)`.

- [ ] **Step 6: Commit.**

```bash
git add src/AcDream.ClientExtensions src/AcDream.App/Extensions/Overlay/OverlayIcons.cs tests/AcDream.App.Tests/Extensions/OverlayIconsTests.cs
git commit -m "feat(overlay): an icon service for extensions over the interface's icon backend"
```

---

### Task 3: Merged fonts

**Goal:** `OverlayFonts` keeps fonts extensions registered (native copies of the bytes and glyph ranges) and merges them into an ImGui font atlas after its default font.

**Files:**
- Create: `src/AcDream.App/Extensions/Overlay/OverlayFonts.cs`
- Test: `tests/AcDream.App.Tests/Extensions/OverlayFontsTests.cs`

**Acceptance Criteria:**
- [ ] `Add` bumps `Version`; `Add` with null/empty bytes, empty glyphs or a scale ≤ 0 is ignored and logged.
- [ ] After `AddFontDefault` + `MergeInto(atlas, 13f)` + building the atlas, `ImFont_FindGlyphNoFallback(font, Sparkle)` is non-null for the Phosphor font with `PhosphorIcons.Baked`; without the merge it is null.
- [ ] Glyph ranges are built from the distinct sorted code points, consecutive ones joined, 0-terminated.
- [ ] `Dispose` frees the native copies; `MergeInto` after `Dispose` does nothing.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo --filter "FullyQualifiedName~OverlayFontsTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** — `tests/AcDream.App.Tests/Extensions/OverlayFontsTests.cs`:

```csharp
using AcDream.App.Extensions.Overlay;
using ImGuiNET;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed unsafe class OverlayFontsTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly OverlayFonts _fonts;
    private readonly IntPtr _context;

    public OverlayFontsTests()
    {
        _fonts = new OverlayFonts(_log.Add);
        _context = ImGui.CreateContext();
        ImGui.SetCurrentContext(_context);
    }

    public void Dispose()
    {
        _fonts.Dispose();
        ImGui.DestroyContext(_context);
    }

    private static bool HasGlyph(string glyph)
    {
        ImFontAtlasPtr atlas = ImGui.GetIO().Fonts;
        atlas.GetTexDataAsRGBA32(out IntPtr _, out int _, out int _, out int _);
        ImFontPtr font = atlas.Fonts[0];
        return ImGuiNative.ImFont_FindGlyphNoFallback(font.NativePtr, glyph[0]) != null;
    }

    private void Build()
    {
        ImFontAtlasPtr atlas = ImGui.GetIO().Fonts;
        atlas.Clear();
        atlas.AddFontDefault();
        _fonts.MergeInto(atlas, 13f);
    }

    [Fact]
    public void WithoutAMergeThereAreNoIcons()
    {
        Build();
        Assert.False(HasGlyph(PhosphorIcons.Sparkle));
    }

    [Fact]
    public void AMergedFontAddsItsGlyphs()
    {
        _fonts.Add(PhosphorFont.Load()!, PhosphorIcons.Baked, 1.2f);
        Build();
        Assert.True(HasGlyph(PhosphorIcons.Sparkle));
        Assert.True(HasGlyph(PhosphorIcons.DotsSixVertical));
        Assert.True(HasGlyph("A"));
    }

    [Fact]
    public void AddingBumpsTheVersionAndBadInputIsIgnored()
    {
        int start = _fonts.Version;
        _fonts.Add(Array.Empty<byte>(), "x", 1f);
        _fonts.Add(new byte[] { 1 }, "", 1f);
        _fonts.Add(new byte[] { 1 }, "x", 0f);
        Assert.Equal(start, _fonts.Version);
        Assert.Equal(3, _log.Count(line => line.Contains("ignored", StringComparison.Ordinal)));

        _fonts.Add(PhosphorFont.Load()!, PhosphorIcons.Baked, 1.2f);
        Assert.Equal(start + 1, _fonts.Version);
    }

    [Fact]
    public void RangesJoinConsecutiveCodePoints()
    {
        Assert.Equal(new ushort[] { 0x41, 0x43, 0x45, 0x45, 0 }, OverlayFonts.Ranges("ECBAA"));
    }

    [Fact]
    public void AfterDisposeNothingIsMerged()
    {
        _fonts.Add(PhosphorFont.Load()!, PhosphorIcons.Baked, 1.2f);
        _fonts.Dispose();
        Build();
        Assert.False(HasGlyph(PhosphorIcons.Sparkle));
    }
}
```

- [ ] **Step 2: Run to see it fail.** Verify command → build error: `OverlayFonts` doesn't exist.

- [ ] **Step 3: Implement.** Create `src/AcDream.App/Extensions/Overlay/OverlayFonts.cs`:

```csharp
using System.Runtime.InteropServices;
using ImGuiNET;

namespace AcDream.App.Extensions.Overlay;

/// <summary>
/// Fonts extensions asked to merge into the overlay's font (icon fonts). The
/// atlas reads a font's bytes and glyph ranges whenever it is rebuilt, so
/// both are kept as native copies until the overlay is disposed.
/// </summary>
internal sealed unsafe class OverlayFonts : IDisposable
{
    private readonly Action<string> _log;
    private readonly List<Entry> _entries = new();

    public OverlayFonts(Action<string> log) => _log = log ?? throw new ArgumentNullException(nameof(log));

    /// <summary>Bumped on every font added; the renderer rebuilds its atlas when it changes.</summary>
    public int Version { get; private set; }

    public void Add(byte[] ttf, string glyphs, float scale)
    {
        if (ttf is null || ttf.Length == 0 || string.IsNullOrEmpty(glyphs) || !(scale > 0))
        {
            _log("[ImGui] merged font ignored: it needs font bytes, glyphs and a positive scale.");
            return;
        }

        void* data = NativeMemory.Alloc((nuint)ttf.Length);
        ttf.AsSpan().CopyTo(new Span<byte>(data, ttf.Length));
        ushort[] ranges = Ranges(glyphs);
        ushort* nativeRanges = (ushort*)NativeMemory.Alloc((nuint)(ranges.Length * sizeof(ushort)));
        ranges.AsSpan().CopyTo(new Span<ushort>(nativeRanges, ranges.Length));
        _entries.Add(new Entry((IntPtr)data, ttf.Length, (IntPtr)nativeRanges, scale));
        Version++;
    }

    /// <summary>Merges every font into the atlas's last added font, at <paramref name="basePixels"/> times its scale.</summary>
    public void MergeInto(ImFontAtlasPtr atlas, float basePixels)
    {
        foreach (Entry entry in _entries)
        {
            float pixels = MathF.Max(1f, MathF.Round(basePixels * entry.Scale));
            ImFontConfigPtr config = ImGuiNative.ImFontConfig_ImFontConfig();
            try
            {
                config.MergeMode = true;
                config.FontDataOwnedByAtlas = false;
                config.PixelSnapH = true;
                config.OversampleH = 1;
                config.OversampleV = 1;
                config.GlyphMinAdvanceX = pixels;
                atlas.AddFontFromMemoryTTF(entry.Data, entry.Length, pixels, config, entry.Ranges);
            }
            catch (Exception error)
            {
                _log($"[ImGui] merged font skipped: {error.GetType().Name}: {error.Message}");
            }
            finally
            {
                config.Destroy();
            }
        }
    }

    /// <summary>ImGui glyph ranges for the code points in <paramref name="glyphs"/>: pairs, consecutive ones joined, 0-terminated.</summary>
    internal static ushort[] Ranges(string glyphs)
    {
        var points = glyphs.Select(c => (ushort)c).Where(c => c != 0).Distinct().Order().ToArray();
        var ranges = new List<ushort>();
        for (int i = 0; i < points.Length; i++)
        {
            ushort start = points[i];
            while (i + 1 < points.Length && points[i + 1] == points[i] + 1)
                i++;
            ranges.Add(start);
            ranges.Add(points[i]);
        }

        ranges.Add(0);
        return ranges.ToArray();
    }

    public void Dispose()
    {
        foreach (Entry entry in _entries)
        {
            NativeMemory.Free((void*)entry.Data);
            NativeMemory.Free((void*)entry.Ranges);
        }

        _entries.Clear();
    }

    private readonly record struct Entry(IntPtr Data, int Length, IntPtr Ranges, float Scale);
}
```

Note: `atlas.Clear()` in the renderer does not free the bytes because `FontDataOwnedByAtlas = false`; `Dispose` must only run after the atlas is done with them (the overlay disposes the renderer first, see Task 4).

- [ ] **Step 4: Run to see it pass.** Verify command → all pass.

- [ ] **Step 5: Commit.**

```bash
git add src/AcDream.App/Extensions/Overlay/OverlayFonts.cs tests/AcDream.App.Tests/Extensions/OverlayFontsTests.cs
git commit -m "feat(overlay): merged fonts kept natively and merged into the atlas"
```

---

### Task 4: The renderer and overlay draw icons and merged fonts; the client binds its icons

**Goal:** The ImGui renderer draws the atlas and issued icon handles (both as UI handles), merges registered fonts and rebuilds the atlas when they change; `ImGuiOverlay` exposes `Icons`, `AddMergedFont` and `BindIcons`; the client binds its icon backend once the retained UI exists.

**Files:**
- Modify: `src/AcDream.App/Extensions/Overlay/ImGuiVulkanRenderer.cs` (constructor, `IsDrawableTexture` → `TryGetDrawSlot`, `PrepareFrame`, the draw loop at lines 211-224, `ReportBadTexture`)
- Modify: `src/AcDream.App/Extensions/Overlay/ImGuiOverlay.cs` (fields, `Icons`, `AddMergedFont`, `BindIcons`, `Start`, `Dispose`)
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs:432-437`
- Modify: `src/AcDream.App/Rendering/GameWindow.cs:996`
- Test: `tests/AcDream.App.Tests/Extensions/ImGuiVulkanRendererTests.cs`, `tests/AcDream.App.Tests/Extensions/ImGuiOverlayTests.cs`, `tests/AcDream.App.Tests/Extensions/OverlayIconsInstalledDatTests.cs`

**Acceptance Criteria:**
- [ ] `TryGetDrawSlot(id, atlas, issued)` gives slot `atlas.Index` for id `atlas.Index + 1`; slot `h - 1` for an issued handle `h`; false for 0, foreign pointers, unissued handles, and anything when there is no atlas and nothing issued.
- [ ] The atlas's ImGui texture id is `UiTextureTableHandle.FromSlot(_atlasSlot)`.
- [ ] `PrepareFrame` rebuilds the atlas when the scale or `OverlayFonts.Version` changed, merging fonts after `AddFontDefault` at `FontSizePoints * scale`.
- [ ] `ImGuiOverlay.Icons` is the overlay's `OverlayIcons`; `BindIcons` binds it; `AddMergedFont` reaches `OverlayFonts`.
- [ ] `ImGuiOverlay.Dispose` disposes the renderer before the fonts.
- [ ] `RetailUiRuntime.ImageBackend` is the backend it binds for plugins; `GameWindow` passes it to `_overlay.BindIcons`.
- [ ] Dat-backed: icon `0x06002C41` and spell `1` resolve to non-zero, issued ids through a real `RetailPluginImageBackend`.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo --filter "FullyQualifiedName~AcDream.App.Tests.Extensions"` → all pass; `ACDREAM_DAT_DIR=/Users/davidsmith/AsheronsCall ~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo --filter "FullyQualifiedName~OverlayIconsInstalledDatTests"` → pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** In `ImGuiVulkanRendererTests.cs`, replace `OnlyTheAtlasTextureIsDrawable` and `NothingIsDrawableWithoutAnAtlas` with:

```csharp
    [Fact]
    public void TheAtlasIsDrawnByItsHandle()
    {
        var atlas = new GpuTextureSlot(7);
        Assert.True(ImGuiVulkanRenderer.TryGetDrawSlot((IntPtr)8, atlas, null, out uint slot));
        Assert.Equal(7u, slot);
        Assert.False(ImGuiVulkanRenderer.TryGetDrawSlot((IntPtr)7, atlas, null, out _));
        Assert.False(ImGuiVulkanRenderer.TryGetDrawSlot(IntPtr.Zero, atlas, null, out _));
        Assert.False(ImGuiVulkanRenderer.TryGetDrawSlot(unchecked((IntPtr)0x7FF6_1234_5678L), atlas, null, out _));
    }

    [Fact]
    public void IssuedIconHandlesAreDrawnAtTheirSlot()
    {
        var atlas = new GpuTextureSlot(7);
        Func<IntPtr, bool> issued = id => id == (IntPtr)40;
        Assert.True(ImGuiVulkanRenderer.TryGetDrawSlot((IntPtr)40, atlas, issued, out uint slot));
        Assert.Equal(39u, slot);
        Assert.False(ImGuiVulkanRenderer.TryGetDrawSlot((IntPtr)41, atlas, issued, out _));
    }

    [Fact]
    public void NothingIsDrawableWithoutAnAtlasOrIssuedIcons()
    {
        GpuTextureSlot none = GpuTextureSlot.Unassigned;
        Assert.False(ImGuiVulkanRenderer.TryGetDrawSlot(unchecked((IntPtr)uint.MaxValue), none, null, out _));
        Assert.False(ImGuiVulkanRenderer.TryGetDrawSlot(IntPtr.Zero, none, _ => true, out _));
        Assert.False(ImGuiVulkanRenderer.TryGetDrawSlot((IntPtr)8, none, null, out _));
    }
```

In `ImGuiOverlayTests.cs`, add:

```csharp
    [Fact]
    public void IconsAreTheOverlaysAndBindToTheBackend()
    {
        Assert.IsType<OverlayIcons>(_overlay.Icons);
        Assert.Equal(IntPtr.Zero, _overlay.Icons.SpellIcon(1));
    }

    [Fact]
    public void AMergedFontReachesTheFontList()
    {
        int before = _overlay.Fonts.Version;
        ((AcDream.ClientExtensions.IClientOverlay)_overlay).AddMergedFont(
            RynthCore.Engine.ImGuiBackend.PhosphorFont.Load()!, RynthCore.Engine.ImGuiBackend.PhosphorIcons.Baked, 1.2f);
        Assert.Equal(before + 1, _overlay.Fonts.Version);
    }
```

Create `tests/AcDream.App.Tests/Extensions/OverlayIconsInstalledDatTests.cs`:

```csharp
using AcDream.App.Extensions.Overlay;
using AcDream.App.Rendering;
using AcDream.App.Tests.Rendering;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;
using AcDream.Content;
using AcDream.Core.Items;
using DatReaderWriter;
using DatReaderWriter.Options;

namespace AcDream.App.Tests.Extensions;

[Trait("Lane", "InstalledDat")]
public sealed class OverlayIconsInstalledDatTests
{
    [Fact]
    public void RealIconsAndSpellIconsResolveToIssuedIds()
    {
        string? datDir = InstalledDatTestPath.Resolve();
        if (datDir is null)
        {
            Assert.Fail("Lane=InstalledDat requires an installed retail DAT directory; see docs/release-gate.md.");
            return;
        }

        using var dats = new DatCollection(datDir, DatAccessType.Read);
        using var adapter = new DatCollectionAdapter(dats);
        var device = new RecordingGpuDevice();
        using var cache = new TextureCache(device, adapter);
        var composer = new IconComposer(adapter, cache);
        var backend = new RetailPluginImageBackend(adapter, new object(), cache, composer, new ClientObjectTable());
        var log = new List<string>();
        var icons = new OverlayIcons(log.Add);
        icons.Bind(backend);

        IntPtr icon = icons.Icon(0x06002C41u);
        IntPtr spell = icons.SpellIcon(1);

        Assert.NotEqual(IntPtr.Zero, icon);
        Assert.NotEqual(IntPtr.Zero, spell);
        Assert.True(icons.IsIssued(icon));
        Assert.True(icons.IsIssued(spell));
        Assert.Empty(log);
    }
}
```

- [ ] **Step 2: Run to see it fail.** First Verify command → build errors: `TryGetDrawSlot`, `ImGuiOverlay.Fonts` don't exist.

- [ ] **Step 3: The renderer.** In `ImGuiVulkanRenderer.cs`:

Add fields after `_log`:

```csharp
    private readonly OverlayFonts? _fonts;
    private readonly Func<IntPtr, bool>? _issued;
    private int _fontsVersion = -1;
```

Change the constructor signature and set them:

```csharp
    public ImGuiVulkanRenderer(
        IGpuDevice device,
        ICurrentGpuFrameSource frames,
        Action<string>? log = null,
        OverlayFonts? fonts = null,
        Func<IntPtr, bool>? issued = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _frames = frames ?? throw new ArgumentNullException(nameof(frames));
        _log = log;
        _fonts = fonts;
        _issued = issued;
```

(rest of the constructor unchanged).

Replace `IsDrawableTexture` and its doc comment with:

```csharp
    /// <summary>
    /// The bindless slot for an ImGui texture id. Ids are UI texture handles
    /// (slot + 1): the font atlas's, or an icon the overlay's icon service
    /// handed out. Anything else, such as a foreign plugin pointer, 0 or a
    /// stale id, would index the bindless table out of range, so it is never drawn.
    /// </summary>
    internal static bool TryGetDrawSlot(IntPtr textureId, GpuTextureSlot atlas, Func<IntPtr, bool>? issued, out uint slot)
    {
        slot = 0;
        long value = textureId.ToInt64();
        if (value <= 0 || value > uint.MaxValue)
            return false;
        uint handle = (uint)value;
        if (handle != UiTextureTableHandle.FromSlot(atlas) && issued?.Invoke(textureId) != true)
            return false;
        slot = UiTextureTableHandle.ToSlot(handle).Index;
        return true;
    }
```

In `PrepareFrame`, change the early-return test and merge after the default font:

```csharp
        int fontsVersion = _fonts?.Version ?? 0;
        if (_atlas is not null && Math.Abs(scale - _atlasScale) < 0.01f && fontsVersion == _fontsVersion)
            return;
```

```csharp
        io.Fonts.AddFontDefault(config);
        config.Destroy();
        _fonts?.MergeInto(io.Fonts, FontSizePoints * scale);
```

and after `_atlasScale = scale;`:

```csharp
        _fontsVersion = fontsVersion;
        io.Fonts.SetTexID((IntPtr)UiTextureTableHandle.FromSlot(_atlasSlot));
```

(replacing the old `io.Fonts.SetTexID((IntPtr)_atlasSlot.Index);`).

In `Render`, replace the block from `if (!IsDrawableTexture(cmd.TextureId, _atlasSlot))` through `uint texture = _atlasSlot.Index;` with:

```csharp
                if (!TryGetDrawSlot(cmd.TextureId, _atlasSlot, _issued, out uint texture))
                {
                    ReportBadTexture(cmd.TextureId);
                    continue;
                }

                if (!ImGuiScissor.TryMap(cmd.ClipRect, drawData.DisplayPos, drawData.FramebufferScale, fbWidth, fbHeight, out var s))
                    continue;
```

In `ReportBadTexture`, change the message to `"[ImGui] skipping draws with unknown texture id 0x{textureId:X}; only the font atlas and client icons can be drawn."`.

- [ ] **Step 4: The overlay.** In `ImGuiOverlay.cs`:

Add fields after `_callbacks`:

```csharp
    private readonly OverlayIcons _icons;
    private readonly OverlayFonts _fonts;
```

Change the constructor:

```csharp
    public ImGuiOverlay(Action<string> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _icons = new OverlayIcons(_log);
        _fonts = new OverlayFonts(_log);
    }
```

Add after `WantsKeyboard`:

```csharp
    /// <summary>The client's icons for extensions; zero until <see cref="BindIcons"/>.</summary>
    public IClientOverlayIcons Icons => _icons;

    /// <summary>Fonts merged into the atlas (the renderer reads them).</summary>
    internal OverlayFonts Fonts => _fonts;

    public void AddMergedFont(byte[] ttf, string glyphs, float scale) => _fonts.Add(ttf, glyphs, scale);

    /// <summary>Points the icons at the client interface's icon services, once it exists.</summary>
    internal void BindIcons(AcDream.App.Plugins.IPluginImageBackend backend) => _icons.Bind(backend);
```

In `Start`, create the renderer with both:

```csharp
            renderer = new ImGuiVulkanRenderer(device, frames, _log, _fonts, _icons.IsIssued);
```

In `Dispose`, after the `finally` block that destroys the context (the end of the method), add `_fonts.Dispose();` so the fonts outlive the renderer and the context.

- [ ] **Step 5: The client binds its icons.** In `RetailUiRuntime.cs`, add a property near the other fields:

```csharp
    /// <summary>The interface's texture services as plugins and the overlay's icons see them.</summary>
    internal AcDream.App.Plugins.IPluginImageBackend ImageBackend { get; }
```

and replace the `bindings.Plugins?.BindImageServices(new RetailPluginImageBackend(...));` statement with:

```csharp
        ImageBackend = new RetailPluginImageBackend(
            bindings.Assets.Dats,
            bindings.Assets.DatLock,
            bindings.Assets.TextureCache,
            bindings.Assets.Icons,
            bindings.Toolbar.Objects);
        bindings.Plugins?.BindImageServices(ImageBackend);
```

In `GameWindow.cs`, after `_retailUiRuntime = retained.Runtime;` add:

```csharp
            _overlay.BindIcons(retained.Runtime.ImageBackend);
```

- [ ] **Step 6: Run to see it pass.** Both Verify commands → pass. `~/.dotnet/dotnet build src/AcDream.App -nologo -v q` → `0 Error(s)`.

- [ ] **Step 7: Commit.**

```bash
git add src/AcDream.App tests/AcDream.App.Tests/Extensions
git commit -m "feat(overlay): draw client icons and merged fonts; the client binds its icons to the overlay"
```

---

### Task 5: The Rynth extension uses the icons; the bar draws Phosphor

**Goal:** With an overlay, the extension points `ScriptIcons.Source` at `overlay.Icons` and merges Phosphor; the bar draws RynthCore's icon for a known title (glyph present in the font), else the first letter.

**Files:**
- Create: `src/AcDream.Rynth.Host/RynthBarIcons.cs`
- Modify: `src/AcDream.Rynth.Host/RynthBar.cs:91-131` (grip, buttons, menu) and remove `Letter` at 176-185
- Modify: `src/AcDream.Rynth.Host/RynthClientExtension.cs` (Attach, Dispose)
- Test: `tests/AcDream.Rynth.Host.Tests/RynthBarIconsTests.cs`, `tests/AcDream.App.Tests/Extensions/RynthOverlayExtensionTests.cs`

**Acceptance Criteria:**
- [ ] `RynthBarIcons.Icon("Oracle")` is `PhosphorIcons.Sparkle`, `Icon("tracker")` is `ChartLineUp`, `Icon("Mystery")` is null; the table is RynthCore's `PanelIcons` (25 entries).
- [ ] `RynthBarIcons.Label(title)` is the icon when the current font has its glyph, else the first letter or digit upper-cased, else `?`.
- [ ] In a headless ImGui with Phosphor merged, `Label("Oracle")` is Sparkle; without it, `"O"`.
- [ ] The grip draws `DotsSixVertical` when the font has it, else `::`; menu entries are `icon + " " + MenuLabel` when the icon is in the font.
- [ ] After `Attach` with an overlay, `ScriptIcons.Source` asks the overlay's `Icons` by kind and the overlay got one `AddMergedFont` with `PhosphorIcons.Baked` at 1.2; after `Dispose`, `Source` is null.
- [ ] Existing `RynthBarTests` and `RynthOverlayExtensionTests` pass.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo` → all pass; `~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo --filter "FullyQualifiedName~RynthOverlayExtensionTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** Create `tests/AcDream.Rynth.Host.Tests/RynthBarIconsTests.cs`:

```csharp
using AcDream.Rynth.Host;
using ImGuiNET;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthBarIconsTests
{
    [Fact]
    public void KnownTitlesHaveRynthCoresIcons()
    {
        Assert.Equal(PhosphorIcons.Sparkle, RynthBarIcons.Icon("Oracle"));
        Assert.Equal(PhosphorIcons.ChartLineUp, RynthBarIcons.Icon("tracker"));
        Assert.Null(RynthBarIcons.Icon("Mystery"));
    }

    [Fact]
    public void WithoutTheIconFontTheLabelIsTheFirstLetter()
    {
        using var imgui = new HeadlessImGui();
        Assert.Equal("O", RynthBarIcons.Label("Oracle"));
        Assert.Equal("M", RynthBarIcons.Label("  mystery"));
        Assert.Equal("?", RynthBarIcons.Label("--"));
    }

    [Fact]
    public unsafe void WithTheIconFontTheLabelIsTheIcon()
    {
        IntPtr context = ImGui.CreateContext();
        ImGui.SetCurrentContext(context);
        byte[] ttf = PhosphorFont.Load()!;
        ushort sparkle = PhosphorIcons.Sparkle[0];
        fixed (byte* data = ttf)
        {
            ImFontAtlasPtr atlas = ImGui.GetIO().Fonts;
            atlas.AddFontDefault();
            ImFontConfigPtr config = ImGuiNative.ImFontConfig_ImFontConfig();
            config.MergeMode = true;
            config.FontDataOwnedByAtlas = false;
            ushort* ranges = stackalloc ushort[] { sparkle, sparkle, 0 };
            atlas.AddFontFromMemoryTTF((IntPtr)data, ttf.Length, 13f, config, (IntPtr)ranges);
            atlas.GetTexDataAsRGBA32(out IntPtr _, out int _, out int _, out int _);
            config.Destroy();
        }

        ImGui.GetIO().DisplaySize = new System.Numerics.Vector2(800, 600);
        ImGui.NewFrame();
        try
        {
            Assert.Equal(PhosphorIcons.Sparkle, RynthBarIcons.Label("Oracle"));
            Assert.Equal("T", RynthBarIcons.Label("Tracker"));
        }
        finally
        {
            ImGui.EndFrame();
            ImGui.DestroyContext(context);
        }
    }
}
```

In `tests/AcDream.App.Tests/Extensions/RynthOverlayExtensionTests.cs`, extend `FakeOverlay`:

```csharp
        public FakeIcons FakeIcons { get; } = new();
        public AcDream.ClientExtensions.IClientOverlayIcons Icons => FakeIcons;
        public List<(int Bytes, string Glyphs, float Scale)> Fonts { get; } = new();

        public void AddMergedFont(byte[] ttf, string glyphs, float scale) => Fonts.Add((ttf.Length, glyphs, scale));
```

add the fake beside it:

```csharp
    private sealed class FakeIcons : AcDream.ClientExtensions.IClientOverlayIcons
    {
        public List<string> Asked { get; } = new();

        public IntPtr Icon(uint iconId) { Asked.Add($"icon:{iconId:X8}"); return (IntPtr)1; }

        public IntPtr SpellIcon(uint spellId) { Asked.Add($"spell:{spellId}"); return (IntPtr)2; }

        public IntPtr ObjectIcon(uint objectId) { Asked.Add($"object:{objectId:X8}"); return (IntPtr)3; }
    }
```

and the test:

```csharp
    [Fact]
    public void WithAnOverlayScriptIconsAskTheOverlayAndPhosphorIsMerged()
    {
        var overlay = new FakeOverlay();
        var extension = new RynthClientExtension();
        extension.Attach(Context(GameRuntimeTestFactory.Create(), overlay));

        Assert.True(ScriptIcons.TryGet(ScriptIconKind.Icon, 0x06001234u, out IntPtr icon));
        Assert.True(ScriptIcons.TryGet(ScriptIconKind.Spell, 7, out IntPtr spell));
        Assert.True(ScriptIcons.TryGet(ScriptIconKind.Object, 0x80000001u, out IntPtr obj));
        Assert.Equal(((IntPtr)1, (IntPtr)2, (IntPtr)3), (icon, spell, obj));
        Assert.Equal(new[] { "icon:06001234", "spell:7", "object:80000001" }, overlay.FakeIcons.Asked);
        (int bytes, string glyphs, float scale) = Assert.Single(overlay.Fonts);
        Assert.True(bytes > 10_000);
        Assert.Equal(PhosphorIcons.Baked, glyphs);
        Assert.Equal(1.2f, scale);

        extension.Dispose();
        Assert.Null(ScriptIcons.Source);
    }
```

Also add `ScriptIcons.Source = null;` to that test class's constructor and `Dispose`.

- [ ] **Step 2: Run to see it fail.** Both Verify commands → build errors: `RynthBarIcons` doesn't exist; the new overlay test fails (`Source` is null).

- [ ] **Step 3: The icon table.** Create `src/AcDream.Rynth.Host/RynthBarIcons.cs`:

```csharp
using ImGuiNET;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host;

/// <summary>
/// The bar's icons: RynthCore's title-to-Phosphor table (ImGuiBar's
/// PanelIcons), drawn only when the overlay's font has the glyph; otherwise
/// the title's first letter, as RynthCore does for titles it doesn't know.
/// </summary>
internal static class RynthBarIcons
{
    private static readonly Dictionary<string, string> Icons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RynthAi"] = PhosphorIcons.Robot,
        ["Chat"] = PhosphorIcons.ChatCircleText,
        ["ChatFilters"] = PhosphorIcons.Funnel,
        ["Radar"] = PhosphorIcons.Compass,
        ["Vision"] = PhosphorIcons.Eye,
        ["Nav"] = PhosphorIcons.MapTrifold,
        ["RynthNav"] = PhosphorIcons.NavigationArrow,
        ["Tracker"] = PhosphorIcons.ChartLineUp,
        ["Lua"] = PhosphorIcons.FileCode,
        ["Settings"] = PhosphorIcons.Gear,
        ["Damage"] = PhosphorIcons.ChartBar,
        ["Meta"] = PhosphorIcons.ListChecks,
        ["Items"] = PhosphorIcons.Backpack,
        ["Loot Editor"] = PhosphorIcons.TreasureChest,
        ["Skills"] = PhosphorIcons.GraduationCap,
        ["Sense"] = PhosphorIcons.Binoculars,
        ["Status"] = PhosphorIcons.Info,
        ["Inventory"] = PhosphorIcons.Package,
        ["Log"] = PhosphorIcons.TerminalWindow,
        ["Item Count"] = PhosphorIcons.Bag,
        ["Mini Remote"] = PhosphorIcons.SquaresFour,
        ["Characters"] = PhosphorIcons.UsersThree,
        ["RynthSort"] = PhosphorIcons.SortAscending,
        ["Oracle"] = PhosphorIcons.Sparkle,
        ["Global Inventory"] = PhosphorIcons.GlobeHemisphereWest,
    };

    /// <summary>RynthCore's icon for a title, or null.</summary>
    internal static string? Icon(string title) => Icons.TryGetValue(title, out string? icon) ? icon : null;

    /// <summary>The title's icon when the current font has it, else its first letter or digit, else "?".</summary>
    internal static string Label(string title)
    {
        if (Icon(title) is { } icon && InFont(icon))
            return icon;
        foreach (char c in title)
        {
            if (char.IsLetterOrDigit(c))
                return new string(char.ToUpperInvariant(c), 1);
        }

        return "?";
    }

    /// <summary>True when the current ImGui font draws <paramref name="glyph"/> (one character).</summary>
    internal static unsafe bool InFont(string glyph)
    {
        ImFontPtr font = ImGui.GetFont();
        return glyph.Length == 1 && font.NativePtr != null
            && ImGuiNative.ImFont_FindGlyphNoFallback(font.NativePtr, glyph[0]) != null;
    }
}
```

Count the entries against `external/RynthCore/src/RynthCore.Engine/ImGui/ImGuiBar.cs` `PanelIcons` (lines 81-108) and fix any mismatch so the two tables are identical.

- [ ] **Step 4: The bar.** In `RynthBar.cs` `DrawContents`:

replace `ImGui.Button("::");` with

```csharp
        ImGui.Button(RynthBarIcons.InFont(PhosphorIcons.DotsSixVertical) ? PhosphorIcons.DotsSixVertical : "::");
```

replace `ImGui.Button(Letter(item.Window.Title) + "##" + item.MenuLabel)` with `ImGui.Button(RynthBarIcons.Label(item.Window.Title) + "##" + item.MenuLabel)`;

replace the menu item line with

```csharp
                string? icon = RynthBarIcons.Icon(item.Window.Title);
                string label = icon is not null && RynthBarIcons.InFont(icon) ? icon + " " + item.MenuLabel : item.MenuLabel;
                if (ImGui.MenuItem(label, string.Empty, ImGuiPanelHost.IsOpen(item.PanelKey)))
                    Click(item);
```

and delete the `Letter` method with its doc comment. Update the class doc comment's last sentence to mention icons: "Buttons show RynthCore's Phosphor icon for a known title, else its first letter."

- [ ] **Step 5: The extension.** In `RynthClientExtension.Attach`, inside `if (context.Overlay is not null)` after `_bar = ...`:

```csharp
            IClientOverlayIcons icons = context.Overlay.Icons;
            _iconSource = (kind, id) => kind switch
            {
                ScriptIconKind.Icon => icons.Icon(id),
                ScriptIconKind.Object => icons.ObjectIcon(id),
                ScriptIconKind.Spell => icons.SpellIcon(id),
                _ => IntPtr.Zero,
            };
            ScriptIcons.Source = _iconSource;
            if (PhosphorFont.Load() is { } phosphor)
                context.Overlay.AddMergedFont(phosphor, PhosphorIcons.Baked, 1.2f);
            else
                context.Log("[Rynth] the Phosphor icon font is missing; the bar shows letters.");
```

Add the field `private Func<ScriptIconKind, uint, IntPtr>? _iconSource;` and `using RynthCore.Engine.UI.ScriptWindows;`. In `Dispose`, beside the layout reset:

```csharp
        if (_iconSource is not null && ScriptIcons.Source == _iconSource)
            ScriptIcons.Source = null;
        _iconSource = null;
```

- [ ] **Step 6: Run to see it pass.** Both Verify commands → all pass. `~/.dotnet/dotnet build src/AcDream.App -nologo -v q` → `0 Error(s)`.

- [ ] **Step 7: Commit.**

```bash
git add src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests tests/AcDream.App.Tests/Extensions/RynthOverlayExtensionTests.cs
git commit -m "feat(rynth): bar draws RynthCore's Phosphor icons; script windows draw client icons"
```

---

### Task 6: Live check on local ACE

> **Status 2026-10-18: done.** Live check on local ACE passed: one `[ImGui] font atlas` line, no `unknown texture id` or `merged font` lines; the bar grip shows the six-dot icon, the Scripts menu shows Sparkle before Oracle (and Globe before Global Inventory), and `/ro` and the menu still open and close Oracle. Step 3 was needed: the icons sat a quarter of their size above the capitals (4 px at 13 px), so `OverlayFonts.MergeInto` sets `GlyphOffset.Y = round(pixels * 0.25)` (4 px at 13 px, 8 px in the Retina atlas); `OverlayFontsTests.IconsSitLevelWithCapitals` pins it. Rechecked live: aligned.

**Goal:** In a live client on the local ACE server, the bar's grip shows the six-dot Phosphor icon and the Scripts menu shows Oracle with its Sparkle icon, with no "unknown texture id" lines in the log.

**Files:**
- Modify: `docs/superpowers/plans/2026-10-18-rynth-icons.md` (record the result)

**Acceptance Criteria:**
- [ ] The user agreed to the launch first (live-testing rule).
- [ ] The log has `[ImGui] font atlas` once at start and no `skipping draws with unknown texture id` or `merged font skipped` lines.
- [ ] Screenshot or user report: the bar's grip is the six-dot icon; the Scripts menu entry reads "✦ Oracle"-style (Sparkle then the title), vertically lined up with the text.
- [ ] `/ro` and the Scripts menu still open and close the Oracle window.

**Verify:** run the client as in the live-testing memory (`source ~/OpenAC-dev/dev-client.env`, `ACDREAM_LIVE=1`, scratch `ACDREAM_ROOT_DIR`, brew `DYLD_LIBRARY_PATH`/`VK_DRIVER_FILES`), then `grep -E "font atlas|unknown texture id|merged font" <client log>` → one atlas line, no others.

**Steps:**

- [ ] **Step 1:** Ask the user before launching. Local ACE 10.10.20.20 only; never print passwords.
- [ ] **Step 2:** Launch, log in, look at the bar and open the Scripts menu; grep the log as in Verify.
- [ ] **Step 3:** If icons sit too high or low against the text, add a `GlyphOffset` to `OverlayFonts.MergeInto` (`config.GlyphOffset = new Vector2(0, offset)`), rerun Task 3's tests, relaunch. Record the value used.
- [ ] **Step 4:** Record the result under this task and commit:

```bash
git add docs/superpowers/plans/2026-10-18-rynth-icons.md
git commit -m "docs: Rynth icons live check"
```
