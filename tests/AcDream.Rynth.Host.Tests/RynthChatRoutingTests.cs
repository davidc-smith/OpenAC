using AcDream.Plugin.Abstractions;
using AcDream.Rynth.Host;
using AcDream.TestSupport;

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
        Assert.Equal(new[] { "/eat this", "hello" }, offered);
    }

    [Fact]
    public void WithoutOneTheVerbsForwardAsBefore()
    {
        var verbs = new Dictionary<string, Action<string>>();
        var offered = new List<string>();

        RynthClientExtension.RouteChatToPlugins(
            null, line => { offered.Add(line); return true; },
            (verb, handler) => { verbs[verb] = handler; return new None(); });

        Assert.Equal(new[] { "ro", "ginv", "ra", "mt", "ub", "vt", "lua", "rnav" }, verbs.Keys);
        verbs["rnav"]("where");
        Assert.Equal(new[] { "/rnav where" }, offered);
    }

    [Fact]
    public void TheRealPluginsEatOnlyTheirOwnCommands()
    {
        using var host = new RynthHost(_view, _log.Add,
            [typeof(RynthCore.Plugin.RynthNav.PluginExports), typeof(RynthCore.Plugin.RynthAi.PluginExports)]);
        foreach (RynthPlugin plugin in host.Plugins)
            plugin.OnLoginComplete();
        bool Offer(string line) => host.Plugins.Any(p => p.OnChatBarEnter(line));

        Assert.True(Offer("/rnav where"));
        Assert.True(Offer("/ra help"));
        Assert.False(Offer("/rnavx"));
        Assert.False(Offer("hello there"));
        Assert.False(Offer("@lifestone"));
    }

    [Fact]
    public void ALineOneEatsStillReachesTheLaterPluginsWithTheSharedFlag()
    {
        var seen = new List<string>();
        var flagOnArrival = new List<int>();
        RynthClientExtension.ChatBarOffer Plugin(string name, bool eats) => (string text, ref int eat) =>
        {
            seen.Add(name + ":" + text);
            flagOnArrival.Add(eat);
            if (eats)
                eat = 1;
        };

        bool eaten = RynthClientExtension.OfferToPlugins([Plugin("a", false), Plugin("b", true), Plugin("c", false)], "/x");

        Assert.True(eaten);
        Assert.Equal(new[] { "a:/x", "b:/x", "c:/x" }, seen);
        Assert.Equal(new[] { 0, 0, 1 }, flagOnArrival);
        Assert.False(RynthClientExtension.OfferToPlugins([Plugin("d", false)], "/y"));
    }

    [Fact]
    public void ALineSubmittedFromInsideAnOfferIsNotOfferedAgain()
    {
        var seen = new List<string>();
        bool? inner = null;
        RynthClientExtension.ChatBarOffer reentrant = (string text, ref int eat) =>
        {
            seen.Add("outer");
            inner = RynthClientExtension.OfferToPlugins(
                [(string t, ref int e) => { seen.Add("inner"); e = 1; }], "/again");
        };

        Assert.False(RynthClientExtension.OfferToPlugins([reentrant], "/first"));
        Assert.False(inner);
        Assert.Equal(new[] { "outer" }, seen);
        // The guard is released afterwards.
        Assert.True(RynthClientExtension.OfferToPlugins([(string t, ref int e) => e = 1], "/next"));
    }
}
