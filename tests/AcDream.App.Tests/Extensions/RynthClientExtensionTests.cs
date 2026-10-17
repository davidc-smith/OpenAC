using AcDream.ClientExtensions;
using AcDream.Rynth.Host;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed class RynthClientExtensionTests
{
    private sealed class Registration : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void AttachesTicksAndReleasesItsSlotOnDispose()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var log = new List<string>();
        var verbs = new List<string>();
        var registration = new Registration();
        var context = new ClientExtensionContext
        {
            Runtime = runtime,
            Log = log.Add,
            RegisterChatCommand = (verb, _) =>
            {
                verbs.Add(verb);
                return registration;
            },
        };

        var extension = new RynthClientExtension();
        extension.Attach(context);

        Assert.Contains("rynth", verbs);
        Assert.Contains("[Rynth] [RynthTracker] Plugin initialized.", log);

        runtime.ActionOwner.Combat.OnUpdateHealth(0x80000010, 0.5f);

        extension.Dispose();
        Assert.True(registration.Disposed);

        var second = new RynthClientExtension();
        second.Attach(context);
        second.Dispose();
    }
}
