using AcDream.App.Streaming;
using AcDream.Core.World;

namespace AcDream.App.Tests.Streaming;

public sealed class ArrivalDrawBehindTunnelGateTests
{
    [Fact]
    public void ThePlacingFrameDoesNotDrawTheWorldBehindTheTunnel()
    {
        var gate = new ArrivalDrawBehindTunnelGate();
        gate.Observe(TeleportAnimState.Tunnel);
        Assert.False(gate.IsOpen);

        // The tick that places the arrival moves the tunnel on.
        gate.Observe(TeleportAnimState.TunnelContinue);
        Assert.False(gate.IsOpen);

        gate.Observe(TeleportAnimState.TunnelContinue);
        Assert.True(gate.IsOpen);
        gate.Observe(TeleportAnimState.TunnelFadeOut);
        Assert.True(gate.IsOpen);
    }

    [Fact]
    public void TheWorldStopsBeingDrawnBehindTheTunnelOnceItFadesIn()
    {
        var gate = new ArrivalDrawBehindTunnelGate();
        gate.Observe(TeleportAnimState.TunnelContinue);
        gate.Observe(TeleportAnimState.TunnelFadeOut);
        Assert.True(gate.IsOpen);

        gate.Observe(TeleportAnimState.WorldFadeIn);
        Assert.False(gate.IsOpen);
    }

    [Fact]
    public void ANewTripStartsClosed()
    {
        var gate = new ArrivalDrawBehindTunnelGate();
        gate.Observe(TeleportAnimState.TunnelContinue);
        gate.Observe(TeleportAnimState.TunnelContinue);
        Assert.True(gate.IsOpen);

        gate.Reset();
        Assert.False(gate.IsOpen);
        gate.Observe(TeleportAnimState.TunnelContinue);
        Assert.False(gate.IsOpen);
    }
}
