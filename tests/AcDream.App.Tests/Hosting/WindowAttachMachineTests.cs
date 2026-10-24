using AcDream.App.Hosting;
using A = AcDream.App.Hosting.WindowAttachAction;
using I = AcDream.App.Hosting.WindowAttachInput;
using R = AcDream.App.Hosting.WindowDetachReason;
using S = AcDream.App.Hosting.WindowAttachState;

namespace AcDream.App.Tests.Hosting;

/// <summary>
/// Ported from the Rust client's attach state machine
/// (crates/ac-render/src/attach.rs, its tests module): what the host does
/// about its window for every input in every state.
/// </summary>
public sealed class WindowAttachMachineTests
{
    [Fact]
    public void Starts_detached() =>
        Assert.Equal(S.Detached, new WindowAttachMachine().State);

    [Fact]
    public void Attach_then_detach_then_attach_again()
    {
        var machine = new WindowAttachMachine();
        Assert.Equal([A.OpenWindow], machine.Step(I.AttachRequested));
        Assert.Equal(S.Attached, machine.State);
        Assert.Equal([A.CloseWindow(R.Requested)], machine.Step(I.DetachRequested));
        Assert.Equal(S.Detached, machine.State);
        Assert.Equal([A.OpenWindow], machine.Step(I.AttachRequested));
        Assert.Equal(S.Attached, machine.State);
    }

    [Fact]
    public void User_close_detaches_but_keeps_the_loop_running()
    {
        var machine = new WindowAttachMachine();
        machine.Step(I.AttachRequested);
        Assert.Equal([A.CloseWindow(R.ClosedByUser)], machine.Step(I.WindowClosedByUser));
        Assert.Equal(S.Detached, machine.State);
        // A later detach finds nothing to do, and a new window can still open.
        Assert.Empty(machine.Step(I.DetachRequested));
        Assert.Equal([A.OpenWindow], machine.Step(I.AttachRequested));
    }

    [Fact]
    public void Second_attach_while_attached_is_rejected()
    {
        var machine = new WindowAttachMachine();
        machine.Step(I.AttachRequested);
        Assert.Equal([A.RejectAttach], machine.Step(I.AttachRequested));
        Assert.Equal(S.Attached, machine.State);
    }

    [Fact]
    public void Failed_attach_returns_to_detached_without_closing()
    {
        var machine = new WindowAttachMachine();
        machine.Step(I.AttachRequested);
        Assert.Empty(machine.Step(I.AttachFailed));
        Assert.Equal(S.Detached, machine.State);
        Assert.Equal([A.OpenWindow], machine.Step(I.AttachRequested));
    }

    [Fact]
    public void Render_failure_closes_the_window()
    {
        var machine = new WindowAttachMachine();
        machine.Step(I.AttachRequested);
        Assert.Equal([A.CloseWindow(R.RenderFailed)], machine.Step(I.RenderFailed));
        Assert.Equal(S.Detached, machine.State);
    }

    [Fact]
    public void Stray_inputs_while_detached_do_nothing()
    {
        var machine = new WindowAttachMachine();
        foreach (I input in new[]
                 {
                     I.DetachRequested, I.WindowClosedByUser, I.AttachFailed, I.RenderFailed,
                 })
        {
            Assert.Empty(machine.Step(input));
            Assert.Equal(S.Detached, machine.State);
        }
    }

    [Fact]
    public void Shutdown_while_detached_exits()
    {
        var machine = new WindowAttachMachine();
        Assert.Equal([A.ExitLoop], machine.Step(I.ShutdownRequested));
        Assert.Equal(S.Exited, machine.State);
    }

    [Fact]
    public void Shutdown_while_attached_closes_then_exits()
    {
        var machine = new WindowAttachMachine();
        machine.Step(I.AttachRequested);
        Assert.Equal(
            [A.CloseWindow(R.Shutdown), A.ExitLoop],
            machine.Step(I.ShutdownRequested));
        Assert.Equal(S.Exited, machine.State);
    }

    [Fact]
    public void Everything_is_ignored_after_exit()
    {
        var machine = new WindowAttachMachine();
        machine.Step(I.ShutdownRequested);
        foreach (I input in Enum.GetValues<I>())
        {
            Assert.Empty(machine.Step(input));
            Assert.Equal(S.Exited, machine.State);
        }
    }
}
