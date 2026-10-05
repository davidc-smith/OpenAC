using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>
/// <see cref="RetailWindowManager.EnforceMinimumSize"/>: a window whose
/// minimum grew is brought up to it whether or not it is resizable, and kept
/// on screen.
/// </summary>
public sealed class RetailWindowManagerMinimumTests
{
    private static (UiRoot Root, UiNineSlicePanel Frame, RetailWindowHandle Handle, List<string> Events) Window(
        float x, float y)
    {
        var frame = MarkupDocument.Build(
            $"<panel x=\"{x}\" y=\"{y}\" w=\"300\" h=\"200\"></panel>", new object(), _ => (1u, 32, 32));
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(frame);
        RetailWindowHandle handle = root.RegisterWindow("plugin-min", frame);
        var events = new List<string>();
        handle.Resized += _ => events.Add("resized");
        handle.Moved += _ => events.Add("moved");
        return (root, frame, handle, events);
    }

    [Fact]
    public void A_window_that_cannot_be_resized_still_grows_to_its_minimum()
    {
        var (root, frame, _, events) = Window(10f, 10f);
        Assert.False(frame.ResizeX);
        frame.MinWidth = 400f;
        frame.MinHeight = 250f;

        Assert.True(root.WindowManager.EnforceMinimumSize("plugin-min"));

        Assert.Equal((10f, 10f, 400f, 250f), (frame.Left, frame.Top, frame.Width, frame.Height));
        Assert.Equal(["resized"], events);
    }

    [Fact]
    public void A_window_grown_past_the_screen_edge_moves_back_onto_it()
    {
        var (root, frame, _, events) = Window(450f, 380f);
        frame.MinWidth = 400f;
        frame.MinHeight = 250f;

        root.WindowManager.EnforceMinimumSize("plugin-min");

        Assert.Equal((400f, 350f, 400f, 250f), (frame.Left, frame.Top, frame.Width, frame.Height));
        Assert.Equal(["resized", "moved"], events);
    }

    [Fact]
    public void A_minimum_larger_than_the_screen_takes_the_screen_at_its_origin()
    {
        var (root, frame, _, _) = Window(100f, 100f);
        frame.MinHeight = 900f;

        root.WindowManager.EnforceMinimumSize("plugin-min");

        Assert.Equal((100f, 0f, 300f, 600f), (frame.Left, frame.Top, frame.Width, frame.Height));
    }

    [Fact]
    public void A_window_already_at_its_minimum_is_left_alone()
    {
        var (root, frame, _, events) = Window(10f, 10f);
        frame.MinWidth = 100f;

        Assert.True(root.WindowManager.EnforceMinimumSize("plugin-min"));
        Assert.False(root.WindowManager.EnforceMinimumSize("nobody"));

        Assert.Equal((300f, 200f), (frame.Width, frame.Height));
        Assert.Empty(events);
    }
}
