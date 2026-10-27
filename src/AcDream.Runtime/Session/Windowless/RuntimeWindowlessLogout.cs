using AcDream.Core.Items;
using AcDream.Runtime.World;

namespace AcDream.Runtime.Session.Windowless;

internal enum RuntimeWindowlessLogoutOutcome
{
    None,
    Completed,
    TimedOut,
}

/// <summary>
/// A plugin-requested log off with no window: there is no portal-tunnel
/// presentation to route it through, so it begins the log off at once and
/// completes it on the session's own tick once the server confirms it,
/// returning the character to character selection. Every client without a
/// window uses it.
/// </summary>
internal sealed class RuntimeWindowlessLogout
{
    // Above the longest server logoff hold, so a real confirmation lands first.
    internal static readonly TimeSpan DefaultConfirmationDeadline =
        TimeSpan.FromSeconds(45);

    private readonly GameRuntime _runtime;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _confirmationDeadline;
    private readonly Func<bool> _isConfirmed;
    private readonly object _gate = new();
    private bool _began;
    private bool _completed;
    private long _deadlineTimestamp;

    internal RuntimeWindowlessLogout(
        GameRuntime runtime,
        TimeProvider? timeProvider = null,
        TimeSpan? confirmationDeadline = null,
        Func<bool>? isConfirmed = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _confirmationDeadline = confirmationDeadline ?? DefaultConfirmationDeadline;
        _isConfirmed = isConfirmed
            ?? (() => _runtime.Session.CurrentSession?.IsCharacterLogOffConfirmed == true);
    }

    internal bool CanRequestLogout
    {
        get
        {
            lock (_gate)
                return CanRequestLogoutLocked();
        }
    }

    private bool CanRequestLogoutLocked() =>
        _runtime.Session.IsInWorld
        && !_runtime.TransitOwner.IsLogoutActive
        && !_runtime.TransitOwner.IsTeleportActive
        && !_runtime.TransitOwner.HasPendingTeleportStart;

    internal bool TryRequestLogout()
    {
        lock (_gate)
        {
            if (!CanRequestLogoutLocked()
                || !_runtime.TransitOwner.TryBeginLogoutRequest(IsLocalPlayerKiller))
            {
                return false;
            }
            // A log off after an earlier one completed (the character entered
            // the world again) is a new one.
            _began = false;
            _completed = false;
            _deadlineTimestamp = AddClamped(
                _timeProvider,
                _timeProvider.GetTimestamp(),
                _confirmationDeadline);
            TryBeginLocked();
            return true;
        }
    }

    private bool IsLocalPlayerKiller
    {
        get
        {
            var bitfield = (PublicWeenieFlags)(_runtime.InventoryOwner.Objects
                .Get(_runtime.PlayerIdentity.ServerGuid)?.PublicWeenieBitfield ?? 0u);
            return (bitfield & PublicWeenieFlags.PlayerKiller) != 0
                || (bitfield & PublicWeenieFlags.PkLiteStatus) != 0;
        }
    }

    // A refusal here, past the transit guard above, means the call landed inside a nested
    // top-level session operation; the next session tick retries it.
    private void TryBeginLocked()
    {
        if (!_began
            && _runtime.Session.BeginCharacterLogOff(_runtime.Generation).Accepted)
        {
            _began = true;
        }
    }

    internal RuntimeWindowlessLogoutOutcome Tick()
    {
        lock (_gate)
        {
            if (_completed || !_runtime.TransitOwner.IsLogoutActive)
                return RuntimeWindowlessLogoutOutcome.None;
            if (!_began)
                TryBeginLocked();
            if (_began
                && _runtime.TransitOwner.LogoutStage == RuntimeLogoutStage.Requested
                && _isConfirmed())
            {
                _runtime.TransitOwner.AcknowledgeLogoutConfirmed();
            }
            if (_runtime.TransitOwner.LogoutStage == RuntimeLogoutStage.Confirmed
                && _runtime.Session.CompleteCharacterLogOff(_runtime.Generation).Accepted)
            {
                _runtime.TransitOwner.CompleteLogout();
                _completed = true;
                return RuntimeWindowlessLogoutOutcome.Completed;
            }
            if (_timeProvider.GetTimestamp() < _deadlineTimestamp)
                return RuntimeWindowlessLogoutOutcome.None;

            _completed = true;
            _runtime.TransitOwner.CancelLogoutRequest();
            return RuntimeWindowlessLogoutOutcome.TimedOut;
        }
    }

    // A deadline past the end of the clock never comes, rather than wrapping.
    private static long AddClamped(
        TimeProvider timeProvider,
        long timestamp,
        TimeSpan duration)
    {
        double ticks = duration.TotalSeconds * timeProvider.TimestampFrequency;
        if (!double.IsFinite(ticks) || ticks > long.MaxValue)
            return long.MaxValue;
        long delta = Math.Max(1L, checked((long)Math.Ceiling(ticks)));
        return timestamp > long.MaxValue - delta
            ? long.MaxValue
            : timestamp + delta;
    }
}
