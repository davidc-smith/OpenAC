using System.Runtime.InteropServices;

namespace AcDream.Launcher.Core.Updates;

/// <summary>Makes a rename inside a directory durable by fsyncing the directory itself.</summary>
internal static partial class DirectorySync
{
    private const int ReadOnly = 0; // O_RDONLY is 0 on macOS and Linux.

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static partial int Fsync(int fd);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int Close(int fd);

    /// <summary>Best effort: returns true when the directory was fsynced. Never throws; the renamed
    /// file is already in place, so a failure here only weakens durability. On Windows it does
    /// nothing (returns false): NTFS journals the rename itself.</summary>
    internal static bool TryFlush(string directory)
    {
        if (OperatingSystem.IsWindows() || string.IsNullOrEmpty(directory))
        {
            return false;
        }

        try
        {
            int fd = Open(directory, ReadOnly);
            if (fd < 0)
            {
                return false;
            }

            try { return Fsync(fd) == 0; }
            finally { _ = Close(fd); }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException
            or MarshalDirectiveException or ArgumentException)
        {
            return false;
        }
    }
}
