using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SatellesTSD;

/// <summary>Whether two paths name one file. The spellings are compared first; when they differ and both files exist,
/// the files themselves are (the volume's serial number and the file's index), so a store on a mapped drive and the
/// same store by its network path, or by a short 8.3 name, are one store (decision 147).</summary>
internal static class FileIdentity
{
    public static bool Same(string a, string? b)
    {
        if (b is null)
        {
            return false;
        }

        string fullA, fullB;
        try
        {
            (fullA, fullB) = (Path.GetFullPath(a), Path.GetFullPath(b));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(fullA, fullB, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Of(fullA) is { } idA && Of(fullB) is { } idB && idA == idB;
    }

    /// <summary>The file's identity on its volume, or null when the file cannot be opened (missing, no access, offline).</summary>
    private static (uint Volume, ulong Index)? Of(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            // shared with every reader and writer: the identity is read while the store is open elsewhere, unless held exclusively
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return GetFileInformationByHandle(handle, out var info) ? (info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation information);
}
