# RynthAi Skills Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The RynthAi dashboard's Skills panel (RynthCore's own `SkillsFace`) opens in OpenAC with live skills / attributes / vitals / buffs and working raise and train buttons.

**Architecture:** RynthCore's Skills sources compile unchanged in `AcDream.Rynth.EngineUi`; engine types they need are stand-ins fed by delegates the host's `RynthChatHookup` sets from new `IRynthGameView` members. Their dat lookups (`Environment.ProcessPath`) are redirected after compile to a stand-in that points at OpenAC's dat folder, by a new `--redirect-member` mode of `tools/RynthInteropRedirect`.

**Tech Stack:** .NET 10, C#, ImGui.NET, Mono.Cecil (redirect tool), xunit.

**Spec:** `docs/superpowers/specs/2026-10-19-rynth-ai-skills-design.md`

## Global Constraints

- Never edit anything under `external/RynthCore` or `external/RynthSuite`; their files are compiled as-is (memory: rynth-no-rewrite).
- Build with the SDK in `~/.dotnet`: `export PATH=~/.dotnet:$PATH DOTNET_ROOT=~/.dotnet`.
- Stand-in style: same namespace and member names as RynthCore's type, a short `<summary>` saying what RynthCore does and what OpenAC does instead, host-set delegates that answer empty when unset (see `StandIns/ChatEngineStandIns.cs`).
- Host style: every view call from a stand-in delegate goes through `RynthChatHookup.Guard(name, func, fallback)`; Dispose unsets only delegates that still point at this hookup.
- RynthCore index conventions: skills 1..54 by skill id; attributes 1..6; vitals 1..3 (health, stamina, mana); raise target for vitals is the maximum's id 1/3/5 (= `PluginVitalInfo.StatId`).
- No contract bump: nothing in `AcDream.Plugin.Abstractions` changes.
- Don't run `tools/update-package-locks.ps1`.
- Commit message trailer: `Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz`.

**User decisions (already made):**
- Skills panel first, Dungeon Map after (2026-10-19).
- Dat location: reuse the dat folder already given to RynthAi for LOS/collision (`ClientExtensionContext.DatDirectory`), applied to the engine side via a build-time `Environment.ProcessPath` member redirect (2026-10-19: "yes, go ahead").
- Live checks happen later, when the user says so (memory: ask-before-live-client).

---

### Task 1: `--redirect-member` in RynthInteropRedirect

**Goal:** The redirect tool can retarget calls to one static member of a referenced type to a same-named static method on a stand-in type in the assembly.

**Files:**
- Modify: `tools/RynthInteropRedirect/Program.cs` (option parsing, `Redirect`, new `RedirectMembers`, usage/summary text)
- Create: `tests/AcDream.Tools.RynthInteropRedirect.Tests/RedirectMemberTests.cs`

**Acceptance Criteria:**
- [ ] `--redirect-member Ext.Env::get_Path=App.PathStandIn` turns every `call Ext.Env::get_Path()` in App into `call App.PathStandIn::get_Path()`; calls to `Ext.Env::get_Other` are untouched; the output line says `1 member call(s) redirected`.
- [ ] Calls inside the stand-in type itself are not rewritten (its fallback may call the real member).
- [ ] A stand-in without a public static method of that name, return type and parameter types fails with exit 1 naming it.
- [ ] A rerun reports `0 member call(s) redirected` and leaves the file byte-identical.
- [ ] A malformed value (no `::` or no `=`) or the same `from` twice is a usage error (exit 2).
- [ ] The existing `RedirectTypeTests` still pass.

**Verify:** `dotnet test tests/AcDream.Tools.RynthInteropRedirect.Tests` → all pass (11 existing + new).

**Steps:**

- [ ] **Step 1: Write the failing tests** in `RedirectMemberTests.cs`, building two tiny assemblies with Cecil like `RedirectTypeTests` does:

```csharp
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace AcDream.Tools.RynthInteropRedirect.Tests;

/// <summary>
/// <c>--redirect-member</c> over two small assemblies built here: Ext (standing for
/// System.Runtime's Environment) and App (code calling it, with a stand-in).
/// </summary>
public sealed class RedirectMemberTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rynth-member-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string AppPath => Path.Combine(_dir, "App.dll");

    private static readonly string[] Map = ["--redirect-member", "Ext.Env::get_Path=App.PathStandIn"];

    [Fact]
    public void CallsToTheMemberMoveToTheStandInAndOthersStay()
    {
        Build();

        (int code, string output, string error) = Run(Map);

        Assert.True(code == 0, error);
        Assert.Contains("1 member call(s) redirected", output, StringComparison.Ordinal);
        using ModuleDefinition app = Read();
        string[] calls = Calls(app, "App.User", "Find");
        Assert.Equal(["System.String App.PathStandIn::get_Path()", "System.String Ext.Env::get_Other()"], calls);
    }

    [Fact]
    public void TheStandInsOwnCallIsLeftAlone()
    {
        Build();

        Assert.Equal(0, Run(Map).Code);

        using ModuleDefinition app = Read();
        Assert.Equal(["System.String Ext.Env::get_Path()"], Calls(app, "App.PathStandIn", "get_Path"));
    }

    [Fact]
    public void AMissingStandInMemberFailsAndNamesIt()
    {
        Build(standInHasPath: false);

        (int code, _, string error) = Run(Map);

        Assert.Equal(1, code);
        Assert.Contains("App.PathStandIn has no public static System.String get_Path()", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ARerunFindsNothingToDo()
    {
        Build();
        Assert.Equal(0, Run(Map).Code);
        byte[] once = File.ReadAllBytes(AppPath);

        (int code, string output, string error) = Run(Map);

        Assert.True(code == 0, error);
        Assert.Contains("0 member call(s) redirected", output, StringComparison.Ordinal);
        Assert.Equal(once, File.ReadAllBytes(AppPath));
    }

    [Theory]
    [InlineData("Ext.Env.get_Path=App.PathStandIn")]
    [InlineData("Ext.Env::get_Path")]
    public void AMalformedMapIsAUsageError(string map)
    {
        Build();

        (int code, _, string error) = Run(["--redirect-member", map]);

        Assert.Equal(2, code);
        Assert.StartsWith("usage:", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameMemberTwiceIsAUsageError()
    {
        Build();

        (int code, _, string error) = Run([.. Map, .. Map]);

        Assert.Equal(2, code);
        Assert.StartsWith("usage:", error, StringComparison.Ordinal);
    }

    private static string[] Calls(ModuleDefinition app, string type, string method) =>
        app.GetType(type).Methods.Single(m => m.Name == method).Body.Instructions
            .Select(i => i.Operand).OfType<MethodReference>().Select(m => m.FullName).ToArray();

    private (int Code, string Output, string Error) Run(string[] options)
    {
        string references = Path.Combine(_dir, "refs.txt");
        File.WriteAllText(references, Path.Combine(_dir, "Ext.dll"));
        using var output = new StringWriter();
        using var error = new StringWriter();
        int code = Program.Run([AppPath, "-", references, .. options], output, error);
        return (code, output.ToString(), error.ToString());
    }

    private ModuleDefinition Read()
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(_dir);
        return ModuleDefinition.ReadModule(new MemoryStream(File.ReadAllBytes(AppPath)), new ReaderParameters { AssemblyResolver = resolver });
    }

    private void Build(bool standInHasPath = true)
    {
        using AssemblyDefinition extAssembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Ext", new Version(1, 0)), "Ext", ModuleKind.Dll);
        ModuleDefinition ext = extAssembly.MainModule;
        var env = new TypeDefinition("Ext", "Env", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, ext.TypeSystem.Object);
        ext.Types.Add(env);
        MethodDefinition getPath = StaticGetter(env, "get_Path", ext.TypeSystem.String);
        MethodDefinition getOther = StaticGetter(env, "get_Other", ext.TypeSystem.String);
        extAssembly.Write(Path.Combine(_dir, "Ext.dll"));

        using AssemblyDefinition appAssembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("App", new Version(1, 0)), "App", ModuleKind.Dll);
        ModuleDefinition app = appAssembly.MainModule;
        MethodReference path = app.ImportReference(getPath), other = app.ImportReference(getOther);

        var standIn = new TypeDefinition("App", "PathStandIn", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, app.TypeSystem.Object);
        app.Types.Add(standIn);
        var standInGet = new MethodDefinition(standInHasPath ? "get_Path" : "get_Elsewhere",
            MethodAttributes.Public | MethodAttributes.Static, app.TypeSystem.String);
        standIn.Methods.Add(standInGet);
        ILProcessor s = standInGet.Body.GetILProcessor();
        s.Emit(OpCodes.Call, path);   // the fallback: the real member
        s.Emit(OpCodes.Ret);

        var user = new TypeDefinition("App", "User", TypeAttributes.Public, app.TypeSystem.Object);
        app.Types.Add(user);
        var find = new MethodDefinition("Find", MethodAttributes.Public | MethodAttributes.Static, app.TypeSystem.Void);
        user.Methods.Add(find);
        ILProcessor f = find.Body.GetILProcessor();
        f.Emit(OpCodes.Call, path);
        f.Emit(OpCodes.Pop);
        f.Emit(OpCodes.Call, other);
        f.Emit(OpCodes.Pop);
        f.Emit(OpCodes.Ret);
        appAssembly.Write(AppPath);
    }

    private static MethodDefinition StaticGetter(TypeDefinition type, string name, TypeReference returns)
    {
        var method = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static, returns);
        type.Methods.Add(method);
        ILProcessor il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        return method;
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test tests/AcDream.Tools.RynthInteropRedirect.Tests --filter RedirectMemberTests`
Expected: FAIL (usage error, exit 2: the option is unknown).

- [ ] **Step 3: Implement.** In `Program.Run` add a `memberMaps` list and parse the option (before the final `else`):

```csharp
            else if (args[i] == "--redirect-member" && i + 1 < args.Length
                     && args[i + 1].Split('=') is [{ Length: > 0 } member, { Length: > 0 } toType]
                     && member.Split("::") is [{ Length: > 0 } fromType, { Length: > 0 } memberName]
                     && !memberMaps.Any(m => m.FromType == fromType && m.Member == memberName))
            {
                memberMaps.Add((fromType, memberName, toType));
                i++;
            }
```

Declare `var memberMaps = new List<(string FromType, string Member, string To)>();` next to `typeMaps`, extend `Usage` with ` [--redirect-member <type>::<member>=<stand-in type>]...`, pass `memberMaps` to `Redirect`, and append `{(memberMaps.Count > 0 ? $", {members} member call(s) redirected" : "")}` to the output line (Redirect now returns a fifth count `Members`). In `Redirect`, after `RedirectTypes`:

```csharp
        int members = RedirectMembers(module, memberMaps);
        if (redirected + inerted + swaps + types + members > 0)
            module.Write(new WriterParameters { WriteSymbols = hasSymbols, DeterministicMvid = true });
```

and add:

```csharp
    /// <summary>
    /// Every call to a mapped static member (<c>Type::member</c> of a referenced type)
    /// becomes a call to the public static method of the same name, return and
    /// parameter types on the stand-in type (in the module). The stand-in's own
    /// methods are skipped, so its fallback can still call the real member.
    /// Returns how many calls were rewritten.
    /// </summary>
    private static int RedirectMembers(ModuleDefinition module, IReadOnlyList<(string FromType, string Member, string To)> maps)
    {
        int rewritten = 0;
        foreach ((string fromType, string member, string to) in maps)
        {
            TypeDefinition standIn = module.GetType(to) ?? throw new InvalidOperationException($"{to} is not in {module.Name}");
            MethodReference? target = null;
            foreach (TypeDefinition type in module.GetTypes())
            {
                if (type == standIn)
                    continue;
                foreach (MethodDefinition method in type.Methods.Where(m => m.HasBody))
                {
                    foreach (Instruction instruction in method.Body.Instructions)
                    {
                        if (instruction.Operand is not MethodReference called || called.HasThis
                            || called.Name != member || called.DeclaringType.FullName != fromType)
                            continue;
                        target ??= module.ImportReference(FindStatic(standIn, called));
                        instruction.Operand = target;
                        rewritten++;
                    }
                }
            }
        }

        return rewritten;
    }

    private static MethodDefinition FindStatic(TypeDefinition standIn, MethodReference called) =>
        standIn.Methods.FirstOrDefault(candidate => candidate.IsPublic && candidate.IsStatic && candidate.Name == called.Name
            && candidate.ReturnType.FullName == called.ReturnType.FullName
            && candidate.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(called.Parameters.Select(p => p.ParameterType.FullName)))
        ?? throw new InvalidOperationException(
            $"{standIn.FullName} has no public static {called.ReturnType.FullName} {called.Name}({string.Join(", ", called.Parameters.Select(p => p.ParameterType.FullName))})");
```

Update the class `<summary>` with one sentence describing `--redirect-member`, matching the existing prose.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/AcDream.Tools.RynthInteropRedirect.Tests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add tools/RynthInteropRedirect/Program.cs tests/AcDream.Tools.RynthInteropRedirect.Tests/RedirectMemberTests.cs
git commit -m "feat(rynth): redirect tool retargets one static member's calls"
```

---

### Task 2: Player progress, stat mods and advancement on the game view

**Goal:** `IRynthGameView` answers the Skills panel's data (`PlayerProgress`, `PlayerStatMods`) and sends raises / training (`RequestAdvancement`), implemented by `RuntimeRynthGameView` from the runtime and the automation surface.

**Files:**
- Modify: `src/AcDream.Rynth.Host/RynthStateTypes.cs` (new records)
- Modify: `src/AcDream.Rynth.Host/IRynthGameView.cs` (three members)
- Modify: `src/AcDream.Rynth.Host/RuntimeRynthGameView.cs` (implementation; share the expiry conversion with `PlayerEnchantments`)
- Modify: `tests/AcDream.Rynth.Host.Tests/FakeGameView.cs` (settable fakes)
- Modify: `tests/AcDream.App.Tests/Extensions/FakeRynthAutomation.cs` (`FakeCharacter`: Level, XP, luminance, `Advancements` log)
- Test: `tests/AcDream.App.Tests/Extensions/RuntimeRynthGameViewTests.cs`

**Acceptance Criteria:**
- [ ] `PlayerProgress` is null before login / without a player description; once in, it lists every skill in `LocalPlayer.Skills` with class = snapshot `Status`, ranks, XP spent, init, and buffed/base from the surface's skill (LevelKnown false when the surface lacks it); attributes 1..6 and vitals 1..3 with ranks, init (`Start`), XP, buffed, base; level, `AvailableSkillCredits` (int property 24), total/unassigned XP, luminance/max, vitae.
- [ ] `PlayerStatMods` lists only bucket 1/2 records whose `StatModType` is set, with type/key/value/category and the same expiry conversion as `PlayerEnchantments` (`double.MaxValue` for no end).
- [ ] `RequestAdvancement(kind 1..4, statId, amount)` calls the surface's `RequestAdvancement` with Attribute/Vital/Skill/TrainSkill and returns `Accepted`; kind 0 or >4 returns false without a call.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter RuntimeRynthGameViewTests` → all pass.

**Steps:**

- [ ] **Step 1: Add the types** to `RynthStateTypes.cs`:

```csharp
/// <summary>One skill as RynthCore's Skills panel reads it: class 1 untrained, 2 trained, 3 specialized.</summary>
public readonly record struct RynthSkillProgress(
    uint SkillId, uint Class, uint Ranks, uint XpSpent, uint Init, bool LevelKnown, int Buffed, int Base);

/// <summary>An attribute (id 1..6) or vital (id 1..3: health, stamina, mana); vitals' buffed and base are maximums.</summary>
public readonly record struct RynthStatProgress(uint Id, uint Ranks, uint Init, uint XpSpent, uint Buffed, uint Base);

/// <summary>The player's progress for the Skills panel: one read, never changed after.</summary>
public sealed record RynthPlayerProgress(
    IReadOnlyList<RynthSkillProgress> Skills,
    IReadOnlyList<RynthStatProgress> Attributes,
    IReadOnlyList<RynthStatProgress> Vitals,
    int Level,
    int SkillCredits,
    long TotalXp,
    long UnassignedXp,
    long Luminance,
    long MaxLuminance,
    float Vitae);

/// <summary>An enchantment's stat change (ACE EnchantmentTypeFlags type, key, value) and its spell category.</summary>
public readonly record struct RynthStatMod(uint SpellId, double ExpiryServerTime, uint Type, uint Key, float Value, uint Category);
```

and to `IRynthGameView` (after `PlayerEnchantments`), each with a one-line `///` summary like its neighbours:

```csharp
    /// <summary>The player's skills, attributes, vitals, level, XP and credits; null when not in the world.</summary>
    RynthPlayerProgress? PlayerProgress { get; }

    /// <summary>The player's enchantments that carry a stat change; null when not in the world.</summary>
    IReadOnlyList<RynthStatMod>? PlayerStatMods { get; }

    /// <summary>
    /// Raise or train as RynthCore's PlayerTraining does: kind 1 attribute, 2 vital (stat id 1/3/5),
    /// 3 skill (amount = XP), 4 train a skill (amount = credits). True when the client sent it.
    /// </summary>
    bool RequestAdvancement(int kind, uint statId, uint amount);
```

- [ ] **Step 2: Write failing view tests** in `RuntimeRynthGameViewTests.cs`:

```csharp
    [Fact]
    public void PlayerProgressIsTheDescriptionsRecordsWithTheSurfacesValues()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        LocalPlayerState local = runtime.CharacterOwner.LocalPlayer;
        var properties = new PropertyBundle();
        properties.Ints[24] = 7;   // AvailableSkillCredits
        local.OnProperties(properties);
        local.OnSkillUpdate(6, ranks: 10, status: 3, xp: 5000, init: 10, resistance: 0, lastUsed: 0, formulaBonus: 100);
        local.OnSkillUpdate(15, ranks: 0, status: 1, xp: 0, init: 0, resistance: 0, lastUsed: 0, formulaBonus: 50);
        local.OnAttributeUpdate(1, ranks: 20, start: 100, xp: 900);
        local.OnVitalUpdate(1, ranks: 4, start: 0, xp: 300, current: 50);
        var automation = new FakeRynthAutomation();
        FakeCharacter c = automation.CharacterArea;
        c.SkillList.Add(new PluginSkillInfo(6, "Melee Defense", PluginSkillTraining.Specialized, 140) { Base = 120 });
        c.AttributeList.Add(new PluginAttributeInfo(0, "Strength", 130) { Base = 120, Ranks = 20, ExperienceSpent = 900 });
        c.VitalList.Add(new PluginVitalInfo(0, "Health", 50, 80) { Base = 70, Ranks = 4, ExperienceSpent = 300 });
        (c.Level, c.TotalExperience, c.UnassignedExperience, c.AvailableLuminance, c.MaximumLuminance, c.VitaeMultiplier) =
            (30, 1_000_000, 2500, 40, 500, 0.95f);
        var view = new RuntimeRynthGameView(runtime, automation: automation);

        RynthPlayerProgress p = view.PlayerProgress!;

        Assert.Equal(
            [new RynthSkillProgress(6, 3, 10, 5000, 10, true, 140, 120), new RynthSkillProgress(15, 1, 0, 0, 0, false, 0, 0)],
            p.Skills.OrderBy(s => s.SkillId));
        Assert.Equal([new RynthStatProgress(1, 20, 100, 900, 130, 120)], p.Attributes);
        Assert.Equal([new RynthStatProgress(1, 4, 0, 300, 80, 70)], p.Vitals);
        Assert.Equal((30, 7, 1_000_000L, 2500L, 40L, 500L, 0.95f),
            (p.Level, p.SkillCredits, p.TotalXp, p.UnassignedXp, p.Luminance, p.MaxLuminance, p.Vitae));
    }

    [Fact]
    public void PlayerProgressAndStatModsAreNullBeforeLogin()
    {
        var view = new RuntimeRynthGameView(GameRuntimeTestFactory.Create(), automation: new FakeRynthAutomation());

        Assert.Null(view.PlayerProgress);
        Assert.Null(view.PlayerStatMods);
    }

    [Fact]
    public void PlayerStatModsAreTheStatChangingEnchantments()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        double clock = runtime.EffectClock.NowSeconds;
        Spellbook book = runtime.CharacterOwner.Spellbook;
        book.OnEnchantmentAdded(new ActiveEnchantmentRecord(0x1234, 1, 60, 0x50000001,
            StatModType: 0x10 | 0x8000, StatModKey: 6, StatModValue: 35f, Bucket: 2, StartTime: clock, SpellCategory: 77));
        book.OnEnchantmentAdded(new ActiveEnchantmentRecord(0x1300, 1, -1, 0x50000001,
            StatModType: 0x1 | 0x8000, StatModKey: 1, StatModValue: 10f, Bucket: 1, StartTime: clock, SpellCategory: 3));
        book.OnEnchantmentAdded(new ActiveEnchantmentRecord(0x1400, 1, 60, 0x50000001, Bucket: 1, StartTime: clock));   // no stat mod
        book.OnEnchantmentAdded(new ActiveEnchantmentRecord(0x1500, 1, -1, 0x50000001,
            StatModType: 0x800000, StatModKey: 0, StatModValue: 0.95f, Bucket: 4, StartTime: clock));   // vitae bucket
        var view = new RuntimeRynthGameView(runtime, automation: new FakeRynthAutomation());

        RynthStatMod[] mods = [.. view.PlayerStatMods!.OrderBy(m => m.SpellId)];

        Assert.Equal([0x1234u, 0x1300u], mods.Select(m => m.SpellId));
        Assert.Equal((0x8010u, 6u, 35f, 77u), (mods[0].Type, mods[0].Key, mods[0].Value, mods[0].Category));
        Assert.InRange(mods[0].ExpiryServerTime - view.ServerTime, 59.0, 60.0);
        Assert.Equal(double.MaxValue, mods[1].ExpiryServerTime);
    }

    [Theory]
    [InlineData(1, PluginAdvancementKind.Attribute)]
    [InlineData(2, PluginAdvancementKind.Vital)]
    [InlineData(3, PluginAdvancementKind.Skill)]
    [InlineData(4, PluginAdvancementKind.TrainSkill)]
    public void RequestAdvancementMapsRynthCoresKinds(int kind, PluginAdvancementKind expected)
    {
        var automation = new FakeRynthAutomation();
        var view = new RuntimeRynthGameView(GameRuntimeTestFactory.Create(), automation: automation);

        Assert.True(view.RequestAdvancement(kind, 3, 1234));

        Assert.Equal([(expected, 3u, 1234ul)], automation.CharacterArea.Advancements);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void RequestAdvancementRefusesUnknownKinds(int kind)
    {
        var automation = new FakeRynthAutomation();
        var view = new RuntimeRynthGameView(GameRuntimeTestFactory.Create(), automation: automation);

        Assert.False(view.RequestAdvancement(kind, 3, 1234));
        Assert.Empty(automation.CharacterArea.Advancements);
    }
```

Add to `FakeCharacter`:

```csharp
    public int Level { get; set; }
    public long TotalExperience { get; set; }
    public long UnassignedExperience { get; set; }
    public long AvailableLuminance { get; set; }
    public long MaximumLuminance { get; set; }
    public IReadOnlyList<PluginVitalInfo> Vitals => VitalList;
    public List<(PluginAdvancementKind Kind, uint StatId, ulong Cost)> Advancements { get; } = new();

    public PluginAdvancementResult RequestAdvancement(PluginAdvancementKind kind, uint statId, ulong cost)
    {
        Advancements.Add((kind, statId, cost));
        return new(PluginAdvancementStatus.Sent);
    }
```

(If `FakeCharacter` already declares `Vitals`, keep the existing one.) Add usings for `AcDream.Core.Player` / `AcDream.Core.Items` / `AcDream.Core.Spells` / `AcDream.Plugin.Abstractions` to the test file if missing.

- [ ] **Step 3: Run to see them fail**

Run: `dotnet test tests/AcDream.App.Tests --filter RuntimeRynthGameViewTests`
Expected: build FAIL (`PlayerProgress` not defined on `RuntimeRynthGameView`).

- [ ] **Step 4: Implement in `RuntimeRynthGameView`.** Extract the expiry conversion used by `PlayerEnchantments` into a private static helper and reuse it:

```csharp
    // A record's end in server time: double.MaxValue when it has none.
    private static double ExpiryServerTime(ActiveEnchantmentRecord record, double serverNow, double clockNow) =>
        record.Duration > 0 ? serverNow + (record.StartTime + record.Duration - clockNow) : double.MaxValue;
```

(`PlayerEnchantments` then selects `new RynthEnchantment(record.SpellId & 0xFFFFu, ExpiryServerTime(record, serverNow, clockNow))`.)

```csharp
    // As RynthCore's ReadPlayerProgressLive: the description's skill table, attribute cache and
    // vitals (ranks, innate, XP spent) with the buffed and base values the surface reports.
    public RynthPlayerProgress? PlayerProgress
    {
        get
        {
            if (!_automation.IsAvailable || PlayerId == 0
                || _runtime.CharacterOwner.LocalPlayer is not { HasDescription: true } local)
                return null;
            ICharacterInfo character = _automation.Character;
            var skills = new List<RynthSkillProgress>(local.Skills.Count);
            foreach (LocalPlayerState.SkillSnapshot s in local.Skills.Values)
            {
                bool known = character.TryGetSkill(s.SkillId, out PluginSkillInfo info);
                skills.Add(new RynthSkillProgress(
                    s.SkillId, s.Status, s.Ranks, s.Xp, s.Init, known,
                    known ? (int)info.Current : 0, known ? (int)info.Base : 0));
            }

            var attributes = new List<RynthStatProgress>(6);
            foreach (PluginAttributeInfo a in character.Attributes)
            {
                uint init = LocalPlayerState.AttributeIdToKind(a.StatId) is { } kind && local.GetAttribute(kind) is { } snap ? snap.Start : 0;
                attributes.Add(new RynthStatProgress(a.StatId, a.Ranks, init, ClampXp(a.ExperienceSpent), a.Current, a.Base));
            }

            var vitals = new List<RynthStatProgress>(3);
            foreach (PluginVitalInfo v in character.Vitals)
            {
                uint init = local.Get((LocalPlayerState.VitalKind)v.Kind) is { } snap ? snap.Start : 0;
                vitals.Add(new RynthStatProgress((uint)v.Kind + 1, v.Ranks, init, ClampXp(v.ExperienceSpent), v.Maximum, v.Base));
            }

            return new RynthPlayerProgress(
                skills, attributes, vitals, character.Level,
                local.Properties.GetInt(AvailableSkillCreditsProperty),
                character.TotalExperience, character.UnassignedExperience,
                character.AvailableLuminance, character.MaximumLuminance, character.VitaeMultiplier);
        }
    }

    private const uint AvailableSkillCreditsProperty = 24;

    private static uint ClampXp(ulong xp) => xp > uint.MaxValue ? uint.MaxValue : (uint)xp;

    // As RynthCore's EnchantmentHooks.CopyPlayerStatMods: the Mult and Add lists' records that change a stat.
    public IReadOnlyList<RynthStatMod>? PlayerStatMods
    {
        get
        {
            if (!_automation.IsAvailable || PlayerId == 0)
                return null;
            double serverNow = ServerTime;
            double clockNow = _runtime.EffectClock.NowSeconds;
            return
            [
                .. _runtime.CharacterOwner.Spellbook.ActiveEnchantmentSnapshot
                    .Where(record => record.Bucket is 1u or 2u && record.StatModType is not null && (record.SpellId & 0xFFFFu) != 0)
                    .Select(record => new RynthStatMod(
                        record.SpellId & 0xFFFFu, ExpiryServerTime(record, serverNow, clockNow),
                        record.StatModType!.Value, record.StatModKey ?? 0, record.StatModValue ?? 0f, record.SpellCategory)),
            ];
        }
    }

    public bool RequestAdvancement(int kind, uint statId, uint amount)
    {
        PluginAdvancementKind? mapped = kind switch
        {
            1 => PluginAdvancementKind.Attribute,
            2 => PluginAdvancementKind.Vital,
            3 => PluginAdvancementKind.Skill,
            4 => PluginAdvancementKind.TrainSkill,
            _ => null,
        };
        return mapped is { } k && _automation.Character.RequestAdvancement(k, statId, amount).Accepted;
    }
```

Add `using AcDream.Core.Player;` if missing. In `FakeGameView` add:

```csharp
    public RynthPlayerProgress? Progress { get; set; }
    public IReadOnlyList<RynthStatMod>? StatMods { get; set; }
    public List<(int Kind, uint StatId, uint Amount)> Advancements { get; } = new();
    public bool AdvancementAccepted { get; set; } = true;
    public RynthPlayerProgress? PlayerProgress => Read(Progress);
    public IReadOnlyList<RynthStatMod>? PlayerStatMods => Read(StatMods);

    public bool RequestAdvancement(int kind, uint statId, uint amount)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        Advancements.Add((kind, statId, amount));
        return AdvancementAccepted;
    }
```

Check for any other `IRynthGameView` implementers (`grep -rn ": IRynthGameView" src tests`) and add the members there too.

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/AcDream.App.Tests --filter RuntimeRynthGameViewTests` and `dotnet build tests/AcDream.Rynth.Host.Tests`
Expected: PASS / build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests/FakeGameView.cs tests/AcDream.App.Tests/Extensions
git commit -m "feat(rynth): game view reads player progress and stat mods, sends raises"
```

---

### Task 3: Compile RynthCore's Skills panel with stand-ins

**Goal:** `SkillsFace` and its data files compile unchanged in `AcDream.Rynth.EngineUi`, the dashboard registers it, and it draws headless; dat lookups go through `ProcessPathStandIn`.

**Files:**
- Modify: `src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj` (Compile items, RynthPanelTypes items, redirect Exec gains `--redirect-member`)
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/DashboardStandIns.cs` (remove `SkillsFace` and `PortalSpellTable` placeholders)
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/ChatEngineStandIns.cs` (`ClientObjectHooks.ProgressReader` + `ReadPlayerProgressLive`; `UiSources.Progression`)
- Create: `src/AcDream.Rynth.EngineUi/StandIns/SkillsStandIns.cs` (`MainThreadGuard`, `TimeSyncHooks`, `EnchantmentHooks`, `PlayerTraining`, `MasteryFeed`, `IltCommands`)
- Create: `src/AcDream.Rynth.EngineUi/StandIns/ProcessPathStandIn.cs`
- Modify: `src/AcDream.Rynth.EngineUi/RynthAiDashboard.cs` (register `SkillsFace`, add `UiSources.Progression` to `Sources`, call `PlayerProgressHooks.Prefetch()` in `Tick`, doc comment lists Skills)
- Test: `tests/AcDream.Rynth.Host.Tests/RynthAiDashboardTests.cs`, create `tests/AcDream.Rynth.Host.Tests/RynthSkillsStandInsTests.cs`

**Acceptance Criteria:**
- [ ] `TheirFiles` in `RynthAiDashboardTests` includes the nine new RynthCore files and `TheProjectCompilesTheirFilesAndHasNoCopies` passes.
- [ ] Toggling `SkillsFace.Title` through `PanelRouter` with `HasObservedLoginComplete` draws >100 vertices for 3 frames with nothing logged, both with no progress (empty panel) and after `PlayerProgressHooks` has published a snapshot.
- [ ] `PlayerTraining.Available`/`TrainAvailable` are false with no sender, true with one; `Raise(Vital, 3, 500, defer: true)` calls the sender with `(Vital, 3, 500)`; `TrainSkill(15, 4)` calls it with `(TrainWithCredits, 15, 4)`.
- [ ] `ProcessPathStandIn.ProcessPath` is `<dir>/acclient.exe` when `DatDirectory` is set, else `Environment.ProcessPath`.
- [ ] The built `AcDream.Rynth.EngineUi.dll` has no call to `System.Environment::get_ProcessPath` outside `ProcessPathStandIn` (test reads it with Mono.Cecil or `System.Reflection.Metadata`).

**Verify:** `dotnet test tests/AcDream.Rynth.Host.Tests` → all pass (305 before + new).

**Steps:**

- [ ] **Step 1: csproj.** Add before the `TellMemory.cs` item:

```xml
    <Compile Include="$(RynthEngine)ImGui\Panels\SkillsFace.cs" Link="RynthCore\SkillsFace.cs" />
    <Compile Include="$(RynthEngine)ImGui\Panels\SkillsFace.Progression.cs" Link="RynthCore\SkillsFace.Progression.cs" />
    <Compile Include="$(RynthEngine)ImGui\Panels\SkillsData.cs" Link="RynthCore\SkillsData.cs" />
    <Compile Include="$(RynthEngine)UI\Data\ProgressionData.cs" Link="RynthCore\Data\ProgressionData.cs" />
    <Compile Include="$(RynthEngine)Compatibility\TrainingCosts.cs" Link="RynthCore\TrainingCosts.cs" />
    <Compile Include="$(RynthEngine)Compatibility\PlayerProgressHooks.cs" Link="RynthCore\PlayerProgressHooks.cs" />
    <Compile Include="$(RynthEngine)ImGui\MonsterHud\PortalSpellTable.cs" Link="RynthCore\PortalSpellTable.cs" />
    <Compile Include="$(RynthEngine)UI\ScriptWindows\AcIconDecoder.cs" Link="RynthCore\AcIconDecoder.cs" />
    <Compile Include="$(RynthEngine)..\RynthCore.StatusAgent\Dat\DxtUtil.cs" Link="RynthCore\DxtUtil.cs" />
```

Add to the RynthPanelTypes group:

```xml
    <RynthPanelTypes Include="$(RynthEngine)Compatibility\AelrynthWire.cs" Types="=MasterySkill,MasteryData" />
    <RynthPanelTypes Include="$(RynthEngine)Compatibility\MasteryFeed.cs" Types="=MasterySnapshot" />
```

Extend the comment above the RynthPanelTypes group with: "and the Skills panel the mastery types from AelrynthWire.cs / MasteryFeed.cs (Aelrynth only; MasteryFeed itself is a stand-in)". Change the redirect Exec to append ` --redirect-member System.Environment::get_ProcessPath=AcDream.Rynth.EngineUi.ProcessPathStandIn`, and extend the comment above that target: "Their two dat lookups (AcDatFile and PortalSpellTable FindPortalDat) look beside Environment.ProcessPath; that one getter is redirected to ProcessPathStandIn, which answers a path in OpenAC's dat folder."

Note: `UiBackgroundWriter` (ScriptWindows stand-in) runs work inline, so the first open of the panel reads the skill/XP tables and the spell table on the game thread once. Accepted.

- [ ] **Step 2: Remove placeholders** — delete `internal static class SkillsFace { public const string Title = "Skills"; }` and the whole `PortalSpellTable` stand-in (with its `<summary>`) from `DashboardStandIns.cs`. If the `RynthCore.Engine.ImGuiBackend.Hud` namespace block becomes empty, delete the block.

- [ ] **Step 3: Write `ProcessPathStandIn.cs`:**

```csharp
namespace AcDream.Rynth.EngineUi;

/// <summary>
/// RynthCore finds client_portal.dat beside acclient.exe (Environment.ProcessPath).
/// After compile, the redirect tool points their Environment.ProcessPath reads here:
/// a path in OpenAC's dat folder once the host has set it, else the real one.
/// </summary>
internal static class ProcessPathStandIn
{
    /// <summary>OpenAC's dat folder; the host sets it on attach and clears it on dispose.</summary>
    internal static string? DatDirectory { get; set; }

    public static string? get_ProcessPath() =>
        string.IsNullOrEmpty(DatDirectory) ? Environment.ProcessPath : Path.Combine(DatDirectory, "acclient.exe");
}
```

(The method is named `get_ProcessPath` so the tool finds it by the getter's name; add `#pragma warning disable IDE1006` around it if the analyzers complain about the name, with a comment saying why.)

- [ ] **Step 4: Write `SkillsStandIns.cs`:**

```csharp
using System;

namespace RynthCore.Engine.Compatibility
{
    /// <summary>RynthCore records acclient's main thread; OpenAC updates and draws on one thread, so every call is on it.</summary>
    internal static class MainThreadGuard
    {
        public static bool IsOnMainThread() => true;
    }

    /// <summary>RynthCore reads acclient's server clock; here the host answers the game view's.</summary>
    internal static class TimeSyncHooks
    {
        internal static Func<double>? ServerTime { get; set; }

        public static double GetCurrentServerTime() => ServerTime?.Invoke() ?? 0;
    }

    /// <summary>RynthCore copies the player's enchantment registry from acclient; here the host fills the arrays.</summary>
    internal static class EnchantmentHooks
    {
        internal delegate int StatModCopier(uint[] spell, double[] expiry, uint[] type, uint[] key, float[] value, uint[] category);

        internal static StatModCopier? Copier { get; set; }

        public static int CopyPlayerStatMods(uint[] spell, double[] expiry, uint[] type, uint[] key, float[] value, uint[] category) =>
            Copier?.Invoke(spell, expiry, type, key, value, category) ?? 0;
    }

    /// <summary>
    /// RynthCore calls acclient's CM_Train senders; here the host's sender requests the
    /// raise or training through OpenAC. Calls already run on the game thread, so defer is ignored.
    /// </summary>
    internal static class PlayerTraining
    {
        internal enum TrainKind : uint
        {
            Attribute = 1,
            Vital = 2,
            Skill = 3,
            TrainWithCredits = 4,
        }

        internal static Func<TrainKind, uint, uint, bool>? Sender { get; set; }

        public static bool Available => Sender is not null;

        public static bool TrainAvailable => Sender is not null;

        public static bool Raise(TrainKind kind, uint stype, uint xp, bool defer = false)
        {
            if (Sender is not { } send || xp == 0 || !send(kind, stype, xp))
                return false;
            PlayerProgressHooks.RefreshSoon();
            return true;
        }

        public static bool TrainSkill(uint stype, uint credits, bool defer = false) =>
            Raise(TrainKind.TrainWithCredits, stype, credits, defer);
    }

    /// <summary>RynthCore's Aelrynth mastery feed (a chat-line protocol); not on this server (ServerInfo.IsAelrynth is false).</summary>
    internal static class MasteryFeed
    {
        public static MasterySnapshot? Snapshot => null;

        public static bool Unsupported => false;

        public static bool NoChatHook => false;

        public static bool Awaiting => false;

        public static string Status => "not available in OpenAC";

        public static void PanelOpened()
        {
        }

        public static void RequestRefresh()
        {
        }

        public static bool QueueRaise(MasterySkill skill, int count, out string why)
        {
            why = "Mastery is not available in OpenAC";
            return false;
        }

        public static long EstimateRadiance(MasterySnapshot s) => 0;
    }
}

namespace RynthCore.Engine.UI.Data
{
    /// <summary>RynthCore relays these to the RynthILT plugin; OpenAC loads no RynthILT.</summary>
    internal static class IltCommands
    {
        public static void Progression(string value)
        {
        }
    }
}
```

In `ChatEngineStandIns.cs` add to `ClientObjectHooks`:

```csharp
        /// <summary>Fills their PlayerProgress from the game view; the host sets it. False when not in the world.</summary>
        internal static Func<PlayerProgress, bool>? ProgressReader { get; set; }

        public static bool ReadPlayerProgressLive(PlayerProgress dst) => ProgressReader is { } read && read(dst);
```

and to `UiSources`: `public static readonly ProgressionSource Progression = new();`

If the build reports another missing member, add the smallest stand-in for it in `SkillsStandIns.cs` with the same comment style, and record it in the task's commit message.

- [ ] **Step 5: Dashboard.** In `RynthAiDashboard.Start` add after the Lua registration:

```csharp
        if (!ImGuiPanelHost.HasFace(SkillsFace.Title))
            SkillsFace.Register();
```

add `UiSources.Progression` to `Sources`, list Skills in the class summary, and in `Tick` before `UiDataHub.Drain`:

```csharp
        // RynthCore's MainThreadSnapshots.Tick: reads only while the Skills panel wants it.
        PlayerProgressHooks.Prefetch();
```

(add `using RynthCore.Engine.Compatibility;`).

- [ ] **Step 6: Tests.** In `RynthAiDashboardTests.TheirFiles` add `@"ImGui\Panels\SkillsFace.cs", @"ImGui\Panels\SkillsFace.Progression.cs", @"ImGui\Panels\SkillsData.cs", @"UI\Data\ProgressionData.cs", @"Compatibility\TrainingCosts.cs", @"Compatibility\PlayerProgressHooks.cs", @"ImGui\MonsterHud\PortalSpellTable.cs", @"UI\ScriptWindows\AcIconDecoder.cs"` (DxtUtil is outside `$(RynthEngine)`; leave it out of the list). Add:

```csharp
    [Fact]
    public void TheSkillsPanelRegistersWithTheDashboardAndDrawsHeadless()
    {
        using var host = new RynthHost(_view, _log.Add, []);
        using var hookup = new RynthChatHookup(host, _view, _log.Add);
        using var imgui = new HeadlessImGui();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        RynthCore.Engine.UI.PanelRouter.Toggle(SkillsFace.Title);

        for (int i = 0; i < 3; i++)
        {
            AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
            int vertices = imgui.Frame(ImGuiPanelHost.DrawAll);
            Assert.True(vertices > 100, $"frame {i}: only {vertices} vertices");
        }

        Assert.True(ImGuiPanelHost.IsOpen(SkillsFace.Title));
        Assert.Empty(_log);
        RynthCore.Engine.UI.PanelRouter.Toggle(SkillsFace.Title);
    }
```

Create `RynthSkillsStandInsTests.cs`:

```csharp
using AcDream.Rynth.EngineUi;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RynthCore.Engine.Compatibility;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthSkillsStandInsTests : IDisposable
{
    public void Dispose()
    {
        PlayerTraining.Sender = null;
        ProcessPathStandIn.DatDirectory = null;
    }

    [Fact]
    public void TrainingIsUnavailableWithoutASenderAndForwardsWithOne()
    {
        Assert.False(PlayerTraining.Available);
        Assert.False(PlayerTraining.Raise(PlayerTraining.TrainKind.Skill, 6, 100));

        var sent = new List<(PlayerTraining.TrainKind, uint, uint)>();
        PlayerTraining.Sender = (kind, stype, amount) => { sent.Add((kind, stype, amount)); return true; };

        Assert.True(PlayerTraining.Available && PlayerTraining.TrainAvailable);
        Assert.True(PlayerTraining.Raise(PlayerTraining.TrainKind.Vital, 3, 500, defer: true));
        Assert.True(PlayerTraining.TrainSkill(15, 4));
        Assert.False(PlayerTraining.Raise(PlayerTraining.TrainKind.Attribute, 1, 0));
        Assert.Equal(
            [(PlayerTraining.TrainKind.Vital, 3u, 500u), (PlayerTraining.TrainKind.TrainWithCredits, 15u, 4u)],
            sent);
    }

    [Fact]
    public void ProcessPathPointsIntoTheDatFolderOnceSet()
    {
        Assert.Equal(Environment.ProcessPath, ProcessPathStandIn.get_ProcessPath());

        ProcessPathStandIn.DatDirectory = "/dats";

        Assert.Equal(Path.Combine("/dats", "acclient.exe"), ProcessPathStandIn.get_ProcessPath());
    }

    [Fact]
    public void OnlyTheStandInReadsTheRealProcessPath()
    {
        string path = typeof(ProcessPathStandIn).Assembly.Location;
        using ModuleDefinition module = ModuleDefinition.ReadModule(path);
        var callers = module.GetTypes()
            .SelectMany(t => t.Methods.Where(m => m.HasBody).Select(m => (Type: t, Method: m)))
            .Where(x => x.Method.Body.Instructions.Any(i =>
                i.Operand is MethodReference r && r.Name == "get_ProcessPath" && r.DeclaringType.FullName == "System.Environment"))
            .Select(x => x.Type.FullName)
            .Distinct()
            .ToList();

        Assert.Equal(["AcDream.Rynth.EngineUi.ProcessPathStandIn"], callers);
    }
}
```

If `AcDream.Rynth.Host.Tests` has no Mono.Cecil reference, add `<PackageReference Include="Mono.Cecil" />` (version is central in `Directory.Packages.props`, already used by the redirect tool) and restore with `dotnet restore AcDream.slnx --force-evaluate`, reverting unrelated lock churn.

- [ ] **Step 7: Build and test**

Run: `dotnet build src/AcDream.Rynth.EngineUi -c Debug` then `dotnet test tests/AcDream.Rynth.Host.Tests`
Expected: build clean (RynthInteropRedirect line shows `1 member call(s) redirected` or 2 — one per `FindPortalDat`), all tests PASS.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.Rynth.EngineUi tests/AcDream.Rynth.Host.Tests
git commit -m "feat(rynth): RynthCore's Skills panel compiles unchanged with stand-ins"
```

---

### Task 4: Host wiring and dat folder

**Goal:** The host feeds the Skills stand-ins from the game view and points the engine's dat lookups at OpenAC's dat folder; with installed dats, their skill/XP tables and spell table load.

**Files:**
- Modify: `src/AcDream.Rynth.Host/RynthChatHookup.cs` (set/clear `ClientObjectHooks.ProgressReader`, `EnchantmentHooks.Copier`, `TimeSyncHooks.ServerTime`, `PlayerTraining.Sender`)
- Modify: `src/AcDream.Rynth.Host/RynthClientExtension.cs` (set/clear `ProcessPathStandIn.DatDirectory` next to `RegistryStandIn.InstallPath`)
- Test: `tests/AcDream.Rynth.Host.Tests/RynthAiDashboardTests.cs` (hookup), `tests/AcDream.Rynth.Host.Tests/RynthClientExtensionDatDirectoryTests.cs`, `tests/AcDream.Rynth.Host.Tests/RynthSkillsStandInsTests.cs` (dats)

**Acceptance Criteria:**
- [ ] With a `FakeGameView.Progress`, `ClientObjectHooks.ReadPlayerProgressLive(p)` returns true and `p` holds: `SkillTableRead`, `SkillInTable[id]`, class, XP, ranks, init, `SkillLevelKnown`, buffed, base per skill; `AttributeCacheRead` and the attribute/vital arrays at ids 1..6 / 1..3; level, credits, XP, luminance, vitae. With `Progress` null it returns false. A view that throws logs once via `Guard` and returns false.
- [ ] `EnchantmentHooks.CopyPlayerStatMods` copies `FakeGameView.StatMods` into the arrays and returns the count, capped at the arrays' length.
- [ ] `PlayerTraining.Raise(Skill, 6, 100)` reaches `FakeGameView.RequestAdvancement(3, 6, 100)`; `TimeSyncHooks.GetCurrentServerTime()` is the view's `ServerTime`.
- [ ] After `RynthPlayerProgress` is set and `PlayerProgressHooks.Want()` + `Prefetch()` run, `PlayerProgressHooks.Current` is non-null and the Skills panel draws headless with it.
- [ ] Hookup Dispose unsets all four; extension attach sets `ProcessPathStandIn.DatDirectory` to `context.DatDirectory` and dispose clears it.
- [ ] With dats at `$ACDREAM_DAT_DIR` or `~/AsheronsCall` (skip with an output note otherwise): `AcDatFile.FindPortalDat()` is `<dats>/client_portal.dat`; after `SkillDat.EnsureLoadQueued()` `SkillDat.Tables` is non-null with skills; after `PortalSpellTable.EnsureLoadQueued()` `PortalSpellTable.Ready` is true and `TryGetName` answers a known spell (id 1, "Strength Other I").

**Verify:** `dotnet test tests/AcDream.Rynth.Host.Tests` → all pass; `dotnet build AcDream.slnx -c Release` clean.

**Steps:**

- [ ] **Step 1: Write failing tests.** In `RynthAiDashboardTests`:

```csharp
    [Fact]
    public void TheHookupReadsProgressStatModsTimeAndSendsRaises()
    {
        _view.Progress = new RynthPlayerProgress(
            [new RynthSkillProgress(6, 3, 10, 5000, 10, true, 140, 120)],
            [new RynthStatProgress(1, 20, 100, 900, 130, 120)],
            [new RynthStatProgress(1, 4, 0, 300, 80, 70)],
            30, 7, 1_000_000, 2500, 40, 500, 0.95f);
        _view.StatMods = [new RynthStatMod(0x1234, 99.0, 0x8010, 6, 35f, 77), new RynthStatMod(0x1300, double.MaxValue, 0x8001, 1, 10f, 3)];
        _view.Time = 12.5;
        using var host = new RynthHost(_view, _log.Add, []);
        using (var hookup = new RynthChatHookup(host, _view, _log.Add))
        {
            var p = new PlayerProgress();
            Assert.True(ClientObjectHooks.ReadPlayerProgressLive(p));
            Assert.True(p.SkillTableRead && p.SkillInTable[6] && p.SkillLevelKnown[6] && p.AttributeCacheRead);
            Assert.Equal((3u, 5000u, 10u, 10u, 140, 120), (p.SkillClass[6], p.SkillXpSpent[6], p.SkillRanks[6], p.SkillInit[6], p.SkillBuffed[6], p.SkillBase[6]));
            Assert.Equal((20u, 100u, 900u, 130u, 120u), (p.AttrRanks[1], p.AttrInit[1], p.AttrXpSpent[1], p.AttrBuffed[1], p.AttrBase[1]));
            Assert.Equal((4u, 0u, 300u, 80u, 70u), (p.VitalRanks[1], p.VitalInit[1], p.VitalXpSpent[1], p.VitalBuffed[1], p.VitalBase[1]));
            Assert.Equal((30, 7, 1_000_000L, 2500L, 40L, 500L, 0.95f), (p.Level, p.SkillCredits, p.TotalXp, p.UnassignedXp, p.Luminance, p.MaxLuminance, p.Vitae));
            Assert.False(p.SkillInTable[15]);

            uint[] spell = new uint[1], type = new uint[1], key = new uint[1], category = new uint[1];
            double[] expiry = new double[1];
            float[] value = new float[1];
            Assert.Equal(1, EnchantmentHooks.CopyPlayerStatMods(spell, expiry, type, key, value, category));   // capped at the arrays
            Assert.Equal((0x1234u, 99.0, 0x8010u, 6u, 35f, 77u), (spell[0], expiry[0], type[0], key[0], value[0], category[0]));

            Assert.Equal(12.5, TimeSyncHooks.GetCurrentServerTime());
            Assert.True(PlayerTraining.Raise(PlayerTraining.TrainKind.Skill, 6, 100));
            Assert.Equal([(3, 6u, 100u)], _view.Advancements);

            _view.Progress = null;
            Assert.False(ClientObjectHooks.ReadPlayerProgressLive(p));
        }

        Assert.Null(ClientObjectHooks.ProgressReader);
        Assert.Null(EnchantmentHooks.Copier);
        Assert.Null(TimeSyncHooks.ServerTime);
        Assert.Null(PlayerTraining.Sender);
        Assert.Empty(_log);
    }

    [Fact]
    public void TheSkillsPanelDrawsAPublishedSnapshot()
    {
        _view.Progress = new RynthPlayerProgress(
            [new RynthSkillProgress(6, 3, 10, 5000, 10, true, 140, 120)],
            [new RynthStatProgress(1, 20, 100, 900, 130, 120)],
            [new RynthStatProgress(1, 4, 0, 300, 80, 70)],
            30, 7, 1_000_000, 2500, 40, 500, 1f);
        using var host = new RynthHost(_view, _log.Add, []);
        using var hookup = new RynthChatHookup(host, _view, _log.Add);
        using var imgui = new HeadlessImGui();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        RynthCore.Engine.UI.PanelRouter.Toggle(SkillsFace.Title);

        for (int i = 0; i < 3; i++)
        {
            PlayerProgressHooks.Want();
            PlayerProgressHooks.RefreshSoon();
            AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
            int vertices = imgui.Frame(ImGuiPanelHost.DrawAll);
            Assert.True(vertices > 100, $"frame {i}: only {vertices} vertices");
        }

        Assert.NotNull(PlayerProgressHooks.Current);
        Assert.Empty(_log);
        RynthCore.Engine.UI.PanelRouter.Toggle(SkillsFace.Title);
    }
```

(`_view.Time` is the existing FakeGameView field behind `ServerTime`; if it has another name, use that.) Note `PlayerProgressHooks.Current` is static and survives tests: the assertion is only that it is non-null after a read.

In `RynthClientExtensionDatDirectoryTests.AttachSetsTheStandInPathAndDisposeClearsIt` add `Assert.Equal("/x", AcDream.Rynth.EngineUi.ProcessPathStandIn.DatDirectory);` after the existing attach assert, `Assert.Null(AcDream.Rynth.EngineUi.ProcessPathStandIn.DatDirectory);` after dispose, and reset it in the outer `finally`.

In `RynthSkillsStandInsTests` add (constructor takes `ITestOutputHelper output` as `RynthAiInteropTests` does):

```csharp
    [Fact]
    public void TheirDatReadersFindAndLoadTheInstalledPortalDat()
    {
        string? dats = Environment.GetEnvironmentVariable("ACDREAM_DAT_DIR");
        if (string.IsNullOrWhiteSpace(dats))
            dats = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AsheronsCall");
        if (!File.Exists(Path.Combine(dats, "client_portal.dat")))
        {
            // xunit 2 (this project) has no dynamic skip; the note shows in the test's output.
            output.WriteLine($"skipped: no dats at {dats}");
            return;
        }

        ProcessPathStandIn.DatDirectory = dats;

        Assert.Equal(Path.Combine(dats, "client_portal.dat"), RynthCore.Engine.UI.ScriptWindows.AcDatFile.FindPortalDat());
        RynthCore.Engine.ImGuiBackend.Panels.SkillDat.EnsureLoadQueued();
        Assert.True(RynthCore.Engine.ImGuiBackend.Panels.SkillDat.Tables is { Skills.Count: > 0 }, RynthCore.Engine.ImGuiBackend.Panels.SkillDat.Status);
        RynthCore.Engine.ImGuiBackend.Hud.PortalSpellTable.EnsureLoadQueued();
        Assert.True(RynthCore.Engine.ImGuiBackend.Hud.PortalSpellTable.Ready, RynthCore.Engine.ImGuiBackend.Hud.PortalSpellTable.Status);
        Assert.True(RynthCore.Engine.ImGuiBackend.Hud.PortalSpellTable.TryGetName(1, out string name));
        Assert.Equal("Strength Other I", name);
    }
```

(Check each type's real namespace with `grep -n '^namespace' external/RynthCore/src/RynthCore.Engine/...` and adjust the qualified names.)

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test tests/AcDream.Rynth.Host.Tests --filter "TheHookupReadsProgress|TheSkillsPanelDrawsAPublished|AttachSetsTheStandInPath|TheirDatReaders"`
Expected: FAIL (delegates null / DatDirectory null / dat not found).

- [ ] **Step 3: Implement the hookup.** In `RynthChatHookup` add fields, set them in the constructor after `InventoryModel.Source = _inventory;`, and unset in `Dispose` with the same `if (X == _x) X = null;` pattern:

```csharp
    private readonly Func<PlayerProgress, bool> _progress;
    private readonly EnchantmentHooks.StatModCopier _statMods;
    private readonly Func<double> _serverTime;
    private readonly Func<PlayerTraining.TrainKind, uint, uint, bool> _train;
```

```csharp
        _progress = ReadProgress;
        _statMods = CopyStatMods;
        _serverTime = () => Guard(nameof(TimeSyncHooks.GetCurrentServerTime), () => _view.ServerTime, 0d);
        _train = (kind, stype, amount) => Guard(
            nameof(PlayerTraining.Raise), () => _view.RequestAdvancement((int)kind, stype, amount), false);
        ...
        ClientObjectHooks.ProgressReader = _progress;
        EnchantmentHooks.Copier = _statMods;
        TimeSyncHooks.ServerTime = _serverTime;
        PlayerTraining.Sender = _train;
```

```csharp
    // As RynthCore's ReadPlayerProgressLive fills its work buffer: indexed by skill id, attribute 1..6, vital 1..3.
    private bool ReadProgress(PlayerProgress dst)
    {
        RynthPlayerProgress? read = Guard(nameof(ClientObjectHooks.ReadPlayerProgressLive), () => _view.PlayerProgress, null);
        if (read is null)
            return false;
        dst.Clear();
        dst.PlayerId = Guard(nameof(ClientHelperHooks.GetPlayerId), () => _view.PlayerId, 0u);
        dst.SkillTableRead = true;
        foreach (RynthSkillProgress s in read.Skills)
        {
            if (s.SkillId is 0 or > PlayerProgress.MaxSkill)
                continue;
            int i = (int)s.SkillId;
            dst.SkillInTable[i] = true;
            dst.SkillClass[i] = s.Class;
            dst.SkillXpSpent[i] = s.XpSpent;
            dst.SkillRanks[i] = s.Ranks;
            dst.SkillInit[i] = s.Init;
            dst.SkillLevelKnown[i] = s.LevelKnown;
            dst.SkillBuffed[i] = s.Buffed;
            dst.SkillBase[i] = s.Base;
        }

        dst.AttributeCacheRead = read.Attributes.Count > 0;
        foreach (RynthStatProgress a in read.Attributes)
        {
            if (a.Id is 0 or > 6)
                continue;
            int i = (int)a.Id;
            (dst.AttrRanks[i], dst.AttrInit[i], dst.AttrXpSpent[i], dst.AttrBuffed[i], dst.AttrBase[i]) = (a.Ranks, a.Init, a.XpSpent, a.Buffed, a.Base);
        }

        foreach (RynthStatProgress v in read.Vitals)
        {
            if (v.Id is 0 or > 3)
                continue;
            int i = (int)v.Id;
            (dst.VitalRanks[i], dst.VitalInit[i], dst.VitalXpSpent[i], dst.VitalBuffed[i], dst.VitalBase[i]) = (v.Ranks, v.Init, v.XpSpent, v.Buffed, v.Base);
        }

        (dst.Level, dst.SkillCredits, dst.TotalXp, dst.UnassignedXp, dst.Luminance, dst.MaxLuminance, dst.Vitae) =
            (read.Level, read.SkillCredits, read.TotalXp, read.UnassignedXp, read.Luminance, read.MaxLuminance, read.Vitae);
        return true;
    }

    private int CopyStatMods(uint[] spell, double[] expiry, uint[] type, uint[] key, float[] value, uint[] category)
    {
        IReadOnlyList<RynthStatMod>? mods = Guard(nameof(EnchantmentHooks.CopyPlayerStatMods), () => _view.PlayerStatMods, null);
        if (mods is null)
            return 0;
        int n = Math.Min(mods.Count, spell.Length);
        for (int i = 0; i < n; i++)
            (spell[i], expiry[i], type[i], key[i], value[i], category[i]) =
                (mods[i].SpellId, mods[i].ExpiryServerTime, mods[i].Type, mods[i].Key, mods[i].Value, mods[i].Category);
        return n;
    }
```

(Use the hookup's existing `Guard` overload; if its fallback parameter is non-nullable for reference types, follow how `_inventory` passes `null`.)

- [ ] **Step 4: Extension.** In `RynthClientExtension.Attach`, next to `RegistryStandIn.InstallPath = context.DatDirectory;`:

```csharp
        // RynthCore's engine finds client_portal.dat beside Environment.ProcessPath (redirected to this stand-in).
        AcDream.Rynth.EngineUi.ProcessPathStandIn.DatDirectory = context.DatDirectory;
```

and in Dispose next to `RegistryStandIn.InstallPath = null;`: `AcDream.Rynth.EngineUi.ProcessPathStandIn.DatDirectory = null;`. (If the Host project can't see the internal type, it already has InternalsVisibleTo from EngineUi.)

- [ ] **Step 5: Run the tests and the Release build**

Run: `dotnet test tests/AcDream.Rynth.Host.Tests` then `dotnet build AcDream.slnx -c Release`
Expected: all PASS; Release build has 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests
git commit -m "feat(rynth): host feeds the Skills panel and points their dat lookups at OpenAC's dats"
```

---

### Task 5: Live check (user)

**Goal:** On the local ACE server, the Skills panel shows the character's real numbers and raising/training works.

**Files:** none (results go in the memory note `rynth-status.md` and this plan's ledger).

**Acceptance Criteria:**
- [ ] Wait until the user says they are available; launch only on the local ACE test server (memory: live-testing-rules), never Dreamweave.
- [ ] Dashboard → Char right-click → Skills opens the panel; no `threw` lines in the log.
- [ ] Level, unassigned XP, skill credits and luminance match the retail character sheet.
- [ ] Three spot values (one attribute, one vital, one specialized skill) match retail buffed and base values.
- [ ] With a buff cast (e.g. Strength Self), it is listed on Strength with name, icon and a counting-down time.
- [ ] `+1` on an attribute, a vital and a skill each raises it in the retail sheet and spends the shown XP.
- [ ] Training an untrained skill with credits makes it trained (needs enough credits).

**Verify:** user confirmation in chat for each item above.

**Steps:**

- [ ] **Step 1:** Ask the user whether they are available for a live check; do nothing else until they say yes.
- [ ] **Step 2:** Walk the list above with the user, record PASS/FAIL per item in the ledger, and fix failures with systematic-debugging.
