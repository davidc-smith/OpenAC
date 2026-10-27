using AcDream.Launcher.Core.Orchestration;

namespace AcDream.Launcher.ViewModels;

/// <summary>
/// Closing the launcher window. While any client the launcher started still
/// runs, the close asks first, because closing the launcher logs every one
/// of them off; with none running it closes at once, so a script that stops
/// every session first is never asked.
/// </summary>
public sealed partial class LauncherWindowViewModel
{
    /// <summary>The confirmation's title.</summary>
    public const string ClosePromptTitle = "Close the launcher?";

    private int? _closePromptRunning;
    private bool _closeConfirmed;
    private RelayCommand? _confirmCloseCommand;
    private RelayCommand? _cancelCloseCommand;

    /// <summary>Raised when the player confirms the close; the window then closes.</summary>
    public event Action? CloseConfirmed;

    /// <summary>Whether the close confirmation is showing.</summary>
    public bool IsClosePromptOpen => _closePromptRunning is not null;

    /// <summary>The confirmation's text, while it shows.</summary>
    public string? ClosePromptMessage =>
        _closePromptRunning is { } running ? ClosePromptText(running) : null;

    /// <summary>Whether the player confirmed closing.</summary>
    public bool IsCloseConfirmed => _closeConfirmed;

    /// <summary>The confirmation's Close: the window closes, stopping every client.</summary>
    public RelayCommand ConfirmCloseCommand =>
        _confirmCloseCommand ??= new RelayCommand(ConfirmClose);

    /// <summary>The confirmation's Cancel: nothing closes and every client runs on.</summary>
    public RelayCommand CancelCloseCommand =>
        _cancelCloseCommand ??= new RelayCommand(CancelClose);

    /// <summary>The confirmation's text for this many running clients.</summary>
    public static string ClosePromptText(int running) =>
        running == 1
            ? "Closing the launcher will log off and close 1 running client."
            : $"Closing the launcher will log off and close {running} running clients.";

    /// <summary>
    /// The window's close button: true when it may close now. While any
    /// client the launcher started still runs it shows the confirmation and
    /// returns false; a session that has ended does not count.
    /// </summary>
    public bool RequestClose()
    {
        if (_closeConfirmed)
            return true;

        int running = CountRunningClients();
        if (running == 0)
        {
            SetClosePrompt(null);
            return true;
        }

        SetClosePrompt(running);
        return false;
    }

    private void ConfirmClose()
    {
        if (_closePromptRunning is null)
            return;
        _closeConfirmed = true;
        SetClosePrompt(null);
        OnPropertyChanged(nameof(IsCloseConfirmed));
        CloseConfirmed?.Invoke();
    }

    private void CancelClose() => SetClosePrompt(null);

    private int CountRunningClients()
    {
        LauncherStateSnapshot snapshot;
        try
        {
            snapshot = _orchestrator.GetSnapshot();
        }
        catch (ObjectDisposedException)
        {
            // Nothing is known to run; closing stops whatever does.
            return 0;
        }

        return snapshot.Sessions.Count(session => session.IsProcessOrStartActive);
    }

    private void SetClosePrompt(int? running)
    {
        if (_closePromptRunning == running)
            return;
        _closePromptRunning = running;
        OnPropertyChanged(nameof(IsClosePromptOpen));
        OnPropertyChanged(nameof(ClosePromptMessage));
        NotifyDesktopModal();
    }
}
