using AcDream.App.UI.Layout;
using AcDream.Core.Net.Messages;

namespace AcDream.App.UI;

public sealed class GameplayConfirmationController : IDisposable
{
    private readonly RetailDialogFactory _dialogs;
    private readonly Action<uint, uint, bool> _sendResponse;
    private readonly Func<uint, string, string?>? _composeMessage;
    private uint _dialogContext;
    private uint _serverType;
    private uint _serverContext;
    private GameEvents.CharacterConfirmationRequest? _request;
    private bool _disposed;

    public GameplayConfirmationController(
        RetailDialogFactory dialogs,
        Action<uint, uint, bool> sendResponse,
        Func<uint, string, string?>? composeMessage = null)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _sendResponse = sendResponse ?? throw new ArgumentNullException(nameof(sendResponse));
        _composeMessage = composeMessage;
        _dialogs.DialogClosed += OnDialogClosed;
    }

    public uint ActiveDialogContext => _dialogContext;

    public bool HandleRequest(GameEvents.CharacterConfirmationRequest request)
    {
        if (_dialogContext != 0u)
            return false;

        _serverType = request.Type;
        _serverContext = request.ContextId;
        _request = request;

        string message = request.Type is 2u or 3u or 5u or 6u
            ? request.Message + " Continue?"
            : _composeMessage?.Invoke(request.Type, request.Message)
                ?? request.Message;
        var data = RetailDialogData.Confirmation(message);
        _dialogContext = _dialogs.MakeDialog(data);
        return _dialogContext != 0u;
    }

    public bool HandleDone(GameEvents.CharacterConfirmationDone done)
    {
        if (_dialogContext == 0u
            || done.Type != _serverType
            || done.ContextId != _serverContext)
            return false;
        return _dialogs.CloseDialog(_dialogContext);
    }

    public bool TryAnswer(uint contextId, bool accept)
    {
        if (_dialogContext == 0u || contextId != _serverContext)
            return false;
        if (!_dialogs.TrySetConfirmationResult(_dialogContext, accept))
            return false;
        return _dialogs.CloseDialog(_dialogContext);
    }

    /// <summary>
    /// Takes the server's question out of an open dialog without answering
    /// it: the dialog closes and nothing is sent, now or when the dialogs are
    /// torn down. A window closing while the session plays on hands the
    /// question to the session this way. Null when no question is open.
    /// </summary>
    public GameEvents.CharacterConfirmationRequest? TakeOpen()
    {
        if (_dialogContext == 0u || _request is not { } request)
            return null;
        uint dialog = _dialogContext;
        ResetSession();
        // Forgotten first, so the close below answers nothing.
        _dialogs.CloseDialog(dialog);
        return request;
    }

    public void ResetSession()
    {
        _dialogContext = 0u;
        _serverType = 0u;
        _serverContext = 0u;
        _request = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dialogs.DialogClosed -= OnDialogClosed;
    }

    private void OnDialogClosed(uint context, RetailDialogData data)
    {
        if (context != _dialogContext
            || data.GetUInt32(RetailDialogProperty.Type) !=
                (uint)RetailDialogType.Confirmation)
            return;

        bool accepted = data.GetBoolean(RetailDialogProperty.ConfirmationResult);
        _sendResponse(_serverType, _serverContext, accepted);
        ResetSession();
    }
}
