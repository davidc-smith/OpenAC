# RynthCore Mini Remote Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The RynthAi dashboard's Char → "Mini Remote" (and `/rc miniremote`) opens RynthCore's own Mini Remote: the sections rail embedding our engine panels, and on Remote their quick-use slot grid that uses items through OpenAC.

**Architecture:** RynthCore's `MiniRemoteFace` (both partials), their `UI/Sections` layout code and `IltDetect` compile unchanged in `AcDream.Rynth.EngineUi`. The panel host stand-in learns RynthCore's embedding (`CanEmbed` / `TryGetDefaultSize` / `DrawEmbedded` + idle sweep). The `InventoryModel` stand-in gains RynthCore's one-at-a-time item `Use` request, run on the game thread from `RynthAiDashboard.Tick` through host hooks. Server/plugin stand-ins answer "no plugin manifests, no active servers". The host routes `/rc` to a small stand-in that handles only `miniremote`.

**Tech Stack:** .NET 10, C#, ImGui.NET, xunit.

**Spec:** `docs/superpowers/specs/2026-10-19-rynth-mini-remote-design.md`

## Global Constraints

- Never edit anything under `external/RynthCore` or `external/RynthSuite`; their files are compiled as-is (memory: rynth-no-rewrite). A compile error in one of their files is fixed by adding the missing member to a stand-in, never by editing theirs.
- Build with the SDK in `~/.dotnet`: `export PATH=~/.dotnet:$PATH DOTNET_ROOT=~/.dotnet`.
- Stand-in style: same namespace and member names as RynthCore's type, a short `<summary>` saying what RynthCore does and what OpenAC does instead (see `src/AcDream.Rynth.EngineUi/StandIns/DashboardStandIns.cs`).
- No new host slots, no `IRynthGameView` changes, no contract bump (nothing in `AcDream.Plugin.Abstractions` changes).
- Not compiled: their real `ServerMenus.cs`, `ServerSelection*.cs`, `ServerStateJson.cs`, `IltInfo.cs`, `ServerInfo.cs`, `AccountHooks.cs`, `RynthCoreChatCommands.cs`, `InventoryModel.cs` code (only its types are extracted).
- Don't run `tools/update-package-locks.ps1`.
- Test command base: `dotnet test tests/AcDream.Rynth.Host.Tests` (current baseline 331/331 passing).
- Commit message trailer: `Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz`.

**User decisions (already made):**
- Full face: slot grid plus the whole sections rail (2026-10-19: "B").
- Server detection stays "never active" (ILT/Aelrynth) instead of compiling their real `IltInfo` (2026-10-19: "happy for you to choose").
- Live checks happen later, when the user says so (memory: ask-before-live-client).

---

### Task 1: Panel host embedding

**Goal:** The ImGui panel host stand-in can draw a separate copy of a registered face inside another panel's window, exactly as RynthCore's `ImGuiPanelHost.DrawEmbedded` does.

**Files:**
- Modify: `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiPanelHost.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/ImGuiPanelHostTests.cs`

**Acceptance Criteria:**
- [ ] `CanEmbed(title)` is true for a registered face and false for an unknown title and for `"Mini Remote"`.
- [ ] `TryGetDefaultSize(title, out size)` returns the spec's `DefaultSize`; false (zero) for unknown.
- [ ] `DrawEmbedded(host, title, size)` creates a new instance from the factory (not the open window's instance), calls `OnShown` once, and draws it every call inside a child of the current window.
- [ ] A copy not drawn for more than 750 ms is dropped by the next `DrawAll` with `OnHidden`; drawing again creates a fresh copy.
- [ ] While a copy draws, `RequestSize`, `RequestPos` and `DragWindowWithLastItem` for any key do nothing.
- [ ] A copy that throws 3 times shows "<title> stopped drawing after repeated errors: ..." and stops calling `Draw`; ImGui stays balanced; each failure is logged.
- [ ] Unknown or non-embeddable title draws "Not available.".
- [ ] `Reset()` drops copies (without calling `OnHidden`).

**Verify:** `dotnet test tests/AcDream.Rynth.Host.Tests --filter ImGuiPanelHostTests` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** (append to `ImGuiPanelHostTests`; `RecordingPanel` already exists at the bottom of the file — add the `Counting` factory helper below)

```csharp
    private sealed class CountingFactory
    {
        public List<RecordingPanel> Made { get; } = new();
        public bool Throw { get; init; }
        public IImGuiPanel Make()
        {
            var p = new RecordingPanel { Throw = Throw };
            Made.Add(p);
            return p;
        }
    }

    private static void HostWindow(Action body)
    {
        ImGui.SetNextWindowSize(new Vector2(400, 300), ImGuiCond.Always);
        ImGui.Begin("host");
        try { body(); }
        finally { ImGui.End(); }
    }

    [Fact]
    public void CanEmbedAndDefaultSizeFollowTheRegistry()
    {
        ImGuiPanelHost.Register("Skills", new PanelSpec(new Vector2(380, 420), new Vector2(50, 50)), () => new RecordingPanel());
        ImGuiPanelHost.Register("Mini Remote", new PanelSpec(new Vector2(184, 150), new Vector2(50, 50)), () => new RecordingPanel());

        Assert.True(ImGuiPanelHost.CanEmbed("Skills"));
        Assert.False(ImGuiPanelHost.CanEmbed("Mini Remote"));
        Assert.False(ImGuiPanelHost.CanEmbed("Nope"));
        Assert.True(ImGuiPanelHost.TryGetDefaultSize("Skills", out Vector2 size));
        Assert.Equal(new Vector2(380, 420), size);
        Assert.False(ImGuiPanelHost.TryGetDefaultSize("Nope", out size));
        Assert.Equal(Vector2.Zero, size);
    }

    [Fact]
    public void AnEmbeddedCopyIsItsOwnInstanceShownOnceAndDrawnEachCall()
    {
        var factory = new CountingFactory();
        ImGuiPanelHost.Register("Skills", new PanelSpec(new Vector2(380, 420), new Vector2(50, 50)), factory.Make);
        ImGuiPanelHost.Open("Skills");

        for (int i = 0; i < 3; i++)
            _imgui.Frame(() => { ImGuiPanelHost.DrawAll(); HostWindow(() => ImGuiPanelHost.DrawEmbedded("MiniRemote", "Skills", new Vector2(200, 150))); });

        Assert.Equal(2, factory.Made.Count);              // the open window's and the copy
        RecordingPanel copy = factory.Made[1];
        Assert.True(copy.Shown);
        Assert.Equal(3, copy.Draws);
        Assert.Equal(new Vector2(200, 150), copy.WindowSize);
    }

    [Fact]
    public void AnIdleCopyIsHiddenAndDroppedByDrawAll()
    {
        var factory = new CountingFactory();
        ImGuiPanelHost.Register("Skills", new PanelSpec(new Vector2(380, 420), new Vector2(50, 50)), factory.Make);
        _imgui.Frame(() => HostWindow(() => ImGuiPanelHost.DrawEmbedded("MiniRemote", "Skills", Vector2.Zero)));
        RecordingPanel first = factory.Made[0];

        Thread.Sleep(800);
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        Assert.True(first.Hidden);

        _imgui.Frame(() => HostWindow(() => ImGuiPanelHost.DrawEmbedded("MiniRemote", "Skills", Vector2.Zero)));
        Assert.Equal(2, factory.Made.Count);
    }

    [Fact]
    public void ACopyCannotResizeOrMoveWindows()
    {
        var copy = new ResizingPanel();
        ImGuiPanelHost.Register("Skills", new PanelSpec(new Vector2(380, 420), new Vector2(50, 50)), () => copy);
        var other = new RecordingPanel();
        ImGuiPanelHost.RegisterDynamic("script:other", "Other", new PanelSpec(new Vector2(200, 100), new Vector2(50, 50)), () => other);
        ImGuiPanelHost.Open("script:other");
        _imgui.Frame(ImGuiPanelHost.DrawAll);

        _imgui.Frame(() => HostWindow(() => ImGuiPanelHost.DrawEmbedded("MiniRemote", "Skills", Vector2.Zero)));
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        _imgui.Frame(ImGuiPanelHost.DrawAll);

        Assert.Equal(new Vector2(200, 100), other.WindowSize);
    }

    [Fact]
    public void ACopyThatKeepsThrowingShowsItsErrorAndStops()
    {
        var log = new List<string>();
        ImGuiPanelHost.Log = log.Add;
        var factory = new CountingFactory { Throw = true };
        ImGuiPanelHost.Register("Skills", new PanelSpec(new Vector2(380, 420), new Vector2(50, 50)), factory.Make);

        for (int i = 0; i < 5; i++)
            _imgui.Frame(() => HostWindow(() => ImGuiPanelHost.DrawEmbedded("MiniRemote", "Skills", Vector2.Zero)));
        void After() { ImGui.Begin("after"); ImGui.Text("still fine"); ImGui.End(); }
        int vertices = _imgui.Frame(After);

        Assert.Equal(3, factory.Made[0].Draws);
        Assert.Equal(3, log.Count(line => line.Contains("embedded Skills", StringComparison.Ordinal)));
        Assert.True(vertices > 0);
    }

    [Fact]
    public void AnUnknownOrHostTitleDrawsNotAvailable()
    {
        ImGuiPanelHost.Register("Mini Remote", new PanelSpec(new Vector2(184, 150), new Vector2(50, 50)), () => new RecordingPanel());
        int vertices = _imgui.Frame(() => HostWindow(() =>
        {
            ImGuiPanelHost.DrawEmbedded("MiniRemote", "Nope", Vector2.Zero);
            ImGuiPanelHost.DrawEmbedded("MiniRemote", "Mini Remote", Vector2.Zero);
        }));
        Assert.True(vertices > 0);
    }

    private sealed class ResizingPanel : IImGuiPanel
    {
        public void Draw()
        {
            ImGuiPanelHost.RequestSize("script:other", new Vector2(600, 600));
            ImGuiPanelHost.RequestPos("script:other", new Vector2(50, 50));
            ImGui.Text("copy");
        }
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/AcDream.Rynth.Host.Tests --filter ImGuiPanelHostTests`
Expected: build errors — `CanEmbed`, `TryGetDefaultSize`, `DrawEmbedded` not defined.

- [ ] **Step 3: Implement** (in `ImGuiPanelHost.cs`)

Add fields beside `_entries`:

```csharp
    private sealed class EmbeddedFace
    {
        public required string Title;
        public required IImGuiPanel Instance;
        public long LastDrawnMs;
        public int Failures;
        public string? Error;
    }

    /// <summary>Embedded copies by host + title (RynthCore's EmbeddedFaces). Draw thread only.</summary>
    private static readonly Dictionary<string, EmbeddedFace> _embedded = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A copy not drawn for this long is hidden and dropped (its section was left).</summary>
    private const long EmbedIdleMs = 750;

    /// <summary>Faces that can't be embedded: the hosts themselves.</summary>
    private static readonly HashSet<string> NotEmbeddable = new(StringComparer.OrdinalIgnoreCase) { "Mini Remote" };

    /// <summary>True while an embedded copy draws: window moves and size requests are ignored.</summary>
    [ThreadStatic] private static bool _inEmbed;
```

Add the public members (after `IsTransitioning`):

```csharp
    /// <summary>True when <paramref name="title"/> is registered and may be drawn inside another panel.</summary>
    public static bool CanEmbed(string title) => !NotEmbeddable.Contains(title) && HasFace(title);

    /// <summary>A registered face's default window size (logical units).</summary>
    public static bool TryGetDefaultSize(string title, out Vector2 size)
    {
        Entry? entry;
        lock (_gate) _entries.TryGetValue(title, out entry);
        size = entry?.Spec.DefaultSize ?? Vector2.Zero;
        return entry != null;
    }

    /// <summary>
    /// RynthCore's DrawEmbedded: draws a copy of the face <paramref name="title"/> into the current
    /// window, in a child of <paramref name="size"/> (pixels; 0 = fill). <paramref name="hostKey"/>
    /// separates the copies of different hosts. Created (OnShown) on first draw, dropped (OnHidden)
    /// by DrawAll once it hasn't drawn for <see cref="EmbedIdleMs"/>. Inside a window.
    /// </summary>
    public static void DrawEmbedded(string hostKey, string title, Vector2 size)
    {
        Entry? entry;
        lock (_gate) _entries.TryGetValue(title, out entry);
        if (entry == null || NotEmbeddable.Contains(title))
        {
            ImGui.TextDisabled("Not available.");
            return;
        }

        string key = hostKey + "\u001f" + title;
        if (!_embedded.TryGetValue(key, out EmbeddedFace? face))
        {
            try
            {
                IImGuiPanel instance = entry.Factory();
                instance.OnShown();
                face = new EmbeddedFace { Title = title, Instance = instance };
            }
            catch (Exception error)
            {
                Log?.Invoke($"[ScriptWindows] embedding {title} failed: {error.GetType().Name}: {error.Message}");
                face = new EmbeddedFace { Title = title, Instance = new NullFace(), Error = $"{title} failed to open: {error.Message}" };
            }
            _embedded[key] = face;
        }
        face.LastDrawnMs = Environment.TickCount64;

        PanelSpec spec = entry.Spec;
        float s = EngineFrameController.FontScale > 0f ? EngineFrameController.FontScale : 1f;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, spec.EdgeToEdge ? Vector2.Zero : new Vector2(6, 4) * s);
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoBackground | spec.BodyFlags;
        if (spec.EdgeToEdge) flags |= ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        bool open = ImGui.BeginChild("##embed_" + title, size, ImGuiChildFlags.AlwaysUseWindowPadding, flags);
        ImGui.PopStyleVar();
        try
        {
            if (!open) return;
            if (face.Error != null)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.4f, 0.3f, 1f));
                ImGui.PushTextWrapPos(0f);
                ImGui.TextUnformatted(face.Error);
                ImGui.PopTextWrapPos();
                ImGui.PopStyleColor();
                return;
            }
            _inEmbed = true;
            try { face.Instance.Draw(); }
            catch (Exception error)
            {
                face.Failures++;
                Log?.Invoke($"[ScriptWindows] embedded {title}: {error.GetType().Name}: {error.Message} ({face.Failures}/{MaxFailures})");
                if (face.Failures >= MaxFailures)
                    face.Error = $"{title} stopped drawing after repeated errors: {error.Message}";
            }
            finally { _inEmbed = false; }
        }
        finally
        {
            ImGui.EndChild();
        }
    }

    /// <summary>Hides and drops embedded copies whose host stopped drawing them.</summary>
    private static void SweepEmbedded()
    {
        if (_embedded.Count == 0) return;
        long now = Environment.TickCount64;
        List<string>? stale = null;
        foreach (KeyValuePair<string, EmbeddedFace> pair in _embedded)
            if (now - pair.Value.LastDrawnMs > EmbedIdleMs) (stale ??= new List<string>()).Add(pair.Key);
        if (stale == null) return;
        foreach (string key in stale)
        {
            EmbeddedFace face = _embedded[key];
            _embedded.Remove(key);
            Guard("embedded " + face.Title + " OnHidden", face.Instance.OnHidden);
        }
    }

    /// <summary>Stands in for a face that failed to open, so its error line can draw.</summary>
    private sealed class NullFace : IImGuiPanel
    {
        public void Draw()
        {
        }
    }
```

Guard the three requests — first line of `DragWindowWithLastItem`, `RequestSize`, `RequestPos`:

```csharp
        if (_inEmbed) return;   // an embedded copy: that window isn't this face's to move or resize
```

In `DrawAll`, call `SweepEmbedded();` as the first statement. In `Reset`, add `_embedded.Clear();`.

ImGui child-padding note: if ImGui.NET's `BeginChild` overload with `ImGuiChildFlags` isn't the one already used in the repo, match the overload `MiniRemoteFace.cs` uses (`BeginChild(string, Vector2, ImGuiChildFlags, ImGuiWindowFlags)`) — it compiles against the same ImGui.NET.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/AcDream.Rynth.Host.Tests --filter ImGuiPanelHostTests` → all pass.
Then the whole suite: `dotnet test tests/AcDream.Rynth.Host.Tests` → 331 + 6 pass.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiPanelHost.cs tests/AcDream.Rynth.Host.Tests/ImGuiPanelHostTests.cs
git commit -m "feat(rynth): panel host draws embedded copies of faces like RynthCore's"
```

---

### Task 2: `InventoryModel` item use

**Goal:** The `InventoryModel` stand-in takes RynthCore's one-at-a-time `InventoryRequest` (Use), sends it on the game thread through the host's `UseObject`, and publishes `Pending` / `LastResult` with RynthCore's own texts.

**Files:**
- Modify: `src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj` (the `InventoryModel.cs` `RynthPanelTypes` item, ~line 116)
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/DashboardStandIns.cs` (`InventoryModel` becomes `static partial`)
- Create: `src/AcDream.Rynth.EngineUi/StandIns/InventoryActionsStandIn.cs`
- Modify: `src/AcDream.Rynth.EngineUi/RynthAiDashboard.cs` (`Tick`, `Stop`)
- Modify: `src/AcDream.Rynth.Host/RynthChatHookup.cs` (set/unset the hooks)
- Test: `tests/AcDream.Rynth.Host.Tests/RynthInventoryUseTests.cs` (create)

**Acceptance Criteria:**
- [ ] `InventoryActionKind`, `InventoryRequest`, `InventoryPending`, `InventoryResult` come from their `InventoryModel.cs` via `RynthPanelTypes` (no hand copies).
- [ ] `Request` refuses with "Still waiting for the server: Using X" while one is pending, "Still waiting for the game: Using X" while held, "Another item action is about to be sent." while one is requested and not yet taken.
- [ ] `MainThreadTick`: not in the world → "Not in the world."; item unreadable → "X is gone."; not carried by the player (container chain, or wielded) → "X is no longer in your packs."; non-Use kind → "Not available in OpenAC."; `UseSender` false → "Couldn't send: Using X.".
- [ ] Busy (`Busy()` true) holds the request (`Pending.WaitingForGame`, `BusyWith` "another item action"); sent the first tick it is free; after 10 s still busy → "Not sent: the game stayed busy with another item action. Try again.".
- [ ] After sending, each tick: WeenieError sequence changed → "Refused: Using X (error 0xNN)." not ok; UseDone sequence changed → "Used X." ok (error 0) or "Couldn't use X (error 0xNN)." not ok; item gone → "Used X." ok; container/wielder/stack changed → "Used X." ok; 6 s with none → "No answer seen for: Using X. You can act again." not ok.
- [ ] `RynthChatHookup` sets `UseSender`/`Busy`/`Replies`/`ReadItem`/`PlayerId` on construction and clears only its own on Dispose; `RynthAiDashboard.Tick` calls `InventoryModel.MainThreadTick()`; `Stop` clears requests/pending/result.

**Verify:** `dotnet test tests/AcDream.Rynth.Host.Tests --filter RynthInventoryUseTests` → all pass.

**Steps:**

- [ ] **Step 1: Extract their action types** — change the csproj item to:

```xml
    <RynthPanelTypes Include="$(RynthEngine)Compatibility\InventoryModel.cs" Types="=InventoryCategory,InventoryItem,InventoryPack,InventoryView,InventoryActionKind,InventoryRequest,InventoryPending,InventoryResult" />
```

Build `dotnet build src/AcDream.Rynth.EngineUi` → succeeds (the types are only extracted).

- [ ] **Step 2: Write the failing tests** (`tests/AcDream.Rynth.Host.Tests/RynthInventoryUseTests.cs`)

```csharp
using System.Diagnostics;
using RynthCore.Engine.Compatibility;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthInventoryUseTests : IDisposable
{
    private const uint Player = 0x50000001u, Kit = 0x80000020u, Pack = 0x80000010u;
    private long _now = 1_000_000;
    private bool _busy;
    private bool _sendAnswer = true;
    private readonly List<uint> _sent = new();
    private (int UseDoneSeq, uint UseDoneError, int ErrorSeq, uint Error) _replies;
    private readonly Dictionary<uint, (uint Container, uint Wielder, int Stack)> _items = new();

    public RynthInventoryUseTests()
    {
        InventoryModel.ResetActions();
        InventoryModel.Clock = () => _now;
        InventoryModel.PlayerId = () => Player;
        InventoryModel.Busy = () => _busy;
        InventoryModel.UseSender = id => { _sent.Add(id); return _sendAnswer; };
        InventoryModel.Replies = () => _replies;
        InventoryModel.ReadItem = (uint id, out uint container, out uint wielder, out int stack) =>
        {
            bool found = _items.TryGetValue(id, out var f);
            (container, wielder, stack) = f;
            return found;
        };
        _items[Kit] = (Pack, 0, 5);
        _items[Pack] = (Player, 0, 1);
    }

    public void Dispose()
    {
        InventoryModel.ResetActions();
        InventoryModel.Clock = Stopwatch.GetTimestamp;
        InventoryModel.PlayerId = null;
        InventoryModel.Busy = null;
        InventoryModel.UseSender = null;
        InventoryModel.Replies = null;
        InventoryModel.ReadItem = null;
    }

    private static InventoryRequest Use(uint id = Kit, string name = "Healing Kit") =>
        new() { Kind = InventoryActionKind.Use, ItemId = id, ItemName = name };

    private void After(double seconds) => _now += (long)(seconds * Stopwatch.Frequency);

    private void Tick() => InventoryModel.MainThreadTick();

    [Fact]
    public void AUseIsSentOnTheNextTickAndUseDoneEndsIt()
    {
        Assert.True(InventoryModel.Request(Use(), out string why));
        Assert.Equal("", why);
        Assert.Empty(_sent);

        Tick();
        Assert.Equal([Kit], _sent);
        Assert.Equal("Using Healing Kit", InventoryModel.Pending!.Label);
        Assert.False(InventoryModel.Request(Use(), out why));
        Assert.Equal("Still waiting for the server: Using Healing Kit", why);

        _replies = (1, 0, 0, 0);
        Tick();
        Assert.Null(InventoryModel.Pending);
        Assert.Equal(("Used Healing Kit.", true), (InventoryModel.LastResult!.Text, InventoryModel.LastResult.Ok));
    }

    [Fact]
    public void ASecondRequestBeforeTheTickIsRefused()
    {
        Assert.True(InventoryModel.Request(Use(), out _));
        Assert.False(InventoryModel.Request(Use(), out string why));
        Assert.Equal("Another item action is about to be sent.", why);
    }

    [Fact]
    public void AnErrorUseDoneOrWeenieErrorIsAFailure()
    {
        InventoryModel.Request(Use(), out _);
        Tick();
        _replies = (1, 0x3E8, 0, 0);
        Tick();
        Assert.Equal(("Couldn't use Healing Kit (error 0x3E8).", false), (InventoryModel.LastResult!.Text, InventoryModel.LastResult.Ok));

        InventoryModel.Request(Use(), out _);
        Tick();
        _replies = (1, 0x3E8, 1, 0x1D);
        Tick();
        Assert.Equal(("Refused: Using Healing Kit (error 0x1D).", false), (InventoryModel.LastResult!.Text, InventoryModel.LastResult.Ok));
    }

    [Fact]
    public void TheItemGoneOrChangedMeansUsed()
    {
        InventoryModel.Request(Use(), out _);
        Tick();
        _items[Kit] = (Pack, 0, 4);
        Tick();
        Assert.Equal("Used Healing Kit.", InventoryModel.LastResult!.Text);

        InventoryModel.Request(Use(), out _);
        Tick();
        _items.Remove(Kit);
        Tick();
        Assert.Equal(("Used Healing Kit.", true), (InventoryModel.LastResult!.Text, InventoryModel.LastResult.Ok));
    }

    [Fact]
    public void NoAnswerForSixSecondsLetsThePlayerActAgain()
    {
        InventoryModel.Request(Use(), out _);
        Tick();
        After(5.9);
        Tick();
        Assert.NotNull(InventoryModel.Pending);
        After(0.2);
        Tick();
        Assert.Equal(("No answer seen for: Using Healing Kit. You can act again.", false),
            (InventoryModel.LastResult!.Text, InventoryModel.LastResult.Ok));
    }

    [Fact]
    public void ABusyGameHoldsTheUseThenSendsOrGivesUp()
    {
        _busy = true;
        InventoryModel.Request(Use(), out _);
        Tick();
        Assert.Empty(_sent);
        Assert.True(InventoryModel.Pending!.WaitingForGame);
        Assert.Equal("another item action", InventoryModel.Pending.BusyWith);
        Assert.False(InventoryModel.Request(Use(), out string why));
        Assert.Equal("Still waiting for the game: Using Healing Kit", why);

        _busy = false;
        Tick();
        Assert.Equal([Kit], _sent);

        InventoryModel.ResetActions();
        _busy = true;
        InventoryModel.Request(Use(), out _);
        Tick();
        After(10.1);
        Tick();
        Assert.Equal("Not sent: the game stayed busy with another item action. Try again.", InventoryModel.LastResult!.Text);
    }

    [Theory]
    [InlineData("gone")]
    [InlineData("notcarried")]
    [InlineData("noplayer")]
    [InlineData("sendfails")]
    [InlineData("otherkind")]
    public void ChecksBeforeSendingFinishWithTheirTexts(string scenario)
    {
        InventoryRequest r = Use();
        string expected = "";
        switch (scenario)
        {
            case "gone": _items.Remove(Kit); expected = "Healing Kit is gone."; break;
            case "notcarried": _items[Kit] = (0x80009999u, 0, 5); expected = "Healing Kit is no longer in your packs."; break;
            case "noplayer": InventoryModel.PlayerId = () => 0; expected = "Not in the world."; break;
            case "sendfails": _sendAnswer = false; expected = "Couldn't send: Using Healing Kit."; break;
            case "otherkind": r = new InventoryRequest { Kind = InventoryActionKind.Drop, ItemId = Kit, ItemName = "Healing Kit" }; expected = "Not available in OpenAC."; break;
        }
        InventoryModel.Request(r, out _);
        Tick();
        Assert.Equal((expected, false), (InventoryModel.LastResult!.Text, InventoryModel.LastResult.Ok));
        Assert.Null(InventoryModel.Pending);
    }

    [Fact]
    public void AWornItemCanBeUsed()
    {
        _items[Kit] = (0, Player, 1);
        InventoryModel.Request(Use(), out _);
        Tick();
        Assert.Equal([Kit], _sent);
    }
}
```

Add to `RynthAiDashboardTests` (it already has `_view`, `_log`, `Carried`):

```csharp
    [Fact]
    public void TheHookupSendsMiniRemoteUsesThroughTheView()
    {
        const uint Player = 0x50000001u, Kit = 0x80000020u;
        _view.Objects[Kit] = Carried(Player, 0, 5);
        using var host = new RynthHost(_view, _log.Add, []);
        using (var hookup = new RynthChatHookup(host, _view, _log.Add))
        {
            Assert.True(InventoryModel.Request(new InventoryRequest { Kind = InventoryActionKind.Use, ItemId = Kit, ItemName = "Kit" }, out _));
            InventoryModel.MainThreadTick();
            Assert.Contains($"use {Kit:X8}", _view.Actions);
        }
        Assert.Null(InventoryModel.UseSender);
        InventoryModel.ResetActions();
    }
```

(If `_view.PlayerId` isn't `0x50000001u` in that test class, set it at the top of the test.)

- [ ] **Step 3: Run to verify they fail** — `dotnet test tests/AcDream.Rynth.Host.Tests --filter "RynthInventoryUseTests|TheHookupSendsMiniRemoteUses"` → build errors (`MainThreadTick`, `Clock`, … not defined).

- [ ] **Step 4: Implement.** In `DashboardStandIns.cs` change `internal static class InventoryModel` to `internal static partial class InventoryModel` (summary: append "Item use: InventoryActionsStandIn.cs."). Create `InventoryActionsStandIn.cs`:

```csharp
using System;
using System.Diagnostics;
using System.Threading;

namespace RynthCore.Engine.Compatibility
{
    /// <summary>
    /// RynthCore's InventoryModel item actions: one at a time, sent on acclient's main thread
    /// behind the client's item gate and watched until the server answers. OpenAC keeps their
    /// rules and texts for Use (the only kind the Mini Remote sends): the host's hooks send it
    /// through the same UseObject RynthAi's slot uses, "busy" is the client's busy count, and
    /// the answer is read from the UseDone / WeenieError counters. Other kinds are refused.
    /// </summary>
    internal static partial class InventoryModel
    {
        internal delegate bool ItemReader(uint id, out uint container, out uint wielder, out int stack);

        private static readonly long Freq = Stopwatch.Frequency;
        private static readonly long ActionTimeoutTicks = Freq * 6;   // no effect seen: stop waiting
        private static readonly long GateWaitTicks = Freq * 10;       // held for the game at most this long
        private const string BusyWithText = "another item action";

        /// <summary>Sends a use (the host: IRynthGameView.UseObject). Game thread.</summary>
        internal static Func<uint, bool>? UseSender { get; set; }

        /// <summary>The client has actions outstanding (BusyCount &gt; 0).</summary>
        internal static Func<bool>? Busy { get; set; }

        /// <summary>The server's UseDone and refusal counters.</summary>
        internal static Func<(int UseDoneSeq, uint UseDoneError, int ErrorSeq, uint Error)>? Replies { get; set; }

        /// <summary>An item's container, wielder and stack size; false when it isn't known.</summary>
        internal static ItemReader? ReadItem { get; set; }

        internal static Func<uint>? PlayerId { get; set; }

        /// <summary>Stopwatch ticks (tests replace it).</summary>
        internal static Func<long> Clock { get; set; } = Stopwatch.GetTimestamp;

        private sealed class InFlight
        {
            public InventoryRequest Request = null!;
            public long Started;
            public int UseDoneSeq, ErrorSeq;
            public uint Container, Wielder;
            public int Stack;
        }

        private static InventoryRequest? _requested;
        private static InventoryRequest? _held;
        private static long _heldSince;
        private static InFlight? _inFlight;
        private static InventoryPending? _pending;
        private static InventoryResult? _result;

        public static InventoryPending? Pending => Volatile.Read(ref _pending);

        public static InventoryResult? LastResult => Volatile.Read(ref _result);

        /// <summary>Their Request: refused (with the reason) while another action waits; nothing is queued.</summary>
        public static bool Request(InventoryRequest request, out string why)
        {
            InventoryPending? busy = Pending;
            if (busy != null)
            {
                why = (busy.WaitingForGame ? "Still waiting for the game: " : "Still waiting for the server: ") + busy.Label;
                return false;
            }
            if (Interlocked.CompareExchange(ref _requested, request, null) != null)
            {
                why = "Another item action is about to be sent.";
                return false;
            }
            why = string.Empty;
            return true;
        }

        /// <summary>Game thread, every dashboard tick: take a request, release a held one, watch the one in flight.</summary>
        internal static void MainThreadTick()
        {
            long now = Clock();
            if (_inFlight != null) Watch(now);
            else if (_held != null) TryReleaseHeld(now);
            InventoryRequest? r = Interlocked.Exchange(ref _requested, null);
            if (r != null) Begin(r, now);
        }

        private static string ActionLabel(InventoryRequest r) => "Using " + r.ItemName;

        private static void Begin(InventoryRequest r, long now)
        {
            if (_inFlight != null || _held != null) { Finish("Still waiting for the server.", false, now); return; }
            if (r.Kind != InventoryActionKind.Use) { Finish("Not available in OpenAC.", false, now); return; }
            if (!(Busy?.Invoke() ?? false)) { Send(r, now); return; }
            _held = r;
            _heldSince = now;
            Volatile.Write(ref _pending, new InventoryPending
            {
                Kind = r.Kind, ItemId = r.ItemId, Label = ActionLabel(r), StartedTicks = now,
                WaitingForGame = true, BusyWith = BusyWithText,
            });
        }

        private static void TryReleaseHeld(long now)
        {
            InventoryRequest r = _held!;
            if (!(Busy?.Invoke() ?? false))
            {
                _held = null;
                Volatile.Write(ref _pending, null);
                Send(r, now);
                return;
            }
            if (now - _heldSince >= GateWaitTicks)
            {
                _held = null;
                Volatile.Write(ref _pending, null);
                Finish($"Not sent: the game stayed busy with {BusyWithText}. Try again.", false, now);
            }
        }

        private static void Send(InventoryRequest r, long now)
        {
            uint player = PlayerId?.Invoke() ?? 0;
            if (player == 0) { Finish("Not in the world.", false, now); return; }
            if (ReadItem == null || !ReadItem(r.ItemId, out uint container, out uint wielder, out int stack))
            {
                Finish($"{r.ItemName} is gone.", false, now);
                return;
            }
            if (wielder != player && !(wielder == 0 && Carries(container, player)))
            {
                Finish($"{r.ItemName} is no longer in your packs.", false, now);
                return;
            }
            if (!(UseSender?.Invoke(r.ItemId) ?? false))
            {
                Finish($"Couldn't send: {ActionLabel(r)}.", false, now);
                return;
            }
            var replies = Replies?.Invoke() ?? default;
            _inFlight = new InFlight
            {
                Request = r, Started = now, UseDoneSeq = replies.UseDoneSeq, ErrorSeq = replies.ErrorSeq,
                Container = container, Wielder = wielder, Stack = stack,
            };
            Volatile.Write(ref _pending, new InventoryPending { Kind = r.Kind, ItemId = r.ItemId, Label = ActionLabel(r), StartedTicks = now });
        }

        /// <summary>The player's main pack, or a pack in it (their OwnedPack).</summary>
        private static bool Carries(uint container, uint player)
        {
            if (container == player) return true;
            return container != 0 && ReadItem != null && ReadItem(container, out uint outer, out _, out _) && outer == player;
        }

        private static void Watch(long now)
        {
            InFlight a = _inFlight!;
            InventoryRequest r = a.Request;
            var replies = Replies?.Invoke() ?? default;
            uint container = 0, wielder = 0;
            int stack = 0;
            bool exists = ReadItem != null && ReadItem(r.ItemId, out container, out wielder, out stack);
            string? done = null;
            bool ok = true;
            if (replies.ErrorSeq != a.ErrorSeq)
            {
                done = $"Refused: {ActionLabel(r)} (error 0x{replies.Error:X}).";
                ok = false;
            }
            else if (!exists)
                done = $"Used {r.ItemName}.";
            else if (replies.UseDoneSeq != a.UseDoneSeq)
            {
                ok = replies.UseDoneError == 0;
                done = ok ? $"Used {r.ItemName}." : $"Couldn't use {r.ItemName} (error 0x{replies.UseDoneError:X}).";
            }
            else if (container != a.Container || wielder != a.Wielder || stack != a.Stack)
                done = $"Used {r.ItemName}.";
            else if (now - a.Started >= ActionTimeoutTicks)
            {
                done = $"No answer seen for: {ActionLabel(r)}. You can act again.";
                ok = false;
            }
            if (done == null) return;
            _inFlight = null;
            Volatile.Write(ref _pending, null);
            Finish(done, ok, now);
        }

        private static void Finish(string text, bool ok, long now) =>
            Volatile.Write(ref _result, new InventoryResult { Text = text, Ok = ok, AtTicks = now });

        /// <summary>Forgets every request, held and in-flight action and the last result (host stop, tests).</summary>
        internal static void ResetActions()
        {
            Volatile.Write(ref _requested, null);
            _held = null;
            _inFlight = null;
            Volatile.Write(ref _pending, null);
            Volatile.Write(ref _result, null);
        }
    }
}
```

The check order (refusal, gone, UseDone, changed, timeout) is RynthCore's `Watch`.

`RynthAiDashboard.Tick`: after `int ran = UiDataHub.Drain(Log);` add `InventoryModel.MainThreadTick();` (RynthCore's main-thread tick). `RynthAiDashboard.Stop`: in the `try`, add `InventoryModel.ResetActions();`.

`RynthChatHookup`: add fields and wire them like the others (constructor sets, `Dispose` clears only if still equal):

```csharp
    private readonly Func<uint, bool> _useSender;
    private readonly Func<bool> _busy;
    private readonly Func<(int, uint, int, uint)> _replies;
    private readonly InventoryModel.ItemReader _readItem;
    private readonly Func<uint> _usePlayerId;

    // constructor:
        _useSender = id => Guard(nameof(InventoryModel.UseSender), () => _view.UseObject(id), false);
        _busy = () => Guard(nameof(InventoryModel.Busy), () => _view.BusyCount > 0, false);
        _replies = () => Guard(nameof(InventoryModel.Replies), () =>
        {
            AcDream.Core.Items.ServerReplySnapshot r = _view.ServerReplies;
            return (r.UseDoneSequence, r.LastUseDoneError, r.WeenieErrorSequence, r.LastWeenieError);
        }, default);
        _readItem = ReadItem;
        _usePlayerId = _playerId;
        InventoryModel.UseSender = _useSender;
        InventoryModel.Busy = _busy;
        InventoryModel.Replies = _replies;
        InventoryModel.ReadItem = _readItem;
        InventoryModel.PlayerId = _usePlayerId;

    // Dispose:
        if (InventoryModel.UseSender == _useSender) InventoryModel.UseSender = null;
        if (InventoryModel.Busy == _busy) InventoryModel.Busy = null;
        if (InventoryModel.Replies == _replies) InventoryModel.Replies = null;
        if (InventoryModel.ReadItem == _readItem) InventoryModel.ReadItem = null;
        if (InventoryModel.PlayerId == _usePlayerId) InventoryModel.PlayerId = null;

    private bool ReadItem(uint id, out uint container, out uint wielder, out int stack)
    {
        RynthObjectInfo? info = Guard(nameof(InventoryModel.ReadItem), () => _view.TryGetObject(id, out RynthObjectInfo i) ? i : null, null);
        container = info?.ContainerId ?? 0;
        wielder = info?.WielderId ?? 0;
        stack = info?.StackSize ?? 0;
        return info is not null;
    }
```

(Match the existing `Guard<T>(string, Func<T>, T)` helper's signature in this file.) The tuple field names of `_replies` must match `InventoryModel.Replies`' named tuple — declare the field as `Func<(int UseDoneSeq, uint UseDoneError, int ErrorSeq, uint Error)>`.

- [ ] **Step 5: Run to verify they pass** — `dotnet test tests/AcDream.Rynth.Host.Tests --filter "RynthInventoryUseTests|TheHookupSendsMiniRemoteUses"` → pass; full suite → no new failures.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.Rynth.EngineUi src/AcDream.Rynth.Host/RynthChatHookup.cs tests/AcDream.Rynth.Host.Tests
git commit -m "feat(rynth): InventoryModel item use through OpenAC, RynthCore's rules and texts"
```

---

### Task 3: Sections compiled with server and plugin stand-ins

**Goal:** RynthCore's `UI/Sections` layout and `IltDetect` compile unchanged, ticked by the dashboard, and `SectionRegistry.Current` lists their 12 core sections filled by the engine panels OpenAC registers, with no active servers.

**Files:**
- Modify: `src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj` (Compile items)
- Create: `src/AcDream.Rynth.EngineUi/StandIns/SectionsStandIns.cs`
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/DashboardStandIns.cs` (`PluginManager`, `ServerInfo`)
- Modify: `src/AcDream.Rynth.EngineUi/RynthAiDashboard.cs` (`Tick`)
- Modify: `src/AcDream.Rynth.Host/RynthChatHookup.cs` (`AccountHooks.WorldName`)
- Test: `tests/AcDream.Rynth.Host.Tests/RynthMiniRemoteTests.cs` (create)

**Acceptance Criteria:**
- [ ] Compiled unchanged: `UI/Sections/SectionCatalog.cs`, `SectionManifest.cs`, `SectionRegistry.cs`, `ServerForce.cs`, `ServerForceStore.cs`, `NotLoadedServers.cs`, `Compatibility/IltDetect.cs`.
- [ ] Stand-ins: `PluginManager.CollectSectionManifests()` empty, `SkippedServerPlugins` empty, `RequestServerPluginLoad` no-op; `IltInfo` (`ServerId` "ilt", `IsIlt` false, `Detected` reason "OpenAC: no ILT plugins", `Invalidate`); `ServerInfo` + `ServerId` "aelrynth", `Detected` (`ServerVerdict`, reason "OpenAC never connects to Aelrynth"), `Invalidate`, `LaunchHost` ""; `AccountHooks.AnnouncedWorldName` from a host-set delegate.
- [ ] `RynthAiDashboard.Tick` calls `SectionRegistry.Tick()`.
- [ ] With the dashboard started and one tick: sections in order char, map, skills, combat, nav, inventory, crafting, spells, chat, friends, fellowship, pets; map panes = ["Dungeon Map"]; skills = ["Skills"]; combat = ["Damage"]; nav = ["Nav"]; inventory = ["Item Count", "Items"]; char = one Overview pane; both servers present and inactive; no groups anywhere.

**Verify:** `dotnet test tests/AcDream.Rynth.Host.Tests --filter RynthMiniRemoteTests` → pass.

**Steps:**

- [ ] **Step 1: Write the failing test** (`tests/AcDream.Rynth.Host.Tests/RynthMiniRemoteTests.cs`)

```csharp
using AcDream.Rynth.EngineUi;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.UI.Sections;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthMiniRemoteTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-mremote-");

    public RynthMiniRemoteTests()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = _log.Add;
    }

    public void Dispose()
    {
        RynthAiDashboard.Stop();
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = null;
        LoginLifecycleHooks.HasObservedLoginComplete = false;
        _sandbox.Dispose();
    }

    private static SectionRegistry.Snapshot StartAndTick()
    {
        RynthAiDashboard.Start();
        SectionRegistry.Invalidate();
        RynthAiDashboard.Tick();
        return SectionRegistry.Current;
    }

    [Fact]
    public void TheCoreSectionsHoldOurEnginePanels()
    {
        SectionRegistry.Snapshot snap = StartAndTick();

        Assert.Equal(
            ["char", "map", "skills", "combat", "nav", "inventory", "crafting", "spells", "chat", "friends", "fellowship", "pets"],
            snap.Sections.Select(v => v.Id));
        string[] Panes(string id) => snap.Find(id)!.Panes.Select(p => p.Title).ToArray();
        Assert.Equal(["Dungeon Map"], Panes("map"));
        Assert.Equal(["Skills"], Panes("skills"));
        Assert.Equal(["Damage"], Panes("combat"));
        Assert.Equal(["Nav"], Panes("nav"));
        Assert.Equal(["Item Count", "Items"], Panes("inventory"));
        Assert.Equal(SectionPaneKind.Overview, Assert.Single(snap.Find("char")!.Panes).Kind);
        Assert.All(snap.Sections, v => Assert.Empty(v.Groups));
        Assert.Equal(["ilt", "aelrynth"], snap.Servers.Select(s => s.Id));
        Assert.All(snap.Servers, s => Assert.False(s.Active));
    }
}
```

(Chat appears in "chat" only when RynthChat's `ChatFace` is registered; this test doesn't start chat, so `chat` has no panes. Don't assert it.)

- [ ] **Step 2: Run to verify it fails** — build errors: `RynthCore.Engine.UI.Sections` not found.

- [ ] **Step 3: Add their files** to the csproj, beside the Dungeon Map items:

```xml
    <Compile Include="$(RynthEngine)UI\Sections\SectionCatalog.cs" Link="RynthCore\Sections\SectionCatalog.cs" />
    <Compile Include="$(RynthEngine)UI\Sections\SectionManifest.cs" Link="RynthCore\Sections\SectionManifest.cs" />
    <Compile Include="$(RynthEngine)UI\Sections\SectionRegistry.cs" Link="RynthCore\Sections\SectionRegistry.cs" />
    <Compile Include="$(RynthEngine)UI\Sections\ServerForce.cs" Link="RynthCore\Sections\ServerForce.cs" />
    <Compile Include="$(RynthEngine)UI\Sections\ServerForceStore.cs" Link="RynthCore\Sections\ServerForceStore.cs" />
    <Compile Include="$(RynthEngine)UI\Sections\NotLoadedServers.cs" Link="RynthCore\Sections\NotLoadedServers.cs" />
    <Compile Include="$(RynthEngine)Compatibility\IltDetect.cs" Link="RynthCore\Compatibility\IltDetect.cs" />
```

- [ ] **Step 4: Stand-ins.** Create `SectionsStandIns.cs`:

```csharp
// Stand-ins for what RynthCore's Mini Remote sections (UI/Sections) read about servers:
// OpenAC loads no server plugins and none of ILT's, so no server is ever active here.
using System;

namespace RynthCore.Engine.Compatibility
{
    /// <summary>
    /// RynthCore detects Infinite Leaftide (host, world name, per-character override) for
    /// RynthILT's features. OpenAC loads no ILT plugins, so it is never ILT.
    /// </summary>
    internal static class IltInfo
    {
        public const string ServerId = "ilt";

        private static readonly IltVerdict NotIlt = new() { IsIlt = false, Reason = "OpenAC: no ILT plugins" };

        public static bool IsIlt => false;

        public static IltVerdict Detected => NotIlt;

        public static void Invalidate()
        {
        }
    }

    /// <summary>RynthCore's Aelrynth verdict (their ServerInfo.cs). Immutable.</summary>
    internal sealed class ServerVerdict
    {
        public bool IsAelrynth { get; init; }

        public string Reason { get; init; } = "";
    }

    /// <summary>RynthCore reads the world name the server announced at login; the host sets the reader.</summary>
    internal static class AccountHooks
    {
        internal static Func<string>? WorldName { get; set; }

        public static string? AnnouncedWorldName
        {
            get
            {
                string name = WorldName?.Invoke() ?? string.Empty;
                return name.Length > 0 ? name : null;
            }
        }
    }
}
```

In `DashboardStandIns.cs`, extend `ServerInfo` (keep `IsAelrynth`):

```csharp
        public const string ServerId = "aelrynth";

        private static readonly ServerVerdict NotAelrynth = new() { IsAelrynth = false, Reason = "OpenAC never connects to Aelrynth" };

        public static ServerVerdict Detected => NotAelrynth;

        /// <summary>RynthCore's launch host (from the launcher); OpenAC has none to read.</summary>
        public static string LaunchHost => string.Empty;

        public static void Invalidate()
        {
        }
```

and `PluginManager` (plus a `SkippedServerPlugin` stand-in in the same namespace):

```csharp
        /// <summary>RynthCore reads each plugin's RynthPluginGetSectionsJson; RynthAi exports none, so none.</summary>
        internal static IEnumerable<(string Plugin, string Json)> CollectSectionManifests() => Array.Empty<(string, string)>();

        /// <summary>Server plugins RynthCore didn't load for this host; OpenAC loads none, so none skipped.</summary>
        internal static IReadOnlyList<SkippedServerPlugin> SkippedServerPlugins => Array.Empty<SkippedServerPlugin>();

        internal static void RequestServerPluginLoad(string serverId)
        {
        }
```

```csharp
    /// <summary>RynthCore's skipped server plugin (path + manifest); never made in OpenAC.</summary>
    internal sealed record SkippedServerPlugin(string Path, SkippedServerPlugin.ManifestInfo Manifest)
    {
        internal sealed record ServerInfo(string Id, string Name, IReadOnlyList<string> WorldNames);

        internal sealed record ManifestInfo(string Name, ServerInfo? Server);

        public string ServerId => Manifest.Server!.Id;

        public string ServerName => Manifest.Server!.Name;

        public string PluginName => Manifest.Name;
    }
```

Check `SectionRegistry.PollManifests`' foreach deconstruction against their `PluginManager.CollectSectionManifests` return type (`grep -n "CollectSectionManifests" external/RynthCore/src/RynthCore.Engine/Plugins/*.cs`) and match it exactly. If the build reports other missing members (e.g. `RynthLog.Milestone`, `RynthLog.Compat`), add them to the existing `RynthLog` stand-in in `src/AcDream.Rynth.ScriptWindows/StandIns/EngineStandIns.cs` in the style of its `UI` member.

`RynthAiDashboard.Tick`: before `int ran = UiDataHub.Drain(Log);` add:

```csharp
        // RynthCore's ImGuiController frame: servers, plugin manifests and the Mini Remote's sections.
        RynthCore.Engine.UI.Sections.SectionRegistry.Tick();
```

`RynthChatHookup`: add `private readonly Func<string> _worldName;`, set `_worldName = _serverName;` and `AccountHooks.WorldName = _worldName;` in the constructor; clear it in `Dispose` if still equal.

- [ ] **Step 5: Run to verify** — `dotnet test tests/AcDream.Rynth.Host.Tests --filter RynthMiniRemoteTests` → pass; full suite → no new failures.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.Rynth.EngineUi src/AcDream.Rynth.ScriptWindows src/AcDream.Rynth.Host/RynthChatHookup.cs tests/AcDream.Rynth.Host.Tests/RynthMiniRemoteTests.cs
git commit -m "feat(rynth): RynthCore's Mini Remote sections compiled unchanged, no servers active"
```

---

### Task 4: Mini Remote face and `/rc miniremote`

**Goal:** RynthCore's `MiniRemoteFace` (both partials) compiles unchanged, is registered by the dashboard, draws its rail and slot grid, embeds our panels on the rail's sections, and opens from `/rc miniremote`.

**Files:**
- Modify: `src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj`
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/DashboardStandIns.cs` (remove the `MiniRemoteFace` stand-in)
- Create: `src/AcDream.Rynth.EngineUi/StandIns/RynthCoreChatCommandsStandIn.cs`
- Modify: `src/AcDream.Rynth.EngineUi/RynthAiDashboard.cs` (`Start`)
- Modify: `src/AcDream.Rynth.Host/RynthClientExtension.cs` (register `rc`)
- Test: `tests/AcDream.Rynth.Host.Tests/RynthMiniRemoteTests.cs`

**Acceptance Criteria:**
- [ ] `MiniRemoteFace.cs` and `MiniRemoteFace.Sections.cs` compile unchanged; the `MiniRemoteFace` stand-in is gone; `RynthAiDashboard.Start` registers it ("Mini Remote").
- [ ] Opened and drawn for several frames on Remote with no log lines from "Mini Remote" and ImGui balanced; with RynthAi absent the engine grid draws (status row text reaches the draw list).
- [ ] `MiniRemoteFace.RequestSection("skills")` then drawing creates an embedded Skills copy (Skills `OnShown` runs through `DrawEmbedded`); no failures logged.
- [ ] `RynthCoreChatCommands.TryHandle("miniremote section map", reply)` opens the Mini Remote on Map; `"miniremote section bogus"` replies "Usage: /rc miniremote section <remote|char|map|...>"; `"miniremote toggle"` toggles; `"miniremote tab character"` runs `/ro character` through `ServerMenus.Run`; `"bogus"` replies "/rc bogus isn't in OpenAC (only /rc miniremote).".
- [ ] The host registers chat verb `rc` → `RynthCoreChatCommands.TryHandle(arguments, line => view.WriteChatLine(line, 0))`.

**Verify:** `dotnet test tests/AcDream.Rynth.Host.Tests --filter RynthMiniRemoteTests` → pass; full suite no new failures; `dotnet build -c Release` clean.

**Steps:**

- [ ] **Step 1: Write the failing tests** (append to `RynthMiniRemoteTests`; add `using RynthCore.Engine.ImGuiBackend.Panels; using RynthCore.Engine.UI;` and a `HeadlessImGui` field disposed in `Dispose`)

```csharp
    private readonly HeadlessImGui _imgui = new();

    private void Frames(int n)
    {
        for (int i = 0; i < n; i++)
            _imgui.Frame(ImGuiPanelHost.DrawAll);
    }

    [Fact]
    public void TheDashboardRegistersTheMiniRemoteAndItDrawsTheGrid()
    {
        StartAndTick();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        Assert.True(ImGuiPanelHost.HasFace(MiniRemoteFace.Title));
        MiniRemoteFace.RequestSection(SectionCatalog.RemoteId);
        ImGuiPanelHost.Open(MiniRemoteFace.Title);

        Frames(4);
        void After() { ImGuiNET.ImGui.Begin("after"); ImGuiNET.ImGui.Text("ok"); ImGuiNET.ImGui.End(); }
        Assert.True(_imgui.Frame(After) > 0);
        Assert.DoesNotContain(_log, l => l.Contains("Mini Remote", StringComparison.Ordinal));
    }

    [Fact]
    public void ARailSectionEmbedsItsPanel()
    {
        StartAndTick();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        MiniRemoteFace.RequestSection("skills");
        ImGuiPanelHost.Open(MiniRemoteFace.Title);

        Frames(4);

        Assert.True(MiniRemoteFace.ShowsSection("skills"));
        Assert.DoesNotContain(_log, l => l.Contains("embedded Skills", StringComparison.Ordinal) || l.Contains("embedding Skills", StringComparison.Ordinal));
    }

    [Fact]
    public void RcMiniremoteOpensSectionsAndRejectsBadOnes()
    {
        StartAndTick();
        var replies = new List<string>();
        var ran = new List<string>();
        ServerMenus.Runner = ran.Add;
        try
        {
            Assert.True(RynthCoreChatCommands.TryHandle("miniremote section map", replies.Add));
            _imgui.Frame(ImGuiPanelHost.DrawAll);
            Assert.True(ImGuiPanelHost.IsOpen(MiniRemoteFace.Title));
            LoginLifecycleHooks.HasObservedLoginComplete = true;
            Frames(1);
            Assert.True(MiniRemoteFace.ShowsSection("map"));

            RynthCoreChatCommands.TryHandle("miniremote section bogus", replies.Add);
            Assert.StartsWith("Usage: /rc miniremote section <remote|char|map|", replies[^1]);

            RynthCoreChatCommands.TryHandle("miniremote toggle", replies.Add);
            Assert.False(ImGuiPanelHost.IsOpen(MiniRemoteFace.Title));

            RynthCoreChatCommands.TryHandle("miniremote tab character", replies.Add);
            Assert.Equal(["/ro character"], ran);

            RynthCoreChatCommands.TryHandle("bogus", replies.Add);
            Assert.Equal("/rc bogus isn't in OpenAC (only /rc miniremote).", replies[^1]);
        }
        finally
        {
            ServerMenus.Runner = null;
        }
    }
```

Add `_imgui.Dispose();` to `Dispose` (after `ImGuiPanelHost.Reset()`).

- [ ] **Step 2: Run to verify they fail** — build errors (`MiniRemoteFace.RequestSection`, `RynthCoreChatCommands` missing).

- [ ] **Step 3: Compile their face.** csproj:

```xml
    <Compile Include="$(RynthEngine)ImGui\Panels\MiniRemoteFace.cs" Link="RynthCore\MiniRemoteFace.cs" />
    <Compile Include="$(RynthEngine)ImGui\Panels\MiniRemoteFace.Sections.cs" Link="RynthCore\MiniRemoteFace.Sections.cs" />
```

Delete the `MiniRemoteFace` stand-in class from `DashboardStandIns.cs`. Build `dotnet build src/AcDream.Rynth.EngineUi` and fix each missing member with a stand-in (expected candidates: `InventoryModel.Subscribe/Unsubscribe` exist; `PanelColumnStore` compiled; `ItemCountFace.NameOf`, `HudItemCache`, `ItemHudMath` compiled; `ImGuiPopOuts.InPopOutFrame` exists). Missing members go into the stand-in that owns their type, with a one-line summary.

`RynthAiDashboard.Start`: after the Dungeon Map registration add

```csharp
        if (!ImGuiPanelHost.HasFace(MiniRemoteFace.Title))
            MiniRemoteFace.Register();
```

and add "Mini Remote" to the class summary's list.

- [ ] **Step 4: `/rc` stand-in.** Create `RynthCoreChatCommandsStandIn.cs` — their `/rc miniremote` branch (`RynthCoreChatCommands.cs:353-400`) with their `MovedTabCommand`, `IsVerb`, `TryPanelMode`, `SetPanelOpen` semantics:

```csharp
using System;
using System.Collections.Generic;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;
using RynthCore.Engine.UI.Data;
using RynthCore.Engine.UI.Sections;

namespace RynthCore.Engine.Compatibility
{
    /// <summary>
    /// RynthCore's /rc commands. OpenAC has only their Mini Remote branch
    /// (/rc miniremote|remote [show|hide|toggle|tab &lt;tab&gt;|section &lt;id&gt;]), with their texts;
    /// every other /rc verb says it isn't here.
    /// </summary>
    internal static class RynthCoreChatCommands
    {
        /// <summary>Handles the text after "/rc". Always true (the verb is ours).</summary>
        public static bool TryHandle(string arguments, Action<string> reply)
        {
            string sub = (arguments ?? string.Empty).Trim();
            if (!IsVerb(sub, "miniremote", out string remoteArgs) && !IsVerb(sub, "remote", out remoteArgs))
            {
                string verb = sub.Split(' ', 2)[0];
                reply($"/rc {verb} isn't in OpenAC (only /rc miniremote).");
                return true;
            }

            if (IsVerb(remoteArgs, "tab", out string moved) && MovedTabCommand(moved) is string movedTo)
                UI.ServerMenus.Run(movedTo);
            else if (!ImGuiPanelHost.HasFace(MiniRemoteFace.Title))
                reply("The Mini Remote panel isn't available (the ImGui layer is off?).");
            else if (IsVerb(remoteArgs, "tab", out string tab)
                     && tab.Length > 0 && MiniRemoteFace.TryRequestSurface("section:" + tab.Trim().ToLowerInvariant()))
                ImGuiPanelHost.Open(MiniRemoteFace.Title);
            else if (IsVerb(remoteArgs, "tab", out tab))
            {
                MiniRemoteFace.RequestSection(SectionCatalog.RemoteId);
                ImGuiPanelHost.Open(MiniRemoteFace.Title);
                RynthAiCommands.ApplyRemoteCommand("hub", tab.Length > 0 ? "tab " + tab : "tab");
            }
            else if (IsVerb(remoteArgs, "section", out string section))
            {
                string id = SectionManifestParser.Id(section);
                SectionRegistry.Snapshot snap = SectionRegistry.Current;
                if (id != SectionCatalog.RemoteId && snap.Find(id) == null)
                {
                    var names = new List<string> { SectionCatalog.RemoteId };
                    foreach (SectionView v in snap.Sections) names.Add(v.Id);
                    reply($"Usage: /rc miniremote section <{string.Join("|", names)}>");
                }
                else
                {
                    MiniRemoteFace.RequestSection(id);
                    ImGuiPanelHost.Open(MiniRemoteFace.Title);
                }
            }
            else if (!TryPanelMode(remoteArgs, out bool? show))
                reply("Usage: /rc miniremote [show|hide|toggle|tab <tab>|section <id>]");
            else if (show is null)
                ImGuiPanelHost.Toggle(MiniRemoteFace.Title);
            else if (show.Value)
                ImGuiPanelHost.Open(MiniRemoteFace.Title);
            else
                ImGuiPanelHost.Close(MiniRemoteFace.Title);
            return true;
        }
    }
}
```

Copy `IsVerb`, `TryPanelMode` and `MovedTabCommand` into this class from `RynthCoreChatCommands.cs` (read their bodies with `grep -n "static bool IsVerb\|static bool TryPanelMode\|static string? MovedTabCommand" -A20 external/RynthCore/src/RynthCore.Engine/Compatibility/RynthCoreChatCommands.cs`), keeping their exact rules (empty args = show? — follow theirs). If `RynthAiCommands.ApplyRemoteCommand` isn't reachable from EngineUi, find where `RynthAiFace.ShowMiniRemoteTab` gets it (`grep -rn "class RynthAiCommands" src external/RynthCore/src/RynthCore.Engine/UI`) and use the same.

- [ ] **Step 5: Host route.** In `RynthClientExtension` beside the `rynth` command:

```csharp
        _commands.Add(context.RegisterChatCommand("rc", arguments =>
            Guard("RynthAi", () => RynthCoreChatCommands.TryHandle(arguments, line => view.WriteChatLine(line, 0)))));
```

(Use the existing `Guard(string, Action)` helper; wrap in a lambda returning void if `TryHandle`'s bool return trips the overload.)

- [ ] **Step 6: Run to verify** — `dotnet test tests/AcDream.Rynth.Host.Tests --filter RynthMiniRemoteTests` → pass; full suite → no new failures; `dotnet build AcDream.slnx -c Release` → 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.Rynth.EngineUi src/AcDream.Rynth.Host/RynthClientExtension.cs tests/AcDream.Rynth.Host.Tests/RynthMiniRemoteTests.cs
git commit -m "feat(rynth): RynthCore's Mini Remote compiled unchanged; /rc miniremote"
```

---

### Task 5: Live check (user)

**Goal:** The user confirms in their own client on the local ACE server that the Mini Remote opens, its slots use items, and the rail shows our panels.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:**
- Modify: `docs/superpowers/plans/2026-10-19-rynth-mini-remote.md` (tick results)

**Acceptance Criteria:**
- [ ] Dashboard Char menu → "Mini Remote" opens the window with the rail.
- [ ] Remote: select a Healing Kit, click an empty slot → slot filled; click it → status "Used Healing Kit." and the kit count drops; a wand slot click wields the wand; right-click menu More/Fewer/Bigger/Smaller and Ctrl+wheel resize.
- [ ] Rail: Map shows the Dungeon Map (indoors), Skills, Combat (Damage), Nav, Inventory (Item Count / Items tabs), Chat; empty sections show their notes.
- [ ] Right-click → untick "Show the sections rail" hides it; ticking brings it back.
- [ ] `/rc miniremote section skills` opens on Skills.
- [ ] Relog: slots and box size kept.

**Verify:** user reports each item; no `threw` / `[ScriptWindows]` error lines in `~/Library/Logs/ACELauncher/aethrixtest.log`.

**Steps:**

- [ ] **Step 1:** Ask the user whether they're available for a live check (never launch a client without asking — memory: ask-before-live-client). Build Release first: `dotnet build AcDream.slnx -c Release`.
- [ ] **Step 2:** Walk the checklist above with the user; fix any failure on the branch with a test first.
- [ ] **Step 3:** Record results in this plan and commit.
