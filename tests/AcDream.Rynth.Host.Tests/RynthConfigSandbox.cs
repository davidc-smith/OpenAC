using System.Runtime.InteropServices;

namespace AcDream.TestSupport;

/// <summary>
/// Points HOME and XDG_CONFIG_HOME at a temp directory so RynthInventory (which stores files
/// under Environment.SpecialFolder.ApplicationData) never touches the real user folder.
/// .NET resolves ApplicationData from XDG_CONFIG_HOME on Linux. On macOS it ignores both HOME and
/// XDG_CONFIG_HOME (it asks CoreFoundation, which honours CFFIXED_USER_HOME read from the native
/// environment on every call), so there it is set with libc setenv, not Environment.SetEnvironmentVariable,
/// and the directory needs a Library/Application Support folder. Process-wide state: only use from tests that never run
/// in parallel (this assembly, or the "Rynth" collection).
/// It also points RYNTHCORE_DIR and RYNTHSUITE_DIR (RynthSuite's install folders) inside the sandbox.
/// </summary>
internal sealed class RynthConfigSandbox : IDisposable
{
    private readonly string? _previousHome = Environment.GetEnvironmentVariable("HOME");
    private readonly string? _previousXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly string? _previousCoreDir = Environment.GetEnvironmentVariable("RYNTHCORE_DIR");
    private readonly string? _previousSuiteDir = Environment.GetEnvironmentVariable("RYNTHSUITE_DIR");
    private readonly string? _previousCfHome = OperatingSystem.IsMacOS() ? NativeGetEnv("CFFIXED_USER_HOME") : null;

    public RynthConfigSandbox(string prefix)
    {
        Root = Directory.CreateTempSubdirectory(prefix).FullName;
        Environment.SetEnvironmentVariable("HOME", Root);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Root);
        // RynthSuite plugins (RynthAi) find their folders through these first, on every OS.
        // Their RynthInstallPaths resolves once per process: the first sandbox's folders stay
        // in use, which is still a temp folder, never the real C:\Games or ApplicationData one.
        Environment.SetEnvironmentVariable("RYNTHCORE_DIR", Path.Combine(Root, "RynthCore"));
        Environment.SetEnvironmentVariable("RYNTHSUITE_DIR", Path.Combine(Root, "RynthSuite"));
        if (OperatingSystem.IsMacOS())
        {
            Directory.CreateDirectory(Path.Combine(Root, "Library", "Application Support"));
            NativeSetEnv("CFFIXED_USER_HOME", Root, 1);
        }
    }

    public string Root { get; }

    [DllImport("libc", EntryPoint = "setenv")]
    private static extern int NativeSetEnv(string name, string value, int overwrite);

    [DllImport("libc", EntryPoint = "unsetenv")]
    private static extern int NativeUnsetEnv(string name);

    private static string? NativeGetEnv(string name) => Marshal.PtrToStringUTF8(NativeGetEnvPtr(name));

    [DllImport("libc", EntryPoint = "getenv")]
    private static extern IntPtr NativeGetEnvPtr(string name);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HOME", _previousHome);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _previousXdg);
        Environment.SetEnvironmentVariable("RYNTHCORE_DIR", _previousCoreDir);
        Environment.SetEnvironmentVariable("RYNTHSUITE_DIR", _previousSuiteDir);
        if (OperatingSystem.IsMacOS())
        {
            if (_previousCfHome is null) NativeUnsetEnv("CFFIXED_USER_HOME");
            else NativeSetEnv("CFFIXED_USER_HOME", _previousCfHome, 1);
        }
        try { Directory.Delete(Root, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
