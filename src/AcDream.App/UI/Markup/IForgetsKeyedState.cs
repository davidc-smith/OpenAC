namespace AcDream.App.UI.Markup;

/// <summary>
/// A control state that can drop every value <see cref="KeyedControlState"/>
/// stored for some repeat keys, so rows that leave their list for good don't
/// leave their remembered state behind.
/// </summary>
internal interface IForgetsKeyedState
{
    /// <summary>
    /// Removes every value whose control name is <c>name#path</c> for one of
    /// <paramref name="paths"/>, or <c>name#path/...</c> for a repeat nested in it.
    /// </summary>
    void ForgetKeyed(IReadOnlySet<string> paths);
}
