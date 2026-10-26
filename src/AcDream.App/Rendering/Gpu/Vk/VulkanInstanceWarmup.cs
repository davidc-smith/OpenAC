using AcDream.App.Platform;

namespace AcDream.App.Rendering.Gpu.Vk;

/// <summary>
/// The Vulkan instance, created on a worker while the rest of the client
/// starts. Creating it loads the Vulkan loader and every layer the machine
/// installs (overlays, capture and shader-cache tools), which took tens of
/// milliseconds the window's thread used to wait for. The window takes the
/// instance when it sets up Vulkan, provided it was made with every
/// extension the window's surface needs and the same optional extensions;
/// otherwise it is destroyed and the window creates its own, as it always
/// did. A creation that failed is dropped the same way, so the window's own
/// creation reports the failure in its usual words.
/// </summary>
internal sealed class VulkanInstanceWarmup : IDisposable
{
    /// <summary>A created instance and the API table it was created with.</summary>
    internal sealed record Warm(
        Silk.NET.Vulkan.Vk Api,
        VulkanInstanceFactory.Created Instance,
        bool OptionalExtensions);

    private const int Open = 0;
    private const int Taken = 1;
    private const int Disposed = 2;

    private readonly Task<Warm> _creation;
    private readonly Action<Warm> _destroy;
    private int _state = Open;

    internal VulkanInstanceWarmup(Task<Warm> creation, Action<Warm> destroy)
    {
        _creation = creation ?? throw new ArgumentNullException(nameof(creation));
        _destroy = destroy ?? throw new ArgumentNullException(nameof(destroy));
    }

    /// <summary>
    /// Starts creating the instance. The window system's required surface
    /// extensions are read here, on the calling (main) thread, because the
    /// window backend is initialized there; only the instance is created on
    /// the worker. Null when the window backend does not start or reports no
    /// Vulkan support: the window then fails in its own words.
    /// </summary>
    internal static VulkanInstanceWarmup? Start(bool enableOptionalExtensions)
    {
        IReadOnlyList<string>? required;
        try
        {
            required = GraphicalWindowBackendConfigurator.RequiredVulkanInstanceExtensions();
        }
        catch (Silk.NET.GLFW.GlfwException)
        {
            // The window backend did not start. Opening the window meets the
            // same failure (the backend keeps it) and reports it as before.
            return null;
        }
        if (required is null)
            return null;
        return new VulkanInstanceWarmup(
            Task.Run(() => Create(required, enableOptionalExtensions)),
            Destroy);
    }

    /// <summary>
    /// Hands the instance over, waiting for it if it is still being made,
    /// when it carries every extension in <paramref name="requiredExtensions"/>
    /// and was made with the same optional-extension choice. Otherwise the
    /// instance is destroyed and null is returned, for the caller to create
    /// its own. Only the first call can take it.
    /// </summary>
    internal Warm? TryTake(
        IReadOnlyList<string> requiredExtensions,
        bool enableOptionalExtensions)
    {
        ArgumentNullException.ThrowIfNull(requiredExtensions);
        if (Interlocked.CompareExchange(ref _state, Taken, Open) != Open)
            return null;

        Warm warm;
        try
        {
            warm = _creation.GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // The caller's own creation meets the same failure and reports it.
            return null;
        }

        bool fits = warm.OptionalExtensions == enableOptionalExtensions
            && requiredExtensions.All(name =>
                warm.Instance.EnabledExtensions.Contains(name, StringComparer.Ordinal));
        if (fits)
            return warm;
        _destroy(warm);
        return null;
    }

    /// <summary>Destroys the instance when nothing took it.</summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _state, Disposed, Open) != Open)
            return;
        Warm warm;
        try
        {
            warm = _creation.GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Nothing was created.
            return;
        }
        _destroy(warm);
    }

    private static Warm Create(
        IReadOnlyList<string> requiredExtensions,
        bool enableOptionalExtensions)
    {
        Silk.NET.Vulkan.Vk vk = GraphicalVulkanLoader.CreateApi();
        try
        {
            return new Warm(
                vk,
                VulkanInstanceFactory.Create(vk, requiredExtensions, enableOptionalExtensions),
                enableOptionalExtensions);
        }
        catch
        {
            vk.Dispose();
            throw;
        }
    }

    private static unsafe void Destroy(Warm warm)
    {
        warm.Api.DestroyInstance(warm.Instance.Instance, null);
        warm.Api.Dispose();
    }
}
