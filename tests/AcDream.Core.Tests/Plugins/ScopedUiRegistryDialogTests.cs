using AcDream.Core.Plugins;
using AcDream.Core.Selection;
using AcDream.Plugin.Abstractions;

namespace AcDream.Core.Tests.Plugins;

/// <summary>
/// A plugin's dialogs reach the host under its own name, report one answer
/// each, and are answered with their last button when the plugin unloads.
/// </summary>
public sealed class ScopedUiRegistryDialogTests
{
    [Fact]
    public void ConfirmReachesTheHostUnderThePluginsOwnName()
    {
        var ui = new RecordingUiRegistry();
        using var scope = new ScopedPluginHost(new StubHost(ui), "acdream.test", "Test");
        var dialog = new PluginDialog("Delete", "Delete the profile?", OwnerView: "main");

        Assert.True(scope.Ui.Confirm(dialog, _ => { }));

        RecordingUiRegistry.Request request = Assert.Single(ui.Requests);
        Assert.Equal("acdream.test", request.Owner.Id);
        Assert.Same(dialog, request.Dialog);
    }

    [Fact]
    public void AHostWithoutDialogsAnswersFalseAndNeverCallsBack()
    {
        using var scope = new ScopedPluginHost(
            new StubHost(NoOpUiRegistry.Instance), "acdream.test", "Test");
        bool called = false;

        Assert.False(scope.Ui.Confirm(new PluginDialog("T", "M"), _ => called = true));
        Assert.False(scope.Ui.ShowModal("options", "main"));
        Assert.False(called);
    }

    [Fact]
    public void TheAnswerReachesThePluginOnceAndLetsGoOfTheDialog()
    {
        var ui = new RecordingUiRegistry();
        var scope = new ScopedPluginHost(new StubHost(ui), "acdream.test", "Test");
        var answers = new List<int>();
        scope.Ui.Confirm(new PluginDialog("T", "M"), answers.Add);
        RecordingUiRegistry.Request request = Assert.Single(ui.Requests);

        request.Answer(0);
        scope.Dispose();

        Assert.Equal([0], answers);
        Assert.Equal(1, request.Handle.DisposeCount);
    }

    [Fact]
    public void UnloadingAnswersAnOpenDialogWithItsLastButton()
    {
        var ui = new RecordingUiRegistry();
        var scope = new ScopedPluginHost(new StubHost(ui), "acdream.test", "Test");
        var answers = new List<int>();
        scope.Ui.Confirm(new PluginDialog("T", "M", ["Yes", "No", "Later"]), answers.Add);

        scope.Dispose();

        Assert.Equal([2], answers);
        Assert.Equal(1, Assert.Single(ui.Requests).Handle.DisposeCount);
    }

    [Fact]
    public void ShowModalReachesTheHostUnderThePluginsOwnName()
    {
        var ui = new RecordingUiRegistry();
        using var scope = new ScopedPluginHost(new StubHost(ui), "acdream.test", "Test");

        Assert.True(scope.Ui.ShowModal("options", "main"));
        Assert.True(scope.Ui.ShowModal("about"));

        Assert.Equal(
            [("acdream.test", "options", (string?)"main"), ("acdream.test", "about", null)],
            ui.Modals);
    }

    [Fact]
    public void AnUnloadedPluginCannotOpenADialog()
    {
        var ui = new RecordingUiRegistry();
        var scope = new ScopedPluginHost(new StubHost(ui), "acdream.test", "Test");
        IUiRegistry registry = scope.Ui;
        scope.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => registry.Confirm(new PluginDialog("T", "M"), _ => { }));
        Assert.Throws<ObjectDisposedException>(() => registry.ShowModal("options", "main"));
        Assert.Empty(ui.Requests);
        Assert.Empty(ui.Modals);
    }

    private sealed class RecordingUiRegistry : IScopedUiRegistry
    {
        public sealed class Handle(Action cancel) : IDisposable
        {
            public int DisposeCount { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
                cancel();
            }
        }

        public sealed class Request
        {
            public required PluginUiOwner Owner { get; init; }
            public required PluginDialog Dialog { get; init; }
            public required Action<int> OnResult { get; init; }
            public Handle Handle { get; set; } = null!;
            private bool _done;

            // A host answers once, whether by a button or by a cancel.
            public void Answer(int index)
            {
                if (_done)
                    return;
                _done = true;
                OnResult(index);
            }
        }

        public List<Request> Requests { get; } = [];
        public List<(string Owner, string View, string? OwnerView)> Modals { get; } = [];

        public void AddMarkupPanel(string markupPath, object binding)
        {
        }

        public IDisposable RegisterMarkupPanel(string markupPath, object binding) =>
            NoOpUiRegistration.Instance;

        public IDisposable? Confirm(PluginUiOwner owner, PluginDialog dialog, Action<int> onResult)
        {
            var request = new Request { Owner = owner, Dialog = dialog, OnResult = onResult };
            request.Handle = new Handle(() => request.Answer(dialog.EffectiveButtons.Count - 1));
            Requests.Add(request);
            return request.Handle;
        }

        public bool ShowModal(PluginUiOwner owner, string viewName, string? ownerView = null)
        {
            Modals.Add((owner.Id, viewName, ownerView));
            return true;
        }
    }

    private sealed class StubHost(IUiRegistry ui) : IPluginHost
    {
        public bool HasUi => true;
        public IPluginLogger Log { get; } = new SilentLogger();
        public IGameState State { get; } = new EmptyGameState();
        public IEvents Events { get; } = new WorldEvents();
        public ISelectionService Selection { get; } = new SelectionState();
        public IUiRegistry Ui { get; } = ui;
        public IAutomationSurface Automation { get; } = NoOpAutomationSurface.Instance;

        private sealed class SilentLogger : IPluginLogger
        {
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message, Exception? error = null) { }
        }

        private sealed class EmptyGameState : IGameState
        {
            public IReadOnlyList<WorldEntitySnapshot> Entities { get; } = [];
        }
    }
}
