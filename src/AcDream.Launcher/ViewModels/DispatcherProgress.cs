namespace AcDream.Launcher.ViewModels;

/// <summary>An <see cref="IProgress{T}"/> that delivers every report on the UI thread.</summary>
internal sealed class DispatcherProgress<T>(IUiDispatcher dispatcher, Action<T> callback) : IProgress<T>
{
    public void Report(T value) => dispatcher.Post(() => callback(value));
}
