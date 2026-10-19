using System;
using System.IO;

namespace AcDream.Rynth.EngineUi;

/// <summary>
/// RynthCore finds client_portal.dat beside acclient.exe (Environment.ProcessPath).
/// After compile, the redirect tool points their Environment.ProcessPath reads here:
/// a path in OpenAC's dat folder once the host has set it, else the real one.
/// </summary>
internal static class ProcessPathStandIn
{
    /// <summary>OpenAC's dat folder; the host sets it on attach and clears it on dispose.</summary>
    internal static string? DatDirectory { get; set; }

    public static string? get_ProcessPath() =>
        string.IsNullOrEmpty(DatDirectory) ? Environment.ProcessPath : Path.Combine(DatDirectory, "acclient.exe");
}
