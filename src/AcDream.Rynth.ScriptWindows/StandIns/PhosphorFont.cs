// RynthCore's Phosphor icon font (MIT; see NOTICE.md), embedded from the submodule.
using System.IO;

namespace RynthCore.Engine.ImGuiBackend
{
    internal static class PhosphorFont
    {
        private const string Resource = "AcDream.Rynth.ScriptWindows.Phosphor.ttf";

        /// <summary>The font file's bytes, or null if the resource is missing.</summary>
        public static byte[]? Load()
        {
            using Stream? stream = typeof(PhosphorFont).Assembly.GetManifestResourceStream(Resource);
            if (stream is null)
                return null;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
    }
}
