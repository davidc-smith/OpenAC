using AcDream.Rynth.EngineUi;
using Mono.Cecil;
using RynthCore.Engine.Compatibility;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthSkillsStandInsTests(Xunit.Abstractions.ITestOutputHelper output) : IDisposable
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

    [Fact]
    public void TheirDatReadersFindAndLoadTheInstalledPortalDat()
    {
        string dats = TestDats.Directory;
        if (!File.Exists(Path.Combine(dats, "client_portal.dat")))
        {
            // xunit 2 (this project) has no dynamic skip; the note shows in the test's output.
            output.WriteLine($"skipped: no dats at {dats}");
            return;
        }

        // PortalSpellTable.EnsureLoadQueued latches its private static _queued even when the load failed, and
        // earlier panel tests open Skills/Nav/Meta without a dat dir. We cannot change their code, so reset it.
        ResetStatics(typeof(RynthCore.Engine.ImGuiBackend.Hud.PortalSpellTable));
        ResetStatics(typeof(RynthCore.Engine.ImGuiBackend.Panels.SkillDat));
        ProcessPathStandIn.DatDirectory = dats;

        Assert.Equal(Path.Combine(dats, "client_portal.dat"), RynthCore.Engine.UI.ScriptWindows.AcDatFile.FindPortalDat());
        // The ScriptWindows stand-in UiBackgroundWriter runs queued work inline, so these loads are synchronous.
        RynthCore.Engine.ImGuiBackend.Panels.SkillDat.EnsureLoadQueued();
        Assert.True(RynthCore.Engine.ImGuiBackend.Panels.SkillDat.Tables is { Skills.Count: > 0 }, RynthCore.Engine.ImGuiBackend.Panels.SkillDat.Status);
        RynthCore.Engine.ImGuiBackend.Hud.PortalSpellTable.EnsureLoadQueued();
        Assert.True(RynthCore.Engine.ImGuiBackend.Hud.PortalSpellTable.Ready, RynthCore.Engine.ImGuiBackend.Hud.PortalSpellTable.Status);
        Assert.True(RynthCore.Engine.ImGuiBackend.Hud.PortalSpellTable.TryGetName(1, out string name));
        Assert.Equal("Strength Other I", name);
    }

    private static void ResetStatics(Type type)
    {
        const System.Reflection.BindingFlags f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        foreach (string name in new[] { "_queued", "_table", "_tables" })
            type.GetField(name, f)?.SetValue(null, type.GetField(name, f)!.FieldType == typeof(bool) ? false : null);
    }
}
