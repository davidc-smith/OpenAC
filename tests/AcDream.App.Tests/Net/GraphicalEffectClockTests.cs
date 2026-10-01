using System.Diagnostics;
using System.Reflection;
using AcDream.App.Composition;
using AcDream.App.Net;
using AcDream.App.Tests.Architecture;
using AcDream.App.UI;
using AcDream.Runtime;

namespace AcDream.App.Tests.Net;

/// <summary>
/// An effect's start time is stamped when it arrives and read back against
/// "now" by the plugin surface and the retained UI. Both ends have to use the
/// same clock, and the plugin surface uses the runtime's simulation clock.
/// </summary>
public sealed class GraphicalEffectClockTests
{
    private const BindingFlags AnyDeclared = BindingFlags.Instance
        | BindingFlags.Static
        | BindingFlags.Public
        | BindingFlags.NonPublic
        | BindingFlags.DeclaredOnly;

    [Fact]
    public void SessionStampsEffectsWithTheRuntimeClock()
    {
        MethodInfo create = typeof(LiveSessionRuntimeFactory).GetMethod(
            "CreateCharacterBindings",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        HashSet<MethodBase> invoked = CompiledCallGraph.Read(create)
            .Select(call => call.Target)
            .ToHashSet();
        CompiledCall[] delegateCalls = CompiledCallGraph
            .ReadMethodReferences(create)
            .Select(call => call.Target)
            .Where(target => !invoked.Contains(target)
                && target.DeclaringType?.Assembly
                    == typeof(LiveSessionRuntimeFactory).Assembly
                && target.GetMethodBody() is not null)
            .Distinct()
            .SelectMany(CompiledCallGraph.Read)
            .ToArray();

        Assert.Contains(delegateCalls, call => ReadsRuntimeClock(call.Target));
        Assert.DoesNotContain(
            delegateCalls,
            call => call.Target.DeclaringType == typeof(Stopwatch));
    }

    [Fact]
    public void RetainedUiMeasuresEffectsOnTheRuntimeClock()
    {
        ConstructorInfo magicBindings =
            Assert.Single(typeof(MagicRuntimeBindings).GetConstructors());
        MethodBase composer = Assert.Single(
            OwnedMethods(typeof(RetailInteractionRetainedUiCompositionFactory)),
            method => CompiledCallGraph.Read(method)
                .Any(call => call.Target == magicBindings));
        IReadOnlyList<CompiledCall> references =
            CompiledCallGraph.ReadMethodReferences(composer);
        int built = references
            .Select((call, index) => (call, index))
            .Single(pair => pair.call.Target == magicBindings)
            .index;

        // ServerTime is the record's last argument: a delegate made from a
        // compiled body, handed straight to the constructor.
        Assert.True(built >= 2, "MagicRuntimeBindings is built from nothing.");
        Assert.Equal(
            typeof(Func<double>),
            references[built - 1].Target.DeclaringType);
        MethodBase serverTime = references[built - 2].Target;
        Assert.Contains(
            CompiledCallGraph.Read(serverTime),
            call => ReadsRuntimeClock(call.Target));
    }

    private static bool ReadsRuntimeClock(MethodBase method) =>
        method.Name == "get_" + nameof(GameRuntimeClock.SimulationTimeSeconds)
        && (method.DeclaringType == typeof(GameRuntimeClock)
            || method.DeclaringType == typeof(IGameRuntimeClock));

    private static IEnumerable<MethodBase> OwnedMethods(Type root) =>
        new[] { root }
            .Concat(root.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            .SelectMany(type => type.GetMethods(AnyDeclared)
                .Cast<MethodBase>()
                .Concat(type.GetConstructors(AnyDeclared)))
            .Where(method => method.GetMethodBody() is not null);
}
