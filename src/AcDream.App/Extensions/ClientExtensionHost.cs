using AcDream.ClientExtensions;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime;

namespace AcDream.App.Extensions;

/// <summary>
/// The client's built-in extensions (<see cref="IClientExtension"/>): created
/// with the window, attached once its runtime exists, ticked every update
/// frame and disposed at shutdown. A failing extension is logged and dropped;
/// it never stops the client.
/// </summary>
internal sealed class ClientExtensionHost : IDisposable
{
    private readonly List<IClientExtension> _extensions =
    [
        new AcDream.Rynth.Host.RynthClientExtension(),
    ];

    public void Attach(
        GameRuntime runtime,
        IPluginCommandRegistry? commands,
        IClientOverlay? overlay = null,
        Func<string, bool>? submitChatCommand = null,
        string? stateDirectory = null,
        IClientChat? chat = null,
        IAutomationSurface? automation = null,
        IPluginWorldTriangles? worldTriangles = null)
    {
        var context = new ClientExtensionContext
        {
            Runtime = runtime,
            Log = line => Serilog.Log.Information("{ExtensionLine}", line),
            RegisterChatCommand = (verb, handler) =>
                commands?.Register(verb, command => handler(command.Arguments))
                ?? new NoRegistration(),
            Overlay = overlay,
            SubmitChatCommand = submitChatCommand,
            StateDirectory = stateDirectory,
            Chat = chat,
            Automation = automation,
            WorldTriangles = worldTriangles,
        };

        foreach (IClientExtension extension in _extensions.ToArray())
            Guard(extension, "attach", () => extension.Attach(context));
    }

    public void Tick(double deltaSeconds)
    {
        foreach (IClientExtension extension in _extensions.ToArray())
            Guard(extension, "tick", () => extension.Tick(deltaSeconds));
    }

    public void Dispose()
    {
        foreach (IClientExtension extension in _extensions)
        {
            try { extension.Dispose(); }
            catch (Exception error)
            {
                Serilog.Log.Error(error, "Client extension {Extension} failed to dispose", extension.GetType().Name);
            }
        }

        _extensions.Clear();
    }

    private void Guard(IClientExtension extension, string stage, Action action)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            Serilog.Log.Error(error, "Client extension {Extension} failed to {Stage}; dropped", extension.GetType().Name, stage);
            _extensions.Remove(extension);
            try { extension.Dispose(); }
            catch { }
        }
    }

    private sealed class NoRegistration : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
