# House Purchase Window Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Using a slumlord opens the retail Buy/Maintenance window (LayoutDesc 0x2100000E). The player drags payment items onto it and presses Buy or Maintenance to send 0x021C BuyHouse or 0x0221 RentHouse.

**Architecture:** Core.Net parses 0x021D HouseProfile and builds the two requests. `RuntimeHousePurchaseState` (AcDream.Runtime) holds the profile and the staged items per tab, and closes on range. `HousePurchaseUiController` (AcDream.App) draws the imported retail layout and stages drops. Button presses go over the command bus to `WorldSession`, like the trade window's commands.

**Tech Stack:** C# / .NET 10, xUnit, DatReaderWriter (layout import), OpenAC retained UI.

**Spec:** `docs/superpowers/specs/2026-10-23-house-purchase-window-design.md` (branch `docs/house-purchase-spec`). Read it before starting.

## Global Constraints

- Work on branch `feat/house-purchase-window`, cut from `origin/upstream-main`, in a worktree at `~/code/OpenAC/OpenAC-house-purchase`. Do not touch `~/code/OpenAC/OpenAC` (another session uses it).
  ```bash
  cd ~/code/OpenAC/OpenAC && git fetch origin && git worktree add -b feat/house-purchase-window ../OpenAC-house-purchase origin/upstream-main
  ```
- Line numbers in this plan come from fork `main`. Four files differ from `upstream-main` (RetailUiRuntime.cs, LiveSessionEventRouter.cs, InteractionRetainedUiComposition.cs, GameRuntime.cs), so find each edit by the quoted anchor text, not by line number.
- Environment for every `dotnet` command: `export PATH=~/.dotnet:$PATH DOTNET_ROOT=~/.dotnet`.
- Run tests with `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-house-purchase` and narrow them with `MAC_TEST_FILTER='FullyQualifiedName~X'`. Never add a second `--filter`. On `upstream-main`, 11 Mac test failures already exist; upstream PR 241 fixes them. A task is green when its own filter passes. The full gate runs in Task 9 on fork `main`.
- Wire formats must match ACE exactly (the spec's Wire section): HouseProfile field order, `u32 count` + `u32` guids for the request lists.
- Element IDs come from the spec's UI table, and the dat supplies the captions. Do not hard-code "Buy" or "Maintenance" in the controller.
- Placeholder wording lives only in `HousePurchaseText` (the user will check it against a retail client).
- Commit messages end with `Claude-Session: https://claude.ai/code/session_01WfC1ZE7JDErNAj3YviNNat`. The repo's commit style is `feat(housing): …` / `test(housing): …` / `refactor(…): …`.
- Never launch a game client and never contact a server without the user saying they are available. The live check uses local ACE 10.10.20.20 only, never Dreamweave.

**User decisions (already made):**
- "option 1": use the retail dat layout 0x2100000E, not a modern or chat-only window.
- "option 2 - this is how retail worked.": payment is by dragging items onto the strip, not gathered automatically.
- "A": a separate `RuntimeHousePurchaseState` plus a retail controller.
- Placeholder captions are OK for now ("use those, but a note of them"); the user will check retail wording later.

---

## File map

| File | Change |
|---|---|
| `src/AcDream.Core.Net/Messages/GameEvents.cs` | `HouseProfile` record + `ParseHouseProfile` |
| `src/AcDream.Core.Net/GameEventWiring.cs` | `onHouseProfile` callback |
| `src/AcDream.Core.Net/Messages/ClientCommandRequests.cs` | `BuyHouseOpcode`, `RentHouseOpcode`, `BuildBuyHouse`, `BuildRentHouse` |
| `src/AcDream.Core.Net/WorldSession.cs` | `SendBuyHouse`, `SendRentHouse` |
| `src/AcDream.App/Net/LiveSessionCommandRouter.cs` | 2 runtime commands, 2 optional bindings, 2 registrations |
| `src/AcDream.App/Net/LiveSessionCommandBindingFactory.cs` | bind them to the session |
| `src/AcDream.Runtime/Gameplay/RuntimeHousePurchaseState.cs` | **new**: state, view, tab and result enums |
| `src/AcDream.Runtime/GameRuntime.cs` | create and own the state, view property, range call |
| `src/AcDream.Runtime/RuntimeGenerationReset.cs` | close it in the House stage |
| `src/AcDream.Runtime/Session/LiveSessionEventRouter.cs` | `LiveSocialSessionBindings.HousePurchase` + route `onHouseProfile` |
| `src/AcDream.App/Net/LiveSessionRuntimeFactory.cs`, `src/AcDream.Headless/Hosting/HeadlessSessionHost.cs` | pass `HousePurchase` |
| `src/AcDream.Runtime/Gameplay/RuntimeUseRange.cs` | **new**: shared range check moved out of vendor |
| `src/AcDream.Runtime/Gameplay/RuntimeVendorRangeQuery.cs` | call `RuntimeUseRange` |
| `src/AcDream.Runtime/Gameplay/RuntimeHousePurchaseRangeQuery.cs` | **new** |
| `src/AcDream.Runtime/Gameplay/HousePaymentNames.cs` | **new**: plural-name rule moved out of `RuntimeHouseState` |
| `src/AcDream.App/UI/Layout/HousePurchaseText.cs` | **new**: all placeholder wording |
| `src/AcDream.App/UI/Layout/HousePurchaseUiController.cs` | **new** |
| `src/AcDream.App/UI/WindowNames.cs`, `src/AcDream.App/UI/RetailUiRuntime.cs`, `src/AcDream.App/Composition/InteractionRetainedUiComposition.cs` | mount and wire the window |
| tests | one test file per unit, listed in each task |

---

### Task 1: Parse 0x021D HouseProfile

**Goal:** `GameEvents.ParseHouseProfile` reads ACE's HouseProfile, and `GameEventWiring.WireAll` delivers it to an `onHouseProfile` callback.

**Files:**
- Modify: `src/AcDream.Core.Net/Messages/GameEvents.cs` (after `ParseHouseData`)
- Modify: `src/AcDream.Core.Net/GameEventWiring.cs` (parameter after `onHouseUpdateRentPayment`; registration after the `UpdateRentPayment` block)
- Test: `tests/AcDream.Core.Net.Tests/Messages/HouseEventsTests.cs`, `tests/AcDream.Core.Net.Tests/GameEventWiringTests.cs`

**Acceptance Criteria:**
- [ ] An unowned profile with empty lists parses with `OwnerGuid == 0`, `OwnerName == ""`, empty `Buy`/`Rent`.
- [ ] An owned profile parses every header field and both payment lists in order.
- [ ] Input truncated in the header or mid-payment returns null.
- [ ] Dispatching a 0x021D envelope through `WireAll` reaches `onHouseProfile`.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~HouseEventsTests|FullyQualifiedName~GameEventWiringTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-house-purchase` → all pass

**Steps:**

- [ ] **Step 1: Write the failing parser tests** (append to `HouseEventsTests`)

```csharp
    private static AceWireWriter ProfileHeader(
        AceWireWriter w,
        uint slumlord = 0x70000100u,
        uint owner = 0u,
        int minLevel = -1,
        int maxLevel = -1,
        int minRank = -1,
        int maxRank = -1,
        bool maintenanceFree = false,
        uint type = 1u,
        string ownerName = "") =>
        w.Write(slumlord)
            .Write(0x12340001u)   // dwelling id
            .Write(owner)
            .Write(1u)            // bitmask: Active
            .Write(minLevel)
            .Write(maxLevel)
            .Write(minRank)
            .Write(maxRank)
            .Write(maintenanceFree ? 1u : 0u)
            .Write(type)
            .WriteString16L(ownerName);

    [Fact]
    public void ParseHouseProfile_Unowned_EmptyLists()
    {
        byte[] wire = ProfileHeader(new AceWireWriter())
            .Write(0).Write(0)
            .ToArray();

        GameEvents.HouseProfile? parsed = GameEvents.ParseHouseProfile(wire);

        Assert.NotNull(parsed);
        GameEvents.HouseProfile p = parsed.Value;
        Assert.Equal(0x70000100u, p.SlumlordGuid);
        Assert.Equal(0x12340001u, p.DwellingId);
        Assert.Equal(0u, p.OwnerGuid);
        Assert.Equal(1u, p.Bitmask);
        Assert.Equal(-1, p.MinLevel);
        Assert.Equal(-1, p.MaxAllegianceRank);
        Assert.False(p.MaintenanceFree);
        Assert.Equal(1u, p.Type);
        Assert.Equal("", p.OwnerName);
        Assert.Empty(p.Buy);
        Assert.Empty(p.Rent);
    }

    [Fact]
    public void ParseHouseProfile_Owned_ReadsHeaderAndBothLists()
    {
        byte[] wire = ProfileHeader(
                new AceWireWriter(),
                owner: 0x50000042u,
                minLevel: 20,
                maxLevel: 99,
                minRank: 6,
                maxRank: 10,
                maintenanceFree: true,
                type: 3u,
                ownerName: "Sammy")
            .Write(2)
            .Write(50_000).Write(50_000).Write(273u)
                .WriteString16L("Pyreal").WriteString16L("Pyreals")
            .Write(1).Write(1).Write(9000u)
                .WriteString16L("Bone Totem").WriteString16L("")
            .Write(1)
            .Write(5_000).Write(1_250).Write(273u)
                .WriteString16L("Pyreal").WriteString16L("Pyreals")
            .ToArray();

        GameEvents.HouseProfile p = GameEvents.ParseHouseProfile(wire)!.Value;

        Assert.Equal(0x50000042u, p.OwnerGuid);
        Assert.Equal(20, p.MinLevel);
        Assert.Equal(99, p.MaxLevel);
        Assert.Equal(6, p.MinAllegianceRank);
        Assert.Equal(10, p.MaxAllegianceRank);
        Assert.True(p.MaintenanceFree);
        Assert.Equal(3u, p.Type);
        Assert.Equal("Sammy", p.OwnerName);
        Assert.Equal(2, p.Buy.Count);
        Assert.Equal(new GameEvents.HousePayment(1, 1, 9000u, "Bone Totem", ""), p.Buy[1]);
        GameEvents.HousePayment rent = Assert.Single(p.Rent);
        Assert.Equal(5_000, rent.Num);
        Assert.Equal(1_250, rent.Paid);
    }

    [Fact]
    public void ParseHouseProfile_TruncatedHeader_ReturnsNull()
    {
        byte[] wire = new AceWireWriter().Write(0x70000100u).Write(1u).ToArray();

        Assert.Null(GameEvents.ParseHouseProfile(wire));
    }

    [Fact]
    public void ParseHouseProfile_TruncatedPayment_ReturnsNull()
    {
        byte[] wire = ProfileHeader(new AceWireWriter())
            .Write(1)
            .Write(50_000).Write(0)   // the wcid and names are missing
            .ToArray();

        Assert.Null(GameEvents.ParseHouseProfile(wire));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `MAC_TEST_FILTER='FullyQualifiedName~HouseEventsTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-house-purchase`
Expected: build error, `GameEvents` has no `HouseProfile` / `ParseHouseProfile`.

- [ ] **Step 3: Add the record and parser** in `GameEvents.cs`, directly after `ParseHouseData` (it reuses `ReadHousePaymentList` and `ReadString16L`; both throw or return null on truncation)

```csharp
    /// <summary>
    /// 0x021D HouseProfile: what a slumlord asks for to buy its house or pay
    /// its maintenance (ACE GameEventHouseProfile / HouseProfile.Write).
    /// </summary>
    public readonly record struct HouseProfile(
        uint SlumlordGuid,
        uint DwellingId,
        uint OwnerGuid,
        uint Bitmask,
        int MinLevel,
        int MaxLevel,
        int MinAllegianceRank,
        int MaxAllegianceRank,
        bool MaintenanceFree,
        uint Type,
        string OwnerName,
        IReadOnlyList<HousePayment> Buy,
        IReadOnlyList<HousePayment> Rent);

    public static HouseProfile? ParseHouseProfile(ReadOnlySpan<byte> payload)
    {
        try
        {
            int pos = 0;
            if (payload.Length < 40) return null;
            uint slumlordGuid = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(pos)); pos += 4;
            uint dwellingId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(pos)); pos += 4;
            uint ownerGuid = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(pos)); pos += 4;
            uint bitmask = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(pos)); pos += 4;
            int minLevel = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(pos)); pos += 4;
            int maxLevel = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(pos)); pos += 4;
            int minRank = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(pos)); pos += 4;
            int maxRank = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(pos)); pos += 4;
            bool maintenanceFree = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(pos)) != 0; pos += 4;
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(pos)); pos += 4;
            string ownerName = ReadString16L(payload, ref pos);

            List<HousePayment>? buy = ReadHousePaymentList(payload, ref pos);
            if (buy is null) return null;
            List<HousePayment>? rent = ReadHousePaymentList(payload, ref pos);
            if (rent is null) return null;

            return new HouseProfile(
                slumlordGuid, dwellingId, ownerGuid, bitmask,
                minLevel, maxLevel, minRank, maxRank,
                maintenanceFree, type, ownerName, buy, rent);
        }
        catch { return null; }
    }
```

- [ ] **Step 4: Run the parser tests** with the same command as Step 2. Expected: PASS.

- [ ] **Step 5: Write the failing wiring test** (append to `GameEventWiringTests`, next to `WireAll_HouseFamily_ReachesTheirCallbacks`)

```csharp
    [Fact]
    public void WireAll_HouseProfile_ReachesItsCallback()
    {
        var dispatcher = new GameEventDispatcher();
        GameEvents.HouseProfile? profile = null;
        GameEventWiring.WireAll(
            dispatcher, new ClientObjectTable(), new CombatState(), new Spellbook(), new ChatLog(),
            onHouseProfile: p => profile = p);

        byte[] wire = new AceWireWriter()
            .Write(0x70000100u).Write(0x12340001u).Write(0u).Write(1u)
            .Write(-1).Write(-1).Write(-1).Write(-1)
            .Write(0u).Write(1u).WriteString16L("")
            .Write(0).Write(0)
            .ToArray();
        dispatcher.Dispatch(GameEventEnvelope.TryParse(
            WrapEnvelope(GameEventType.HouseProfile, wire))!.Value);

        Assert.Equal(0x70000100u, profile!.Value.SlumlordGuid);
        Assert.Equal(0, dispatcher.GetUnhandledCount(GameEventType.HouseProfile));
    }
```

- [ ] **Step 6: Run it to see it fail.** Run: `MAC_TEST_FILTER='FullyQualifiedName~GameEventWiringTests.WireAll_HouseProfile' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-house-purchase`. Expected: build error, no parameter `onHouseProfile`.

- [ ] **Step 7: Add the callback** in `GameEventWiring.WireAll`. Add the parameter after `Action<IReadOnlyList<GameEvents.HousePayment>>? onHouseUpdateRentPayment = null,`:

```csharp
        Action<GameEvents.HouseProfile>? onHouseProfile = null,
```

Then add the registration after the `if (onHouseUpdateRentPayment is not null) { … }` block:

```csharp
        if (onHouseProfile is not null)
        {
            registrar.Register(GameEventType.HouseProfile, e =>
            {
                var p = GameEvents.ParseHouseProfile(e.Payload.Span);
                if (p is not null) onHouseProfile(p.Value);
            });
        }
```

- [ ] **Step 8: Run both test classes** (the Verify command). Expected: PASS.

- [ ] **Step 9: Commit**

```bash
cd ~/code/OpenAC/OpenAC-house-purchase
git add src/AcDream.Core.Net/Messages/GameEvents.cs src/AcDream.Core.Net/GameEventWiring.cs tests/AcDream.Core.Net.Tests/Messages/HouseEventsTests.cs tests/AcDream.Core.Net.Tests/GameEventWiringTests.cs
git commit -m "feat(housing): parse the 0x021D house profile

Claude-Session: https://claude.ai/code/session_01WfC1ZE7JDErNAj3YviNNat"
```

---

### Task 2: BuyHouse / RentHouse requests over the command bus

**Goal:** `BuyHouseRuntimeCmd` and `RentHouseRuntimeCmd` published on the command bus send 0x021C / 0x0221 with the slumlord guid and the item guids.

**Files:**
- Modify: `src/AcDream.Core.Net/Messages/ClientCommandRequests.cs`
- Modify: `src/AcDream.Core.Net/WorldSession.cs` (next to `SendAbandonHouse`)
- Modify: `src/AcDream.App/Net/LiveSessionCommandRouter.cs`
- Modify: `src/AcDream.App/Net/LiveSessionCommandBindingFactory.cs`
- Test: `tests/AcDream.Core.Net.Tests/Messages/HouseEventsTests.cs`, `tests/AcDream.App.Tests/Net/LiveSessionCommandRouterTests.cs`

**Acceptance Criteria:**
- [ ] `BuildBuyHouse(7, 0x70000100, [0x60000001, 0x60000002])` is 28 bytes: envelope, 7, 0x021C, slumlord, 2, both guids.
- [ ] `BuildRentHouse` with an empty list is 20 bytes ending in count 0, opcode 0x0221.
- [ ] Publishing `BuyHouseRuntimeCmd` / `RentHouseRuntimeCmd` on an active router calls the `BuyHouse` / `RentHouse` binding with the same values.
- [ ] Existing `LiveSessionCommandRouterTests` still compile and pass (the new bindings are optional).

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~HouseEventsTests|FullyQualifiedName~LiveSessionCommandRouterTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-house-purchase` → all pass

**Steps:**

- [ ] **Step 1: Write the failing builder tests** (append to `HouseEventsTests`)

```csharp
    [Fact]
    public void BuildBuyHouse_WritesSlumlordCountAndGuids()
    {
        byte[] body = ClientCommandRequests.BuildBuyHouse(
            7u, 0x70000100u, [0x60000001u, 0x60000002u]);

        Assert.Equal(28, body.Length);
        Assert.Equal(7u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4)));
        Assert.Equal(0x021Cu, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8)));
        Assert.Equal(0x70000100u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(12)));
        Assert.Equal(2u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(16)));
        Assert.Equal(0x60000001u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(20)));
        Assert.Equal(0x60000002u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(24)));
    }

    [Fact]
    public void BuildRentHouse_EmptyList_WritesZeroCount()
    {
        byte[] body = ClientCommandRequests.BuildRentHouse(3u, 0x70000100u, []);

        Assert.Equal(20, body.Length);
        Assert.Equal(0x0221u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8)));
        Assert.Equal(0x70000100u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(12)));
        Assert.Equal(0u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(16)));
    }
```

- [ ] **Step 2: Run them to see them fail.** Run: `MAC_TEST_FILTER='FullyQualifiedName~HouseEventsTests.Build' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-house-purchase`. Expected: build error.

- [ ] **Step 3: Add the builders.** In `ClientCommandRequests`, add the opcodes after `public const uint HouseQueryOpcode = 0x021Eu;`:

```csharp
    public const uint BuyHouseOpcode = 0x021Cu;
    public const uint RentHouseOpcode = 0x0221u;
```

Add the builders after `BuildAbandonHouse`:

```csharp
    // Buy at a slumlord — GameActionHouseBuyHouse.Handle: ReadUInt32()
    // slumlord, ReadListUInt32() the items offered (u32 count, then guids).
    public static byte[] BuildBuyHouse(
        uint sequence, uint slumlordGuid, IReadOnlyList<uint> itemGuids) =>
        BuildGuidList(sequence, BuyHouseOpcode, slumlordGuid, itemGuids);

    // Pay maintenance at a slumlord — GameActionHouseRentHouse.Handle: the
    // same body as BuyHouse.
    public static byte[] BuildRentHouse(
        uint sequence, uint slumlordGuid, IReadOnlyList<uint> itemGuids) =>
        BuildGuidList(sequence, RentHouseOpcode, slumlordGuid, itemGuids);
```

Add the helper next to the other private builders (after `BuildUInt32`):

```csharp
    private static byte[] BuildGuidList(
        uint sequence, uint opcode, uint target, IReadOnlyList<uint> guids)
    {
        ArgumentNullException.ThrowIfNull(guids);
        byte[] body = CreateBody(sequence, opcode, 8 + 4 * guids.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(12), target);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(16), (uint)guids.Count);
        for (int i = 0; i < guids.Count; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(20 + 4 * i), guids[i]);
        return body;
    }
```

- [ ] **Step 4: Run the builder tests** (Step 2 command). Expected: PASS.

- [ ] **Step 5: Add the session sends** in `WorldSession.cs` after `SendAbandonHouse()`:

```csharp
    public void SendBuyHouse(uint slumlordGuid, IReadOnlyList<uint> itemGuids)
    {
        uint seq = NextGameActionSequence();
        SendGameAction(ClientCommandRequests.BuildBuyHouse(seq, slumlordGuid, itemGuids));
    }

    public void SendRentHouse(uint slumlordGuid, IReadOnlyList<uint> itemGuids)
    {
        uint seq = NextGameActionSequence();
        SendGameAction(ClientCommandRequests.BuildRentHouse(seq, slumlordGuid, itemGuids));
    }
```

- [ ] **Step 6: Write the failing router test.** In `LiveSessionCommandRouterTests`, give `NewRouter` two more optional parameters after `Action<uint>? sendSetTitle = null`:

```csharp
        Action<uint>? sendSetTitle = null,
        Action<uint, IReadOnlyList<uint>>? buyHouse = null,
        Action<uint, IReadOnlyList<uint>>? rentHouse = null) => new(
```

Pass them at the end of the `LiveSessionCommandBindings` constructor call:

```csharp
            Log: log,
            BuyHouse: buyHouse,
            RentHouse: rentHouse));
```

Then add the test:

```csharp
    [Fact]
    public void HousePurchaseCommands_ReachTheSessionBindings()
    {
        var calls = new List<string>();
        var router = NewRouter(
            buyHouse: (slumlord, items) =>
                calls.Add($"buy:{slumlord:X8}:{string.Join(",", items)}"),
            rentHouse: (slumlord, items) =>
                calls.Add($"rent:{slumlord:X8}:{string.Join(",", items)}"));
        router.Activate();

        router.Publish(new BuyHouseRuntimeCmd(0x70000100u, [1u, 2u]));
        router.Publish(new RentHouseRuntimeCmd(0x70000100u, [3u]));

        Assert.Equal(["buy:70000100:1,2", "rent:70000100:3"], calls);
    }
```

- [ ] **Step 7: Run it to see it fail.** Run: `MAC_TEST_FILTER='FullyQualifiedName~LiveSessionCommandRouterTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-house-purchase`. Expected: build error.

- [ ] **Step 8: Add the commands, bindings and registrations** in `LiveSessionCommandRouter.cs`.
  - Append two optional members at the end of `LiveSessionCommandBindings`, after `Action<string>? Log = null`:

```csharp
    Action<string>? Log = null,
    Action<uint, IReadOnlyList<uint>>? BuyHouse = null,
    Action<uint, IReadOnlyList<uint>>? RentHouse = null);
```

  - Add the command records after `internal readonly record struct DeclineTradeRuntimeCmd;`:

```csharp
internal readonly record struct BuyHouseRuntimeCmd(
    uint SlumlordGuid,
    IReadOnlyList<uint> ItemGuids);
internal readonly record struct RentHouseRuntimeCmd(
    uint SlumlordGuid,
    IReadOnlyList<uint> ItemGuids);
```

  - Register them after the `commands.Register<ResetTradeRuntimeCmd>(…);` registration:

```csharp
        commands.Register<BuyHouseRuntimeCmd>(
            command => SendIfActive(() =>
                bindings.BuyHouse?.Invoke(command.SlumlordGuid, command.ItemGuids)));
        commands.Register<RentHouseRuntimeCmd>(
            command => SendIfActive(() =>
                bindings.RentHouse?.Invoke(command.SlumlordGuid, command.ItemGuids)));
```

- [ ] **Step 9: Bind them to the session** in `LiveSessionCommandBindingFactory.cs`. In the `new LiveSessionCommandBindings(…)` call, after the `Log:` argument (or as the last arguments if `Log` is not passed), add:

```csharp
            BuyHouse: session.SendBuyHouse,
            RentHouse: session.SendRentHouse,
```

Keep the call's trailing `)` / `);` as it is.

- [ ] **Step 10: Run the Verify command.** Expected: PASS.

- [ ] **Step 11: Commit**

```bash
git add src/AcDream.Core.Net/Messages/ClientCommandRequests.cs src/AcDream.Core.Net/WorldSession.cs src/AcDream.App/Net/LiveSessionCommandRouter.cs src/AcDream.App/Net/LiveSessionCommandBindingFactory.cs tests/AcDream.Core.Net.Tests/Messages/HouseEventsTests.cs tests/AcDream.App.Tests/Net/LiveSessionCommandRouterTests.cs
git commit -m "feat(housing): send BuyHouse and RentHouse from the command bus

Claude-Session: https://claude.ai/code/session_01WfC1ZE7JDErNAj3YviNNat"
```

---

### Task 3: RuntimeHousePurchaseState

**Goal:** A runtime state holds the open profile and the items staged per tab, and enforces the spec's staging rules.

**Files:**
- Create: `src/AcDream.Runtime/Gameplay/RuntimeHousePurchaseState.cs`
- Test: `tests/AcDream.Runtime.Tests/Gameplay/RuntimeHousePurchaseStateTests.cs`

**Acceptance Criteria:**
- [ ] `ApplyProfile` opens the state and raises `Revision`. A different slumlord clears both lists; the same slumlord keeps staged items still held.
- [ ] `Stage` returns each `HouseStageResult` refusal as the spec lists it: `NotOpen`, `AlreadyOwned`, `MaintenanceFree`, `NotInInventory` (not held, or wielded), `AlreadyStaged`, `NotRequired`, `AlreadyCovered`.
- [ ] A trade note (`ItemType.PromissoryNote`) stages against the wcid 273 line and counts its `Value`. Other items count their `StackSize`.
- [ ] `GetStaged` and `GetStagedAmount` leave out items no longer in inventory. `Revalidate()` drops them and raises `Revision` only when something changed.
- [ ] `Unstage` and `Close` work. `Close` clears the profile and both lists.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~RuntimeHousePurchaseStateTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-house-purchase` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
using AcDream.Core.Items;
using AcDream.Core.Net.Messages;
using AcDream.Runtime.Gameplay;

namespace AcDream.Runtime.Tests.Gameplay;

public sealed class RuntimeHousePurchaseStateTests
{
    private const uint Player = 0x50000001u;
    private const uint Pack = 0x60000900u;
    private const uint Ground = 0x70000999u;
    private const uint Slumlord = 0x70000100u;
    private const uint OtherSlumlord = 0x70000200u;
    private const uint Pyreal = 273u;
    private const uint Totem = 9000u;

    private readonly ClientObjectTable _objects = new();
    private readonly RuntimeHousePurchaseState _state;

    public RuntimeHousePurchaseStateTests()
    {
        _objects.AddOrUpdate(new ClientObject { ObjectId = Player, Type = ItemType.Creature });
        _objects.AddOrUpdate(new ClientObject { ObjectId = Ground, Type = ItemType.Container });
        _objects.AddOrUpdate(new ClientObject { ObjectId = Pack, Type = ItemType.Container });
        _objects.MoveItem(Pack, Player, 0);
        _state = new RuntimeHousePurchaseState(_objects, () => Player);
    }

    private ClientObject Item(
        uint id, uint wcid, int stack = 1, int value = 0,
        ItemType type = ItemType.Misc, uint container = Player)
    {
        var item = new ClientObject
        {
            ObjectId = id, WeenieClassId = wcid, StackSize = stack, Value = value, Type = type,
        };
        _objects.AddOrUpdate(item);
        _objects.MoveItem(id, container, 0);
        return item;
    }

    private static GameEvents.HousePayment Pay(uint wcid, int num, int paid = 0) =>
        new(num, paid, wcid, "Thing", "Things");

    private static GameEvents.HouseProfile Profile(
        uint slumlord = Slumlord,
        uint owner = 0u,
        bool maintenanceFree = false,
        GameEvents.HousePayment[]? buy = null,
        GameEvents.HousePayment[]? rent = null) =>
        new(slumlord, 0x12340001u, owner, 1u, -1, -1, -1, -1,
            maintenanceFree, 1u, owner == 0u ? "" : "Owner",
            buy ?? [Pay(Pyreal, 50_000), Pay(Totem, 1)],
            rent ?? [Pay(Pyreal, 5_000)]);

    [Fact]
    public void ApplyProfile_OpensAndRaisesRevision()
    {
        long before = _state.View.Snapshot.Revision;

        _state.ApplyProfile(Profile());

        RuntimeHousePurchaseSnapshot snapshot = _state.View.Snapshot;
        Assert.True(snapshot.IsOpen);
        Assert.Equal(Slumlord, snapshot.Profile!.Value.SlumlordGuid);
        Assert.True(snapshot.Revision > before);
    }

    [Fact]
    public void Stage_WithNoProfile_IsNotOpen()
    {
        Item(1, Pyreal, stack: 100);

        Assert.Equal(HouseStageResult.NotOpen, _state.Stage(HousePurchaseTab.Buy, 1));
    }

    [Fact]
    public void Stage_MatchingItem_IsStagedAndCountsItsStack()
    {
        Item(1, Pyreal, stack: 20_000);
        _state.ApplyProfile(Profile());

        Assert.Equal(HouseStageResult.Staged, _state.Stage(HousePurchaseTab.Buy, 1));
        Assert.Equal([1u], _state.View.GetStaged(HousePurchaseTab.Buy));
        Assert.Equal(20_000, _state.View.GetStagedAmount(HousePurchaseTab.Buy, 0));
        Assert.Empty(_state.View.GetStaged(HousePurchaseTab.Rent));
    }

    [Fact]
    public void Stage_ItemInASidePack_IsStaged()
    {
        Item(1, Totem, container: Pack);
        _state.ApplyProfile(Profile());

        Assert.Equal(HouseStageResult.Staged, _state.Stage(HousePurchaseTab.Buy, 1));
    }

    [Fact]
    public void Stage_TradeNote_CountsItsValueTowardsPyreals()
    {
        Item(1, 2621u, value: 10_000, type: ItemType.PromissoryNote);
        _state.ApplyProfile(Profile());

        Assert.Equal(HouseStageResult.Staged, _state.Stage(HousePurchaseTab.Buy, 1));
        Assert.Equal(10_000, _state.View.GetStagedAmount(HousePurchaseTab.Buy, 0));
    }

    [Fact]
    public void Stage_RefusesTheWrongClass()
    {
        Item(1, 1234u);
        _state.ApplyProfile(Profile());

        Assert.Equal(HouseStageResult.NotRequired, _state.Stage(HousePurchaseTab.Buy, 1));
    }

    [Fact]
    public void Stage_RefusesTheSameItemTwice()
    {
        Item(1, Pyreal, stack: 10);
        _state.ApplyProfile(Profile());
        _state.Stage(HousePurchaseTab.Buy, 1);

        Assert.Equal(HouseStageResult.AlreadyStaged, _state.Stage(HousePurchaseTab.Buy, 1));
    }

    [Fact]
    public void Stage_RefusesALineAlreadyCovered()
    {
        Item(1, Pyreal, stack: 30_000);
        Item(2, Pyreal, stack: 5);
        _state.ApplyProfile(Profile(buy: [Pay(Pyreal, 50_000, paid: 20_000)]));

        Assert.Equal(HouseStageResult.Staged, _state.Stage(HousePurchaseTab.Buy, 1));
        Assert.Equal(HouseStageResult.AlreadyCovered, _state.Stage(HousePurchaseTab.Buy, 2));
    }

    [Fact]
    public void Stage_RefusesBuyingAnOwnedHouse()
    {
        Item(1, Pyreal, stack: 10);
        _state.ApplyProfile(Profile(owner: 0x50000042u));

        Assert.Equal(HouseStageResult.AlreadyOwned, _state.Stage(HousePurchaseTab.Buy, 1));
    }

    [Fact]
    public void Stage_RefusesRentWhenMaintenanceIsFree()
    {
        Item(1, Pyreal, stack: 10);
        _state.ApplyProfile(Profile(owner: Player, maintenanceFree: true));

        Assert.Equal(HouseStageResult.MaintenanceFree, _state.Stage(HousePurchaseTab.Rent, 1));
    }

    [Fact]
    public void Stage_RefusesItemsNotInTheInventory()
    {
        Item(1, Pyreal, stack: 10, container: Ground);
        ClientObject wielded = Item(2, Totem);
        wielded.WielderId = Player;
        _state.ApplyProfile(Profile());

        Assert.Equal(HouseStageResult.NotInInventory, _state.Stage(HousePurchaseTab.Buy, 1));
        Assert.Equal(HouseStageResult.NotInInventory, _state.Stage(HousePurchaseTab.Buy, 2));
        Assert.Equal(HouseStageResult.NotInInventory, _state.Stage(HousePurchaseTab.Buy, 99));
    }

    [Fact]
    public void ApplyProfile_FromAnotherSlumlord_ClearsStagedItems()
    {
        Item(1, Pyreal, stack: 10);
        _state.ApplyProfile(Profile());
        _state.Stage(HousePurchaseTab.Buy, 1);

        _state.ApplyProfile(Profile(slumlord: OtherSlumlord));

        Assert.Empty(_state.View.GetStaged(HousePurchaseTab.Buy));
    }

    [Fact]
    public void ApplyProfile_FromTheSameSlumlord_KeepsItemsStillHeld()
    {
        Item(1, Pyreal, stack: 10);
        Item(2, Pyreal, stack: 20);
        _state.ApplyProfile(Profile(owner: Player));
        _state.Stage(HousePurchaseTab.Rent, 1);
        _state.Stage(HousePurchaseTab.Rent, 2);
        _objects.Remove(2);

        _state.ApplyProfile(Profile(owner: Player, rent: [Pay(Pyreal, 5_000, paid: 20)]));

        Assert.Equal([1u], _state.View.GetStaged(HousePurchaseTab.Rent));
    }

    [Fact]
    public void Revalidate_DropsItemsThatLeftTheInventory()
    {
        Item(1, Pyreal, stack: 10);
        _state.ApplyProfile(Profile());
        _state.Stage(HousePurchaseTab.Buy, 1);
        long before = _state.View.Snapshot.Revision;

        Assert.False(_state.Revalidate());
        Assert.Equal(before, _state.View.Snapshot.Revision);

        _objects.MoveItem(1, Ground, 0);

        Assert.Empty(_state.View.GetStaged(HousePurchaseTab.Buy));
        Assert.True(_state.Revalidate());
        Assert.True(_state.View.Snapshot.Revision > before);
    }

    [Fact]
    public void Unstage_AndClose()
    {
        Item(1, Pyreal, stack: 10);
        Item(2, Totem);
        _state.ApplyProfile(Profile());
        _state.Stage(HousePurchaseTab.Buy, 1);
        _state.Stage(HousePurchaseTab.Buy, 2);

        _state.Unstage(HousePurchaseTab.Buy, 1);
        Assert.Equal([2u], _state.View.GetStaged(HousePurchaseTab.Buy));

        _state.Close();
        Assert.False(_state.View.Snapshot.IsOpen);
        Assert.Null(_state.View.Snapshot.Profile);
        Assert.Empty(_state.View.GetStaged(HousePurchaseTab.Buy));
    }
}
```

- [ ] **Step 2: Run them to see them fail.** Run the Verify command. Expected: build error, the type doesn't exist.

- [ ] **Step 3: Write the state**

```csharp
using AcDream.Core.Items;
using AcDream.Core.Net.Messages;

namespace AcDream.Runtime.Gameplay;

/// <summary>The two pages of the slumlord window.</summary>
public enum HousePurchaseTab
{
    Buy = 0,
    Rent = 1,
}

/// <summary>What happened to an item offered to the slumlord window.</summary>
public enum HouseStageResult
{
    Staged,
    NotOpen,
    AlreadyOwned,
    MaintenanceFree,
    NotInInventory,
    AlreadyStaged,
    NotRequired,
    AlreadyCovered,
}

public readonly record struct RuntimeHousePurchaseSnapshot(
    long Revision,
    bool IsOpen,
    GameEvents.HouseProfile? Profile);

public interface IRuntimeHousePurchaseView
{
    RuntimeHousePurchaseSnapshot Snapshot { get; }

    /// <summary>Staged item guids for one tab, in stage order, leaving out
    /// any that are no longer in the player's inventory.</summary>
    IReadOnlyList<uint> GetStaged(HousePurchaseTab tab);

    /// <summary>How much of payment line <paramref name="lineIndex"/> the
    /// tab's staged items cover: stack sizes, or a trade note's value.</summary>
    int GetStagedAmount(HousePurchaseTab tab, int lineIndex);
}

/// <summary>
/// The slumlord window's state: the last 0x021D HouseProfile and the items
/// the player has dragged onto each tab. Nothing is sent from here; the
/// window sends the staged guids with BuyHouse or RentHouse, and the server
/// takes only what is still owed.
/// </summary>
public sealed class RuntimeHousePurchaseState
{
    /// <summary>The pyreal class. Trade notes pay towards it too.</summary>
    public const uint PyrealWcid = 273u;

    private readonly object _gate = new();
    private readonly ClientObjectTable _objects;
    private readonly Func<uint> _playerGuid;
    private readonly List<uint> _buy = [];
    private readonly List<uint> _rent = [];
    private GameEvents.HouseProfile? _profile;
    private long _revision;

    public RuntimeHousePurchaseState(ClientObjectTable objects, Func<uint> playerGuid)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _playerGuid = playerGuid ?? throw new ArgumentNullException(nameof(playerGuid));
        View = new PurchaseView(this);
    }

    public IRuntimeHousePurchaseView View { get; }

    public void ApplyProfile(GameEvents.HouseProfile profile)
    {
        lock (_gate)
        {
            bool sameHouse = _profile is { } current
                && current.SlumlordGuid == profile.SlumlordGuid;
            _profile = profile with
            {
                Buy = (profile.Buy ?? []).ToArray(),
                Rent = (profile.Rent ?? []).ToArray(),
            };
            if (sameHouse)
            {
                RevalidateLocked();
            }
            else
            {
                _buy.Clear();
                _rent.Clear();
            }
            _revision++;
        }
    }

    public HouseStageResult Stage(HousePurchaseTab tab, uint itemGuid)
    {
        lock (_gate)
        {
            if (_profile is not { } profile)
                return HouseStageResult.NotOpen;
            if (tab == HousePurchaseTab.Buy && profile.OwnerGuid != 0u)
                return HouseStageResult.AlreadyOwned;
            if (tab == HousePurchaseTab.Rent && profile.MaintenanceFree)
                return HouseStageResult.MaintenanceFree;
            if (!IsInInventory(itemGuid) || _objects.Get(itemGuid) is not { } item)
                return HouseStageResult.NotInInventory;

            List<uint> staged = Staged(tab);
            if (staged.Contains(itemGuid))
                return HouseStageResult.AlreadyStaged;

            IReadOnlyList<GameEvents.HousePayment> lines = Lines(profile, tab);
            int line = LineIndex(lines, item);
            if (line < 0)
                return HouseStageResult.NotRequired;
            if (lines[line].Paid + StagedAmountLocked(tab, line) >= lines[line].Num)
                return HouseStageResult.AlreadyCovered;

            staged.Add(itemGuid);
            _revision++;
            return HouseStageResult.Staged;
        }
    }

    public void Unstage(HousePurchaseTab tab, uint itemGuid)
    {
        lock (_gate)
        {
            if (Staged(tab).Remove(itemGuid))
                _revision++;
        }
    }

    /// <summary>Drops staged items that are no longer in the player's
    /// inventory. True when anything was dropped.</summary>
    public bool Revalidate()
    {
        lock (_gate)
        {
            if (!RevalidateLocked())
                return false;
            _revision++;
            return true;
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            if (_profile is null && _buy.Count == 0 && _rent.Count == 0)
                return;
            _profile = null;
            _buy.Clear();
            _rent.Clear();
            _revision++;
        }
    }

    private bool RevalidateLocked()
    {
        int removed = _buy.RemoveAll(guid => !IsInInventory(guid))
            + _rent.RemoveAll(guid => !IsInInventory(guid));
        return removed > 0;
    }

    private bool IsInInventory(uint itemGuid)
    {
        uint player = _playerGuid();
        return itemGuid != player
            && _objects.Get(itemGuid) is { WielderId: 0u }
            && _objects.IsOwnedByObject(itemGuid, player);
    }

    private List<uint> Staged(HousePurchaseTab tab) =>
        tab == HousePurchaseTab.Buy ? _buy : _rent;

    private static IReadOnlyList<GameEvents.HousePayment> Lines(
        GameEvents.HouseProfile profile, HousePurchaseTab tab) =>
        tab == HousePurchaseTab.Buy ? profile.Buy : profile.Rent;

    private static bool IsTradeNote(ClientObject item) =>
        (item.Type & ItemType.PromissoryNote) != 0;

    private static int Amount(ClientObject item) =>
        IsTradeNote(item) ? item.Value : item.StackSize;

    private static int LineIndex(
        IReadOnlyList<GameEvents.HousePayment> lines, ClientObject item)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].WeenieID == item.WeenieClassId
                || (IsTradeNote(item) && lines[i].WeenieID == PyrealWcid))
            {
                return i;
            }
        }
        return -1;
    }

    private int StagedAmountLocked(HousePurchaseTab tab, int lineIndex)
    {
        if (_profile is not { } profile)
            return 0;
        IReadOnlyList<GameEvents.HousePayment> lines = Lines(profile, tab);
        int total = 0;
        foreach (uint guid in Staged(tab))
        {
            if (IsInInventory(guid)
                && _objects.Get(guid) is { } item
                && LineIndex(lines, item) == lineIndex)
            {
                total += Amount(item);
            }
        }
        return total;
    }

    private sealed class PurchaseView(RuntimeHousePurchaseState owner)
        : IRuntimeHousePurchaseView
    {
        public RuntimeHousePurchaseSnapshot Snapshot
        {
            get
            {
                lock (owner._gate)
                {
                    return new RuntimeHousePurchaseSnapshot(
                        owner._revision, owner._profile is not null, owner._profile);
                }
            }
        }

        public IReadOnlyList<uint> GetStaged(HousePurchaseTab tab)
        {
            lock (owner._gate)
                return owner.Staged(tab).Where(owner.IsInInventory).ToArray();
        }

        public int GetStagedAmount(HousePurchaseTab tab, int lineIndex)
        {
            lock (owner._gate)
                return owner.StagedAmountLocked(tab, lineIndex);
        }
    }
}
```

- [ ] **Step 4: Run the tests.** Run the Verify command. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Runtime/Gameplay/RuntimeHousePurchaseState.cs tests/AcDream.Runtime.Tests/Gameplay/RuntimeHousePurchaseStateTests.cs
git commit -m "feat(housing): runtime state for the slumlord window

Claude-Session: https://claude.ai/code/session_01WfC1ZE7JDErNAj3YviNNat"
```

---

### Task 4: Own the state in GameRuntime and feed it HouseProfile

**Goal:** `GameRuntime` creates the purchase state, resets it with the house state, and both the live and headless routes feed it 0x021D.

**Files:**
- Modify: `src/AcDream.Runtime/GameRuntime.cs`
- Modify: `src/AcDream.Runtime/RuntimeGenerationReset.cs`
- Modify: `src/AcDream.Runtime/Session/LiveSessionEventRouter.cs`
- Modify: `src/AcDream.App/Net/LiveSessionRuntimeFactory.cs`
- Modify: `src/AcDream.Headless/Hosting/HeadlessSessionHost.cs`
- Test: `tests/AcDream.Runtime.Tests/Gameplay/RuntimeHousePurchaseWiringTests.cs`

**Acceptance Criteria:**
- [ ] `runtime.HousePurchaseOwner` exists and `runtime.HousePurchase` returns its view.
- [ ] A generation reset closes an open purchase state.
- [ ] `LiveSocialSessionBindings` has an optional `HousePurchase`, and the router passes `ApplyProfile` as `onHouseProfile`.
- [ ] The live factory and the headless host pass `HousePurchase`.
- [ ] The solution builds.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~RuntimeHousePurchase|FullyQualifiedName~RuntimeGenerationReset|FullyQualifiedName~LiveSessionEventRouter' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-house-purchase` → all pass

**Steps:**

- [ ] **Step 1: Write the failing test.** It reuses the vendor range test's way of building a `GameRuntime`. First move that test's private helpers into a shared file, `tests/AcDream.Runtime.Tests/Gameplay/UseRangeTestWorld.cs`, as `internal static class UseRangeTestWorld`. Move `Create`, `Add`, `Spawn`, `SetPosition`, `CacheBody`, the `SetupId` constant and the `Operations` class, all marked `internal`. In `RuntimeVendorRangeQueryTests`, delete those members and add `using static AcDream.Runtime.Tests.Gameplay.UseRangeTestWorld;`. Its test bodies stay unchanged; `Open` stays in the vendor class. Check the vendor tests still pass: `MAC_TEST_FILTER='FullyQualifiedName~RuntimeVendorRangeQueryTests' …run-mac-tests.sh …` → PASS.

Then write `RuntimeHousePurchaseWiringTests.cs`:

```csharp
using AcDream.Core.Net.Messages;
using AcDream.Runtime.Gameplay;
using static AcDream.Runtime.Tests.Gameplay.UseRangeTestWorld;

namespace AcDream.Runtime.Tests.Gameplay;

public sealed class RuntimeHousePurchaseWiringTests
{
    internal static GameEvents.HouseProfile Profile(uint slumlord) =>
        new(slumlord, 1u, 0u, 1u, -1, -1, -1, -1, false, 1u, "",
            [new GameEvents.HousePayment(10, 0, 273u, "Pyreal", "Pyreals")], []);

    [Fact]
    public void GameRuntime_ExposesThePurchaseStateAndItsView()
    {
        using GameRuntime runtime = Create();

        runtime.HousePurchaseOwner.ApplyProfile(Profile(0x70000100u));

        Assert.True(runtime.HousePurchase.Snapshot.IsOpen);
    }
}
```

For the reset: if `tests/AcDream.Runtime.Tests` already has a test that runs `RuntimeGenerationReset` through to completion (search for `GenerationReset.Reset(` or `ResetGeneration(`), add a case there that opens the purchase state first and asserts `runtime.HousePurchase.Snapshot.IsOpen == false` afterwards, copying that test's setup. If none exists, cover the reset in the Task 9 live check instead and say so in the commit message.

- [ ] **Step 2: Run it to see it fail.** Run the Verify command. Expected: build error, no `HousePurchaseOwner`.

- [ ] **Step 3: Create and own the state** in `GameRuntime.cs`.
  - In `GameRuntimeConstructionContext`, after `public RuntimeHouseState? House { get; set; }`:

```csharp
    public RuntimeHousePurchaseState? HousePurchase { get; set; }
```

  - After `context.House = new RuntimeHouseState(context.EntityObjects.Objects);`:

```csharp
            context.HousePurchase = new RuntimeHousePurchaseState(
                context.EntityObjects.Objects,
                () => context.PlayerIdentity!.ServerGuid);
```

  - In the `new RuntimeGenerationReset(` call, add `context.HousePurchase,` after `context.House,`.
  - After `HouseOwner = context.House;`, add `HousePurchaseOwner = context.HousePurchase;`.
  - After `public RuntimeHouseState HouseOwner { get; }`:

```csharp
    public RuntimeHousePurchaseState HousePurchaseOwner { get; }
```

  - After `public IRuntimeTradeView Trade => TradeOwner.View;`:

```csharp
    public IRuntimeHousePurchaseView HousePurchase => HousePurchaseOwner.View;
```

- [ ] **Step 4: Reset it with the house.** In `RuntimeGenerationReset.cs`:
  - Add the field `private readonly RuntimeHousePurchaseState _housePurchase;` after `_house`.
  - Add the constructor parameter `RuntimeHousePurchaseState housePurchase,` after `RuntimeHouseState house,`.
  - Assign it after the `_house = …` line: `_housePurchase = housePurchase ?? throw new ArgumentNullException(nameof(housePurchase));`.
  - Change the House stage to:

```csharp
                case RuntimeGenerationResetStage.House:
                    Advance(state, () =>
                    {
                        _house.ResetSession();
                        _housePurchase.Close();
                    });
                    break;
```

- [ ] **Step 5: Route the event.** In `LiveSessionEventRouter.cs`, append to `LiveSocialSessionBindings` after `Action<string>? OnLocalPlayerDeath = null`:

```csharp
    Action<string>? OnLocalPlayerDeath = null,
    RuntimeHousePurchaseState? HousePurchase = null);
```

In the `GameEventWiring.WireAll(` call, after the `onHouseUpdateRentPayment:` argument, add:

```csharp
                onHouseProfile: social.HousePurchase is { } housePurchase
                    ? housePurchase.ApplyProfile
                    : null,
```

- [ ] **Step 6: Pass it from both hosts.** In `LiveSessionRuntimeFactory.cs`, inside `new LiveSocialSessionBindings(`, add `HousePurchase: _domain.Runtime.HousePurchaseOwner,` after `House: _domain.Runtime.HouseOwner,`. In `HeadlessSessionHost.cs`, add `HousePurchase: Runtime.HousePurchaseOwner,` after `House: Runtime.HouseOwner,`. Named arguments may come in any order, but `OnLocalPlayerDeath:` must stay last if the call ends with it.

- [ ] **Step 7: Build and run the Verify command.** `dotnet build ~/code/OpenAC/OpenAC-house-purchase -c Release` → 0 errors. Then run Verify → PASS.

- [ ] **Step 8: Commit**

```bash
git add -A src/AcDream.Runtime src/AcDream.App/Net/LiveSessionRuntimeFactory.cs src/AcDream.Headless tests/AcDream.Runtime.Tests/Gameplay
git commit -m "feat(housing): own the slumlord window state and feed it house profiles

Claude-Session: https://claude.ai/code/session_01WfC1ZE7JDErNAj3YviNNat"
```

---

### Task 5: Close the slumlord window when out of range

**Goal:** The vendor's range check becomes a shared `RuntimeUseRange.ShouldClose` with no change in behaviour, and the purchase state closes on teleport, a missing slumlord, or leaving its use radius.

**Files:**
- Create: `src/AcDream.Runtime/Gameplay/RuntimeUseRange.cs`
- Create: `src/AcDream.Runtime/Gameplay/RuntimeHousePurchaseRangeQuery.cs`
- Modify: `src/AcDream.Runtime/Gameplay/RuntimeVendorRangeQuery.cs`
- Modify: `src/AcDream.Runtime/GameRuntime.cs` (the frame callback that calls `RuntimeVendorRangeQuery.EnforceRange(this);`)
- Test: `tests/AcDream.Runtime.Tests/Gameplay/RuntimeHousePurchaseRangeQueryTests.cs`

**Acceptance Criteria:**
- [ ] Every existing `RuntimeVendorRangeQueryTests` test passes unchanged.
- [ ] Purchase state stays open with the player in the slumlord's use radius, and closes when the player walks out of it, when the slumlord entity is removed, or when a teleport is queued.
- [ ] `EnforceRange` with no profile open does nothing.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~RangeQueryTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-house-purchase` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
using AcDream.Runtime.Entities;
using AcDream.Runtime.Gameplay;
using static AcDream.Runtime.Tests.Gameplay.UseRangeTestWorld;

namespace AcDream.Runtime.Tests.Gameplay;

public sealed class RuntimeHousePurchaseRangeQueryTests
{
    private const uint Player = 0x50000001u;
    private const uint Slumlord = 0x70000100u;
    private const uint Landblock = 0x01010001u;

    private static GameRuntime Opened(out RuntimeEntityRecord player, out RuntimeEntityRecord slumlord)
    {
        GameRuntime runtime = Create();
        runtime.PlayerIdentity.ServerGuid = Player;
        player = Add(runtime, Player, Landblock, 100f, 100f);
        slumlord = Add(runtime, Slumlord, Landblock, 102f, 100f, useRadius: 3f);
        runtime.HousePurchaseOwner.ApplyProfile(
            RuntimeHousePurchaseWiringTests.Profile(Slumlord));
        return runtime;
    }

    [Fact]
    public void InRange_StaysOpen()
    {
        using GameRuntime runtime = Opened(out _, out _);

        RuntimeHousePurchaseRangeQuery.EnforceRange(runtime);

        Assert.True(runtime.HousePurchase.Snapshot.IsOpen);
    }

    [Fact]
    public void WalkingAway_Closes()
    {
        using GameRuntime runtime = Opened(out RuntimeEntityRecord player, out _);
        SetPosition(player, Landblock, 122f, 100f);

        RuntimeHousePurchaseRangeQuery.EnforceRange(runtime);

        Assert.False(runtime.HousePurchase.Snapshot.IsOpen);
    }

    [Fact]
    public void SlumlordGone_Closes()
    {
        using GameRuntime runtime = Opened(out _, out RuntimeEntityRecord slumlord);
        Assert.True(runtime.EntityObjects.Entities.RemoveActive(slumlord));

        RuntimeHousePurchaseRangeQuery.EnforceRange(runtime);

        Assert.False(runtime.HousePurchase.Snapshot.IsOpen);
    }

    [Fact]
    public void TeleportQueued_Closes()
    {
        using GameRuntime runtime = Opened(out _, out _);
        Assert.True(runtime.TransitOwner.TryQueueTeleportStart(1));

        RuntimeHousePurchaseRangeQuery.EnforceRange(runtime);

        Assert.False(runtime.HousePurchase.Snapshot.IsOpen);
    }

    [Fact]
    public void NothingOpen_DoesNothing()
    {
        using GameRuntime runtime = Create();

        RuntimeHousePurchaseRangeQuery.EnforceRange(runtime);

        Assert.False(runtime.HousePurchase.Snapshot.IsOpen);
    }
}
```

- [ ] **Step 2: Run them to see them fail.** Run the Verify command. Expected: build error, no `RuntimeHousePurchaseRangeQuery`.

- [ ] **Step 3: Extract `RuntimeUseRange`.** Create `RuntimeUseRange.cs`. Move the body of `RuntimeVendorRangeQuery.EnforceRange` into it from the teleport check down: the same usings, the same comments with "vendor" replaced by "target", and `vendor.Close(); return;` replaced by `return true;`. The result must be:

```csharp
using System.Numerics;
using AcDream.Core.Physics;
using AcDream.Runtime.Entities;
using AcDream.Runtime.Physics;
using AcDream.Runtime.World;

namespace AcDream.Runtime.Gameplay;

/// <summary>
/// Whether a window opened by using an object (a vendor, a slumlord) should
/// close: on a teleport, when the object is gone, or when the player is no
/// longer within its use radius.
/// </summary>
internal static class RuntimeUseRange
{
    public static bool ShouldClose(GameRuntime runtime, uint targetGuid)
    {
        RuntimeWorldTransitState transit = runtime.TransitOwner;
        if (transit.HasPendingTeleportStart || transit.IsTeleportActive)
            return true;

        uint playerGuid = runtime.PlayerIdentity.ServerGuid;
        if (playerGuid == 0u
            || !runtime.EntityObjects.Entities.TryGetActive(
                playerGuid,
                out RuntimeEntityRecord playerRecord)
            || !RuntimePhysicsState.TryGetAbsoluteWorldPosition(
                playerRecord,
                out Vector3 playerPosition))
        {
            return false;
        }

        if (!runtime.EntityObjects.Entities.TryGetActive(
                targetGuid,
                out RuntimeEntityRecord targetRecord)
            || !RuntimePhysicsState.TryGetAbsoluteWorldPosition(
                targetRecord,
                out Vector3 targetPosition))
        {
            return true;
        }

        // (move the vendor's two explanatory comments here unchanged, apart
        // from saying "target" for "vendor")
        float useRadius = targetRecord.Snapshot.UseRadius ?? 0f;
        (float Radius, float Height) playerBody =
            runtime.EntityObjects.Physics.EntityBodyShape(playerGuid)
                ?? (0f, 0f);
        (float Radius, float Height) targetBody =
            runtime.EntityObjects.Physics.EntityBodyShape(targetGuid)
                ?? (0f, 0f);

        return !ObjectRangeMath.ObjectsInRange(
            playerPosition,
            playerBody.Radius,
            playerBody.Height,
            targetPosition,
            targetBody.Radius,
            targetBody.Height,
            useRadius,
            useRadii: true,
            ignoreZDelta: false);
    }
}
```

Compare it with the original before deleting anything: every branch must give the same result for the vendor. If a using is unused after the move, drop it from whichever file no longer needs it.

- [ ] **Step 4: Make the vendor call it**

```csharp
public static class RuntimeVendorRangeQuery
{
    public static void EnforceRange(GameRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        VendorState vendor = runtime.InventoryOwner.Vendor;
        uint vendorId = vendor.VendorId;
        if (vendorId != 0u && RuntimeUseRange.ShouldClose(runtime, vendorId))
            vendor.Close();
    }
}
```

- [ ] **Step 5: Add the slumlord query**

```csharp
namespace AcDream.Runtime.Gameplay;

public static class RuntimeHousePurchaseRangeQuery
{
    public static void EnforceRange(GameRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        RuntimeHousePurchaseState purchase = runtime.HousePurchaseOwner;
        if (purchase.View.Snapshot.Profile is { } profile
            && RuntimeUseRange.ShouldClose(runtime, profile.SlumlordGuid))
        {
            purchase.Close();
        }
    }
}
```

- [ ] **Step 6: Run it every frame.** In `GameRuntime.CreateLocalPlayerFrameController`, after `RuntimeVendorRangeQuery.EnforceRange(this);`, add:

```csharp
                RuntimeHousePurchaseRangeQuery.EnforceRange(this);
```

- [ ] **Step 7: Run the Verify command.** Expected: PASS, vendor tests included.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.Runtime/Gameplay/RuntimeUseRange.cs src/AcDream.Runtime/Gameplay/RuntimeVendorRangeQuery.cs src/AcDream.Runtime/Gameplay/RuntimeHousePurchaseRangeQuery.cs src/AcDream.Runtime/GameRuntime.cs tests/AcDream.Runtime.Tests/Gameplay/RuntimeHousePurchaseRangeQueryTests.cs
git commit -m "feat(housing): close the slumlord window out of range, sharing the vendor check

Claude-Session: https://claude.ai/code/session_01WfC1ZE7JDErNAj3YviNNat"
```

---

### Task 6: Window wording (HousePaymentNames + HousePurchaseText)

**Goal:** One place builds every placeholder line the window shows, and payment names follow the same plural rule as the house panel.

**Files:**
- Create: `src/AcDream.Runtime/Gameplay/HousePaymentNames.cs`
- Modify: `src/AcDream.Runtime/Gameplay/RuntimeHouseState.cs` (`PaymentName` → `HousePaymentNames.For`)
- Create: `src/AcDream.App/UI/Layout/HousePurchaseText.cs`
- Test: `tests/AcDream.App.Tests/UI/Layout/HousePurchaseTextTests.cs`

**Acceptance Criteria:**
- [ ] `RuntimeHouseState` tests still pass (no wording change).
- [ ] `OwnerLine` gives `Owner: Sammy` or `For Sale`.
- [ ] `RequirementLines` gives the type, any limits, and one line per payment, with digit grouping and ` (+N staged)` when N > 0. On Rent with maintenance free, a single free line replaces the payments.
- [ ] `Refusal` gives a sentence for every `HouseStageResult` except `Staged`.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~HousePurchaseTextTests|FullyQualifiedName~RuntimeHouseState' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-house-purchase` → all pass

**Steps:**

- [ ] **Step 1: Move the plural rule.** Create `HousePaymentNames.cs` with the body of `RuntimeHouseState.PaymentName`:

```csharp
using AcDream.Core.Net.Messages;

namespace AcDream.Runtime.Gameplay;

public static class HousePaymentNames
{
    /// <summary>The item name for a payment line: singular for one,
    /// otherwise the plural the server sent, or an English guess.</summary>
    public static string For(GameEvents.HousePayment payment)
    {
        if (payment.Num == 1)
            return payment.Name;
        if (!string.IsNullOrEmpty(payment.PluralName))
            return payment.PluralName;

        return payment.Name.EndsWith('s') || payment.Name.EndsWith('x')
            ? payment.Name + "es"
            : payment.Name + "s";
    }
}
```

Delete `RuntimeHouseState.PaymentName` and change its one caller in `ComposePayments` to `HousePaymentNames.For(payment)`.

- [ ] **Step 2: Write the failing text tests**

```csharp
using AcDream.App.UI.Layout;
using AcDream.Core.Net.Messages;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.Tests.UI.Layout;

public sealed class HousePurchaseTextTests
{
    private static GameEvents.HouseProfile Profile(
        uint owner = 0u, string ownerName = "", uint type = 1u,
        int minLevel = -1, int maxLevel = -1, int minRank = -1,
        bool maintenanceFree = false) =>
        new(0x70000100u, 1u, owner, 1u, minLevel, maxLevel, minRank, -1,
            maintenanceFree, type, ownerName,
            [new(50_000, 0, 273u, "Pyreal", "Pyreals"), new(1, 0, 9000u, "Bone Totem", "")],
            [new(5_000, 1_250, 273u, "Pyreal", "Pyreals")]);

    [Fact]
    public void OwnerLine_NamesTheOwnerOrSaysForSale()
    {
        Assert.Equal("For Sale", HousePurchaseText.OwnerLine(Profile()));
        Assert.Equal("Owner: Sammy",
            HousePurchaseText.OwnerLine(Profile(owner: 5u, ownerName: "Sammy")));
    }

    [Fact]
    public void RequirementLines_Buy_TypeThenPaymentsWithStaged()
    {
        IReadOnlyList<string> lines = HousePurchaseText.RequirementLines(
            Profile(), HousePurchaseTab.Buy, [8_000, 0]);

        Assert.Equal(
            ["Cottage", "Pyreals: 0 / 50,000 (+8,000 staged)", "Bone Totem: 0 / 1"],
            lines);
    }

    [Fact]
    public void RequirementLines_ShowLimits()
    {
        Assert.Contains("Requires level 20+",
            HousePurchaseText.RequirementLines(Profile(minLevel: 20), HousePurchaseTab.Buy, [0, 0]));
        Assert.Contains("Requires level 20 to 50",
            HousePurchaseText.RequirementLines(Profile(minLevel: 20, maxLevel: 50), HousePurchaseTab.Buy, [0, 0]));
        Assert.Contains("Requires allegiance rank 6+",
            HousePurchaseText.RequirementLines(Profile(type: 3u, minRank: 6), HousePurchaseTab.Buy, [0, 0]));
    }

    [Fact]
    public void RequirementLines_Rent_PaidAmountsOrFree()
    {
        Assert.Equal(
            ["Villa", "Pyreals: 1,250 / 5,000"],
            HousePurchaseText.RequirementLines(Profile(type: 2u), HousePurchaseTab.Rent, [0]));
        Assert.Equal(
            ["Mansion", "Maintenance is free this period."],
            HousePurchaseText.RequirementLines(
                Profile(type: 3u, maintenanceFree: true), HousePurchaseTab.Rent, [0]));
    }

    [Fact]
    public void Refusal_HasASentenceForEveryRefusal()
    {
        foreach (HouseStageResult result in Enum.GetValues<HouseStageResult>())
        {
            if (result == HouseStageResult.Staged) continue;
            Assert.False(string.IsNullOrWhiteSpace(HousePurchaseText.Refusal(result)));
        }
    }
}
```

- [ ] **Step 3: Run them to see them fail.** Run the Verify command. Expected: build error.

- [ ] **Step 4: Write `HousePurchaseText`**

```csharp
using System.Globalization;
using AcDream.Core.Net.Messages;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.UI.Layout;

/// <summary>
/// Every line the slumlord window writes itself. The tab and button captions
/// come from the dat; this wording does not, and is a placeholder until it
/// is checked against a retail client (spec: Captions to check against
/// retail).
/// </summary>
internal static class HousePurchaseText
{
    public static string OwnerLine(GameEvents.HouseProfile profile) =>
        profile.OwnerGuid == 0u ? "For Sale" : "Owner: " + profile.OwnerName;

    public static IReadOnlyList<string> RequirementLines(
        GameEvents.HouseProfile profile,
        HousePurchaseTab tab,
        IReadOnlyList<int> stagedAmounts)
    {
        var lines = new List<string> { TypeName(profile.Type) };

        if (profile.MinLevel >= 0 && profile.MaxLevel >= 0)
            lines.Add($"Requires level {profile.MinLevel} to {profile.MaxLevel}");
        else if (profile.MinLevel >= 0)
            lines.Add($"Requires level {profile.MinLevel}+");
        if (profile.MinAllegianceRank >= 0)
            lines.Add($"Requires allegiance rank {profile.MinAllegianceRank}+");

        if (tab == HousePurchaseTab.Rent && profile.MaintenanceFree)
        {
            lines.Add("Maintenance is free this period.");
            return lines;
        }

        IReadOnlyList<GameEvents.HousePayment> payments =
            tab == HousePurchaseTab.Buy ? profile.Buy : profile.Rent;
        for (int i = 0; i < payments.Count; i++)
        {
            GameEvents.HousePayment payment = payments[i];
            string line = $"{HousePaymentNames.For(payment)}: "
                + $"{Number(payment.Paid)} / {Number(payment.Num)}";
            int staged = i < stagedAmounts.Count ? stagedAmounts[i] : 0;
            if (staged > 0)
                line += $" (+{Number(staged)} staged)";
            lines.Add(line);
        }
        return lines;
    }

    public static string Refusal(HouseStageResult result) => result switch
    {
        HouseStageResult.NotOpen => "There is no house to pay for.",
        HouseStageResult.AlreadyOwned => "This dwelling already has an owner.",
        HouseStageResult.MaintenanceFree => "Maintenance is free this period.",
        HouseStageResult.NotInInventory => "That item is not in your pack.",
        HouseStageResult.AlreadyStaged => "That item is already offered.",
        HouseStageResult.NotRequired => "That item is not needed here.",
        HouseStageResult.AlreadyCovered => "You have already offered enough of that.",
        _ => string.Empty,
    };

    private static string TypeName(uint type) => type switch
    {
        1u => "Cottage",
        2u => "Villa",
        3u => "Mansion",
        4u => "Apartment",
        _ => "Dwelling",
    };

    private static string Number(int value) =>
        value.ToString("N0", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 5: Run the Verify command.** Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.Runtime/Gameplay/HousePaymentNames.cs src/AcDream.Runtime/Gameplay/RuntimeHouseState.cs src/AcDream.App/UI/Layout/HousePurchaseText.cs tests/AcDream.App.Tests/UI/Layout/HousePurchaseTextTests.cs
git commit -m "feat(housing): slumlord window wording in one place

Claude-Session: https://claude.ai/code/session_01WfC1ZE7JDErNAj3YviNNat"
```

---

### Task 7: HousePurchaseUiController

**Goal:** A controller binds the 0x2100000E elements: tabs, requirement and owner texts, item strips with drop-to-stage and drag-off or right-click to unstage, buttons that send the staged guids, and the close button.

**Files:**
- Create: `src/AcDream.App/UI/Layout/HousePurchaseUiController.cs`
- Test: `tests/AcDream.App.Tests/UI/Layout/HousePurchaseUiControllerTests.cs`

**Acceptance Criteria:**
- [ ] Hidden until the state opens. `Tick` shows it on open and hides it on close.
- [ ] An unowned profile selects Buy (Buy page visible, Maintenance page hidden); an owned one selects Maintenance. Clicking a tab switches pages.
- [ ] The texts show `HousePurchaseText` lines, and the owner text shows `OwnerLine`.
- [ ] A drop from the inventory calls `Stage` for that strip's tab, and a refusal is reported once with `HousePurchaseText.Refusal`.
- [ ] Dragging a cell off a strip, or `ExamineItemRequested` on it, calls `Unstage`.
- [ ] Each button is disabled with nothing staged, on Buy for an owned house, and on Maintenance when maintenance is free. Pressing an enabled button calls `BuyHouse` / `RentHouse` with the slumlord and the staged guids.
- [ ] The close button calls `Close`. An object-table removal or move calls `Revalidate`.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~HousePurchaseUiControllerTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-house-purchase` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** (hand-built layout, the same way `SalvageUiControllerTests` does it)

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Core.Items;
using AcDream.Core.Net.Messages;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.Tests.UI.Layout;

public sealed class HousePurchaseUiControllerTests
{
    private const uint Player = 0x50000001u;
    private const uint Slumlord = 0x70000100u;
    private sealed class Element : UiElement { }

    private sealed class Harness : IDisposable
    {
        public ClientObjectTable Objects { get; } = new();
        public RuntimeHousePurchaseState State { get; }
        public UiButton BuyTab { get; } = new(new ElementInfo { Id = HousePurchaseUiController.BuyTabId, Type = 1 }, _ => (0u, 0, 0));
        public UiButton RentTab { get; } = new(new ElementInfo { Id = HousePurchaseUiController.MaintenanceTabId, Type = 1 }, _ => (0u, 0, 0));
        public UiElement BuyPage { get; } = new Element();
        public UiElement RentPage { get; } = new Element();
        public UiText BuyText { get; } = new();
        public UiText RentText { get; } = new();
        public UiText BuyOwner { get; } = new();
        public UiItemList BuyList { get; } = new() { Width = 555, Height = 32 };
        public UiItemList RentList { get; } = new() { Width = 555, Height = 32 };
        public UiButton BuyButton { get; } = new(new ElementInfo { Id = HousePurchaseUiController.BuyButtonId, Type = 1 }, _ => (0u, 0, 0));
        public UiButton RentButton { get; } = new(new ElementInfo { Id = HousePurchaseUiController.MaintenanceButtonId, Type = 1 }, _ => (0u, 0, 0));
        public UiButton Close { get; } = new(new ElementInfo { Id = HousePurchaseUiController.CloseButtonId, Type = 1 }, _ => (0u, 0, 0));
        public HousePurchaseUiController Controller { get; }
        public bool Visible;
        public List<string> Reports { get; } = [];
        public List<string> Sent { get; } = [];

        public Harness()
        {
            Objects.AddOrUpdate(new ClientObject { ObjectId = Player, Type = ItemType.Creature });
            State = new RuntimeHousePurchaseState(Objects, () => Player);
            var root = new Element();
            foreach (UiElement e in new UiElement[]
                     { BuyTab, RentTab, BuyPage, RentPage, BuyText, RentText, BuyOwner,
                       BuyList, RentList, BuyButton, RentButton, Close })
                root.AddChild(e);
            var layout = new ImportedLayout(root, new()
            {
                [HousePurchaseUiController.BuyTabId] = BuyTab,
                [HousePurchaseUiController.MaintenanceTabId] = RentTab,
                [HousePurchaseUiController.BuyPageId] = BuyPage,
                [HousePurchaseUiController.MaintenancePageId] = RentPage,
                [HousePurchaseUiController.BuyRequirementsTextId] = BuyText,
                [HousePurchaseUiController.MaintenanceRequirementsTextId] = RentText,
                [HousePurchaseUiController.BuyOwnerId] = BuyOwner,
                [HousePurchaseUiController.BuyItemsListId] = BuyList,
                [HousePurchaseUiController.MaintenanceItemsListId] = RentList,
                [HousePurchaseUiController.BuyButtonId] = BuyButton,
                [HousePurchaseUiController.MaintenanceButtonId] = RentButton,
                [HousePurchaseUiController.CloseButtonId] = Close,
            });
            Controller = HousePurchaseUiController.Bind(layout, new(
                Purchase: State.View,
                Objects: Objects,
                Stage: State.Stage,
                Unstage: State.Unstage,
                Revalidate: () => State.Revalidate(),
                Close: State.Close,
                BuyHouse: (s, items) => Sent.Add($"buy:{s:X8}:{string.Join(",", items)}"),
                RentHouse: (s, items) => Sent.Add($"rent:{s:X8}:{string.Join(",", items)}"),
                ResolveIcon: (_, icon, _, _, _) => icon,
                SetWindowVisible: v => Visible = v,
                Report: Reports.Add,
                ResolveAppropriateName: ItemTooltipCaptionNames.Resolve))!;
        }

        public void Item(uint id, uint wcid, int stack = 1)
        {
            Objects.AddOrUpdate(new ClientObject { ObjectId = id, WeenieClassId = wcid, StackSize = stack, IconId = 5 });
            Objects.MoveItem(id, Player, 0);
        }

        public void Open(uint owner = 0u, bool free = false)
        {
            State.ApplyProfile(new GameEvents.HouseProfile(
                Slumlord, 1u, owner, 1u, -1, -1, -1, -1, free, 1u, owner == 0u ? "" : "Sammy",
                [new(50_000, 0, 273u, "Pyreal", "Pyreals")],
                [new(5_000, 0, 273u, "Pyreal", "Pyreals")]));
            Controller.Tick();
        }

        public void Drop(UiItemList list, uint id)
        {
            var cell = new UiItemSlot();
            Controller.HandleDropRelease(list, cell, new ItemDragPayload(id, ItemDragSource.Inventory, 0, cell));
            Controller.Tick();
        }

        public static string Text(UiText text) =>
            string.Join("\n", text.LinesProvider().Select(l => l.Text));

        public void Dispose() => Controller.Dispose();
    }

    [Fact]
    public void Hidden_UntilOpened_ThenShownOnBuyForAnUnownedHouse()
    {
        using var h = new Harness();
        Assert.False(h.Visible);

        h.Open();

        Assert.True(h.Visible);
        Assert.True(h.BuyPage.Visible);
        Assert.False(h.RentPage.Visible);
        Assert.Equal("For Sale", Harness.Text(h.BuyOwner));
        Assert.Contains("Pyreals: 0 / 50,000", Harness.Text(h.BuyText));
    }

    [Fact]
    public void AnOwnedHouse_OpensOnMaintenance_AndTabsSwitch()
    {
        using var h = new Harness();
        h.Open(owner: Player);

        Assert.True(h.RentPage.Visible);
        Assert.False(h.BuyPage.Visible);

        h.BuyTab.OnClick!();

        Assert.True(h.BuyPage.Visible);
        Assert.False(h.RentPage.Visible);
    }

    [Fact]
    public void Drop_StagesAndEnablesTheButton_WhichSendsTheStagedGuids()
    {
        using var h = new Harness();
        h.Item(1, 273u, stack: 20_000);
        h.Open();
        Assert.False(h.BuyButton.Enabled);

        h.Drop(h.BuyList, 1);

        Assert.Equal(1, h.BuyList.GetNumUIItems());
        Assert.True(h.BuyButton.Enabled);
        Assert.Contains("(+20,000 staged)", Harness.Text(h.BuyText));

        h.BuyButton.OnClick!();

        Assert.Equal(["buy:70000100:1"], h.Sent);
    }

    [Fact]
    public void Drop_Refused_IsReportedOnce()
    {
        using var h = new Harness();
        h.Item(1, 1234u);
        h.Open();

        h.Drop(h.BuyList, 1);

        Assert.Equal([HousePurchaseText.Refusal(HouseStageResult.NotRequired)], h.Reports);
        Assert.Equal(0, h.BuyList.GetNumUIItems());
    }

    [Fact]
    public void DragOffAndRightClick_Unstage()
    {
        using var h = new Harness();
        h.Item(1, 273u, stack: 10);
        h.Item(2, 273u, stack: 10);
        h.Open(owner: Player);
        h.Drop(h.RentList, 1);
        h.Drop(h.RentList, 2);

        var cell = new UiItemSlot();
        h.Controller.OnDragLift(h.RentList, cell, new ItemDragPayload(1, ItemDragSource.Inventory, 0, cell));
        h.RentList.ExamineItemRequested!(2);
        h.Controller.Tick();

        Assert.Empty(h.State.View.GetStaged(HousePurchaseTab.Rent));
        Assert.False(h.RentButton.Enabled);
    }

    [Fact]
    public void Buttons_DisabledForAnOwnedBuyAndFreeMaintenance()
    {
        using var h = new Harness();
        h.Open(owner: Player, free: true);

        Assert.False(h.BuyButton.Enabled);
        Assert.False(h.RentButton.Enabled);
    }

    [Fact]
    public void CloseButton_ClosesAndHides()
    {
        using var h = new Harness();
        h.Open();

        h.Close.OnClick!();
        h.Controller.Tick();

        Assert.False(h.State.View.Snapshot.IsOpen);
        Assert.False(h.Visible);
    }

    [Fact]
    public void AnItemLeavingTheInventory_IsUnstaged()
    {
        using var h = new Harness();
        h.Item(1, 273u, stack: 10);
        h.Open();
        h.Drop(h.BuyList, 1);

        h.Objects.Remove(1);
        h.Controller.Tick();

        Assert.Equal(0, h.BuyList.GetNumUIItems());
    }
}
```

- [ ] **Step 2: Run them to see them fail.** Run the Verify command. Expected: build error.

- [ ] **Step 3: Write the controller**

```csharp
using System.Numerics;
using AcDream.Core.Items;
using AcDream.Core.Net.Messages;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.UI.Layout;

/// <summary>
/// The slumlord window, LayoutDesc 0x2100000E: a Buy page and a Maintenance
/// page, each with the requirements, the owner, an item strip the player
/// drags payment onto, and a button that offers the staged items. Element
/// names in comments are the retail names from client_portal.dat.
/// </summary>
public sealed class HousePurchaseUiController : IRetainedPanelController, IItemListDragHandler
{
    public const uint LayoutId = 0x2100000Eu;
    public const uint RootId = 0x1000008Cu;                        // RootSlumlord_Field
    public const uint BuyTabId = 0x1000008Eu;                      // HouseBuyTab
    public const uint MaintenanceTabId = 0x1000008Fu;              // HouseMaintenanceTab
    public const uint BuyPageId = 0x10000090u;                     // HouseBuyPage
    public const uint BuyRequirementsTextId = 0x10000091u;         // HouseBuyRequirementsText
    public const uint BuyOwnerId = 0x10000093u;                    // HouseBuy_HouseOwner
    public const uint BuyButtonId = 0x10000094u;                   // HouseBuy_Button
    public const uint BuyItemsListId = 0x10000095u;                // HouseBuyItemsList
    public const uint BuyItemListScrollId = 0x10000096u;           // HouseBuy_ItemListScroll
    public const uint MaintenancePageId = 0x10000097u;             // HouseMaintenancePage
    public const uint MaintenanceRequirementsTextId = 0x10000098u; // HouseMaintenanceRequirementsText
    public const uint MaintenanceOwnerId = 0x1000009Au;            // HouseMaintenance_HouseOwner
    public const uint MaintenanceButtonId = 0x1000009Bu;           // HouseMaintenance_Button
    public const uint MaintenanceItemsListId = 0x1000009Cu;        // HouseMaintenanceItemsList
    public const uint MaintenanceItemListScrollId = 0x1000009Du;   // HouseMaintenance_ItemListScroll
    public const uint CloseButtonId = 0x1000009Eu;                 // CloseSlumlordButton
    private const uint TradeOverlaySprite = 0x06001DAEu;

    public sealed record Bindings(
        IRuntimeHousePurchaseView Purchase,
        ClientObjectTable Objects,
        Func<HousePurchaseTab, uint, HouseStageResult> Stage,
        Action<HousePurchaseTab, uint> Unstage,
        Action Revalidate,
        Action Close,
        Action<uint, IReadOnlyList<uint>> BuyHouse,
        Action<uint, IReadOnlyList<uint>> RentHouse,
        Func<ItemType, uint, uint, uint, uint, uint> ResolveIcon,
        Action<bool> SetWindowVisible,
        Action<string> Report,
        /// <summary>Composes an item's displayed name, material prefix
        /// included, as the other item strips do.</summary>
        Func<ClientObject, string> ResolveAppropriateName,
        uint EmptySlotSprite = 0u);

    private sealed record Page(
        HousePurchaseTab Tab,
        UiElement? TabElement,
        UiElement? PageElement,
        UiText? Requirements,
        UiText? Owner,
        UiElement? Button,
        UiItemList List);

    private readonly Bindings _bindings;
    private readonly Page _buy;
    private readonly Page _rent;
    private readonly UiElement? _closeButton;
    private HousePurchaseTab _selected = HousePurchaseTab.Buy;
    private long _lastRevision = long.MinValue;
    private bool _wasOpen;
    private uint _shownSlumlord;
    private uint _shownOwner;
    private bool _disposed;

    private HousePurchaseUiController(ImportedLayout layout, Bindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings.ResolveAppropriateName);
        _bindings = bindings;
        _buy = BindPage(layout, HousePurchaseTab.Buy, BuyTabId, BuyPageId,
            BuyRequirementsTextId, BuyOwnerId, BuyButtonId, BuyItemsListId, BuyItemListScrollId);
        _rent = BindPage(layout, HousePurchaseTab.Rent, MaintenanceTabId, MaintenancePageId,
            MaintenanceRequirementsTextId, MaintenanceOwnerId, MaintenanceButtonId,
            MaintenanceItemsListId, MaintenanceItemListScrollId);
        _closeButton = layout.FindElement(CloseButtonId);
        RetailTabBinding.SetClick(_closeButton, () => _bindings.Close());

        bindings.Objects.ObjectRemoved += OnObjectRemoved;
        bindings.Objects.ObjectMoved += OnObjectMoved;
        bindings.Objects.ContainerContentsReplaced += OnContainerContentsReplaced;

        Select(HousePurchaseTab.Buy);
        _bindings.SetWindowVisible(false);
    }

    public static HousePurchaseUiController? Bind(ImportedLayout layout, Bindings bindings)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(bindings);
        return layout.FindElement(BuyItemsListId) is UiItemList
            && layout.FindElement(MaintenanceItemsListId) is UiItemList
                ? new HousePurchaseUiController(layout, bindings)
                : null;
    }

    private Page BindPage(
        ImportedLayout layout, HousePurchaseTab tab, uint tabId, uint pageId,
        uint requirementsId, uint ownerId, uint buttonId, uint listId, uint scrollId)
    {
        var list = (UiItemList)layout.FindElement(listId)!;
        list.Columns = 1;
        list.SingleRow = true;
        list.HorizontalScroll = true;
        list.CellWidth = 32f;
        list.CellHeight = 32f;
        list.FillVisibleEmptySlots = true;
        if (_bindings.EmptySlotSprite != 0u)
            list.CellEmptySprite = _bindings.EmptySlotSprite;
        list.EmptySlotFactory = () => new UiItemSlot
        {
            SpriteResolve = list.SpriteResolve,
            AllowDragSource = false,
        };
        list.RegisterDragHandler(this);
        list.ExamineItemRequested = guid => _bindings.Unstage(tab, guid);
        if (layout.FindElement(scrollId) is UiScrollbar scrollbar)
        {
            scrollbar.Model = list.Scroll;
            scrollbar.Horizontal = true;
        }

        UiElement? button = layout.FindElement(buttonId);
        if (button is UiButton uiButton)
            uiButton.SuppressSelfToggle = true;
        RetailTabBinding.SetClick(button, () => Submit(tab));

        UiElement? tabElement = layout.FindElement(tabId);
        RetailTabBinding.SetClick(tabElement, () => Select(tab));

        return new Page(
            tab,
            tabElement,
            layout.FindElement(pageId),
            layout.FindElement(requirementsId) as UiText,
            layout.FindElement(ownerId) as UiText,
            button,
            list);
    }

    /// <summary>Applies the latest state (revision-gated).</summary>
    public void Tick()
    {
        if (_disposed) return;
        RuntimeHousePurchaseSnapshot snapshot = _bindings.Purchase.Snapshot;

        if (snapshot.IsOpen != _wasOpen)
        {
            _wasOpen = snapshot.IsOpen;
            _bindings.SetWindowVisible(snapshot.IsOpen);
        }

        if (snapshot.Revision == _lastRevision) return;
        _lastRevision = snapshot.Revision;
        if (snapshot.Profile is not { } profile)
        {
            _shownSlumlord = 0u;
            return;
        }

        if (profile.SlumlordGuid != _shownSlumlord || profile.OwnerGuid != _shownOwner)
        {
            _shownSlumlord = profile.SlumlordGuid;
            _shownOwner = profile.OwnerGuid;
            Select(profile.OwnerGuid != 0u ? HousePurchaseTab.Rent : HousePurchaseTab.Buy);
        }

        Refresh(_buy, profile);
        Refresh(_rent, profile);
    }

    public void OnShown() => Tick();

    private void Select(HousePurchaseTab tab)
    {
        _selected = tab;
        foreach (Page page in new[] { _buy, _rent })
        {
            bool open = page.Tab == _selected;
            if (page.PageElement is not null)
                page.PageElement.Visible = open;
            RetailTabBinding.SetOpen(page.TabElement, open);
        }
    }

    private void Refresh(Page page, GameEvents.HouseProfile profile)
    {
        IReadOnlyList<uint> staged = _bindings.Purchase.GetStaged(page.Tab);
        IReadOnlyList<GameEvents.HousePayment> payments =
            page.Tab == HousePurchaseTab.Buy ? profile.Buy : profile.Rent;
        int[] stagedAmounts = new int[payments.Count];
        for (int i = 0; i < stagedAmounts.Length; i++)
            stagedAmounts[i] = _bindings.Purchase.GetStagedAmount(page.Tab, i);

        SetLines(page.Requirements,
            HousePurchaseText.RequirementLines(profile, page.Tab, stagedAmounts));
        SetLines(page.Owner, [HousePurchaseText.OwnerLine(profile)]);

        using (page.List.DeferLayout())
        {
            page.List.Flush();
            foreach (uint guid in staged)
            {
                if (_bindings.Objects.Get(guid) is not { } item) continue;
                var slot = new UiItemSlot
                {
                    SlotIndex = page.List.GetNumUIItems(),
                    SpriteResolve = page.List.SpriteResolve,
                    AllowDragSource = true,
                    SourceKind = ItemDragSource.Inventory,
                    ShowTradeOverlay = true,
                    TradeOverlaySprite = TradeOverlaySprite,
                    TooltipTextResolve = id => ItemTooltipCaption.Resolve(
                        _bindings.Objects, id, _bindings.ResolveAppropriateName),
                };
                slot.SetItem(guid, _bindings.ResolveIcon(
                    item.Type, item.IconId, item.IconUnderlayId, item.IconOverlayId, item.Effects));
                page.List.AddItem(slot);
            }
        }

        if (page.Button is not null)
            page.Button.Enabled = CanSubmit(page.Tab, profile, staged.Count);
    }

    private static bool CanSubmit(
        HousePurchaseTab tab, GameEvents.HouseProfile profile, int stagedCount) =>
        stagedCount > 0
        && !(tab == HousePurchaseTab.Buy && profile.OwnerGuid != 0u)
        && !(tab == HousePurchaseTab.Rent && profile.MaintenanceFree);

    private void Submit(HousePurchaseTab tab)
    {
        if (_disposed || _bindings.Purchase.Snapshot.Profile is not { } profile) return;
        uint[] staged = _bindings.Purchase.GetStaged(tab).ToArray();
        if (!CanSubmit(tab, profile, staged.Length)) return;
        if (tab == HousePurchaseTab.Buy)
            _bindings.BuyHouse(profile.SlumlordGuid, staged);
        else
            _bindings.RentHouse(profile.SlumlordGuid, staged);
    }

    private static void SetLines(UiText? text, IReadOnlyList<string> lines)
    {
        if (text is null) return;
        UiText.Line[] rendered = lines
            .Select(line => new UiText.Line(line, Vector4.One))
            .ToArray();
        text.LinesProvider = () => rendered;
    }

    private Page? PageFor(UiItemList list) =>
        ReferenceEquals(list, _buy.List) ? _buy
        : ReferenceEquals(list, _rent.List) ? _rent
        : null;

    public void OnDragLift(UiItemList sourceList, UiItemSlot sourceCell, ItemDragPayload payload)
    {
        if (PageFor(sourceList) is { } page)
            _bindings.Unstage(page.Tab, payload.ObjId);
    }

    public ItemDragAcceptance OnDragOver(UiItemList targetList, UiItemSlot targetCell, ItemDragPayload payload)
        => PageFor(targetList) is not null && payload.SourceKind == ItemDragSource.Inventory
            ? ItemDragAcceptance.Accept
            : ItemDragAcceptance.Reject;

    public void HandleDropRelease(UiItemList targetList, UiItemSlot targetCell, ItemDragPayload payload)
    {
        if (PageFor(targetList) is not { } page
            || payload.SourceKind != ItemDragSource.Inventory)
            return;
        HouseStageResult result = _bindings.Stage(page.Tab, payload.ObjId);
        if (result != HouseStageResult.Staged)
            _bindings.Report(HousePurchaseText.Refusal(result));
    }

    private void OnObjectRemoved(ClientObject item) => _bindings.Revalidate();
    private void OnObjectMoved(ClientObjectMove move) => _bindings.Revalidate();
    private void OnContainerContentsReplaced(uint containerId) => _bindings.Revalidate();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _bindings.Objects.ObjectRemoved -= OnObjectRemoved;
        _bindings.Objects.ObjectMoved -= OnObjectMoved;
        _bindings.Objects.ContainerContentsReplaced -= OnContainerContentsReplaced;
        foreach (Page page in new[] { _buy, _rent })
        {
            RetailTabBinding.SetClick(page.Button, null);
            RetailTabBinding.SetClick(page.TabElement, null);
            page.List.ExamineItemRequested = null;
        }
        RetailTabBinding.SetClick(_closeButton, null);
    }
}
```

If a member used here has a different name in this codebase (for example `UiItemList.Scroll`, `DeferLayout`, `UiElement.Visible` or `Enabled`), use the one `SalvageUiController` uses. It is the reference for every list and slot call.

- [ ] **Step 4: Run the Verify command.** Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Layout/HousePurchaseUiController.cs tests/AcDream.App.Tests/UI/Layout/HousePurchaseUiControllerTests.cs
git commit -m "feat(housing): slumlord Buy/Maintenance window controller

Claude-Session: https://claude.ai/code/session_01WfC1ZE7JDErNAj3YviNNat"
```

---

### Task 8: Mount the window and check it against the real dats

**Goal:** The client mounts the window from the dats, ticks it, publishes its button commands, and an installed-dat test proves the real layout binds and carries the retail captions.

**Files:**
- Modify: `src/AcDream.App/UI/WindowNames.cs`
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs`
- Modify: `src/AcDream.App/Composition/InteractionRetainedUiComposition.cs`
- Test: `tests/AcDream.App.Tests/UI/Layout/HousePurchaseLayoutInstalledDatTests.cs`

**Acceptance Criteria:**
- [ ] `WindowNames.HousePurchase == "house-purchase"`, and it is in `stateManagedVisibilityWindows`.
- [ ] `SocialRuntimeBindings` has an optional `HousePurchase`, and composition passes `d.Runtime.HousePurchaseOwner`.
- [ ] `MountHousePurchase()` runs after `MountSalvage()`, and `HousePurchaseController?.Tick()` runs after `SecureTradeController?.Tick()`.
- [ ] The installed-dat test passes: both item strips are `UiItemList`, both requirement and owner texts are `UiText`, both buttons, both tabs and the close button exist, `Bind` returns non-null, and the tabs and buttons show "Buy" / "Maintenance".
- [ ] The solution builds and the App tests touched so far pass.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~HousePurchase|FullyQualifiedName~WindowNames|FullyQualifiedName~RetailUiRuntime' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-house-purchase` → all pass

**Steps:**

- [ ] **Step 1: Write the failing installed-dat test**

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Core.Items;
using AcDream.Runtime.Gameplay;
using DatReaderWriter;
using DatReaderWriter.Options;

namespace AcDream.App.Tests.UI.Layout;

[Trait("Lane", "InstalledDat")]
public sealed class HousePurchaseLayoutInstalledDatTests
{
    [InstalledDatFact]
    public void Layout2100000E_BindsAndCarriesTheRetailCaptions()
    {
        string datDirectory = Environment.GetEnvironmentVariable("ACDREAM_DAT_DIR")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Documents",
                "Asheron's Call");
        using var dats = new DatCollection(datDirectory, DatAccessType.Read);
        ImportedLayout layout = Assert.IsType<ImportedLayout>(LayoutImporter.Import(
            dats,
            HousePurchaseUiController.LayoutId,
            HousePurchaseUiController.RootId,
            static _ => (0u, 0, 0),
            null));

        Assert.IsType<UiItemList>(layout.FindElement(HousePurchaseUiController.BuyItemsListId));
        Assert.IsType<UiItemList>(layout.FindElement(HousePurchaseUiController.MaintenanceItemsListId));
        Assert.IsType<UiText>(layout.FindElement(HousePurchaseUiController.BuyRequirementsTextId));
        Assert.IsType<UiText>(layout.FindElement(HousePurchaseUiController.MaintenanceRequirementsTextId));
        Assert.IsType<UiText>(layout.FindElement(HousePurchaseUiController.BuyOwnerId));
        Assert.IsType<UiText>(layout.FindElement(HousePurchaseUiController.MaintenanceOwnerId));
        Assert.NotNull(layout.FindElement(HousePurchaseUiController.CloseButtonId));

        Assert.Equal("Buy", Caption(layout.FindElement(HousePurchaseUiController.BuyTabId)));
        Assert.Equal("Maintenance", Caption(layout.FindElement(HousePurchaseUiController.MaintenanceTabId)));
        Assert.Equal("Buy", Caption(layout.FindElement(HousePurchaseUiController.BuyButtonId)));
        Assert.Equal("Maintenance", Caption(layout.FindElement(HousePurchaseUiController.MaintenanceButtonId)));

        var objects = new ClientObjectTable();
        var state = new RuntimeHousePurchaseState(objects, static () => 0u);
        using HousePurchaseUiController? controller = HousePurchaseUiController.Bind(layout, new(
            state.View, objects, state.Stage, state.Unstage, () => state.Revalidate(), state.Close,
            static (_, _) => { }, static (_, _) => { }, static (_, _, _, _, _) => 0u,
            static _ => { }, static _ => { }, ItemTooltipCaptionNames.Resolve));
        Assert.NotNull(controller);
    }

    private static string? Caption(UiElement? element) => element switch
    {
        UiButton button => button.Label,
        UiText text => string.Concat(text.LinesProvider().Select(line => line.Text)),
        _ => null,
    };
}
```

If `Caption` comes back null because the imported widget keeps its caption in another member, look at where `DatWidgetFactory` stores the string it resolves from property `0x17u`, and read that member instead. Do not hard-code the captions in the controller. If the importer drops the caption completely for this element type, stop and report it, because that is an importer gap and outside this plan.

- [ ] **Step 2: Run it.** Run: `MAC_TEST_FILTER='FullyQualifiedName~HousePurchaseLayoutInstalledDatTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-house-purchase`. Expected: PASS if Tasks 1-7 are right; the runner sets `ACDREAM_RUN_INSTALLED_DAT_TESTS=1`. A failing type assertion means the controller binds the wrong element type, so fix the controller's casts to match the imported type and rerun Task 7's tests.

- [ ] **Step 3: Add the window name.** In `WindowNames.cs`, after `public const string Salvage = "salvage";`:

```csharp
    public const string HousePurchase = "house-purchase";
```

- [ ] **Step 4: Add the binding.** In `RetailUiRuntime.cs`, extend `SocialRuntimeBindings` after `AcDream.Runtime.Gameplay.IRuntimeTradeView? Trade = null`:

```csharp
    AcDream.Runtime.Gameplay.IRuntimeTradeView? Trade = null,
    AcDream.Runtime.Gameplay.RuntimeHousePurchaseState? HousePurchase = null);
```

In `InteractionRetainedUiComposition.cs`, change `Trade: d.Runtime.Trade),` to:

```csharp
                    Trade: d.Runtime.Trade,
                    HousePurchase: d.Runtime.HousePurchaseOwner),
```

- [ ] **Step 5: Mount, tick, and manage visibility** in `RetailUiRuntime.cs`:
  - Add `WindowNames.HousePurchase,` after `WindowNames.Salvage,` in `stateManagedVisibilityWindows`.
  - Add `MountHousePurchase();` after `MountSalvage();`.
  - Add `HousePurchaseController?.Tick();` after `SecureTradeController?.Tick();`.
  - Add the property and mount method after `MountSecureTrade()`:

```csharp
    public Layout.HousePurchaseUiController? HousePurchaseController { get; private set; }

    private void MountHousePurchase()
    {
        if (_bindings.Social.HousePurchase is not { } purchase)
        {
            Console.WriteLine("[UI] house purchase: no runtime house purchase state bound.");
            return;
        }

        ImportedLayout? layout;
        uint emptySlotSprite;
        lock (_bindings.Assets.DatLock)
        {
            layout = LayoutImporter.Import(
                _bindings.Assets.Dats,
                Layout.HousePurchaseUiController.LayoutId,
                Layout.HousePurchaseUiController.RootId,
                _bindings.Assets.ResolveSprite,
                _bindings.Assets.DefaultFont,
                _bindings.Assets.ResolveFont);
            emptySlotSprite = ItemListCellTemplate.ResolveEmptySprite(
                _bindings.Assets.Dats,
                Layout.HousePurchaseUiController.LayoutId,
                Layout.HousePurchaseUiController.BuyItemsListId);
        }
        if (layout is null)
        {
            Console.WriteLine(
                "[UI] house purchase: LayoutDesc 0x2100000E root 0x1000008C not found.");
            return;
        }

        Layout.HousePurchaseUiController? controller =
            Layout.HousePurchaseUiController.Bind(
                layout,
                new Layout.HousePurchaseUiController.Bindings(
                    Purchase: purchase.View,
                    Objects: _bindings.Inventory.Objects,
                    Stage: purchase.Stage,
                    Unstage: purchase.Unstage,
                    Revalidate: () => purchase.Revalidate(),
                    Close: purchase.Close,
                    BuyHouse: (slumlord, items) => _bindings.Options.CommandBus().Publish(
                        new BuyHouseRuntimeCmd(slumlord, items)),
                    RentHouse: (slumlord, items) => _bindings.Options.CommandBus().Publish(
                        new RentHouseRuntimeCmd(slumlord, items)),
                    ResolveIcon: _bindings.Inventory.ResolveIcon,
                    SetWindowVisible: visible =>
                    {
                        if (visible) Host.ShowWindow(WindowNames.HousePurchase);
                        else Host.HideWindow(WindowNames.HousePurchase);
                    },
                    Report: _bindings.Options.DisplaySystemMessage,
                    ResolveAppropriateName: ResolveAppropriateItemName,
                    EmptySlotSprite: emptySlotSprite));
        if (controller is null)
        {
            Console.WriteLine("[UI] house purchase: required authored item lists are missing.");
            return;
        }

        UiElement root = layout.Root;
        RetailWindowFrame.Mount(
            Host.Root,
            root,
            _bindings.Assets.ResolveSprite,
            new RetailWindowFrame.Options
            {
                WindowName = WindowNames.HousePurchase,
                Chrome = RetailWindowChrome.NineSlice,
                Left = MathF.Max(0f, (Host.Root.Width - root.Width) * 0.5f),
                Top = MathF.Max(0f, (Host.Root.Height - root.Height) * 0.5f),
                ContentWidth = root.Width,
                ContentHeight = root.Height,
                MinWidth = root.Width,
                MinHeight = root.Height,
                Visible = false,
                ResizeX = false,
                ResizeY = false,
                ConstrainResizeToParent = true,
            });

        HousePurchaseController = controller;
        Host.WindowManager.AttachController(WindowNames.HousePurchase, controller);
        Console.WriteLine("[UI] retail house purchase window from LayoutDesc 0x2100000E.");
    }
```

If `BuyHouseRuntimeCmd` is not visible here, add `using AcDream.App.Net;` (the trade commands come from the same namespace, so it is probably already imported).

- [ ] **Step 6: Build and run the Verify command.** `dotnet build ~/code/OpenAC/OpenAC-house-purchase -c Release` → 0 errors. If a test lists every window name or every state-managed window (search the tests for `WindowNames.Salvage`), add `HousePurchase` there too. Run Verify → PASS.

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.App/UI/WindowNames.cs src/AcDream.App/UI/RetailUiRuntime.cs src/AcDream.App/Composition/InteractionRetainedUiComposition.cs tests/AcDream.App.Tests/UI/Layout/HousePurchaseLayoutInstalledDatTests.cs
git commit -m "feat(housing): mount the retail slumlord window

Claude-Session: https://claude.ai/code/session_01WfC1ZE7JDErNAj3YviNNat"
```

---

### Task 9: Gate, merge, live check, deliver

**Goal:** The feature is green on fork `main`, live-checked by the user on local ACE, pushed, and offered upstream.

**Files:**
- No new source files. Merges only, plus memory notes under `~/.claude/projects/-Users-davidsmith-code-OpenAC-OpenAC/memory/`.

**Acceptance Criteria:**
- [ ] The full `run-mac-tests.sh` run on the feature branch shows no failures beyond the 11 already known on `upstream-main`, and the run on fork `main` after the merge shows 0 failures.
- [ ] The user ran the spec's live check (steps 1-5) and reported the result. The client was not launched before they said they were available.
- [ ] `feat/house-purchase-window` merged into fork `main` and `rynth`, and all three pushed to origin. The worktrees are cleaned up.
- [ ] The upstream PR is opened only if the user asks, with "Part of #93" in the body.

**Verify:** `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC` on merged `main` → `Failed: 0`

**Steps:**

- [ ] **Step 1: Full run on the branch.** `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-house-purchase`. Compare any failure with the 11 known `upstream-main` Mac failures (the ones fixed by PR 241). Anything new is a regression, so fix it before going on.
- [ ] **Step 2: Merge into fork `main`.** Follow BRANCHING.md, as for upstream PR 242: in the main worktree (check `git status` is clean first, and ask the user if another session has it busy), run `git merge --no-ff feat/house-purchase-window -m "Merge branch 'feat/house-purchase-window' (slumlord Buy/Maintenance window)"`. Then do the same in the rynth worktree `~/code/OpenAC/OpenAC-gateway-rynth`. Run the Verify command on `main` → `Failed: 0`.
- [ ] **Step 3: Live check.** Ask the user whether they are available. Only after they say yes, follow `live-testing-rules` (local ACE 10.10.20.20 via `~/OpenAC-dev/dev-client.env`) and walk through the spec's live check steps 1-5 with them. Record what they report. If a step fails, open a fix task here before pushing.
- [ ] **Step 4: Push.** Push `main`, `rynth` and `feat/house-purchase-window` to origin.
- [ ] **Step 5: Upstream PR, only when the user asks.** Open it from `feat/house-purchase-window` against `eriknihlen/OpenAC` main as davidc-smith. The title is "Housing: slumlord Buy/Maintenance window (Part of #93)". The body covers what it does, the captions still to check, and the test evidence, and ends with `https://claude.ai/code/session_01WfC1ZE7JDErNAj3YviNNat`.
- [ ] **Step 6: Tidy and remember.** Remove the `OpenAC-house-purchase` and `OpenAC-house-spec` worktrees once merged and pushed. Update `housing-issue-93.md` (gap 2 done: main commit, PR number) and the `MEMORY.md` line.
