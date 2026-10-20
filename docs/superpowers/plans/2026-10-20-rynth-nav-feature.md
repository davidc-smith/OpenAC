# RynthNav Feature Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** RynthNav becomes a usable feature in OpenAC: `/rnav` commands (typed in either chat bar), RynthCore's RynthNav panel and arrow overlay behind a bar button, chat coordinate "Go to / Arrow to" links, and goto / Atlas travel / portals / recalls / Town Network working on the host slots they need.

**Architecture:** RynthCore's `RynthNavFace` and `RynthNavArrowFace` compile unchanged in `AcDream.Rynth.EngineUi`; their data already flows through the compiled `RynthNavData.cs` and `PluginExportBinder`. The UI source pump moves out of `RynthAiDashboard` into a shared `RynthEngineHub` so the panel works without RynthAi. Every typed line reaches the Rynth plugins through the client's existing plugin chat input interceptor, as RynthCore's chat-bar pre-dispatch does. Three empty host slots (`GetLiveObjectIds`, `DoMovement`, `StopMovement`) are filled.

**Tech Stack:** .NET 10, C#, ImGui.NET, xunit.

**Spec:** `docs/superpowers/specs/2026-10-20-rynth-nav-feature-design.md`

## Global Constraints

- Never edit anything under `external/RynthCore` or `external/RynthSuite`; their files are compiled as-is (memory: rynth-no-rewrite).
- Build/test with the SDK in `~/.dotnet`: `export PATH=~/.dotnet:$PATH DOTNET_ROOT=~/.dotnet`.
- Stand-in style: same namespace and member names as RynthCore's type, a short `<summary>` saying what RynthCore does and what OpenAC does instead (see `src/AcDream.Rynth.EngineUi/StandIns/DashboardStandIns.cs`).
- Host slot style: `[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]`, read `_current`, catch everything, `table.Fail(nameof(X), error)`, return RynthCore's "not available" value (see `src/AcDream.Rynth.Host/RynthApiTable.Movement.cs`).
- No contract bump, `ApiVersion` stays 86; nothing in `AcDream.Plugin.Abstractions` changes.
- Don't run `tools/update-package-locks.ps1`.
- Commit message trailer: `Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz`.
- Work on branch `feat/rynth-nav-feature`.

**User decisions (already made):**
- Full scope: `/rnav`, both faces unchanged, bar button, missing slots, coordinate links, live check of every travel kind (2026-10-20: "1").
- Every typed line reaches the plugins, as in RynthCore, via the existing interceptor (2026-10-20: "1").
- One shared engine hub instead of the dashboard-owned pump (2026-10-20: section 2 "Yes").
- Their defaults: portals off until `/rnav portals on`, recalls on, `rynthnav.json`, installed `townnet.json`.
- Live checks happen when the user says so (memory: ask-before-live-client); local ACE only.

---

### Task 1: Host slots `GetLiveObjectIds`, `DoMovement`, `StopMovement`

**Goal:** RynthCore's `GetLiveObjectIdsFn`, `DoMovementFn` and `StopMovementFn` are filled with RynthCore's semantics.

**Files:**
- Modify: `src/AcDream.Rynth.Host/IRynthGameView.cs` (add `LiveObjectIds`)
- Modify: `src/AcDream.Rynth.Host/RuntimeRynthGameView.cs` (implement it)
- Modify: `src/AcDream.Rynth.Host/RynthApiTable.State.cs` (bind + `GetLiveObjectIds`)
- Modify: `src/AcDream.Rynth.Host/RynthApiTable.Movement.cs` (bind + `DoMovement`, `StopMovement`)
- Modify: `tests/AcDream.Rynth.Host.Tests/FakeGameView.cs` (`Live` list)
- Test: `tests/AcDream.Rynth.Host.Tests/RynthApiTableTests.cs`

**Acceptance Criteria:**
- [ ] `host.GetLiveObjectIds()` (RynthCoreHost) returns every id of the view's live objects; with more than 1024 ids it still returns all (their wrapper retries with the total).
- [ ] The slot returns the total even when `capacity` is smaller, fills at most `capacity`, counts with a null buffer, and returns 0 out of world (view answers null) or when the view throws (logged once as `GetLiveObjectIds threw`).
- [ ] `host.DoMovement(0x45000006, 1f, 1)` holds walk-backward (`view.Intents[^1] == new PluginMovementIntent(Backward: true, Run: false)`); calling it again sends no new intent; `host.StopMovement(0x45000006, 1)` releases (`Intents[^1] == null`).
- [ ] `DoMovement` / `StopMovement` with a motion `RynthMotionHolds` does not hold (0x41000012) return false.
- [ ] `HasGetLiveObjectIds`, `HasDoMovement`, `HasStopMovement` are true.

**Verify:** `dotnet test tests/AcDream.Rynth.Host.Tests --filter RynthApiTableTests` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** (append to `RynthApiTableTests`)

```csharp
    [Fact]
    public void LiveObjectIds_AreTheViewsObjectsAndCountPastTheBuffer()
    {
        var view = new FakeGameView { Live = [.. Enumerable.Range(1, 1500).Select(i => 0x80000000u + (uint)i)] };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.HasGetLiveObjectIds);
        Assert.Equal(view.Live, host.GetLiveObjectIds());

        var fn = (delegate* unmanaged[Cdecl]<uint*, int, int>)table.Native->GetLiveObjectIdsFn;
        uint* two = stackalloc uint[2];
        Assert.Equal(1500, fn(two, 2));
        Assert.Equal((0x80000001u, 0x80000002u), (two[0], two[1]));
        Assert.Equal(1500, fn(null, 0));
    }

    [Fact]
    public void LiveObjectIds_AreNoneOutOfWorldOrWhenTheViewThrows()
    {
        var view = new FakeGameView { Live = null };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);
        Assert.Empty(host.GetLiveObjectIds());

        view.Live = [1, 2];
        view.Throw = true;
        Assert.Empty(host.GetLiveObjectIds());
        Assert.Contains(_log, line => line.Contains("GetLiveObjectIds threw", StringComparison.Ordinal));
    }

    [Fact]
    public void DoMovementHoldsAndStopMovementReleasesWalkBackward()
    {
        var view = new FakeGameView();
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.HasDoMovement && host.HasStopMovement);
        Assert.True(host.DoMovement(0x45000006, 1f, 1));
        Assert.Equal(new AcDream.Plugin.Abstractions.PluginMovementIntent(Backward: true, Run: false), view.Intents[^1]);
        int sent = view.Intents.Count;
        Assert.True(host.DoMovement(0x45000006, 1f, 1));   // RynthNav's heartbeat re-send
        Assert.Equal(sent, view.Intents.Count);
        Assert.True(host.StopMovement(0x45000006, 1));
        Assert.Null(view.Intents[^1]);

        Assert.False(host.DoMovement(0x41000012, 1f, 1));
        Assert.False(host.StopMovement(0x41000012, 1));
    }
```

If the heartbeat assertion fails because `RynthMotionHolds.SetMotion` re-sends an intent for an already-held key, check `SetMotion`'s body: it should only call its send path when the held set changes (it returns early for an unheld release today, `ReleasingTheLastKeyClearsAndReleasingAnUnheldKeySendsNothing`). Make the hold side match only if it does not already; otherwise drop that `Assert.Equal(sent, …)` line and note it in the commit.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~LiveObjectIds|FullyQualifiedName~DoMovementHolds"`
Expected: build error, `FakeGameView` has no `Live`.

- [ ] **Step 3: Add the view member**

`IRynthGameView.cs`, after `GetContainerContents`:

```csharp
    /// <summary>
    /// Every object the client knows (RynthCore: acclient's live object table, world
    /// objects and items alike). Null out of world.
    /// </summary>
    IReadOnlyList<uint>? LiveObjectIds { get; }
```

`RuntimeRynthGameView.cs`, after `GetContainerContents`:

```csharp
    public IReadOnlyList<uint>? LiveObjectIds =>
        PlayerId == 0 ? null : [.. _runtime.InventoryOwner.Objects.Objects.Select(static o => o.ObjectId)];
```

`FakeGameView.cs`, beside `Known`:

```csharp
    public List<uint>? Live { get; set; } = [];
    public IReadOnlyList<uint>? LiveObjectIds => Read(Live);
```

- [ ] **Step 4: Add the slots**

`RynthApiTable.State.cs`, in `BindStateSlots` after the `GetLastWeenieErrorFn` line:

```csharp
        Native->GetLiveObjectIdsFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint*, int, int>)&GetLiveObjectIds;
```

and beside `ReadKnownSpells`:

```csharp
    // RynthCore v69: the total live object count; fills up to capacity (a null buffer just counts).
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetLiveObjectIds(uint* buffer, int capacity)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            if (table._view.LiveObjectIds is not { } ids)
                return 0;
            if (buffer is not null && capacity > 0)
            {
                int count = Math.Min(capacity, ids.Count);
                for (int i = 0; i < count; i++)
                    buffer[i] = ids[i];
            }
            return ids.Count;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetLiveObjectIds), error);
            return 0;
        }
    }
```

`RynthApiTable.Movement.cs`, in `BindMovementSlots` after `SetMotionFn`:

```csharp
        Native->DoMovementFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, float, int, int>)&DoMovement;
        Native->StopMovementFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int, int>)&StopMovement;
```

and after `SetMotion`:

```csharp
    // RynthCore's DoMovementCommand: here a held key, as SetMotion holds it. Speed and the
    // hold key are ignored; run or walk follows the held run state.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int DoMovement(uint motion, float speed, int holdKey)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table.Holds.SetMotion(motion, true) ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(DoMovement), error);
            return 0;
        }
    }

    // RynthCore's StopMovementCommand: lets go of the held key.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int StopMovement(uint motion, int holdKey)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table.Holds.SetMotion(motion, false) ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(StopMovement), error);
            return 0;
        }
    }
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test tests/AcDream.Rynth.Host.Tests --filter RynthApiTableTests`
Expected: all pass. Then the whole project: `dotnet test tests/AcDream.Rynth.Host.Tests` → all pass (other fakes of `IRynthGameView`, if any fail to compile, get `public IReadOnlyList<uint>? LiveObjectIds => null;`).

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests
git commit -m "feat(rynth): GetLiveObjectIds, DoMovement and StopMovement host slots"
```

---

### Task 2: One engine hub for the UI sources

**Goal:** The source pump (posted work + polling subscribed, due sources) lives in a shared `RynthEngineHub` that parts join and leave; the RynthAi dashboard becomes one part with unchanged behaviour.

**Files:**
- Create: `src/AcDream.Rynth.EngineUi/RynthEngineHub.cs`
- Modify: `src/AcDream.Rynth.EngineUi/RynthAiDashboard.cs` (Start joins, Tick forwards, Stop leaves, `PollAll` and its loop removed, `Log` forwards)
- Modify: `src/AcDream.Rynth.EngineUi/NavAtlasInstall.cs` (add `EnsureOnce`)
- Test: `tests/AcDream.Rynth.Host.Tests/RynthEngineHubTests.cs` (new), `tests/AcDream.Rynth.Host.Tests/RynthAiDashboardTests.cs` (move the PollAll test)

**Acceptance Criteria:**
- [ ] `RynthEngineHub.Join(part)` is idempotent by `part.Name`; `Tick()` with no parts returns 0 and does nothing.
- [ ] `Tick()` order: `PromoteDeferredRefresh` on every part's sources, every part's `BeforeDrain`, `UiDataHub.Drain(Log)`, every part's `AfterDrain`, then `PollAll` over all sources (a source in two parts polled once).
- [ ] A throwing source logs `"[Rynth] Bad poll: InvalidOperationException: boom"` once and does not stop the others (moved test).
- [ ] `Leave(name)` resets that part's sources; when the last part leaves, the posted queue is cleared and `PluginManager.RaisePluginsUnloaded()` runs. Leaving a name never joined still does that cleanup when nothing is joined.
- [ ] `RynthAiDashboard.Tick()` returns `RynthEngineHub.Tick()` when started, else 0; its Prefetch / SectionRegistry / InventoryModel steps run in the same order as before. All existing `RynthAiDashboardTests`, `RynthDungeonMapTests`, `RynthMiniRemoteTests`, `RynthInventoryPanelTests` pass unchanged.
- [ ] `NavAtlasInstall.EnsureOnce(log)` installs and warms the Atlas the first time only; the dashboard calls it instead of its inline steps.

**Verify:** `dotnet test tests/AcDream.Rynth.Host.Tests` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing hub tests** (`tests/AcDream.Rynth.Host.Tests/RynthEngineHubTests.cs`)

```csharp
using AcDream.Rynth.EngineUi;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthEngineHubTests : IDisposable
{
    private readonly List<string> _log = new();

    public RynthEngineHubTests()
    {
        RynthEngineHub.LeaveAll();
        UiDataHub.Clear();
        RynthEngineHub.Log = _log.Add;
    }

    public void Dispose()
    {
        RynthEngineHub.LeaveAll();
        RynthEngineHub.Log = _ => { };
    }

    private sealed class ProbeSource(string name, bool throws = false) : UiSource(name, 1000)
    {
        public int Polls, Resets;

        protected internal override void Poll()
        {
            Polls++;
            if (throws)
                throw new InvalidOperationException("boom");
        }

        public override void Reset()
        {
            Resets++;
            base.Reset();
        }
    }

    [Fact]
    public void ASourceThatThrowsDoesNotStopTheOthersOrRepeatEveryTick()
    {
        var bad = new ProbeSource("Bad", throws: true);
        var good = new ProbeSource("Good");
        bad.Subscribe();
        good.Subscribe();

        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        RynthEngineHub.PollAll([bad, good], now);
        RynthEngineHub.PollAll([bad, good], now);

        Assert.Equal(1, good.Polls);
        Assert.Equal(1, bad.Polls);
        Assert.Single(_log, line => line == "[Rynth] Bad poll: InvalidOperationException: boom");
    }

    [Fact]
    public void NothingRunsWithNoParts()
    {
        bool ran = false;
        UiDataHub.Post("x", () => ran = true);
        Assert.Equal(0, RynthEngineHub.Tick());
        Assert.False(ran);
    }

    [Fact]
    public void ATickRunsHooksDrainsAndPollsEachSharedSourceOnce()
    {
        var shared = new ProbeSource("Shared");
        var own = new ProbeSource("Own");
        shared.Subscribe();
        own.Subscribe();
        var order = new List<string>();
        RynthEngineHub.Join(new RynthEngineHub.Part("A", [shared], BeforeDrain: () => order.Add("A before"), AfterDrain: () => order.Add("A after")));
        RynthEngineHub.Join(new RynthEngineHub.Part("A", [own]));   // same name: ignored
        RynthEngineHub.Join(new RynthEngineHub.Part("B", [shared, own], BeforeDrain: () => order.Add("B before")));
        UiDataHub.Post("posted", () => order.Add("posted"));

        Assert.Equal(1, RynthEngineHub.Tick());

        Assert.Equal(["A before", "B before", "posted", "A after"], order);
        Assert.Equal(1, shared.Polls);
        Assert.Equal(1, own.Polls);
    }

    [Fact]
    public void LeavingResetsThePartsSourcesAndTheLastLeaveClearsTheQueue()
    {
        var a = new ProbeSource("A1");
        var b = new ProbeSource("B1");
        int unloaded = 0;
        void OnUnloaded() => unloaded++;
        RynthCore.Engine.Plugins.PluginManager.PluginsUnloaded += OnUnloaded;
        try
        {
            RynthEngineHub.Join(new RynthEngineHub.Part("A", [a]));
            RynthEngineHub.Join(new RynthEngineHub.Part("B", [b]));
            bool ran = false;
            UiDataHub.Post("x", () => ran = true);

            RynthEngineHub.Leave("A");
            Assert.Equal((1, 0, 0), (a.Resets, b.Resets, unloaded));

            RynthEngineHub.Leave("B");
            Assert.Equal((1, 1), (b.Resets, unloaded));
            Assert.Equal(0, UiDataHub.Drain(_ => { }));
            Assert.False(ran);
        }
        finally { RynthCore.Engine.Plugins.PluginManager.PluginsUnloaded -= OnUnloaded; }
    }
}
```

Before writing, check `UiSource` in `external/RynthCore/src/RynthCore.Engine/UI/Data/UiSource.cs` (or wherever `class UiSource` lives: `grep -rn "abstract class UiSource" external/RynthCore/src`): use its real `Reset` signature (virtual or not) and the `PluginManager.PluginsUnloaded` event in our stand-in (`grep -rn "PluginsUnloaded" src/AcDream.Rynth.EngineUi`). If `Reset` is not virtual, count resets through a field `Reset` clears (for example `Current` becoming null after a poll that set it) instead of overriding.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/AcDream.Rynth.Host.Tests --filter RynthEngineHubTests`
Expected: build error, `RynthEngineHub` does not exist.

- [ ] **Step 3: Write the hub** (`src/AcDream.Rynth.EngineUi/RynthEngineHub.cs`)

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RynthCore.Engine.Plugins;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.EngineUi;

/// <summary>
/// RynthCore's UiDataHub step, shared by everything that shows their engine panels (the
/// RynthAi dashboard, the RynthNav panel): each tick runs the parts' per-frame steps, the
/// posted work, then polls every subscribed source that is due. Parts join when they start
/// and leave when they stop; the last to leave clears the queue and tells the sources the
/// plugins are gone. Game thread.
/// </summary>
internal static class RynthEngineHub
{
    /// <summary>One panel set's sources and the per-frame steps it runs around the posted work.</summary>
    internal sealed record Part(string Name, IReadOnlyList<UiSource> Sources, Action? BeforeDrain = null, Action? AfterDrain = null);

    private static readonly List<Part> Parts = new();

    /// <summary>Where posted work or a source that throws is logged; the host sets it.</summary>
    internal static Action<string> Log { get; set; } = _ => { };

    internal static bool HasParts => Parts.Count > 0;

    internal static void Join(Part part)
    {
        if (!Parts.Any(p => p.Name == part.Name))
            Parts.Add(part);
    }

    /// <summary>One hub step. Returns how many posted actions ran.</summary>
    internal static int Tick()
    {
        if (Parts.Count == 0)
            return 0;
        UiSource[] sources = AllSources();
        foreach (UiSource source in sources)
            source.PromoteDeferredRefresh();
        foreach (Part part in Parts.ToArray())
            part.BeforeDrain?.Invoke();
        int ran = UiDataHub.Drain(Log);
        foreach (Part part in Parts.ToArray())
            part.AfterDrain?.Invoke();
        PollAll(sources, Stopwatch.GetTimestamp());
        return ran;
    }

    /// <summary>
    /// Polls each subscribed, due source inside its own try/catch (as the hub's
    /// Step does): one that throws is logged and still marked polled, so it
    /// neither stops the others nor is retried every tick.
    /// </summary>
    internal static void PollAll(IReadOnlyList<UiSource> sources, long now)
    {
        foreach (UiSource source in sources)
        {
            if (!source.HasSubscribers || !source.IsDue(now))
                continue;
            long before = Stopwatch.GetTimestamp();
            try { source.Poll(); }
            catch (Exception ex) { Log($"[Rynth] {source.Name} poll: {ex.GetType().Name}: {ex.Message}"); }
            long after = Stopwatch.GetTimestamp();
            source.MarkPolled(after, after - before);
        }
    }

    /// <summary>Resets the part's sources; the last part out clears the queue and unloads.</summary>
    internal static void Leave(string name)
    {
        Part? part = Parts.FirstOrDefault(p => p.Name == name);
        if (part is not null)
        {
            Parts.Remove(part);
            foreach (UiSource source in part.Sources)
            {
                try { source.Reset(); }
                catch (Exception ex) { Log($"[Rynth] {source.Name} reset: {ex.GetType().Name}: {ex.Message}"); }
            }
        }

        if (Parts.Count == 0)
        {
            UiDataHub.Clear();
            PluginManager.RaisePluginsUnloaded();
        }
    }

    /// <summary>Test reset: every part leaves.</summary>
    internal static void LeaveAll()
    {
        foreach (Part part in Parts.ToArray())
            Leave(part.Name);
        Leave(string.Empty);
    }

    private static UiSource[] AllSources() => Parts.SelectMany(p => p.Sources).Distinct().ToArray();
}
```

- [ ] **Step 4: Move the dashboard onto the hub** (`RynthAiDashboard.cs`)

Replace the `Log` property with a forward:

```csharp
    /// <summary>Where posted work that throws is logged; the host sets it (the hub's log).</summary>
    internal static Action<string> Log
    {
        get => RynthEngineHub.Log;
        set => RynthEngineHub.Log = value;
    }
```

In `Start`, replace the body of `if (!_started) { ... }` with:

```csharp
            EntryPoint.InitStartedUtc = DateTime.UtcNow;
            _started = true;
            // RynthCore's EntryPoint loads radar_settings.txt at startup; their Radar panel isn't compiled here.
            RynthCore.Engine.UI.Panels.RadarSettingsStore.Load();
            NavAtlasInstall.EnsureOnce(Log);
```

and at the end of `Start` (after the last `Register`):

```csharp
        RynthEngineHub.Join(HubPart);
```

Add the part beside `Sources`:

```csharp
    // RynthCore's ImGuiController frame and MainThreadSnapshots.Tick, in their order around the drain.
    private static readonly RynthEngineHub.Part HubPart = new("RynthAi", Sources,
        BeforeDrain: () =>
        {
            // MainThreadSnapshots.Tick: reads only while the Skills panel wants it.
            PlayerProgressHooks.Prefetch();
            // Servers, plugin manifests and the Mini Remote's sections.
            RynthCore.Engine.UI.Sections.SectionRegistry.Tick();
        },
        AfterDrain: InventoryModel.MainThreadTick);
```

(`Sources` is declared above `HubPart` so it is initialised first; move the `Sources` field above `HubPart` if it is not.)

Replace `Tick`, delete `PollAll`, and replace `Stop`:

```csharp
    /// <summary>One hub step (the hub runs every part, this one included). Returns how many posted actions ran.</summary>
    internal static int Tick() => _started ? RynthEngineHub.Tick() : 0;

    internal static void Stop()
    {
        try { RynthEngineHub.Leave(HubPart.Name); }
        finally { _started = false; }
    }
```

Update the class `<summary>`: "Tick is UiDataHub's step" → "Tick runs the shared RynthEngineHub (this dashboard is one of its parts)".

- [ ] **Step 5: `NavAtlasInstall.EnsureOnce`** (append to `NavAtlasInstall`)

```csharp
    private static bool _ensured;

    /// <summary>
    /// Once per process: the Atlas file in place, then RynthNavAtlas warmed on a pool thread
    /// (our UiBackgroundWriter runs work inline, and parsing the 1.9 MB Atlas would stall the
    /// first indoor frame; their loader is written for a background thread, and later calls
    /// see the path already requested). Whichever of the dashboard and the RynthNav panel
    /// starts first runs it.
    /// </summary>
    internal static void EnsureOnce(Action<string> log)
    {
        if (_ensured)
            return;
        _ensured = true;
        Ensure(RynthCore.Install.RynthInstallPaths.CoreDir, OperatingSystem.IsWindows(), log);
        System.Threading.Tasks.Task.Run(RynthCore.Engine.UI.Data.RynthNavAtlas.EnsureLoadedForNames);
    }
```

Check the namespaces against the `using`s at the top of `RynthAiDashboard.cs` (`RynthCore.Install` for `RynthInstallPaths`, `RynthCore.Engine.UI.Data` for `RynthNavAtlas`) and remove the now-unused `using System.Threading.Tasks;` / `System.Diagnostics` from the dashboard if the compiler warns.

- [ ] **Step 6: Move the old dashboard PollAll test**

Delete `ASourceThatThrowsDoesNotStopTheOthersOrRepeatEveryTick` and its `ProbeSource` from `RynthAiDashboardTests.cs` (the hub test replaces it).

- [ ] **Step 7: Run the whole test project**

Run: `dotnet test tests/AcDream.Rynth.Host.Tests`
Expected: all pass (count = previous count − 1 + 4).

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.Rynth.EngineUi tests/AcDream.Rynth.Host.Tests
git commit -m "refactor(rynth): one engine hub for the UI sources; the RynthAi dashboard is a part"
```

---

### Task 3: Every typed line reaches the Rynth plugins

**Goal:** Each line submitted through the client's chat router (OpenAC's chat bar, floating chat windows, `InvokeChatParser` / Rynth chat) is offered to each Rynth plugin's `OnChatBarEnter` in load order through the plugin chat input interceptor; the forwarding verb registrations remain only as the fallback when there is no automation surface.

**Files:**
- Modify: `src/AcDream.Rynth.Host/RynthClientExtension.cs` (`OfferChatBarText` returns bool; new `RouteChatToPlugins`; Attach uses it)
- Test: `tests/AcDream.Rynth.Host.Tests/RynthChatRoutingTests.cs` (new)

**Acceptance Criteria:**
- [ ] With an `IPluginChat`, `RouteChatToPlugins` registers exactly one input interceptor and no verbs; the interceptor answers `Suppress` when the offer is eaten and `Pass` otherwise.
- [ ] Without one, it registers the forwarding verbs `ro`, `ginv`, `ra`, `mt`, `ub`, `vt`, `lua`, `rnav`, each offering `"/<verb> " + arguments`.
- [ ] With the real RynthNav + RynthAi plugins loaded: `/rnav where` and `/ra help` are eaten; `/rnavx`, `hello there` and `@lifestone` are not.
- [ ] `/rynth` and `/rc` stay registered verbs in both cases.

**Verify:** `dotnet test tests/AcDream.Rynth.Host.Tests --filter RynthChatRoutingTests` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** (`tests/AcDream.Rynth.Host.Tests/RynthChatRoutingTests.cs`)

```csharp
using AcDream.Plugin.Abstractions;
using AcDream.Rynth.Host;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthChatRoutingTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-route-");
    private readonly FakeGameView _view = new() { PlayerId = 0x50000003u };

    public void Dispose() => _sandbox.Dispose();

    private sealed class Chat : IPluginChat
    {
        public List<Func<string, PluginChatInputDecision>> Interceptors { get; } = new();
        public void PostSystemMessage(string text) { }
        public IDisposable RegisterInputInterceptor(Func<string, PluginChatInputDecision> intercept)
        {
            Interceptors.Add(intercept);
            return NoOpPluginRegistration.Instance;
        }
    }

    private sealed class None : IDisposable { public void Dispose() { } }

    [Fact]
    public void WithAChatSurfaceOneInterceptorDecidesAndNoVerbsAreClaimed()
    {
        var chat = new Chat();
        var verbs = new List<string>();
        var offered = new List<string>();

        IReadOnlyList<IDisposable> handles = RynthClientExtension.RouteChatToPlugins(
            chat, line => { offered.Add(line); return line.StartsWith("/eat", StringComparison.Ordinal); },
            (verb, _) => { verbs.Add(verb); return new None(); });

        Assert.Single(handles);
        Assert.Empty(verbs);
        var decide = Assert.Single(chat.Interceptors);
        Assert.Equal(PluginChatInputAction.Suppress, decide("/eat this").Action);
        Assert.Equal(PluginChatInputAction.Pass, decide("hello").Action);
        Assert.Equal(["/eat this", "hello"], offered);
    }

    [Fact]
    public void WithoutOneTheVerbsForwardAsBefore()
    {
        var verbs = new Dictionary<string, Action<string>>();
        var offered = new List<string>();

        RynthClientExtension.RouteChatToPlugins(
            null, line => { offered.Add(line); return true; },
            (verb, handler) => { verbs[verb] = handler; return new None(); });

        Assert.Equal(["ro", "ginv", "ra", "mt", "ub", "vt", "lua", "rnav"], verbs.Keys);
        verbs["rnav"]("where");
        Assert.Equal(["/rnav where"], offered);
    }

    [Fact]
    public void TheRealPluginsEatOnlyTheirOwnCommands()
    {
        using var host = new RynthHost(_view, _log.Add,
            [typeof(RynthCore.Plugin.RynthNav.PluginExports), typeof(RynthCore.Plugin.RynthAi.PluginExports)]);
        bool Offer(string line) => host.Plugins.Any(p => p.OnChatBarEnter(line));

        Assert.True(Offer("/rnav where"));
        Assert.True(Offer("/ra help"));
        Assert.False(Offer("/rnavx"));
        Assert.False(Offer("hello there"));
        Assert.False(Offer("@lifestone"));
    }
}
```

`TheRealPluginsEatOnlyTheirOwnCommands` offers to every plugin, unlike `OfferChatBarText` which stops at the first eater; that is fine for the assertions. If `/ra help` is not eaten by RynthAi's `OnChatBarEnter` before its Init has run, add `host.Tick();` before the asserts.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/AcDream.Rynth.Host.Tests --filter RynthChatRoutingTests`
Expected: build error, `RouteChatToPlugins` does not exist.

- [ ] **Step 3: Implement** (`RynthClientExtension.cs`)

Add `using AcDream.Plugin.Abstractions;` at the top. Add beside `OfferChatBarText`:

```csharp
    /// <summary>The plugin commands forwarded as verbs when the client has no chat interceptor.</summary>
    internal static readonly string[] ForwardedVerbs = ["ro", "ginv", "ra", "mt", "ub", "vt", "lua", "rnav"];

    /// <summary>
    /// RynthCore's chat-bar pre-dispatch (ChatCommandDispatcher.Dispatch): every line typed
    /// or sent through InvokeChatParser goes to each plugin's OnChatBarEnter first, and one
    /// that eats it stops it. Here that is the client's chat input interceptor; a client
    /// without one (tests, headless) still reaches the plugins' own commands through verbs.
    /// </summary>
    internal static IReadOnlyList<IDisposable> RouteChatToPlugins(
        IPluginChat? chat, Func<string, bool> offer, Func<string, Action<string>, IDisposable> register)
    {
        if (chat is not null)
            return [chat.RegisterInputInterceptor(line => offer(line) ? PluginChatInputDecision.Suppress : PluginChatInputDecision.Pass)];
        var handles = new List<IDisposable>();
        foreach (string verb in ForwardedVerbs)
            handles.Add(register(verb, arguments => offer($"/{verb} " + arguments)));
        return handles;
    }
```

Change `OfferChatBarText` to return whether a plugin ate the text:

```csharp
    /// <summary>Offers chat-bar text to each plugin until one eats it. True when one did.</summary>
    private bool OfferChatBarText(string text)
    {
        if (_host is null)
            return false;
        foreach (RynthPlugin plugin in _host.Plugins)
        {
            if (plugin.OnChatBarEnter(text.TrimEnd()))
                return true;
        }

        return false;
    }
```

In `Attach`, replace the three registrations for `ro`, `ginv` and the `foreach (string verb in new[] { "ra", "mt", "ub", "vt", "lua" })` loop with:

```csharp
        _commands.AddRange(RouteChatToPlugins(context.Automation?.Chat, OfferChatBarText, context.RegisterChatCommand));
```

Keep the `rynth` and `rc` registrations. Update the class `<summary>` sentence about commands: "every line typed in either chat bar is offered to the plugins first, as in RynthCore, so `/rnav`, `/ro`, `/ginv`, `/ra`, `/mt`, `/ub`, `/vt`, `/lua` and script commands reach them; `/rynth tracker` and `/rc` are the host's".

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/AcDream.Rynth.Host.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests
git commit -m "feat(rynth): every typed line reaches the Rynth plugins first, as RynthCore's pre-dispatch"
```

---

### Task 4: RynthNav panel, arrow and bar button

**Goal:** RynthCore's `RynthNavFace` and `RynthNavArrowFace` compile unchanged, register through a new `RynthNavPanel` part of the hub when the RynthNav plugin is loaded, get a bar button, and turn on ChatFace's coordinate links.

**Files:**
- Modify: `src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj` (compile the two faces)
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/ChatEngineStandIns.cs` (delete `RynthNavFace` stand-in; `UiSources.RynthNavAtlas`, `UiSources.RynthNavArrowWatch`; `PanelFace` enum; `PanelRouter.ResolveDockedFace`)
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/DashboardStandIns.cs` (`MiniMapFace` beside `WorldMapFace`)
- Modify: `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiStandIns.cs` (`PanelSpec.NoResize`)
- Modify: `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiPanelHost.cs` (honour `NoResize`)
- Create: `src/AcDream.Rynth.EngineUi/RynthNavPanel.cs`
- Modify: `src/AcDream.Rynth.Host/RynthClientExtension.cs` (start, bar button, hub tick, stop)
- Test: `tests/AcDream.Rynth.Host.Tests/RynthNavPanelTests.cs` (new), `tests/AcDream.Rynth.Host.Tests/RynthAiDashboardTests.cs` (`TheirFiles`)

**Acceptance Criteria:**
- [ ] The EngineUi project compiles `ImGui\Panels\RynthNavFace.cs` and `ImGui\Panels\RynthNavArrowFace.cs` from `$(RynthEngine)`; no copies under `src` (`TheProjectCompilesTheirFilesAndHasNoCopies` covers both).
- [ ] `RynthNavPanel.Start()` registers `"RynthNav"` and `"RynthNav Arrow"`, joins the hub with `UiSources.RynthNav`, `UiSources.RynthNavAtlas`, `UiSources.RynthNavArrowWatch`, and runs `NavAtlasInstall.EnsureOnce`; a second `Start` after `ImGuiPanelHost.Reset()` registers again.
- [ ] With the real RynthNav plugin, a pose in the fake view and the chat hookup's export resolver, a hub tick with `UiSources.RynthNav` subscribed yields `UiSources.RynthNav.Current?.Value?.HasPose == true` with RynthAi never started.
- [ ] The panel draws headless (more than 100 vertices) with no host log lines; `RynthNavArrowFace.Show()` opens `"RynthNav Arrow"` and it draws headless.
- [ ] The extension starts `RynthNavPanel` only when `RynthPluginLookup.Candidates(host.Plugins, "RynthNav")` is non-empty and an overlay exists; the bar's panel buttons are `[Chat, RynthAi, RynthNav]`; the extension ticks `RynthEngineHub.Tick()` once per frame instead of `RynthAiDashboard.Tick()`; Dispose stops the panel before the dashboard.
- [ ] `PanelSpec` has `bool NoResize = false` as its last parameter and the host adds `ImGuiWindowFlags.NoResize` for it.

**Verify:** `dotnet test tests/AcDream.Rynth.Host.Tests` → all pass; `dotnet build AcDream.slnx -c Release` → 0 errors.

**Steps:**

- [ ] **Step 1: Write the failing tests** (`tests/AcDream.Rynth.Host.Tests/RynthNavPanelTests.cs`)

```csharp
using AcDream.Rynth.EngineUi;
using AcDream.Rynth.Host;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthNavPanelTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-navpanel-");
    private readonly FakeGameView _view = new() { PlayerId = 0x50000004u };

    public RynthNavPanelTests()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = _log.Add;
        UiDataHub.Clear();
    }

    public void Dispose()
    {
        RynthNavPanel.Stop();
        RynthAiDashboard.Stop();
        PluginExportBinder.Resolver = null;
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = null;
        LoginLifecycleHooks.HasObservedLoginComplete = false;
        _sandbox.Dispose();
    }

    [Fact]
    public void StartRegistersBothFacesAndRegistersAgainAfterAReset()
    {
        RynthNavPanel.Start();
        Assert.True(ImGuiPanelHost.HasFace(RynthNavFace.Title));
        Assert.True(ImGuiPanelHost.HasFace(RynthNavArrowFace.Title));
        Assert.True(RynthEngineHub.HasParts);

        ImGuiPanelHost.Reset();
        RynthNavPanel.Start();
        Assert.True(ImGuiPanelHost.HasFace(RynthNavFace.Title));
    }

    [Fact]
    public void TheStatusReachesTheSourceWithoutRynthAi()
    {
        _view.Pose = (0xA9B40021u, new System.Numerics.Vector3(100, 100, 50), System.Numerics.Quaternion.Identity);
        using var host = new RynthHost(_view, _log.Add, [typeof(RynthCore.Plugin.RynthNav.PluginExports)]);
        using var exports = new RynthChatHookup(host, _view, _log.Add);
        RynthNavPanel.Start();
        UiSources.RynthNav.Subscribe();
        try
        {
            for (int i = 0; i < 3; i++)
            {
                host.Tick();
                UiSources.RynthNav.RequestRefresh();
                RynthEngineHub.Tick();
            }

            Assert.True(UiSources.RynthNav.Current?.Value?.HasPose);
        }
        finally { UiSources.RynthNav.Unsubscribe(); }
    }

    [Fact]
    public void ThePanelAndTheArrowDrawHeadless()
    {
        using var imgui = new HeadlessImGui();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        RynthNavPanel.Start();

        RynthCore.Engine.UI.PanelRouter.Toggle(RynthNavFace.Title);
        RynthNavArrowFace.Show();
        try
        {
            Assert.True(ImGuiPanelHost.IsOpen(RynthNavArrowFace.Title));
            for (int i = 0; i < 3; i++)
            {
                RynthEngineHub.Tick();
                int vertices = imgui.Frame(ImGuiPanelHost.DrawAll);
                Assert.True(vertices > 100, $"frame {i}: only {vertices} vertices");
            }

            Assert.Empty(_log);
        }
        finally
        {
            RynthCore.Engine.UI.PanelRouter.Toggle(RynthNavFace.Title);
            ImGuiPanelHost.Close(RynthNavArrowFace.Title);
        }
    }
}
```

Add `"ImGui\\Panels\\RynthNavFace.cs"` and `"ImGui\\Panels\\RynthNavArrowFace.cs"` to the `TheirFiles` list in `RynthAiDashboardTests.cs` (same form as its other entries).

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/AcDream.Rynth.Host.Tests --filter RynthNavPanelTests`
Expected: build error, `RynthNavPanel` / `RynthNavArrowFace` do not exist.

- [ ] **Step 3: Compile their faces** (`AcDream.Rynth.EngineUi.csproj`, after the `DungeonMapFace.cs` line)

```xml
    <Compile Include="$(RynthEngine)ImGui\Panels\RynthNavFace.cs" Link="RynthCore\RynthNavFace.cs" />
    <Compile Include="$(RynthEngine)ImGui\Panels\RynthNavArrowFace.cs" Link="RynthCore\RynthNavArrowFace.cs" />
```

- [ ] **Step 4: Stand-ins**

`ChatEngineStandIns.cs`: delete the `RynthNavFace` stand-in class and its `<summary>` ("RynthNav's panel title; never registered here..."). In `UiSources`, after `RynthNav`:

```csharp
        public static readonly RynthNavAtlasSource RynthNavAtlas = new();
        public static readonly RynthNavArrowWatch RynthNavArrowWatch = new();
```

In the `RynthCore.Engine.UI` namespace block, beside `PanelRouter`:

```csharp
    /// <summary>Which face a panel shows (RynthCore: Avalonia, ImGui or both, per player choice). OpenAC has only ImGui.</summary>
    internal enum PanelFace
    {
        Avalonia,
        ImGui,
        Both,
    }
```

and inside `PanelRouter`:

```csharp
        /// <summary>The face a docked panel uses: always ImGui here.</summary>
        public static PanelFace ResolveDockedFace(string title) => PanelFace.ImGui;
```

`DashboardStandIns.cs`, after `WorldMapFace`:

```csharp
    /// <summary>RynthCore's minimap (RynthNav maps); never registered in OpenAC, so the RynthNav panel hides its map buttons.</summary>
    internal static class MiniMapFace
    {
        public const string Title = "Mini Map";
    }
```

(Check the real title: `grep -n "const string Title" external/RynthCore/src/RynthCore.Engine/ImGui/Panels/MiniMapFace.cs`, and use that string.)

`ImGuiStandIns.cs`: append `bool NoResize = false` as the last `PanelSpec` parameter (after `OpenCentered`) and extend its `<summary>`: "NoResize drops the resize grip (the RynthNav arrow sizes itself)". `ImGuiPanelHost.cs`, after `if (entry.Spec.Chrome == PanelChrome.None) flags |= ...;`:

```csharp
            if (entry.Spec.NoResize)
                flags |= ImGuiWindowFlags.NoResize;
```

- [ ] **Step 5: `RynthNavPanel`** (`src/AcDream.Rynth.EngineUi/RynthNavPanel.cs`)

```csharp
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.EngineUi;

/// <summary>
/// Runs RynthCore's RynthNav panel: Start does what RynthCore's EntryPoint does when
/// RynthNav is installed (register the panel, which registers the arrow overlay and
/// subscribes the arrow watch), joins the engine hub with the panel's sources, and makes
/// sure the Atlas is installed. Stop leaves the hub. Game thread.
/// </summary>
internal static class RynthNavPanel
{
    /// <summary>Their panel's title (the bar button and the layout key).</summary>
    internal const string Title = RynthNavFace.Title;

    private static readonly RynthEngineHub.Part HubPart = new("RynthNav",
        [UiSources.RynthNav, UiSources.RynthNavAtlas, UiSources.RynthNavArrowWatch]);

    internal static void Start()
    {
        NavAtlasInstall.EnsureOnce(RynthEngineHub.Log);
        // The panel host can be reset (tests, an extension restart): register again then.
        if (!ImGuiPanelHost.HasFace(RynthNavFace.Title))
            RynthNavFace.Register();
        RynthEngineHub.Join(HubPart);
    }

    internal static void Stop() => RynthEngineHub.Leave(HubPart.Name);
}
```

- [ ] **Step 6: Wire the extension** (`RynthClientExtension.cs`)

Bar buttons in `Attach`:

```csharp
            _bar = new RynthBar(_layout, context.Log) { PanelButtons = () => [RynthChatWindow.Title, RynthAiDashboard.Title, RynthNavPanel.Title] };
```

After `if (context.Overlay is not null) Guard("RynthAi", RynthAiDashboard.Start);`:

```csharp
        // RynthCore registers the RynthNav panel only with RynthNav installed; here, only when it loaded.
        if (context.Overlay is not null && RynthPluginLookup.Candidates(_host.Plugins, "RynthNav").Any())
            Guard("RynthNav", RynthNavPanel.Start);
```

In `Tick`, replace `Guard("RynthAi", () => RynthAiDashboard.Tick());` with:

```csharp
        Guard("Rynth", () => RynthEngineHub.Tick());
```

In `Dispose`, inside the `try` before `Guard("RynthAi", RynthAiDashboard.Stop);`:

```csharp
            Guard("RynthNav", RynthNavPanel.Stop);
```

Update the class `<summary>` to mention "RynthCore's RynthNav panel and arrow with a RynthNav button".

- [ ] **Step 7: Run to verify they pass**

Run: `dotnet test tests/AcDream.Rynth.Host.Tests`
Expected: all pass. If `ThePanelAndTheArrowDrawHeadless` logs a missing member at draw time, add the stand-in member the log names (same rules as Step 4) rather than changing their file.

Run: `dotnet build AcDream.slnx -c Release`
Expected: 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.Rynth.EngineUi src/AcDream.Rynth.ScriptWindows src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests
git commit -m "feat(rynth): RynthCore's RynthNav panel and arrow, a bar button and chat coordinate links"
```

---

### Task 5: Live check (user)

**Goal:** The user confirms on the local ACE server (Holtburg test hub) that RynthNav works as a feature in the client.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:**
- Modify: `docs/superpowers/plans/2026-10-20-rynth-nav-feature.md` (tick results in this task)

**Acceptance Criteria:**
- [ ] Bar button opens the panel; Go / Atlas / Arrow tabs draw; drag + relog keep the layout.
- [ ] `/rnav help` and `/rnav where` answer in chat, typed in OpenAC's chat bar and in Rynth chat.
- [ ] Walking goto by coordinates and by an Atlas name arrives; `/rnav stop` (or the panel's stop) halts it.
- [ ] `/rnav arrow Holtburg` opens the arrow with the panel shut; it points the right way while turning.
- [ ] Rynth chat coordinate link: right-click → Go to / Arrow to work.
- [ ] `/rnav portals on`, then a route through a Holtburg hub portal takes the portal.
- [ ] A recall leg (lifestone recall) casts, wielding a caster if needed; a typed `@lifestone` is learned (RynthNav's recall list shows it).
- [ ] A Town Network route through the hub completes.
- [ ] A RynthLua script slash command reaches RynthLua.
- [ ] Optional, same session: sub-project A's checks (outdoor snag detour, unroutable goal, RynthLua bridge).

**Verify:** user's report per item; client log `~/Library/Logs/ACELauncher/aethrixtest.log` (or the dev client log) has no `[Rynth]` threw / poll lines.

**Steps:**

- [ ] **Step 1:** Merge the branch into local main only when the user wants the check run from main; otherwise build the branch: `dotnet build AcDream.slnx -c Release`.
- [ ] **Step 2:** Ask the user when they are available (memory: ask-before-live-client). Local ACE 10.10.20.20 only, never Dreamweave.
- [ ] **Step 3:** Walk the checklist with the user; fix failures on the branch (one commit each, tests first where the failure is testable).
- [ ] **Step 4:** Record results here and in memory (`rynth-status.md`).
