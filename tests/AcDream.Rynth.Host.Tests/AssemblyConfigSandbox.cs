using System.Runtime.CompilerServices;
using AcDream.TestSupport;

namespace AcDream.Rynth.Host.Tests;

/// <summary>
/// Every RynthHost test loads the Oracle and Tracker plugins, which write under the user's
/// ApplicationData folder too. Keep the whole test process out of the real folder; classes that
/// need their own folder (RynthInventoryTests) nest a sandbox on top of this one.
/// </summary>
internal static class AssemblyConfigSandbox
{
    [ModuleInitializer]
    internal static void Install()
    {
        var sandbox = new RynthConfigSandbox("rynth-host-tests-");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => sandbox.Dispose();
    }
}
