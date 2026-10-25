using System.Reflection;
using AcDream.App.Rendering;
using Silk.NET.Windowing;

namespace AcDream.App.Tests.Rendering;

public sealed class NativeWindowLoopTests
{
    // The window's lifetime releases the native window last, after the input
    // context and the graphics surface bound to it. A loop that destroyed it
    // on the way out left those owners writing into a freed window: heap
    // corruption at exit.
    [Fact]
    public void LoopEndLeavesTheNativeWindowForItsLifetimeOwner()
    {
        var window = RecordingWindow.Create(framesBeforeClose: 2);

        NativeWindowLoop.Run(window);

        List<string> calls = RecordingWindow.Calls(window);
        Assert.DoesNotContain("Reset", calls);
        Assert.DoesNotContain("Dispose", calls);
        Assert.DoesNotContain("Close", calls);
    }

    [Fact]
    public void LoopRunsTheFramesAndHidesTheWindowWhenItCloses()
    {
        var window = RecordingWindow.Create(framesBeforeClose: 2);

        NativeWindowLoop.Run(window);

        Assert.Equal(
            [
                "Initialize",
                "Run",
                "DoEvents", "DoUpdate", "DoRender",
                "DoEvents", "DoUpdate", "DoRender",
                "DoEvents",
                "DoEvents",
                "IsVisible=False",
            ],
            RecordingWindow.Calls(window));
    }

    // A stand-in window: records what the loop asks of it and runs the frame
    // callback until it has been asked to close.
    public class RecordingWindow : DispatchProxy
    {
        private readonly List<string> _calls = [];
        private int _framesBeforeClose;
        private int _frames;

        public static IWindow Create(int framesBeforeClose)
        {
            IWindow window = Create<IWindow, RecordingWindow>();
            ((RecordingWindow)(object)window)._framesBeforeClose = framesBeforeClose;
            return window;
        }

        public static List<string> Calls(IWindow window) =>
            ((RecordingWindow)(object)window)._calls;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(method);
            switch (method.Name)
            {
                case "get_IsClosing":
                    return _frames >= _framesBeforeClose;
                case "Run" when args is [Action frame]:
                    _calls.Add("Run");
                    while (_frames <= _framesBeforeClose)
                    {
                        frame();
                        _frames++;
                    }
                    return null;
                case var name when name.StartsWith("set_", StringComparison.Ordinal):
                    _calls.Add($"{name[4..]}={args![0]}");
                    return null;
                case var name when name.StartsWith("get_", StringComparison.Ordinal):
                    return DefaultOf(method.ReturnType);
                default:
                    _calls.Add(method.Name);
                    return DefaultOf(method.ReturnType);
            }
        }

        private static object? DefaultOf(Type type) =>
            type.IsValueType && type != typeof(void) ? Activator.CreateInstance(type) : null;
    }
}
