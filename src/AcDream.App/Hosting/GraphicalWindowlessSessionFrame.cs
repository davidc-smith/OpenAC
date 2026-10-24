using AcDream.App.Update;
using AcDream.Runtime;
using AcDream.Runtime.Gameplay;
using AcDream.Runtime.Session;
using AcDream.Runtime.Session.Windowless;

namespace AcDream.App.Hosting;

/// <summary>
/// The session's frame while no window presents it: the frame every client
/// without a window takes, in the same order. The world's clock, the
/// character's own step with nothing typed, every other creature's body,
/// what the server said, the character's arrival and the expiry of objects
/// out of sight, the character's commands after the network, and a pending
/// attack.
/// </summary>
internal sealed class GraphicalWindowlessSessionFrame : IGraphicalSessionFrame
{
    private readonly GameRuntime _runtime;
    private readonly LiveSessionHost _session;
    private readonly RuntimeWindowlessSessionRoute _route;
    private readonly RuntimeLocalPlayerFrameController _localPlayer;

    internal GraphicalWindowlessSessionFrame(
        GameRuntime runtime,
        LiveSessionHost session,
        RuntimeWindowlessSessionRoute route)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _route = route ?? throw new ArgumentNullException(nameof(route));
        _localPlayer = runtime.CreateLocalPlayerFrameController(
            new RuntimeWindowlessLocalPlayerFrameHost(runtime, session),
            new RuntimeWindowlessMovementInputSource(runtime.MovementOwner));
    }

    public void TickSessionFrame(UpdateFrameInput input)
    {
        double deltaSeconds = input.HostDeltaSeconds;
        // The world's clock stands still while there is no world to
        // simulate; that is the runtime's call.
        _ = _runtime.AdvanceFrameClock(deltaSeconds);
        _localPlayer.AdvanceBeforeNetwork(checked((float)deltaSeconds));
        // After the character's own step and before anything the server has
        // said this frame is read, so every body spends the same elapsed
        // time on every client.
        _route.AdvanceRemoteBodies(checked((float)deltaSeconds));
        _runtime.FinishRemoteBodyPass();
        _session.Tick();
        _route.PumpAfterNetwork();
        _localPlayer.RunPostNetworkCommandPhase();
        _runtime.ActionOwner.CombatAttack.Tick();
    }
}
