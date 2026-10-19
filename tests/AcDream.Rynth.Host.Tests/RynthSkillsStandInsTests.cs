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
