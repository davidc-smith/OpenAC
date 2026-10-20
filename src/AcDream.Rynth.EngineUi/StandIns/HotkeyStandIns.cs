// Stand-ins for the engine types RynthCore's HotkeyService uses that OpenAC doesn't compile.
using System;

namespace RynthCore.Engine.Compatibility
{
    /// <summary>
    /// "Is the player typing in one of AC's own text boxes?" RynthCore reads acclient's UI focus
    /// element; here the host points <see cref="IsTyping"/> at OpenAC's chat entry and UI focus.
    /// </summary>
    internal static class AcKeyboardFocus
    {
        internal static Func<bool>? IsTyping { get; set; }

        internal static Func<string>? Where { get; set; }

        /// <summary>Null (unknown) until the host points it somewhere, as RynthCore without a UI manager.</summary>
        public static bool? HasTextFocus() => IsTyping?.Invoke();

        public static string Describe() =>
            IsTyping is null ? "OpenAC's keyboard focus: unknown." : $"OpenAC's keyboard focus: {Where?.Invoke() ?? "unknown"}.";
    }
}

namespace RynthCore.Engine
{
    /// <summary>RynthCore reloads its engine (the "reload" hotkey, /rc rl); OpenAC has no engine reload.</summary>
    internal static class EngineLifecycle
    {
        public static void SignalReload() =>
            Compatibility.AcMainThreadQueue.EnqueueWriteToChat("[RynthCore] Reload isn't in OpenAC.", 1);
    }
}
