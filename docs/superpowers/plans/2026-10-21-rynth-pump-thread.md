# Rynth Plugin Pump Thread Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run every Rynth plugin's tick on its own pump thread (RynthCore's 16 ms pump), with reads served from a per-frame snapshot and actions queued to the frame thread, so a slow plugin tick (RynthNav's 500 ms `FindPath`) no longer freezes the game.

**Architecture:** The frame thread drains a bounded action queue and, when the pump is idle, publishes an immutable `RynthWorldSnapshot` built from the live `RuntimeRynthGameView`. A `ThreadRoutedGameView` answers from the live view on the frame thread and from `SnapshotRynthGameView` on any other thread, so the ~115 table slots and the hook delegates are unchanged. `RynthPump` runs events, plugin ticks, Nav3D and the panel-data hub on its own thread.

**Tech Stack:** C# / .NET 10, xUnit. Projects: `src/AcDream.Rynth.Host`, `src/AcDream.Rynth.EngineUi`, `src/AcDream.ClientExtensions`, `src/AcDream.App`, `src/AcDream.Runtime`. Tests: `tests/AcDream.Rynth.Host.Tests` (fakes), `tests/AcDream.App.Tests/Extensions` (real `GameRuntime` via `GameRuntimeTestFactory.Create()`).

**Spec:** `docs/superpowers/specs/2026-10-21-rynth-pump-thread-design.md`

## Global Constraints

- **Their code, unchanged**: nothing under `external/` is edited. Only our host, our stand-ins (`src/AcDream.Rynth.EngineUi/StandIns`), our client extension API and our runtime change.
- **No plugin contract change**: `ApiVersion` stays 86; no `RynthCoreApiNative` slot added or removed.
- Pump cadence: a pass at most every **16 ms** (RynthCore `NormalPluginPumpInterval`).
- Action queue: capacity **256**, stop/cancel reserve **32**; queued action returns **true**; starts refused while portalling or dead; full ring drops and counts, drop count logged at most once per **10 s**.
- Quiesce timeout **5 s**; **50** failing passes in a row stop the pump with a chat notice.
- Kill switch: env `ACDREAM_RYNTH_PUMP=0` or session-config `"rynthPump": false` → today's inline ticking; routed view always live.
- Routing rule: frame thread → live view; any other thread → snapshot view; frame thread not latched (pump off, or tests) → live view.
- Commit messages end with `Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W`.
- Build/test with `~/.dotnet/dotnet`. Mac baseline has known pre-existing failures elsewhere (70-failure Mac baseline, see memory); Rynth.Host.Tests must be fully green.

**User decisions (already made):**
- "All Rynth plugins (Recommended)" — one pump for every plugin, as RynthCore.
- "B: snapshots + queue" — RynthCore's model, not the world gate, not pacing only.
- "yes - go all the way to implementation plan" — spec approved as written.

---

## File map

| File | Role |
|---|---|
| `src/AcDream.Rynth.Host/RynthFrameThread.cs` (new) | Latches the frame thread; `IsCurrent`; `Latched`. |
| `src/AcDream.Rynth.Host/RynthActionQueue.cs` (new) | Bounded multi-producer action ring with RynthCore admission; `Drain` on the frame thread. |
| `src/AcDream.ClientExtensions/ClientScreenProjection.cs` (new) | Immutable captured camera projection with a pure `TryProject`. |
| `src/AcDream.ClientExtensions/IClientScreen.cs` | + `TryCaptureProjection` (default: false). |
| `src/AcDream.App/Extensions/ClientScreen.cs` | Implements capture; `TryProject` delegates to the captured projection. |
| `src/AcDream.Runtime/Gameplay/RuntimeCharacterState.cs` | + public `CharacterRevision`, `SpellbookRevision`. |
| `src/AcDream.Rynth.Host/RynthWorldSnapshot.cs` (new) | Immutable snapshot types. |
| `src/AcDream.Rynth.Host/RynthSnapshotBuilder.cs` (new) | Builds snapshots from the live view + runtime, sharing unchanged parts. |
| `src/AcDream.Rynth.Host/RynthObjectMirror.cs` (new) | Object dirty tracking + per-object deep copies. |
| `src/AcDream.Rynth.Host/SnapshotRynthGameView.cs` (new) | `IRynthGameView` over a snapshot + the action queue. |
| `src/AcDream.Rynth.Host/ThreadRoutedGameView.cs` (new) | Forwards to live or snapshot view by thread. |
| `src/AcDream.Rynth.Host/RynthPump.cs` (new) | Pump thread, scheduler seam, frame tick, quiesce, failure stop. |
| `src/AcDream.Rynth.Host/RynthHost.cs` | Tick split into `PumpPass()` / frame work; holds sync moves to frame. |
| `src/AcDream.Rynth.Host/RynthClientExtension.cs` | Wiring: routed view, pump, kill switch, lifecycle quiesce. |
| `src/AcDream.Rynth.Host/RynthEventBridge.cs`, `RynthApiTable*.cs`, `RynthVendorTrades.cs`, `RynthSalvagePanel.cs`, `RynthCombatEvents.cs` | Thread safety of state both threads touch. |
| `src/AcDream.Rynth.EngineUi/RynthEngineHub.cs` | Split into pump part and frame part. |
| `src/AcDream.Rynth.EngineUi/StandIns/SkillsStandIns.cs`, `StandIns/DashboardStandIns.cs` | `MainThreadGuard` / `AcMainThreadQueue` stand-ins follow the frame thread. |

---

### Task 1: Frame thread latch and action queue

**Goal:** `RynthFrameThread` and `RynthActionQueue` exist with RynthCore's admission rules, fully unit-tested.

**Files:**
- Create: `src/AcDream.Rynth.Host/RynthFrameThread.cs`
- Create: `src/AcDream.Rynth.Host/RynthActionQueue.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthActionQueueTests.cs`

**Acceptance Criteria:**
- [ ] `RynthFrameThread.IsCurrent` is false before `Latch()`, true on the latching thread after it, false on another thread; `Reset()` clears it (tests only).
- [ ] `TryEnqueue` returns true and `Drain` runs entries in FIFO order on the draining thread.
- [ ] Starts are refused while the admission gate says portalling or dead; stops are admitted.
- [ ] Starts are refused once 224 entries are queued (capacity 256 minus reserve 32); stops fill up to 256; beyond 256 everything is dropped and `Dropped` counts it.
- [ ] A throwing or false-returning entry is logged (at most once per 10 s per kind) and does not stop the drain.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthActionQueueTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/AcDream.Rynth.Host.Tests/RynthActionQueueTests.cs
using AcDream.Rynth.Host;

namespace AcDream.Rynth.Host.Tests;

[Collection("Rynth")]
public sealed class RynthActionQueueTests : IDisposable
{
    private readonly List<string> _log = [];
    private bool _blocked;
    private long _now;

    public void Dispose() => RynthFrameThread.Reset();

    private RynthActionQueue NewQueue() => new(_log.Add, () => _blocked, () => _now);

    [Fact]
    public void TheFrameThreadIsTheOneThatLatched()
    {
        Assert.False(RynthFrameThread.IsCurrent);
        RynthFrameThread.Latch();
        Assert.True(RynthFrameThread.IsCurrent);
        bool other = true;
        var t = new Thread(() => other = RynthFrameThread.IsCurrent);
        t.Start();
        t.Join();
        Assert.False(other);
    }

    [Fact]
    public void EntriesRunInOrderWhenDrained()
    {
        var queue = NewQueue();
        var ran = new List<int>();
        Assert.True(queue.TryEnqueue(RynthActionKind.Start, "a", () => { ran.Add(1); return true; }));
        Assert.True(queue.TryEnqueue(RynthActionKind.Stop, "b", () => { ran.Add(2); return true; }));
        Assert.Empty(ran);
        Assert.Equal(2, queue.Drain());
        Assert.Equal([1, 2], ran);
        Assert.Equal(0, queue.Drain());
    }

    [Fact]
    public void StartsAreRefusedWhilePortallingOrDeadButStopsAreTaken()
    {
        var queue = NewQueue();
        _blocked = true;
        Assert.False(queue.TryEnqueue(RynthActionKind.Start, "use", () => true));
        Assert.True(queue.TryEnqueue(RynthActionKind.Stop, "stop", () => true));
        Assert.Equal(1, queue.Drain());
    }

    [Fact]
    public void TheReserveKeepsRoomForStops()
    {
        var queue = NewQueue();
        for (int i = 0; i < RynthActionQueue.Capacity - RynthActionQueue.StopReserve; i++)
            Assert.True(queue.TryEnqueue(RynthActionKind.Start, "s", () => true));
        Assert.False(queue.TryEnqueue(RynthActionKind.Start, "s", () => true));
        for (int i = 0; i < RynthActionQueue.StopReserve; i++)
            Assert.True(queue.TryEnqueue(RynthActionKind.Stop, "x", () => true));
        Assert.False(queue.TryEnqueue(RynthActionKind.Stop, "x", () => true));
        Assert.Equal(2, queue.Dropped);
    }

    [Fact]
    public void AFailingEntryIsLoggedOnceAndTheDrainGoesOn()
    {
        var queue = NewQueue();
        bool after = false;
        queue.TryEnqueue(RynthActionKind.Start, "use", () => throw new InvalidOperationException("boom"));
        queue.TryEnqueue(RynthActionKind.Start, "use", () => false);
        queue.TryEnqueue(RynthActionKind.Start, "select", () => after = true);
        queue.Drain();
        Assert.True(after);
        Assert.Single(_log, l => l.Contains("use", StringComparison.Ordinal));
        _now += 10_000;
        queue.TryEnqueue(RynthActionKind.Start, "use", () => false);
        queue.Drain();
        Assert.Equal(2, _log.Count(l => l.Contains("use", StringComparison.Ordinal)));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthActionQueueTests"`
Expected: build FAIL — `RynthFrameThread`, `RynthActionQueue`, `RynthActionKind` not defined.

- [ ] **Step 3: Implement**

```csharp
// src/AcDream.Rynth.Host/RynthFrameThread.cs
namespace AcDream.Rynth.Host;

/// <summary>
/// The client's frame thread, latched when the Rynth pump starts. Off it, the routed view
/// answers from the snapshot and queues actions; before a latch everything is live.
/// </summary>
public static class RynthFrameThread
{
    private static int _id;

    public static bool Latched => Volatile.Read(ref _id) != 0;

    public static bool IsCurrent => Volatile.Read(ref _id) == Environment.CurrentManagedThreadId;

    public static void Latch() => Volatile.Write(ref _id, Environment.CurrentManagedThreadId);

    /// <summary>Back to "not latched" (pump stopped, tests).</summary>
    public static void Reset() => Volatile.Write(ref _id, 0);
}
```

```csharp
// src/AcDream.Rynth.Host/RynthActionQueue.cs
namespace AcDream.Rynth.Host;

/// <summary>Start: may be refused. Stop: stops, cancels, releases; may use the reserve.</summary>
public enum RynthActionKind { Start, Stop }

/// <summary>
/// Actions from off the frame thread, run on it in order (RynthCore's AcMainThreadQueue):
/// a bounded ring, multi-producer under a short lock, drained by the frame thread. Starts are
/// refused while <c>blocked</c> (portalling or dead) and leave a reserve for stops; a full ring
/// drops and counts, never runs off-thread.
/// </summary>
public sealed class RynthActionQueue(Action<string> log, Func<bool> blocked, Func<long>? clock = null)
{
    public const int Capacity = 256;
    public const int StopReserve = 32;
    private const long LogEveryMs = 10_000;

    private readonly object _gate = new();
    private readonly Queue<(string Label, Func<bool> Work)> _entries = new();
    private readonly Func<long> _clock = clock ?? (() => Environment.TickCount64);
    private readonly Dictionary<string, long> _lastLogged = new();
    private long _droppedLoggedAt = long.MinValue;
    private int _dropped;

    public int Dropped => Volatile.Read(ref _dropped);

    public bool TryEnqueue(RynthActionKind kind, string label, Func<bool> work)
    {
        if (kind == RynthActionKind.Start && blocked())
            return false;
        lock (_gate)
        {
            int limit = kind == RynthActionKind.Stop ? Capacity : Capacity - StopReserve;
            if (_entries.Count >= limit)
            {
                _dropped++;
                long now = _clock();
                if (now - _droppedLoggedAt >= LogEveryMs)
                {
                    _droppedLoggedAt = now;
                    log($"action queue full: '{label}' dropped ({_dropped} dropped so far)");
                }
                return false;
            }
            _entries.Enqueue((label, work));
            return true;
        }
    }

    /// <summary>Frame thread: runs what is queued now, in order. Returns how many ran.</summary>
    public int Drain()
    {
        (string Label, Func<bool> Work)[] batch;
        lock (_gate)
        {
            if (_entries.Count == 0)
                return 0;
            batch = [.. _entries];
            _entries.Clear();
        }
        foreach ((string label, Func<bool> work) in batch)
        {
            string? failure;
            try { failure = work() ? null : "refused"; }
            catch (Exception error) { failure = $"{error.GetType().Name}: {error.Message}"; }
            if (failure is not null)
                LogFailure(label, failure);
        }
        return batch.Length;
    }

    /// <summary>Drops everything queued (logout, dispose).</summary>
    public void Clear()
    {
        lock (_gate)
            _entries.Clear();
    }

    private void LogFailure(string label, string failure)
    {
        long now = _clock();
        if (_lastLogged.TryGetValue(label, out long at) && now - at < LogEveryMs)
            return;
        _lastLogged[label] = now;
        log($"queued action '{label}' failed: {failure}");
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthActionQueueTests"`
Expected: PASS (5 tests). Note `TheReserveKeepsRoomForStops` expects `Dropped == 2` (one refused start counts as a drop because the ring was at its start limit, one refused stop at 256).

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Rynth.Host/RynthFrameThread.cs src/AcDream.Rynth.Host/RynthActionQueue.cs tests/AcDream.Rynth.Host.Tests/RynthActionQueueTests.cs
git commit -m "feat(rynth): frame-thread latch and RynthCore-style action queue

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 2: Screen projection capture and runtime revisions

**Goal:** A captured, thread-safe camera projection and public character/spellbook revision counters, the two pieces of client API the snapshot needs.

**Files:**
- Create: `src/AcDream.ClientExtensions/ClientScreenProjection.cs`
- Modify: `src/AcDream.ClientExtensions/IClientScreen.cs`
- Modify: `src/AcDream.App/Extensions/ClientScreen.cs`
- Modify: `src/AcDream.Runtime/Gameplay/RuntimeCharacterState.cs:55-56,399-410`
- Test: `tests/AcDream.App.Tests/Extensions/ClientScreenProjectionTests.cs`
- Test: `tests/AcDream.App.Tests/Runtime/RuntimeCharacterRevisionTests.cs`

**Acceptance Criteria:**
- [ ] `ClientScreen.TryCaptureProjection` returns false until the camera is bound and the origin known; after, `projection.TryProject(p, out s)` equals `ClientScreen.TryProject(p, out s)` for points in front of and behind the camera.
- [ ] `IClientScreen.TryCaptureProjection` has a default implementation returning false (other implementers and fakes compile unchanged).
- [ ] `RuntimeCharacterState.CharacterRevision` increases on a vital, attribute or character change; `SpellbookRevision` on a spellbook change.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ClientScreenProjectionTests|FullyQualifiedName~RuntimeCharacterRevisionTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/AcDream.App.Tests/Extensions/ClientScreenProjectionTests.cs
using System.Numerics;
using AcDream.App.Extensions;
using AcDream.App.World;
using AcDream.ClientExtensions;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Extensions;

public sealed class ClientScreenProjectionTests
{
    [Fact]
    public void ACapturedProjectionAnswersAsTheLiveScreenDoes()
    {
        var origin = new LiveWorldOriginState();
        origin.Set(0xA9, 0xB4);   // use the setter LiveWorldOriginState exposes; see its file
        var screen = new ClientScreen(origin);
        Assert.False(screen.TryCaptureProjection(out _));

        Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0, -10, 5), Vector3.Zero, Vector3.UnitZ);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(1f, 16f / 9f, 0.1f, 1000f);
        screen.BindCamera(() => (view, projection, new Vector2(1600, 900)));
        Assert.True(screen.TryCaptureProjection(out ClientScreenProjection captured));

        foreach (var local in new[] { new Vector3(96, 96, 0), new Vector3(10, 180, 3), new Vector3(96, -500, 0) })
        {
            PluginNavigationPosition p = AcDream.Runtime.Navigation.RuntimeNavigationPositions.FromCellLocal(0xA9B40001, local);
            bool live = screen.TryProject(p, out Vector2 a);
            bool snap = captured.TryProject(p, out Vector2 b);
            Assert.Equal(live, snap);
            Assert.Equal(a, b);
        }
    }
}
```

Before writing this test, open `src/AcDream.App/World/LiveWorldOriginState.cs` and use whatever it exposes to make `IsKnown` true with a centre (constructor argument or setter); adjust the `origin.Set(...)` line to that member.

```csharp
// tests/AcDream.App.Tests/Runtime/RuntimeCharacterRevisionTests.cs
namespace AcDream.App.Tests.Runtime;

public sealed class RuntimeCharacterRevisionTests
{
    [Fact]
    public void RevisionsMoveWhenTheCharacterOrSpellbookChanges()
    {
        var runtime = GameRuntimeTestFactory.Create();
        long character = runtime.CharacterOwner.CharacterRevision;
        long spellbook = runtime.CharacterOwner.SpellbookRevision;

        runtime.CharacterOwner.LocalPlayer.OnVitalUpdate(2, 0, 0, 50);   // the same call RuntimeRynthGameViewTests uses to set a vital
        Assert.True(runtime.CharacterOwner.CharacterRevision > character);

        runtime.CharacterOwner.Spellbook.OnSpellLearned(1);              // the same call the spellbook tests use
        Assert.True(runtime.CharacterOwner.SpellbookRevision > spellbook);
    }
}
```

Use the vital-update and spell-learned calls already used in `tests/AcDream.App.Tests/Extensions/RuntimeRynthGameViewTests.cs` (search it for `Vital` and `Spellbook`) if the names above differ.

- [ ] **Step 2: Run to verify they fail**

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ClientScreenProjectionTests|FullyQualifiedName~RuntimeCharacterRevisionTests"`
Expected: build FAIL — `TryCaptureProjection`, `ClientScreenProjection`, `CharacterRevision`, `SpellbookRevision` missing.

- [ ] **Step 3: Implement**

```csharp
// src/AcDream.ClientExtensions/ClientScreenProjection.cs
using System.Numerics;
using AcDream.Plugin.Abstractions;

namespace AcDream.ClientExtensions;

/// <summary>
/// One frame's camera, captured: answers <see cref="IClientScreen.TryProject"/> the way the
/// screen did for that frame, from any thread.
/// </summary>
public sealed record ClientScreenProjection(Matrix4x4 ViewProjection, Vector2 Viewport, int CenterX, int CenterY)
{
    public bool TryProject(PluginNavigationPosition position, out Vector2 screen)
    {
        screen = default;
        if (Viewport.X <= 0 || Viewport.Y <= 0)
            return false;
        Vector3 world = PluginNavigationProjection.ToWorld(position, CenterX, CenterY);
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), ViewProjection);
        if (clip.W <= 0.001f)
            return false;
        float ndcX = clip.X / clip.W, ndcY = clip.Y / clip.W;
        screen = new Vector2((ndcX * 0.5f + 0.5f) * Viewport.X, (1f - (ndcY * 0.5f + 0.5f)) * Viewport.Y);
        return true;
    }
}
```

Check that `PluginNavigationProjection` is reachable from `AcDream.ClientExtensions` (it is used by `ClientScreen` in AcDream.App; find its project with `grep -rn "class PluginNavigationProjection" src`). If it lives in a project ClientExtensions does not reference, put `ClientScreenProjection` in that project's namespace instead and reference it from `IClientScreen` the same way `PluginNavigationPosition` is.

Add to `IClientScreen`:

```csharp
    /// <summary>
    /// This frame's camera, captured so another thread can project with it. False before the
    /// world view exists.
    /// </summary>
    bool TryCaptureProjection(out ClientScreenProjection projection)
    {
        projection = null!;
        return false;
    }
```

In `ClientScreen`, replace the body of `TryProject` and add the capture:

```csharp
    public bool TryCaptureProjection(out ClientScreenProjection projection)
    {
        projection = null!;
        if (_camera is null || !origin.IsKnown)
            return false;
        (Matrix4x4 view, Matrix4x4 proj, Vector2 viewport) = _camera();
        if (viewport.X <= 0 || viewport.Y <= 0)
            return false;
        projection = new ClientScreenProjection(view * proj, viewport, origin.CenterX, origin.CenterY);
        return true;
    }

    public bool TryProject(PluginNavigationPosition position, out Vector2 screen)
    {
        screen = default;
        return TryCaptureProjection(out ClientScreenProjection projection)
            && projection.TryProject(position, out screen);
    }
```

In `RuntimeCharacterState`, next to the private fields:

```csharp
    /// <summary>Moves on every vital, attribute or character change.</summary>
    public long CharacterRevision => Interlocked.Read(ref _characterRevision);

    /// <summary>Moves on every spellbook change.</summary>
    public long SpellbookRevision => Interlocked.Read(ref _spellbookRevision);
```

- [ ] **Step 4: Run to verify they pass**

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ClientScreenProjectionTests|FullyQualifiedName~RuntimeCharacterRevisionTests|FullyQualifiedName~RynthScreenSlots|FullyQualifiedName~ClientScreen"`
Expected: PASS, including the existing screen tests.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.ClientExtensions src/AcDream.App/Extensions/ClientScreen.cs src/AcDream.Runtime/Gameplay/RuntimeCharacterState.cs tests/AcDream.App.Tests/Extensions/ClientScreenProjectionTests.cs tests/AcDream.App.Tests/Runtime/RuntimeCharacterRevisionTests.cs
git commit -m "feat(client): captured screen projection; character and spellbook revisions

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 3: The snapshot (player, entities, character, spellbook, clocks, screen, vendor, trade)

**Goal:** `RynthWorldSnapshot` and `RynthSnapshotBuilder` produce every non-object part of the snapshot from the live view, sharing unchanged parts between publishes.

**Files:**
- Create: `src/AcDream.Rynth.Host/RynthWorldSnapshot.cs`
- Create: `src/AcDream.Rynth.Host/RynthSnapshotBuilder.cs`
- Test: `tests/AcDream.App.Tests/Extensions/RynthSnapshotBuilderTests.cs`

**Acceptance Criteria:**
- [ ] Every publish copies the per-frame scalars listed in the spec §2.1 (including `Dead` = player vitals readable and current health 0) and one `RynthEntityPose` per active entity.
- [ ] `Character` is the same instance across publishes until `CharacterRevision`, `DescriptionRevision` or the player id moves.
- [ ] `Spellbook` is the same instance until `SpellbookRevision` moves or 1 s passes.
- [ ] `ServerTimeAt(stopwatchTimestamp)` = published server time + elapsed seconds since publish.
- [ ] `Vendor` / `Trade` are rebuilt only when their generation (or `MutationRevision` for the vendor) moved.
- [ ] `Projection`, `Viewport`, `RadarDisc` are captured from `IClientScreen` when present.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~RynthSnapshotBuilderTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** (real runtime, as `RuntimeRynthGameViewTests` does)

```csharp
// tests/AcDream.App.Tests/Extensions/RynthSnapshotBuilderTests.cs
using System.Diagnostics;
using AcDream.Core.Items;
using AcDream.Rynth.Host;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed class RynthSnapshotBuilderTests
{
    private static (AcDream.Runtime.GameRuntime Runtime, RuntimeRynthGameView Live, RynthSnapshotBuilder Builder) Make()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        runtime.InventoryOwner.Objects.AddOrUpdate(new ClientObject { ObjectId = 0x50000001, Name = "Sammy" });
        var live = new RuntimeRynthGameView(runtime);
        return (runtime, live, new RynthSnapshotBuilder(live, runtime, screen: null));
    }

    [Fact]
    public void ScalarsComeFromTheLiveView()
    {
        var (_, live, builder) = Make();
        RynthWorldSnapshot s = builder.Build();
        Assert.Equal(live.PlayerId, s.PlayerId);
        Assert.Equal(live.SelectedObjectId, s.SelectedObjectId);
        Assert.Equal(live.IsPortaling, s.IsPortaling);
        Assert.Equal(live.BusyCount, s.BusyCount);
        Assert.Equal(live.CombatMode, s.CombatMode);
        Assert.Equal(live.AutoRunActive, s.AutoRunActive);
        Assert.Equal(live.OpenContainerId, s.OpenContainerId);
        Assert.Equal(live.AccountName, s.AccountName);
    }

    [Fact]
    public void CharacterIsSharedUntilItsRevisionMoves()
    {
        var (runtime, _, builder) = Make();
        RynthWorldSnapshot a = builder.Build();
        RynthWorldSnapshot b = builder.Build();
        Assert.Same(a.Character, b.Character);
        runtime.CharacterOwner.LocalPlayer.OnVitalUpdate(2, 0, 0, 50);   // as in Task 2's test
        RynthWorldSnapshot c = builder.Build();
        Assert.NotSame(b.Character, c.Character);
    }

    [Fact]
    public void ServerTimeKeepsRunningAfterThePublish()
    {
        var (_, live, builder) = Make();
        RynthWorldSnapshot s = builder.Build();
        long later = s.PublishedAt + Stopwatch.Frequency * 2;
        Assert.Equal(s.ServerTime + 2.0, s.ServerTimeAt(later), 6);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~RynthSnapshotBuilderTests"`
Expected: build FAIL — types missing.

- [ ] **Step 3: Implement the snapshot types**

```csharp
// src/AcDream.Rynth.Host/RynthWorldSnapshot.cs
using System.Collections.Frozen;
using System.Diagnostics;
using System.Numerics;
using AcDream.ClientExtensions;
using AcDream.Plugin.Abstractions;

namespace AcDream.Rynth.Host;

/// <summary>One entity's pose and state, as the live view reads them.</summary>
public readonly record struct RynthEntityPose(
    uint CellId, Vector3 Origin, Quaternion Orientation, uint RawState, bool? MotionOn,
    IReadOnlyList<(uint SubPaletteId, uint Offset)>? Palettes);

/// <summary>The player's character: refreshed when the character revision moves.</summary>
public sealed record RynthCharacterPart(
    bool HasVitals, RynthVitals Vitals,
    bool HasBaseVitals, uint BaseHealth, uint BaseStamina, uint BaseMana,
    FrozenDictionary<uint, (int Buffed, int Base, int Training)> Skills,
    FrozenDictionary<uint, (uint Buffed, uint Base)> Attributes,
    FrozenDictionary<uint, uint> VitalBases,
    RynthPlayerProgress? Progress);

/// <summary>The player's spellbook: refreshed when its revision moves, or each second.</summary>
public sealed record RynthSpellbookPart(
    IReadOnlyList<RynthEnchantment>? Enchantments,
    IReadOnlyList<RynthStatMod>? StatMods,
    float VitaeMultiplier,
    IReadOnlyList<uint>? KnownSpells,
    FrozenSet<uint> KnownSet,
    bool SpellsAvailable);

/// <summary>Everything the pump reads in one pass. Immutable; published by the frame thread.</summary>
public sealed record RynthWorldSnapshot
{
    public long Sequence { get; init; }
    /// <summary><see cref="Stopwatch.GetTimestamp"/> at publish.</summary>
    public long PublishedAt { get; init; }

    public uint PlayerId { get; init; }
    public uint SelectedObjectId { get; init; }
    public string AccountName { get; init; } = "";
    public string WorldName { get; init; } = "";
    public bool IsPortaling { get; init; }
    public bool Dead { get; init; }
    public bool HasHeading { get; init; }
    public float Heading { get; init; }
    public int BusyCount { get; init; }
    public bool HasPendingItemRequest { get; init; }
    public uint PendingAcType { get; init; }
    public uint PendingObjectId { get; init; }
    public int CombatMode { get; init; }
    public bool IsAttackInProgress { get; init; }
    public bool IsCastGestureActive { get; init; }
    public bool AutoRunActive { get; init; }
    public PluginMovementIntent? MovementIntent { get; init; }
    public uint OpenContainerId { get; init; }
    public ServerReplySnapshot ServerReplies { get; init; }
    public double ServerTime { get; init; }

    public FrozenDictionary<uint, RynthEntityPose> Entities { get; init; } = FrozenDictionary<uint, RynthEntityPose>.Empty;
    public RynthCharacterPart? Character { get; init; }
    public RynthSpellbookPart? Spellbook { get; init; }
    public RynthObjectsPart Objects { get; init; } = RynthObjectsPart.Empty;   // Task 4
    public RynthVendor? Vendor { get; init; }
    public RynthTradeState? Trade { get; init; }

    public ClientScreenProjection? Projection { get; init; }
    public bool HasViewport { get; init; }
    public uint ViewportWidth { get; init; }
    public uint ViewportHeight { get; init; }
    public bool HasRadar { get; init; }
    public (int X0, int Y0, int X1, int Y1) Radar { get; init; }

    /// <summary>Server time at <paramref name="timestamp"/>: the published time plus the time since.</summary>
    public double ServerTimeAt(long timestamp) =>
        ServerTime + (timestamp - PublishedAt) / (double)Stopwatch.Frequency;
}
```

`RynthObjectsPart` is defined in Task 4; for this task add a placeholder-free minimal version in `RynthObjectMirror.cs`:

```csharp
// src/AcDream.Rynth.Host/RynthObjectMirror.cs (Task 4 completes it)
namespace AcDream.Rynth.Host;

public sealed record RynthObjectsPart
{
    public static readonly RynthObjectsPart Empty = new();
}
```

- [ ] **Step 4: Implement the builder** (everything is read through the live view so the snapshot agrees with it)

```csharp
// src/AcDream.Rynth.Host/RynthSnapshotBuilder.cs
using System.Collections.Frozen;
using System.Diagnostics;
using System.Numerics;
using AcDream.ClientExtensions;
using AcDream.Runtime;
using AcDream.Runtime.Entities;

namespace AcDream.Rynth.Host;

/// <summary>
/// Builds <see cref="RynthWorldSnapshot"/>s on the frame thread from the live view: small parts
/// every time, big parts only when their revision moved, the rest shared with the last one.
/// </summary>
public sealed class RynthSnapshotBuilder(IRynthGameView live, GameRuntime runtime, IClientScreen? screen)
{
    // RynthCore's id ranges: skills, primary attributes, max-vital stat ids.
    private const uint MaxSkillId = 64;
    private static readonly uint[] AttributeIds = [1, 2, 3, 4, 5, 6];
    private static readonly uint[] VitalStatIds = [1, 3, 5];
    private static readonly long SpellbookRefreshTicks = Stopwatch.Frequency;

    private RynthWorldSnapshot? _last;
    private long _sequence;
    private (long Character, long Description, uint Player) _characterKey = (-1, -1, 0);
    private (long Revision, uint Player) _spellbookKey = (-1, 0);
    private long _spellbookAt;
    private (uint Generation, ulong Mutation) _vendorKey = (uint.MaxValue, ulong.MaxValue);
    private RynthObjectMirror? _objects;   // Task 4

    public RynthWorldSnapshot Build()
    {
        long now = Stopwatch.GetTimestamp();
        uint player = live.PlayerId;
        bool hasVitals = live.TryGetPlayerVitals(out RynthVitals vitals);
        bool hasHeading = live.TryGetPlayerHeading(out float heading);
        bool pending = live.TryGetPendingItemRequest(out uint acType, out uint pendingId);
        var snapshot = new RynthWorldSnapshot
        {
            Sequence = ++_sequence,
            PublishedAt = now,
            PlayerId = player,
            SelectedObjectId = live.SelectedObjectId,
            AccountName = live.AccountName,
            WorldName = live.WorldName,
            IsPortaling = live.IsPortaling,
            Dead = hasVitals && vitals.CurrentHealth == 0,
            HasHeading = hasHeading,
            Heading = heading,
            BusyCount = live.BusyCount,
            HasPendingItemRequest = pending,
            PendingAcType = acType,
            PendingObjectId = pendingId,
            CombatMode = live.CombatMode,
            IsAttackInProgress = live.IsAttackInProgress,
            IsCastGestureActive = live.IsCastGestureActive,
            AutoRunActive = live.AutoRunActive,
            MovementIntent = live.CurrentMovementIntent,
            OpenContainerId = live.OpenContainerId,
            ServerReplies = live.ServerReplies,
            ServerTime = live.ServerTime,
            Entities = BuildEntities(),
            Character = BuildCharacter(player),
            Spellbook = BuildSpellbook(player, now),
            Objects = (_objects ??= new RynthObjectMirror(live, runtime)).Build(player),   // Task 4
            Vendor = BuildVendor(),
            Trade = live.Trade,
        };
        snapshot = WithScreen(snapshot);
        _last = snapshot;
        return snapshot;
    }

    public void Dispose() => _objects?.Dispose();

    private FrozenDictionary<uint, RynthEntityPose> BuildEntities()
    {
        var poses = new Dictionary<uint, RynthEntityPose>(runtime.EntityObjects.Entities.Count);
        foreach (RuntimeEntityRecord record in runtime.EntityObjects.Entities.ActiveRecords)
        {
            uint guid = record.ServerGuid;   // the record's server guid property; check RuntimeEntityRecord for its exact name
            if (record.PhysicsBody is not { } body || body.CellPosition.ObjCellId == 0)
                continue;
            live.TryGetObjectState(guid, out uint state);
            bool? motion = live.TryGetObjectMotionOn(guid, out bool on) ? on : null;
            poses[guid] = new RynthEntityPose(
                body.CellPosition.ObjCellId, body.CellPosition.Frame.Origin, body.Orientation,
                state, motion, live.GetPalettes(guid));
        }
        return poses.ToFrozenDictionary();
    }

    private RynthCharacterPart? BuildCharacter(uint player)
    {
        var key = (runtime.CharacterOwner.CharacterRevision,
                   runtime.CharacterOwner.LocalPlayer.DescriptionRevision, player);
        if (_last?.Character is { } shared && key == _characterKey)
            return shared;
        _characterKey = key;
        var skills = new Dictionary<uint, (int, int, int)>();
        for (uint id = 0; id <= MaxSkillId; id++)
            if (live.TryGetSkill(player, id, out int buffed, out int baseLevel, out int training))
                skills[id] = (buffed, baseLevel, training);
        var attributes = new Dictionary<uint, (uint, uint)>();
        foreach (uint id in AttributeIds)
            if (live.TryGetAttribute(player, id, out uint buffed, out uint baseLevel))
                attributes[id] = (buffed, baseLevel);
        var vitalBases = new Dictionary<uint, uint>();
        foreach (uint id in VitalStatIds)
            if (live.TryGetVitalBase(player, id, out uint baseMaximum))
                vitalBases[id] = baseMaximum;
        bool hasVitals = live.TryGetPlayerVitals(out RynthVitals vitals);
        bool hasBase = live.TryGetPlayerBaseVitals(out uint h, out uint st, out uint m);
        return new RynthCharacterPart(hasVitals, vitals, hasBase, h, st, m,
            skills.ToFrozenDictionary(), attributes.ToFrozenDictionary(), vitalBases.ToFrozenDictionary(),
            live.PlayerProgress);
    }

    private RynthSpellbookPart? BuildSpellbook(uint player, long now)
    {
        var key = (runtime.CharacterOwner.SpellbookRevision, player);
        if (_last?.Spellbook is { } shared && key == _spellbookKey && now - _spellbookAt < SpellbookRefreshTicks)
            return shared;
        _spellbookKey = key;
        _spellbookAt = now;
        IReadOnlyList<uint>? known = live.KnownSpells;
        return new RynthSpellbookPart(
            live.PlayerEnchantments, live.PlayerStatMods, live.VitaeMultiplier, known,
            (known ?? []).ToFrozenSet(), live.IsSpellKnown(player, 0) != -1);
    }

    private RynthVendor? BuildVendor()
    {
        var key = (RynthVendorGeneration(), runtime.InventoryOwner.Objects.MutationRevision);
        if (_last is { } last && key == _vendorKey)
            return last.Vendor;
        _vendorKey = key;
        return live.Vendor;
    }

    /// <summary>Set by the host: the vendor list generation (RynthVendorTrades.Generation).</summary>
    public Func<uint> RynthVendorGeneration { get; set; } = () => 0;

    private RynthWorldSnapshot WithScreen(RynthWorldSnapshot s)
    {
        bool hasViewport = live.TryGetViewportSize(out uint w, out uint h);
        bool hasRadar = live.TryGetRadarRect(out int x0, out int y0, out int x1, out int y1);
        ClientScreenProjection? projection = null;
        if (screen is not null && screen.TryCaptureProjection(out ClientScreenProjection captured))
            projection = captured;
        return s with
        {
            Projection = projection,
            HasViewport = hasViewport, ViewportWidth = w, ViewportHeight = h,
            HasRadar = hasRadar, Radar = (x0, y0, x1, y1),
        };
    }
}
```

Field-name checks while implementing (do them; don't guess): `RynthVitals`' current-health member (open `src/AcDream.Rynth.Host/RynthStateTypes.cs`), `RuntimeEntityRecord`'s server guid member, `ServerReplySnapshot`'s namespace. Trade: rebuild every publish is acceptable here (it is small, spec §2.8 allows it); keep `Trade = live.Trade`.

- [ ] **Step 5: Run to verify they pass**

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~RynthSnapshotBuilderTests"`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.Rynth.Host/RynthWorldSnapshot.cs src/AcDream.Rynth.Host/RynthSnapshotBuilder.cs src/AcDream.Rynth.Host/RynthObjectMirror.cs tests/AcDream.App.Tests/Extensions/RynthSnapshotBuilderTests.cs
git commit -m "feat(rynth): world snapshot builder (player, entities, character, spellbook, screen)

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 4: Objects in the snapshot (dirty tracking and deep copies)

**Goal:** `RynthObjectMirror` keeps per-object deep copies, recopying only objects the table reported changed, and publishes contents, equipped-by, live ids and derived predicates.

**Files:**
- Modify: `src/AcDream.Rynth.Host/RynthObjectMirror.cs`
- Modify: `src/AcDream.Rynth.Host/RuntimeRynthGameView.cs` (expose a deep-copy helper, see Step 3)
- Test: `tests/AcDream.App.Tests/Extensions/RynthObjectMirrorTests.cs`

**Acceptance Criteria:**
- [ ] After `Build`, `Objects.TryGet(id)` holds the same values `live.TryGetObject(id)` gives, with **copied** property dictionaries (mutating the live object afterwards doesn't change the snapshot's).
- [ ] An object not changed since the last build is the same `RynthObjectEntry` instance in the next build; an updated one is a new instance; a removed one is gone; `Cleared` empties it.
- [ ] Contents (`GetContainerContents`), `TryCountContents`, `LiveObjectIds` and `InventoryRevision` match the live view after each build.
- [ ] `IsOwnedItem`, `IsOwnedTinkeringTool`, `IsAttackable`, `GetObjectEnchantments` match the live view for every object; recomputed for all objects only when `MutationRevision` or the player id moved.
- [ ] The mirror subscribes to the table's events on construction and unsubscribes on `Dispose`.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~RynthObjectMirrorTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/AcDream.App.Tests/Extensions/RynthObjectMirrorTests.cs
using AcDream.Core.Items;
using AcDream.Rynth.Host;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed class RynthObjectMirrorTests
{
    private static (AcDream.Runtime.GameRuntime, RuntimeRynthGameView, RynthObjectMirror) Make()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        runtime.InventoryOwner.Objects.AddOrUpdate(new ClientObject { ObjectId = 0x50000001, Name = "Sammy" });
        var pack = new ClientObject { ObjectId = 0x80000110, ContainerId = 0x50000001, ItemsCapacity = 24, Name = "Pack" };
        var gem = new ClientObject { ObjectId = 0x80000111, ContainerId = 0x80000110, Name = "Gem" };
        gem.Properties.Int64s[1] = 42;
        gem.Properties.Ints[5] = 7;
        runtime.InventoryOwner.Objects.AddOrUpdate(pack);
        runtime.InventoryOwner.Objects.AddOrUpdate(gem);
        runtime.InventoryOwner.Objects.ReplaceContents(0x50000001, new uint[] { 0x80000110 });
        runtime.InventoryOwner.Objects.ReplaceContents(0x80000110, new uint[] { 0x80000111 });
        var live = new RuntimeRynthGameView(runtime);
        return (runtime, live, new RynthObjectMirror(live, runtime));
    }

    [Fact]
    public void ObjectsAreDeepCopies()
    {
        var (runtime, live, mirror) = Make();
        RynthObjectsPart part = mirror.Build(live.PlayerId);
        Assert.True(part.TryGet(0x80000111, out RynthObjectEntry gem));
        Assert.Equal("Gem", gem.Name);
        Assert.Equal(7, gem.Info.Ints[5]);
        runtime.InventoryOwner.Objects.Get(0x80000111)!.Properties.Ints[5] = 99;
        Assert.Equal(7, gem.Info.Ints[5]);
        Assert.True(gem.Int64s.TryGetValue(1, out long v) && v == 42);
    }

    [Fact]
    public void OnlyChangedObjectsAreCopiedAgain()
    {
        var (runtime, live, mirror) = Make();
        RynthObjectsPart a = mirror.Build(live.PlayerId);
        RynthObjectsPart b = mirror.Build(live.PlayerId);
        a.TryGet(0x80000110, out RynthObjectEntry packA);
        b.TryGet(0x80000110, out RynthObjectEntry packB);
        Assert.Same(packA, packB);

        runtime.InventoryOwner.Objects.AddOrUpdate(new ClientObject { ObjectId = 0x80000110, ContainerId = 0x50000001, ItemsCapacity = 24, Name = "Big Pack" });
        RynthObjectsPart c = mirror.Build(live.PlayerId);
        c.TryGet(0x80000110, out RynthObjectEntry packC);
        Assert.NotSame(packB, packC);
        Assert.Equal("Big Pack", packC.Name);

        runtime.InventoryOwner.Objects.Remove(0x80000111);
        Assert.False(mirror.Build(live.PlayerId).TryGet(0x80000111, out _));
    }

    [Fact]
    public void IndexesAndPredicatesMatchTheLiveView()
    {
        var (_, live, mirror) = Make();
        RynthObjectsPart part = mirror.Build(live.PlayerId);
        Assert.Equal(live.GetContainerContents(0x80000110), part.Contents(0x80000110));
        Assert.Equal(live.LiveObjectIds!.OrderBy(i => i), part.LiveObjectIds!.OrderBy(i => i));
        Assert.Equal(live.InventoryRevision, part.Revision);
        Assert.Equal(live.TryCountContents(0x50000001, out int i1, out int c1), part.TryCountContents(0x50000001, out int i2, out int c2));
        Assert.Equal((i1, c1), (i2, c2));
        foreach (uint id in new uint[] { 0x50000001, 0x80000110, 0x80000111 })
        {
            part.TryGet(id, out RynthObjectEntry e);
            Assert.Equal(live.IsOwnedItem(id), e.IsOwnedItem);
            Assert.Equal(live.IsAttackable(id), e.IsAttackable);
            Assert.Equal(live.IsOwnedTinkeringTool(id), e.IsOwnedTinkeringTool);
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~RynthObjectMirrorTests"`
Expected: build FAIL — `RynthObjectEntry`, `RynthObjectMirror` members missing.

- [ ] **Step 3: Make the live view's object copy available**

In `RuntimeRynthGameView`, add next to `TryGetObject` (and stop `TryGetObject` from leaking live dictionaries only on the snapshot path — the live view keeps its behaviour, since it is frame-thread only):

```csharp
    /// <summary>
    /// <see cref="TryGetObject"/> with every property table copied, and the Int64 table
    /// the way <see cref="TryGetInt64Property"/> reads it: safe to hand to another thread.
    /// </summary>
    internal bool TryCopyObject(uint objectId, out RynthObjectInfo info, out IReadOnlyDictionary<uint, long> int64s)
    {
        int64s = System.Collections.Frozen.FrozenDictionary<uint, long>.Empty;
        if (!TryGetObject(objectId, out RynthObjectInfo shared))
        {
            info = null!;
            return false;
        }
        info = shared with
        {
            Ints = new Dictionary<uint, int>(shared.Ints),
            DataIds = new Dictionary<uint, uint>(shared.DataIds),
            InstanceIds = new Dictionary<uint, uint>(shared.InstanceIds),
            Bools = new Dictionary<uint, bool>(shared.Bools),
            Floats = new Dictionary<uint, double>(shared.Floats),
            Strings = new Dictionary<uint, string>(shared.Strings),
            AppraisedSpellIds = shared.AppraisedSpellIds is null ? null : [.. shared.AppraisedSpellIds],
        };
        ClientObject? item = _runtime.InventoryOwner.Objects.Get(objectId);
        var copy = new Dictionary<uint, long>(item?.Properties.Int64s ?? []);
        if (objectId != 0 && objectId == PlayerId && _runtime.CharacterOwner.LocalPlayer is { HasDescription: true } player)
            foreach ((uint key, long value) in player.Properties.Int64s)
                copy[key] = value;
        int64s = copy;
        return true;
    }
```

Check `RynthObjectInfo` (`src/AcDream.Rynth.Host/RynthObjectInfo.cs`): if it is a class, not a record, add a `Clone()` there that copies each member, and use it instead of `with`. Match each dictionary's real value type to the declaration there, and match the player-merge rule to `TryGetInt64Property`'s body (read it).

- [ ] **Step 4: Implement the mirror**

```csharp
// src/AcDream.Rynth.Host/RynthObjectMirror.cs
using System.Collections.Frozen;
using AcDream.Core.Items;
using AcDream.Runtime;

namespace AcDream.Rynth.Host;

/// <summary>One object as the snapshot holds it: a deep copy plus the live view's derived answers.</summary>
public sealed record RynthObjectEntry(
    string Name, RynthObjectInfo Info, IReadOnlyDictionary<uint, long> Int64s,
    bool IsOwnedItem, bool IsOwnedTinkeringTool, bool IsAttackable,
    IReadOnlyList<RynthEnchantment>? Enchantments);

public sealed record RynthObjectsPart
{
    public static readonly RynthObjectsPart Empty = new();

    public FrozenDictionary<uint, RynthObjectEntry> Entries { get; init; } = FrozenDictionary<uint, RynthObjectEntry>.Empty;
    public FrozenDictionary<uint, IReadOnlyList<uint>> ContentsByContainer { get; init; } = FrozenDictionary<uint, IReadOnlyList<uint>>.Empty;
    public FrozenDictionary<uint, (bool Ok, int Items, int Containers)> Counts { get; init; } = FrozenDictionary<uint, (bool, int, int)>.Empty;
    public IReadOnlyList<uint>? LiveObjectIds { get; init; }
    public ulong Revision { get; init; }

    public bool TryGet(uint id, out RynthObjectEntry entry) => Entries.TryGetValue(id, out entry!);

    public IReadOnlyList<uint> Contents(uint containerId) =>
        ContentsByContainer.TryGetValue(containerId, out IReadOnlyList<uint>? ids) ? ids : [];

    public bool TryCountContents(uint containerId, out int items, out int containers)
    {
        (bool ok, items, containers) = Counts.TryGetValue(containerId, out var c) ? c : (false, 0, 0);
        return ok;
    }
}

/// <summary>
/// The object table, mirrored for the snapshot: objects the table reported added, updated or
/// moved are copied again at the next build; the rest are shared. Frame thread only.
/// </summary>
public sealed class RynthObjectMirror : IDisposable
{
    private readonly RuntimeRynthGameView _live;
    private readonly ClientObjectTable _table;
    private readonly HashSet<uint> _dirty = [];
    private readonly Dictionary<uint, RynthObjectEntry> _entries = [];
    private bool _all = true;
    private (ulong Revision, uint Player) _key = (ulong.MaxValue, 0);
    private RynthObjectsPart _last = RynthObjectsPart.Empty;

    public RynthObjectMirror(IRynthGameView live, GameRuntime runtime)
    {
        _live = (RuntimeRynthGameView)live;
        _table = runtime.InventoryOwner.Objects;
        _table.ObjectAdded += OnObject;
        _table.ObjectUpdated += OnObject;
        _table.ObjectRemoved += OnObject;
        _table.ObjectMoved += OnMoved;
        _table.Cleared += OnCleared;
    }

    public RynthObjectsPart Build(uint player)
    {
        var key = (_table.MutationRevision, player);
        if (key == _key && _dirty.Count == 0 && !_all)
            return _last;
        bool recomputeAll = _all || key != _key;
        IReadOnlyList<uint> ids = _live.LiveObjectIds ?? [];
        if (_all)
            _entries.Clear();
        var present = new HashSet<uint>(ids);
        foreach (uint id in _entries.Keys.Where(id => !present.Contains(id)).ToList())
            _entries.Remove(id);
        foreach (uint id in ids)
        {
            if (!recomputeAll && !_dirty.Contains(id) && _entries.ContainsKey(id))
                continue;
            if (!_live.TryCopyObject(id, out RynthObjectInfo info, out IReadOnlyDictionary<uint, long> int64s))
            {
                _entries.Remove(id);
                continue;
            }
            _live.TryGetObjectName(id, out string name);
            _entries[id] = new RynthObjectEntry(name ?? "", info, int64s,
                _live.IsOwnedItem(id), _live.IsOwnedTinkeringTool(id), _live.IsAttackable(id),
                _live.GetObjectEnchantments(id));
        }
        _dirty.Clear();
        _all = false;

        var contents = new Dictionary<uint, IReadOnlyList<uint>>();
        var counts = new Dictionary<uint, (bool, int, int)>();
        foreach (uint id in ids.Append(player))
        {
            IReadOnlyList<uint> c = _live.GetContainerContents(id);
            if (c.Count > 0)
                contents[id] = [.. c];
            bool ok = _live.TryCountContents(id, out int items, out int containers);
            if (ok)
                counts[id] = (true, items, containers);
        }
        _key = key;
        _last = new RynthObjectsPart
        {
            Entries = _entries.ToFrozenDictionary(),
            ContentsByContainer = contents.ToFrozenDictionary(),
            Counts = counts.ToFrozenDictionary(),
            LiveObjectIds = _live.LiveObjectIds,
            Revision = _live.InventoryRevision,
        };
        return _last;
    }

    public void Dispose()
    {
        _table.ObjectAdded -= OnObject;
        _table.ObjectUpdated -= OnObject;
        _table.ObjectRemoved -= OnObject;
        _table.ObjectMoved -= OnMoved;
        _table.Cleared -= OnCleared;
    }

    private void OnObject(ClientObject item) => _dirty.Add(item.ObjectId);

    private void OnMoved(ClientObjectMove move) => _dirty.Add(move.ObjectId);   // check ClientObjectMove's id member

    private void OnCleared() => _all = true;
}
```

Notes for the implementer: the contents/counts pass runs only when the key or dirty set changed (the early return above covers the idle case, which is the common one). If `GetContainerContents` for non-containers is costly on 2k objects, restrict the pass to ids whose entry has `Info.ItemsCapacity > 0` plus the player; keep the parity test green.

- [ ] **Step 5: Run to verify they pass**

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~RynthObjectMirrorTests|FullyQualifiedName~RynthSnapshotBuilderTests|FullyQualifiedName~RuntimeRynthGameViewTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.Rynth.Host/RynthObjectMirror.cs src/AcDream.Rynth.Host/RuntimeRynthGameView.cs src/AcDream.Rynth.Host/RynthObjectInfo.cs tests/AcDream.App.Tests/Extensions/RynthObjectMirrorTests.cs
git commit -m "feat(rynth): object mirror for the snapshot (dirty tracking, deep copies)

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 5: Snapshot view, routed view and read parity

**Goal:** `SnapshotRynthGameView` answers every `IRynthGameView` read from a snapshot and queues every action; `ThreadRoutedGameView` picks live or snapshot by thread; a table-driven test proves every read agrees with the live view.

**Files:**
- Create: `src/AcDream.Rynth.Host/SnapshotRynthGameView.cs`
- Create: `src/AcDream.Rynth.Host/ThreadRoutedGameView.cs`
- Test: `tests/AcDream.App.Tests/Extensions/SnapshotRynthGameViewParityTests.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/ThreadRoutedGameViewTests.cs`

**Acceptance Criteria:**
- [ ] For a populated runtime (player, pack, item, a second creature entity, vitals, a vendor open), every read member of `IRynthGameView` gives the same answer through `SnapshotRynthGameView` (after a `Build`) as through `RuntimeRynthGameView`.
- [ ] `ServerTime` through the snapshot view = `snapshot.ServerTimeAt(now)`.
- [ ] `TryWorldToScreen` uses `snapshot.Projection` and the snapshot player cell; false when there is no projection.
- [ ] Every action member enqueues (Start or Stop kind per the spec's list) and returns the queue's answer; `VendorBuy/VendorSell` give `notice = null`, and a drain-time failure calls the `onVendorQueuedFailure` callback with the live notice.
- [ ] `ThreadRoutedGameView` forwards to the live view when `RynthFrameThread.IsCurrent` or not latched, else to the snapshot view; `VendorTransactionCompleted` subscriptions go to the live view.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~SnapshotRynthGameViewParityTests" && ~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~ThreadRoutedGameViewTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing parity test**

```csharp
// tests/AcDream.App.Tests/Extensions/SnapshotRynthGameViewParityTests.cs
using AcDream.Core.Items;
using AcDream.Rynth.Host;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed class SnapshotRynthGameViewParityTests
{
    [Fact]
    public void EveryReadAgreesWithTheLiveView()
    {
        var runtime = GameRuntimeTestFactory.Create();
        // Populate: copy the setup of RuntimeRynthGameViewTests.TheOpenVendorComesFromTheVendorStateWithRynthCoresCounts
        // (player with coins, pack, stacked items, an open vendor), plus one creature entity and player vitals,
        // using the same helper calls that file uses for entities and vitals.
        PopulateLikeRuntimeRynthGameViewTests(runtime);
        var live = new RuntimeRynthGameView(runtime);
        var builder = new RynthSnapshotBuilder(live, runtime, screen: null);
        var queue = new RynthActionQueue(_ => { }, () => false);
        var snap = new SnapshotRynthGameView(builder.Build(), queue, live, _ => { });

        uint[] ids = [0x50000001, 0x80000110, 0x80000111, 0x80000112, 0x80000700, 0x12345678];
        Assert.Equal(live.PlayerId, snap.PlayerId);
        Assert.Equal(live.SelectedObjectId, snap.SelectedObjectId);
        Assert.Equal(live.AccountName, snap.AccountName);
        Assert.Equal(live.WorldName, snap.WorldName);
        Assert.Equal(live.IsPortaling, snap.IsPortaling);
        Assert.Equal(live.BusyCount, snap.BusyCount);
        Assert.Equal(live.CombatMode, snap.CombatMode);
        Assert.Equal(live.VitaeMultiplier, snap.VitaeMultiplier);
        Assert.Equal(live.OpenContainerId, snap.OpenContainerId);
        Assert.Equal(live.IsAttackInProgress, snap.IsAttackInProgress);
        Assert.Equal(live.InventoryRevision, snap.InventoryRevision);
        Assert.Equal(live.IsCastGestureActive, snap.IsCastGestureActive);
        Assert.Equal(live.AutoRunActive, snap.AutoRunActive);
        Assert.Equal(live.CurrentMovementIntent, snap.CurrentMovementIntent);
        Assert.Equal(live.KnownSpells, snap.KnownSpells);
        Assert.Equal(live.LiveObjectIds?.Order(), snap.LiveObjectIds?.Order());
        Assert.Equal(live.TryGetPlayerVitals(out var v1), snap.TryGetPlayerVitals(out var v2)); Assert.Equal(v1, v2);
        Assert.Equal(live.TryGetPlayerBaseVitals(out var a1, out var b1, out var c1), snap.TryGetPlayerBaseVitals(out var a2, out var b2, out var c2));
        Assert.Equal((a1, b1, c1), (a2, b2, c2));
        Assert.Equal(live.TryGetPlayerPose(out var pc1, out var po1, out var pq1), snap.TryGetPlayerPose(out var pc2, out var po2, out var pq2));
        Assert.Equal((pc1, po1, pq1), (pc2, po2, pq2));
        Assert.Equal(live.TryGetPlayerHeading(out var h1), snap.TryGetPlayerHeading(out var h2)); Assert.Equal(h1, h2);
        Assert.Equal(live.TryGetPendingItemRequest(out var t1, out var o1), snap.TryGetPendingItemRequest(out var t2, out var o2));
        Assert.Equal((t1, o1), (t2, o2));
        Assert.Equal(live.Vendor?.VendorId, snap.Vendor?.VendorId);
        Assert.Equal(live.Vendor?.Items.Count, snap.Vendor?.Items.Count);
        for (uint skill = 0; skill <= 64; skill++)
        {
            Assert.Equal(live.TryGetSkill(live.PlayerId, skill, out var x1, out var y1, out var z1), snap.TryGetSkill(live.PlayerId, skill, out var x2, out var y2, out var z2));
            Assert.Equal((x1, y1, z1), (x2, y2, z2));
        }
        foreach (uint id in ids)
        {
            Assert.Equal(live.TryGetObjectName(id, out var n1), snap.TryGetObjectName(id, out var n2)); Assert.Equal(n1, n2);
            Assert.Equal(live.TryGetInt64Property(id, 1, out var l1), snap.TryGetInt64Property(id, 1, out var l2)); Assert.Equal(l1, l2);
            Assert.Equal(live.TryGetObject(id, out var i1), snap.TryGetObject(id, out var i2));
            if (i1 is not null) { Assert.Equal(i1.StackSize, i2.StackSize); Assert.Equal(i1.ContainerId, i2.ContainerId); Assert.Equal(i1.Ints, i2.Ints); }
            Assert.Equal(live.GetContainerContents(id), snap.GetContainerContents(id));
            Assert.Equal(live.TryCountContents(id, out var ci1, out var cc1), snap.TryCountContents(id, out var ci2, out var cc2)); Assert.Equal((ci1, cc1), (ci2, cc2));
            Assert.Equal(live.TryGetObjectPosition(id, out var oc1, out var oo1), snap.TryGetObjectPosition(id, out var oc2, out var oo2)); Assert.Equal((oc1, oo1), (oc2, oo2));
            Assert.Equal(live.TryGetObjectHeading(id, out var oh1), snap.TryGetObjectHeading(id, out var oh2)); Assert.Equal(oh1, oh2, 3);
            Assert.Equal(live.TryGetObjectState(id, out var s1), snap.TryGetObjectState(id, out var s2)); Assert.Equal(s1, s2);
            Assert.Equal(live.TryGetObjectMotionOn(id, out var m1), snap.TryGetObjectMotionOn(id, out var m2)); Assert.Equal(m1, m2);
            Assert.Equal(live.GetPalettes(id), snap.GetPalettes(id));
            Assert.Equal(live.IsAttackable(id), snap.IsAttackable(id));
            Assert.Equal(live.IsOwnedItem(id), snap.IsOwnedItem(id));
            Assert.Equal(live.IsOwnedTinkeringTool(id), snap.IsOwnedTinkeringTool(id));
            Assert.Equal(live.IsSpellKnown(id, 1), snap.IsSpellKnown(id, 1));
            Assert.Equal(live.TryGetTargetVitals(id, out var tv1), snap.TryGetTargetVitals(id, out var tv2)); Assert.Equal(tv1, tv2);
            Assert.Equal(live.TryGetHealthRatio(id, out var r1), snap.TryGetHealthRatio(id, out var r2)); Assert.Equal(r1, r2);
        }
    }

    private static void PopulateLikeRuntimeRynthGameViewTests(AcDream.Runtime.GameRuntime runtime)
    {
        // Implementer: fill from RuntimeRynthGameViewTests (vendor test body + an entity + vitals).
        // Use only calls that already appear in that test file.
        throw new NotImplementedException("write the setup from RuntimeRynthGameViewTests");
    }
}
```

The `PopulateLikeRuntimeRynthGameViewTests` body must be written in this step by copying the setup statements already in `tests/AcDream.App.Tests/Extensions/RuntimeRynthGameViewTests.cs` (vendor test: objects, contents, vendor state; the entity/pose test: an entity with a physics body; the vitals test). Do not leave the `throw`; it is there so the test fails loudly if skipped.

```csharp
// tests/AcDream.Rynth.Host.Tests/ThreadRoutedGameViewTests.cs
using AcDream.Rynth.Host;

namespace AcDream.Rynth.Host.Tests;

[Collection("Rynth")]
public sealed class ThreadRoutedGameViewTests : IDisposable
{
    public void Dispose() => RynthFrameThread.Reset();

    [Fact]
    public void TheFrameThreadReadsLiveAndOthersReadTheSnapshot()
    {
        var live = new FakeGameView { PlayerId = 1 };
        var other = new FakeGameView { PlayerId = 2 };
        var routed = new ThreadRoutedGameView(live, () => other);

        Assert.Equal(1u, routed.PlayerId);           // not latched: live
        RynthFrameThread.Latch();
        Assert.Equal(1u, routed.PlayerId);           // frame thread: live
        uint seen = 0;
        var t = new Thread(() => seen = routed.PlayerId);
        t.Start();
        t.Join();
        Assert.Equal(2u, seen);                      // other thread: snapshot view
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: the Verify command. Expected: build FAIL — `SnapshotRynthGameView`, `ThreadRoutedGameView` missing.

- [ ] **Step 3: Implement `SnapshotRynthGameView`**

Constructor: `SnapshotRynthGameView(RynthWorldSnapshot snapshot, RynthActionQueue queue, IRynthGameView live, Action<string> onVendorQueuedFailure)`. It holds one snapshot (the pump gives it the pass's snapshot via a `Func<RynthWorldSnapshot>` in the routed view; one instance per pass is fine — it is a small object). `live` is captured only inside queued closures, which run on the frame thread, and for the two `ConcurrentDictionary` reads.

Reads — exact rules:

```csharp
public uint PlayerId => _s.PlayerId;
public uint SelectedObjectId => _s.SelectedObjectId;
public string AccountName => _s.AccountName;
public string WorldName => _s.WorldName;
public bool IsPortaling => _s.IsPortaling;
public int BusyCount => _s.BusyCount;
public int CombatMode => _s.CombatMode;
public bool IsAttackInProgress => _s.IsAttackInProgress;
public bool IsCastGestureActive => _s.IsCastGestureActive;
public bool AutoRunActive => _s.AutoRunActive;
public PluginMovementIntent? CurrentMovementIntent => _s.MovementIntent;
public uint OpenContainerId => _s.OpenContainerId;
public ServerReplySnapshot ServerReplies => _s.ServerReplies;
public double ServerTime => _s.ServerTimeAt(Stopwatch.GetTimestamp());
public ulong InventoryRevision => _s.Objects.Revision;
public IReadOnlyList<uint>? LiveObjectIds => _s.Objects.LiveObjectIds;
public RynthVendor? Vendor => _s.Vendor;
public RynthTradeState? Trade => _s.Trade;
public float VitaeMultiplier => _s.Spellbook?.VitaeMultiplier ?? 1f;   // match the live view's no-player value
public IReadOnlyList<RynthEnchantment>? PlayerEnchantments => _s.Spellbook?.Enchantments;
public IReadOnlyList<RynthStatMod>? PlayerStatMods => _s.Spellbook?.StatMods;
public IReadOnlyList<uint>? KnownSpells => _s.Spellbook?.KnownSpells;
public RynthPlayerProgress? PlayerProgress => _s.Character?.Progress;

public int IsSpellKnown(uint objectId, uint spellId) =>
    !IsPlayer(objectId) || _s.Spellbook is not { SpellsAvailable: true } sb ? -1 : sb.KnownSet.Contains(spellId) ? 1 : 0;

public bool TryGetSkill(uint objectId, uint skillId, out int buffed, out int baseLevel, out int training)
{
    if (IsPlayer(objectId) && _s.Character is { } c && c.Skills.TryGetValue(skillId, out var s))
    { (buffed, baseLevel, training) = s; return true; }
    buffed = baseLevel = training = 0; return false;
}
// TryGetAttribute, TryGetVitalBase: same shape over c.Attributes / c.VitalBases.
// TryGetPlayerVitals: c.HasVitals / c.Vitals. TryGetPlayerBaseVitals: c.HasBaseVitals + the three bases.
// TryGetPlayerHeading: _s.HasHeading / _s.Heading. TryGetPendingItemRequest: _s.HasPendingItemRequest + the two ids.

public bool TryGetPlayerPose(out uint cellId, out Vector3 origin, out Quaternion orientation)
{
    if (_s.PlayerId != 0 && _s.Entities.TryGetValue(_s.PlayerId, out RynthEntityPose p))
    { (cellId, origin, orientation) = (p.CellId, p.Origin, p.Orientation); return true; }
    (cellId, origin, orientation) = (0, default, Quaternion.Identity); return false;
}
// TryGetObjectPosition: Entities lookup → CellId/Origin.
// TryGetObjectHeading: Entities lookup → heading from the orientation with the SAME formula
//   the live path yields (MoveToMath.GetHeading(orientation)); the parity test pins it.
// TryGetObjectState: Entities → RawState. TryGetObjectMotionOn: Entities → MotionOn is { } on.
// GetPalettes: Entities → Palettes, else null.

public bool TryGetObject(uint objectId, out RynthObjectInfo info)
{ if (_s.Objects.TryGet(objectId, out var e)) { info = e.Info; return true; } info = null!; return false; }
// TryGetObjectName: e.Name. TryGetInt64Property: e.Int64s.TryGetValue.
// IsOwnedItem / IsOwnedTinkeringTool / IsAttackable: e.<flag>, false when missing.
// GetObjectEnchantments: e.Enchantments, null when missing.
// GetContainerContents: _s.Objects.Contents(id). TryCountContents: _s.Objects.TryCountContents.

public bool TryGetTargetVitals(uint objectId, out RynthVitals vitals) => _live.TryGetTargetVitals(objectId, out vitals);   // ConcurrentDictionary
public bool TryGetHealthRatio(uint objectId, out float ratio) => _live.TryGetHealthRatio(objectId, out ratio);       // ConcurrentDictionary

public bool TryWorldToScreen(float x, float y, float z, out float sx, out float sy)
{
    sx = sy = 0f;
    if (_s.Projection is not { } proj || !TryGetPlayerPose(out uint cell, out _, out _)) return false;
    if (!proj.TryProject(RuntimeRynthGameView.ToNavigationPosition(cell, x, y, z), out Vector2 p)) return false;
    (sx, sy) = (p.X, p.Y); return true;
}
// TryGetViewportSize: _s.HasViewport + width/height. TryGetRadarRect: _s.HasRadar + _s.Radar.

private bool IsPlayer(uint objectId) => /* the live view's IsPlayer rule; read RuntimeRynthGameView.IsPlayer and copy it, using _s.PlayerId */;
```

Copy `RuntimeRynthGameView.IsPlayer`'s exact rule into the snapshot view (read it first; it decides whether objectId 0 means the player).

Actions — every one enqueues a closure over the live view:

```csharp
private bool Start(string label, Func<bool> work) => _queue.TryEnqueue(RynthActionKind.Start, label, work);
private bool Stop(string label, Func<bool> work) => _queue.TryEnqueue(RynthActionKind.Stop, label, work);

public bool SelectObject(uint id) => Start("SelectObject", () => _live.SelectObject(id));
public bool UseObject(uint id) => Start("UseObject", () => _live.UseObject(id));
public bool RequestIdentify(uint id) => Start("RequestIdentify", () => _live.RequestIdentify(id));
public bool QueryHealth(uint id) => Start("QueryHealth", () => _live.QueryHealth(id));
public bool ChangeCombatMode(int mode) => Start("ChangeCombatMode", () => _live.ChangeCombatMode(mode));
public bool MoveItem(uint o, uint c, int slot, int amount) => Start("MoveItem", () => _live.MoveItem(o, c, slot, amount));
public bool MergeStack(uint s, uint t) => Start("MergeStack", () => _live.MergeStack(s, t));
public void ResetBusyCount() => Stop("ResetBusyCount", () => { _live.ResetBusyCount(); return true; });
public bool WieldItem(uint o, uint mask) => Start("WieldItem", () => _live.WieldItem(o, mask));
public bool CastSpell(uint t, uint s) => !_s.IsPortaling && Start("CastSpell", () => _live.CastSpell(t, s));
public bool MeleeAttack(uint t, int h, float p) => Start("MeleeAttack", () => _live.MeleeAttack(t, h, p));
public bool MissileAttack(uint t, int h, float a) => Start("MissileAttack", () => _live.MissileAttack(t, h, a));
public bool CancelAttack() => Stop("CancelAttack", _live.CancelAttack);
public bool UseObjectOn(uint s, uint t) => Start("UseObjectOn", () => _live.UseObjectOn(s, t));
public bool SetAutoRun(bool on) => on ? Start("SetAutoRun", () => _live.SetAutoRun(true)) : Stop("SetAutoRun(false)", () => _live.SetAutoRun(false));
public bool SnapHeading(float deg) => Start("SnapHeading", () => _live.SnapHeading(deg));
public bool SetMovementIntent(in PluginMovementIntent intent) { var copy = intent; return Start("SetMovementIntent", () => _live.SetMovementIntent(copy)); }
public bool ClearMovementIntent() => Stop("ClearMovementIntent", _live.ClearMovementIntent);
public bool StopCompletely() => Stop("StopCompletely", _live.StopCompletely);
public bool BeginJump(float power) => Start("BeginJump", () => _live.BeginJump(power));
public bool MoveItemExternal(uint o, uint t, uint a) => Start("MoveItemExternal", () => _live.MoveItemExternal(o, t, a));
public bool GiveItem(uint o, uint t, uint a) => Start("GiveItem", () => _live.GiveItem(o, t, a));
public bool CloseContainer(uint c) => Stop("CloseContainer", () => _live.CloseContainer(c));
public bool Salvage(uint tool, IReadOnlyList<uint> items) { uint[] copy = [.. items]; return Start("Salvage", () => _live.Salvage(tool, copy)); }
public bool RequestAdvancement(int kind, uint stat, uint amount) => Start("RequestAdvancement", () => _live.RequestAdvancement(kind, stat, amount));
public bool TradeOpen(uint p) => Start("TradeOpen", () => _live.TradeOpen(p));
public bool TradeAdd(uint i) => Start("TradeAdd", () => _live.TradeAdd(i));
public bool TradeAccept() => Start("TradeAccept", _live.TradeAccept);
public bool TradeDecline() => Stop("TradeDecline", _live.TradeDecline);
public bool TradeReset() => Start("TradeReset", _live.TradeReset);
public bool TradeClose() => Stop("TradeClose", _live.TradeClose);
public bool SubmitChatCommand(string text) => Start("SubmitChatCommand", () => _live.SubmitChatCommand(text));
public void WriteChatLine(string text, int type) => Stop("WriteChatLine", () => { _live.WriteChatLine(text, type); return true; });

public bool VendorBuy(IReadOnlyList<(uint ObjectId, int Amount)> items, out string? notice)
{
    notice = null;
    (uint, int)[] copy = [.. items];
    return Start("VendorBuy", () =>
    {
        bool sent = _live.VendorBuy(copy, out string? n);
        if (!sent) _onVendorQueuedFailure(n ?? "the client would not send it");
        return sent;
    });
}
// VendorSell: same shape.

public event Action<RynthVendorOutcome>? VendorTransactionCompleted
{ add => _live.VendorTransactionCompleted += value; remove => _live.VendorTransactionCompleted -= value; }
```

`WriteChatLine` uses the Stop kind so chat lines are never refused while portalling (RynthCore enqueues WriteToChat the same way).

- [ ] **Step 4: Implement `ThreadRoutedGameView`**

```csharp
// src/AcDream.Rynth.Host/ThreadRoutedGameView.cs
namespace AcDream.Rynth.Host;

/// <summary>
/// The view every slot and hook delegate holds: the live view on the frame thread (or before
/// the pump latched it), the current pass's snapshot view anywhere else.
/// </summary>
public sealed partial class ThreadRoutedGameView(IRynthGameView live, Func<IRynthGameView> offFrame) : IRynthGameView
{
    private IRynthGameView V => !RynthFrameThread.Latched || RynthFrameThread.IsCurrent ? live : offFrame();

    public uint PlayerId => V.PlayerId;
    // ... every IRynthGameView member forwards to V, written out one per member.

    public event Action<RynthVendorOutcome>? VendorTransactionCompleted
    {
        add => live.VendorTransactionCompleted += value;
        remove => live.VendorTransactionCompleted -= value;
    }
}
```

Write out every member (≈95) as a one-line forward; `in` parameters forward as `in`. Let the compiler's "does not implement interface member" errors drive completeness.

- [ ] **Step 5: Run to verify they pass**

Run: the Verify command.
Expected: PASS. If `TryGetObjectHeading` differs, make the snapshot compute it exactly as `ProjectWorldObject` does for `HeadingDegrees` (read `RuntimeAutomationSurface.cs` around `ProjectWorldObject`), not with an approximation.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.Rynth.Host/SnapshotRynthGameView.cs src/AcDream.Rynth.Host/ThreadRoutedGameView.cs tests/AcDream.App.Tests/Extensions/SnapshotRynthGameViewParityTests.cs tests/AcDream.Rynth.Host.Tests/ThreadRoutedGameViewTests.cs
git commit -m "feat(rynth): snapshot view and thread-routed view, with read parity test

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 6: Host state both threads touch

**Goal:** Every piece of host and stand-in state reachable from both the pump and the frame thread is safe.

**Files:**
- Modify: `src/AcDream.Rynth.Host/RynthEventBridge.cs` (queue lock, swap under lock)
- Modify: `src/AcDream.Rynth.Host/RynthApiTable.cs` (`_nameBuffer`, `_stringBuffer` → `[ThreadStatic]`; `_interfaceFailures` lock)
- Modify: `src/AcDream.Rynth.Host/RynthApiTable.Movement.cs` (holds changes through the frame)
- Modify: `src/AcDream.Rynth.Host/RynthVendorTrades.cs` (lock; `FailQueued`)
- Modify: `src/AcDream.Rynth.Host/RynthSalvagePanel.cs` (item list lock)
- Modify: `src/AcDream.Rynth.Host/RynthCombatEvents.cs` (`OnLoggedIn` reads through the routed view)
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/SkillsStandIns.cs`, `src/AcDream.Rynth.EngineUi/StandIns/DashboardStandIns.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthCrossThreadStateTests.cs`

**Acceptance Criteria:**
- [ ] `RynthEventBridge`: events enqueued from one thread while another runs `DeliverQueued` in a loop for 10 000 iterations lose none and throw nothing.
- [ ] Two threads calling a string-returning slot concurrently each get their own string (no buffer reuse across threads).
- [ ] Off the frame thread (latched), `SetMotion`/`DoMovement`/`StopMovement`/`SetAutoRun` slots don't touch `RynthMotionHolds` directly: they enqueue a frame action (stop kinds for releases) and return 1 when queued; on the frame thread they behave as today.
- [ ] `RynthVendorTrades.FailQueued(reason)` ends the in-flight request with the reason; `Buy`/`Sell`/`Tick`/`OnCompleted` are under one lock.
- [ ] Stand-ins: `MainThreadGuard.IsOnMainThread()` = `!RynthFrameThread.Latched || RynthFrameThread.IsCurrent`; the `AcMainThreadQueue` stand-in runs work inline on the frame thread and enqueues it on the shared `RynthActionQueue` elsewhere.
- [ ] All existing Rynth.Host.Tests still pass.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/AcDream.Rynth.Host.Tests/RynthCrossThreadStateTests.cs
using AcDream.Rynth.Host;

namespace AcDream.Rynth.Host.Tests;

[Collection("Rynth")]
public sealed class RynthCrossThreadStateTests : IDisposable
{
    public void Dispose() => RynthFrameThread.Reset();

    [Fact]
    public void EventsQueuedWhileDeliveringAreNeitherLostNorFatal()
    {
        int delivered = 0;
        var bridge = new RynthEventBridge([], _ => { });
        const int count = 10_000;
        var producer = new Thread(() =>
        {
            for (int i = 0; i < count; i++)
                bridge.Enqueue(RynthEventBridge.EventKind.ChatWindowText, () => Interlocked.Increment(ref delivered));
        });
        producer.Start();
        while (producer.IsAlive)
            bridge.DeliverQueued();
        producer.Join();
        bridge.DeliverQueued();
        Assert.Equal(count, delivered);
    }

    [Fact]
    public void VendorTradesFailQueuedEndsTheRequest()
    {
        var view = new FakeGameView();   // set up an open vendor with one listing as RynthVendorTradesTests does
        var trades = new RynthVendorTrades(view);
        // Arrange a sent Buy exactly as RynthVendorTradesTests' "sent" case does, then:
        trades.FailQueued("the client would not send it");
        Assert.False(trades.InFlight);
        Assert.Contains("would not send", trades.Status.Message, StringComparison.Ordinal);
    }
}
```

Check `RynthEventBridge.Enqueue`/`EventKind` accessibility (`internal` with InternalsVisibleTo the tests — the existing `RynthEventQueueTests` uses them); copy the vendor setup from `RynthVendorTradesTests`. Add a string-slot test modelled on an existing `RynthApiTableTests` test that reads a name slot: call it from two threads 1 000 times each and assert each thread always gets its own expected string.

- [ ] **Step 2: Run to verify they fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthCrossThreadStateTests"`
Expected: FAIL (lost events or an exception from `List` mutation; `FailQueued` missing).

- [ ] **Step 3: Implement**

`RynthEventBridge`: add `private readonly object _queueGate = new();`. `Enqueue` adds under the lock. `DeliverQueued` swaps each kind's list for an empty one under the lock (keep the swapped-out lists for reuse after running, as today), then runs the swapped entries outside the lock in RynthCore's drain order.

`RynthApiTable`: mark the per-call buffers `[ThreadStatic] private static` (they are instance fields today: move them to a `[ThreadStatic]` static holder keyed per thread; free them on the same thread that allocated them, as the existing free-then-replace code does). Wrap every `_interfaceFailures` access in `lock (_interfaceFailures)`.

`RynthApiTable.Movement.cs`: in `SetAutoRun`, `SetMotion`, `DoMovement`, `StopMovement`, `StopCompletely`, route through a helper on the table:

```csharp
    /// <summary>Runs <paramref name="work"/> now on the frame thread; elsewhere queues it for the frame.</summary>
    internal bool OnFrame(RynthActionKind kind, string label, Func<bool> work) =>
        !RynthFrameThread.Latched || RynthFrameThread.IsCurrent ? work() : Actions!.TryEnqueue(kind, label, work);

    /// <summary>The frame action queue; set when the pump starts.</summary>
    internal RynthActionQueue? Actions { get; set; }
```

e.g. `SetMotion`: `return table.OnFrame(enabled != 0 ? RynthActionKind.Start : RynthActionKind.Stop, "SetMotion", () => table.Holds.SetMotion(motion, enabled != 0)) ? 1 : 0;` and `SetAutoRun`: `table.OnFrame(kind, "SetAutoRun", () => table._view.SetAutoRun(on) && Refresh(table))` where the closure also calls `table.Holds.Refresh()` as today. `TurnToHeading` stays on the view (the routed view queues it).

`RynthVendorTrades`: `private readonly object _gate = new();` around the bodies of `Buy`, `Sell`, `Tick`, `Reset`, `OnCompleted`, `NoteOpened`, and the new:

```csharp
    /// <summary>A queued send the client refused when it ran: ends the request as refused.</summary>
    public void FailQueued(string reason)
    {
        lock (_gate)
        {
            if (_inFlight)
                Finish(ResultAnsweredNoRefresh, "the client would not send it: " + reason);
        }
    }
```

`RynthSalvagePanel`: guard its item list with a lock in every method that reads or writes it.

`RynthCombatEvents.OnLoggedIn`: read the combat mode and selection through the `IRynthGameView` the host was given (the routed view) instead of `_combat.CurrentMode` / `_selection`, so on the pump it reads the snapshot. Pass the view into its constructor from `RynthClientExtension`.

Stand-ins: in `SkillsStandIns.cs` change `MainThreadGuard.IsOnMainThread` to `!RynthFrameThread.Latched || RynthFrameThread.IsCurrent` (add a project reference from EngineUi to Rynth.Host only if it doesn't exist; if that would create a cycle, put `RynthFrameThread` in `AcDream.Rynth.EngineUi` instead and reference it from Host — check `src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj` for the direction). In `DashboardStandIns.cs` the `AcMainThreadQueue` stand-in: run inline when on the frame thread, else `RynthFrameActions.Queue?.TryEnqueue(RynthActionKind.Start, label, () => { work(); return true; })`, with `RynthFrameActions.Queue` a static set by the pump.

- [ ] **Step 4: Run to verify**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests`
Expected: all PASS (existing suite included).

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Rynth.Host src/AcDream.Rynth.EngineUi/StandIns tests/AcDream.Rynth.Host.Tests/RynthCrossThreadStateTests.cs
git commit -m "fix(rynth): make host state shared with the pump thread safe

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 7: The pump and its wiring

**Goal:** `RynthPump` runs plugin passes on its own thread at RynthCore's cadence, the extension wires the routed view, frame work, lifecycle quiesce and kill switch, and a blocking plugin no longer blocks the frame.

**Files:**
- Create: `src/AcDream.Rynth.Host/RynthPump.cs`
- Modify: `src/AcDream.Rynth.Host/RynthHost.cs` (split `Tick` into `PumpPass()` and `FrameTick()`)
- Modify: `src/AcDream.Rynth.EngineUi/RynthEngineHub.cs` (split into `PumpTick()` and `FrameTick()`)
- Modify: `src/AcDream.Rynth.Host/RynthClientExtension.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthPumpTests.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthPumpEndToEndTests.cs`

**Acceptance Criteria:**
- [ ] With the thread scheduler, a plugin whose `Tick` sleeps 500 ms leaves each `RynthPump.FrameTick()` under 2 ms (measured over 50 frame ticks) and that plugin still gets ticked.
- [ ] Passes start no more often than every 16 ms and only after a newer snapshot was published; while a pass runs no snapshot is built.
- [ ] `FrameTick` drains the action queue before building the snapshot.
- [ ] `Quiesce(timeout)` returns once the running pass ends (or logs on timeout) and no pass starts until `Resume()`; `OnLeavingWorld`, `Dispose` and the `rynth` switch quiesce before plugin `OnLogout`/`Shutdown`.
- [ ] 50 failing passes in a row stop the pump (`Stopped` true, no further passes) and write one chat line; the next login creates a fresh pump.
- [ ] `ACDREAM_RYNTH_PUMP=0` / `"rynthPump": false`: no pump thread, `RynthFrameThread` not latched, `Tick` runs today's inline sequence.
- [ ] End-to-end: the real plugin set loads under the manual scheduler, two passes run, and RynthNav's `OnTick` runs on the pump thread (assert via a probe export or `RynthDispatch.Current` set during the pass).

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/AcDream.Rynth.Host.Tests/RynthPumpTests.cs
using System.Diagnostics;
using AcDream.Rynth.Host;

namespace AcDream.Rynth.Host.Tests;

[Collection("Rynth")]
public sealed class RynthPumpTests : IDisposable
{
    public void Dispose() => RynthFrameThread.Reset();

    private sealed class Probe
    {
        public int Passes;
        public int Snapshots;
        public int SleepMs;
        public bool Throw;
    }

    private static RynthPump NewPump(Probe probe, IRynthPumpScheduler scheduler, RynthActionQueue? queue = null, List<string>? log = null) =>
        new(
            pass: () =>
            {
                Interlocked.Increment(ref probe.Passes);
                if (probe.SleepMs > 0) Thread.Sleep(probe.SleepMs);
                if (probe.Throw) throw new InvalidOperationException("boom");
            },
            publish: () => Interlocked.Increment(ref probe.Snapshots),
            actions: queue ?? new RynthActionQueue(_ => { }, () => false),
            log: (log ?? []).Add,
            notify: _ => { },
            scheduler: scheduler);

    [Fact]
    public void ASlowPassDoesNotHoldUpTheFrame()
    {
        RynthFrameThread.Latch();
        var probe = new Probe { SleepMs = 500 };
        using var pump = NewPump(probe, new ThreadPumpScheduler());
        pump.Start();
        var worst = TimeSpan.Zero;
        var until = Stopwatch.StartNew();
        while (until.ElapsedMilliseconds < 1200)
        {
            var t = Stopwatch.StartNew();
            pump.FrameTick();
            worst = t.Elapsed > worst ? t.Elapsed : worst;
            Thread.Sleep(5);
        }
        Assert.True(worst < TimeSpan.FromMilliseconds(2), $"worst frame tick {worst.TotalMilliseconds} ms");
        Assert.InRange(probe.Passes, 2, 4);
    }

    [Fact]
    public void ActionsDrainBeforeTheSnapshotAndPassesWaitForOne()
    {
        RynthFrameThread.Latch();
        var probe = new Probe();
        var queue = new RynthActionQueue(_ => { }, () => false);
        var scheduler = new ManualPumpScheduler();
        using var pump = NewPump(probe, scheduler, queue);
        pump.Start();
        int order = 0, drainedAt = 0;
        queue.TryEnqueue(RynthActionKind.Start, "a", () => { drainedAt = ++order; return true; });
        pump.FrameTick();
        Assert.Equal(1, drainedAt);
        Assert.Equal(1, probe.Snapshots);
        scheduler.RunPending();
        Assert.Equal(1, probe.Passes);
        scheduler.RunPending();             // no new snapshot yet: nothing runs
        Assert.Equal(1, probe.Passes);
    }

    [Fact]
    public void QuiesceWaitsForTheRunningPass()
    {
        RynthFrameThread.Latch();
        var probe = new Probe { SleepMs = 200 };
        using var pump = NewPump(probe, new ThreadPumpScheduler());
        pump.Start();
        pump.FrameTick();
        Thread.Sleep(20);
        var t = Stopwatch.StartNew();
        Assert.True(pump.Quiesce(TimeSpan.FromSeconds(5)));
        Assert.True(t.ElapsedMilliseconds >= 100);
        int passes = probe.Passes;
        for (int i = 0; i < 5; i++) { pump.FrameTick(); Thread.Sleep(20); }
        Assert.Equal(passes, probe.Passes);
        pump.Resume();
    }

    [Fact]
    public void FiftyFailingPassesStopThePump()
    {
        RynthFrameThread.Latch();
        var probe = new Probe { Throw = true };
        var scheduler = new ManualPumpScheduler();
        var log = new List<string>();
        using var pump = NewPump(probe, scheduler, log: log);
        pump.Start();
        for (int i = 0; i < 60; i++) { pump.FrameTick(); scheduler.RunPending(); pump.AdvanceClockForTests(17); }
        Assert.Equal(50, probe.Passes);
        Assert.True(pump.Stopped);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthPumpTests"`
Expected: build FAIL — pump types missing.

- [ ] **Step 3: Implement the pump**

```csharp
// src/AcDream.Rynth.Host/RynthPump.cs
using System.Diagnostics;

namespace AcDream.Rynth.Host;

/// <summary>Where passes run: a real thread in the client, on demand in tests.</summary>
public interface IRynthPumpScheduler : IDisposable
{
    void Start(Action pass);
    /// <summary>A pass may start now (called by the frame thread after a publish).</summary>
    void Wake();
}

/// <summary>RynthCore's pump: one background thread, woken by the frame thread.</summary>
public sealed class ThreadPumpScheduler : IRynthPumpScheduler
{
    private readonly AutoResetEvent _wake = new(false);
    private Thread? _thread;
    private volatile bool _stop;

    public void Start(Action pass)
    {
        _thread = new Thread(() =>
        {
            while (true)
            {
                _wake.WaitOne();
                if (_stop) return;
                pass();
            }
        }) { IsBackground = true, Name = "Rynth pump" };
        _thread.Start();
    }

    public void Wake() => _wake.Set();

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        _thread?.Join(TimeSpan.FromSeconds(5));
        _wake.Dispose();
    }
}

/// <summary>Tests: a woken pass runs on a fresh thread when <see cref="RunPending"/> is called, and is joined.</summary>
public sealed class ManualPumpScheduler : IRynthPumpScheduler
{
    private Action? _pass;
    private bool _woken;

    public void Start(Action pass) => _pass = pass;
    public void Wake() => _woken = true;

    public void RunPending()
    {
        if (!_woken || _pass is null) return;
        _woken = false;
        var t = new Thread(() => _pass()) { Name = "Rynth pump (test)" };
        t.Start();
        t.Join();
    }

    public void Dispose() { }
}

/// <summary>
/// Runs the Rynth plugins' passes off the frame thread (RynthCore's StartNormalPluginPump).
/// The frame thread calls <see cref="FrameTick"/> each frame: it drains the queued actions,
/// and when the pump is idle, 16 ms have passed and it isn't quiesced, publishes a snapshot
/// and wakes a pass.
/// </summary>
public sealed class RynthPump(
    Action pass, Action publish, RynthActionQueue actions, Action<string> log, Action<string> notify,
    IRynthPumpScheduler scheduler) : IDisposable
{
    public const int IntervalMs = 16;
    private const int MaxFailures = 50;

    private readonly ManualResetEventSlim _idle = new(true);
    private long _lastPassStart = long.MinValue / 2;
    private long _clockOffsetMs;
    private int _failures;
    private volatile bool _paused;

    public bool Stopped { get; private set; }

    public void Start() => scheduler.Start(RunPass);

    public void FrameTick()
    {
        actions.Drain();
        if (_paused || Stopped || !_idle.IsSet || NowMs() - _lastPassStart < IntervalMs)
            return;
        try { publish(); }
        catch (Exception error) { log($"snapshot failed: {error.GetType().Name}: {error.Message}"); return; }
        _idle.Reset();
        _lastPassStart = NowMs();
        scheduler.Wake();
    }

    /// <summary>Stops scheduling passes and waits for the running one. False on timeout (logged).</summary>
    public bool Quiesce(TimeSpan timeout)
    {
        _paused = true;
        if (_idle.Wait(timeout))
            return true;
        log($"the pump pass did not end within {timeout.TotalSeconds:0} s; going on without it");
        return false;
    }

    public void Resume() => _paused = false;

    internal void AdvanceClockForTests(long ms) => _clockOffsetMs += ms;

    public void Dispose()
    {
        Quiesce(TimeSpan.FromSeconds(5));
        scheduler.Dispose();
        _idle.Dispose();
    }

    private void RunPass()
    {
        try
        {
            pass();
            _failures = 0;
        }
        catch (Exception error)
        {
            log($"pump pass failed: {error.GetType().Name}: {error.Message}");
            if (++_failures >= MaxFailures)
            {
                Stopped = true;
                notify("[Rynth] plugins stopped: 50 failing ticks in a row (see the log). Relog to restart them.");
            }
        }
        finally
        {
            _idle.Set();
        }
    }

    private long NowMs() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency + _clockOffsetMs;
}
```

(The per-plugin catch stays in `RynthHost.PumpPass`, as today in `plugin.Tick()`; a pass "fails" only when the host-level work around the plugins throws. For the 50-failure test, the probe's pass throws at that level.)

- [ ] **Step 4: Split `RynthHost.Tick`**

```csharp
    /// <summary>The pump's pass: events, then each plugin inside one Nav3D frame (RynthCore's TickAll).</summary>
    public void PumpPass()
    {
        Bridge.DeliverQueued();
        try { _table.Vendor.Tick(); }
        catch (Exception error) { _log($"vendor tick failed: {error.Message}"); }
        try { _table.Nav3D.BeginTick(); }
        catch (Exception error) { _log($"Nav3D begin failed: {error.Message}"); }
        try
        {
            foreach (RynthPlugin plugin in _plugins)
                plugin.Tick();
        }
        finally
        {
            try { _table.Nav3D.CommitTick(); }
            catch (Exception error) { _log($"Nav3D commit failed: {error.Message}"); }
        }
    }

    /// <summary>The frame's part: held keys re-sent (RynthMotionHolds watches the client drop them).</summary>
    public void FrameTick()
    {
        try { _table.Holds.Sync(); }
        catch (Exception error) { _log($"holding movement failed: {error.Message}"); }
    }

    /// <summary>Inline mode (pump off): today's order.</summary>
    public void Tick()
    {
        FrameTick();
        PumpPass();
    }
```

Keep `Tick()`'s existing order exactly: today `Holds.Sync` runs after `Vendor.Tick` and before Nav3D; moving it first is harmless (it only re-sends held keys) — if any existing test pins the order, keep the original order inside `Tick()` and call the two halves' bodies in that order.

- [ ] **Step 5: Split `RynthEngineHub.Tick`**

```csharp
    /// <summary>Pump thread (RynthCore's UiDataHub.Step): refreshes, BeforeDrain, posted actions, polls.</summary>
    internal static int PumpTick()
    {
        if (Parts.Count == 0) return 0;
        foreach (UiSource source in _sourcesSnapshot) source.PromoteDeferredRefresh();
        foreach (Part part in _partsSnapshot) part.BeforeDrain?.Invoke();
        int ran = UiDataHub.Drain(Log);
        PollAll(_sourcesSnapshot, Stopwatch.GetTimestamp());
        return ran;
    }

    /// <summary>Frame thread: the AfterDrain hooks (InventoryModel.MainThreadTick and the like).</summary>
    internal static void FrameTick()
    {
        foreach (Part part in _partsSnapshot) part.AfterDrain?.Invoke();
    }

    /// <summary>Inline mode: today's order.</summary>
    internal static int Tick()
    {
        // keep the existing body unchanged
    }
```

`Parts` is mutated by `Join`/`Leave` (frame thread at start/stop): read `_partsSnapshot`/`_sourcesSnapshot` (already snapshot arrays) with `Volatile.Read` in `PumpTick`, and make `Rebuild` publish them with `Volatile.Write`.

- [ ] **Step 6: Wire `RynthClientExtension`**

In `Start` (the method shown at `RynthClientExtension.cs:99-150`):

```csharp
        var live = new RuntimeRynthGameView(context.Runtime, context.SubmitChatCommand, context.Automation, context.Screen);
        _pumpEnabled = RynthPumpSwitch.IsEnabled(context);   // env ACDREAM_RYNTH_PUMP=0 or session-config "rynthPump": false → false
        _actions = new RynthActionQueue(context.Log, () => _currentSnapshot is { } s && (s.IsPortaling || s.Dead));
        var routed = new ThreadRoutedGameView(live, () => _currentView ?? (IRynthGameView)live);
        _view = routed;
        // pass `routed` everywhere `view` was passed: RynthHost, the four hookups, RynthCombatEvents, OnRynthCommand, /rc
```

After the host and hookups are built:

```csharp
        if (_pumpEnabled)
        {
            _builder = new RynthSnapshotBuilder(live, context.Runtime, context.Screen)
            {
                RynthVendorGeneration = () => _host.Vendor.Generation,
            };
            _host.Table.Actions = _actions;          // expose RynthApiTable via an internal Table property on RynthHost
            RynthFrameActions.Queue = _actions;
            RynthFrameThread.Latch();
            _pump = new RynthPump(
                pass: () => { _host!.PumpPass(); RynthEngineHub.PumpTick(); },
                publish: () =>
                {
                    RynthWorldSnapshot s = _builder.Build();
                    _currentSnapshot = s;
                    _currentView = new SnapshotRynthGameView(s, _actions, live, reason => _host!.Vendor.FailQueued(reason));
                },
                actions: _actions,
                log: context.Log,
                notify: line => live.WriteChatLine(line, 0),
                scheduler: new ThreadPumpScheduler());
            _pump.Start();
        }
```

`_currentSnapshot` and `_currentView` are fields written with `Volatile.Write` in `publish` and read with `Volatile.Read` in the lambdas.

Replace `Tick`:

```csharp
    public void Tick(double deltaSeconds)
    {
        if (_pump is { } pump)
        {
            _host?.FrameTick();
            pump.FrameTick();
            RynthEngineHub.FrameTick();
        }
        else
        {
            _host?.Tick();
        }
        // nav hint, TickChat, inventory hookup: unchanged
        if (_pump is null)
            Guard("Rynth", () => RynthEngineHub.Tick());
    }
```

Lifecycle: in the leaving-world path (where `RynthEventBridge.OnLeavingWorld` triggers `plugin.OnLogout()` — find its subscription in `RynthClientExtension` or `RynthHost`), call `_pump?.Quiesce(TimeSpan.FromSeconds(5))` first, `_actions.Clear()`, run the logout, then `_pump?.Resume()`. In `Dispose`, dispose `_pump` (quiesces) before `_host.Dispose()`, then `RynthFrameThread.Reset()`, `RynthFrameActions.Queue = null`, `_builder?.Dispose()`. The `rynth` off switch goes through the same Dispose path.

`RynthPumpSwitch.IsEnabled(context)`: `Environment.GetEnvironmentVariable("ACDREAM_RYNTH_PUMP") != "0"` and the session-config flag; find how `"rynth": false` is read (`grep -rn "\"rynth\"" src`, memory note: rynth launch switch) and read `"rynthPump"` the same way.

- [ ] **Step 7: End-to-end test with the real plugins**

```csharp
// tests/AcDream.Rynth.Host.Tests/RynthPumpEndToEndTests.cs
using AcDream.Rynth.Host;

namespace AcDream.Rynth.Host.Tests;

[Collection("Rynth")]
public sealed class RynthPumpEndToEndTests : IDisposable
{
    public void Dispose() => RynthFrameThread.Reset();

    [Fact]
    public void TheRealPluginsTickOnThePumpThread()
    {
        // Build a RynthHost with the real exports the way RynthNavPluginTests/RynthAiPluginTests do
        // (their setup: FakeGameView, RynthConfigSandbox, the exports types), but give it a
        // ThreadRoutedGameView(live: fake, offFrame: () => fake).
        // Then: RynthFrameThread.Latch(); a RynthPump with ManualPumpScheduler whose pass records
        // Environment.CurrentManagedThreadId and calls host.PumpPass(); FrameTick + RunPending twice.
        // Assert: two passes ran, on a thread id different from the test thread, and no plugin logged
        // a failure (collect the host log and assert no line contains "failed").
    }
}
```

Write the body from the setup of `tests/AcDream.Rynth.Host.Tests/RynthNavPluginTests.cs` and `RynthAiPluginTests.cs` (copy their host construction verbatim); keep the three asserts above.

- [ ] **Step 8: Run to verify**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests && ~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~Rynth"`
Expected: all PASS.

- [ ] **Step 9: Commit**

```bash
git add src/AcDream.Rynth.Host src/AcDream.Rynth.EngineUi tests/AcDream.Rynth.Host.Tests
git commit -m "feat(rynth): plugins tick on their own pump thread (RynthCore's 16 ms pump)

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 8: Gate, build and live check

**Goal:** Full suites green, Release build deployed to the dev install, and the user's live check of the original failure.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:**
- Modify: `docs/superpowers/plans/2026-10-21-rynth-pump-thread.md` (record results)

**Acceptance Criteria:**
- [ ] `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → 0 failed; `tests/AcDream.App.Tests --filter Rynth` → 0 failed; full solution test run shows no new failures against the 70-failure Mac baseline.
- [ ] Release build of the client succeeds and is deployed to the dev root (`~/OpenAC-dev`, the usual dev build path).
- [ ] Live (user, local ACE 10.10.20.20 only; ask before launching): the Samurai Hausu goto runs with no stutter while RynthNav re-plans — dotnet-trace frame p99 under 20 ms during re-plans (was 450-520 ms stalls).
- [ ] Live: RynthAi patrol with combat and looting; vendor buy/sell; dashboard, nav panel, arrow, maps; chat and `/rnav`; logout + login; `ACDREAM_RYNTH_PUMP=0` restores old behaviour.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → `Failed: 0`; live trace captured with `dotnet-trace collect -p <pid> --profile dotnet-sampled-thread-time --duration 00:00:00:45` and analysed for frame p99 and Replan durations.

**Steps:**

- [ ] **Step 1:** Run the suites; record counts in this plan.
- [ ] **Step 2:** Release build + dev deploy (same commands as previous Rynth sub-projects' Task 5/6 gates; see `docs/superpowers/plans/2026-10-20-rynth-nav-feature.md` gate task).
- [ ] **Step 3:** Ask the user when they are available for the live check (never launch a client unasked); run the checklist with them; capture the trace during a re-planning goto.
- [ ] **Step 4:** Record results here; commit.

```bash
git add docs/superpowers/plans/2026-10-21-rynth-pump-thread.md
git commit -m "docs(plan): rynth pump thread gate results

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```
