using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MusicScanIntegrity.Core.Discovery;

/// <summary>What kind of drive holds the scanned folder.</summary>
public enum StorageType
{
    /// <summary>Could not be determined: network, virtual disk, access denied.</summary>
    Unknown,

    /// <summary>Spinning disk, where seeking costs time.</summary>
    HardDisk,

    /// <summary>Solid state, where seeking is free.</summary>
    SolidState,
}

/// <summary>
/// Detects the drive type a folder sits on.
/// </summary>
/// <remarks>
/// Exists for one decision: how many files to read at once. On a spinning
/// disk eight parallel reads make the head seek between tracks and run slower
/// than two threads. An SSD has no such penalty.
/// </remarks>
public static class StorageTypeDetector
{
    private const uint IoctlStorageQueryProperty = 0x2D1400;
    private const int SeekPenaltyProperty = 7;
    private const int StandardQuery = 0;
    private const uint GenericNone = 0;
    private const uint FileShareReadWrite = 0x00000003;
    private const uint OpenExisting = 3;

    /// <summary>Detects the drive type for a path.</summary>
    /// <param name="path">Folder or file.</param>
    /// <returns>The drive type, or <see cref="StorageType.Unknown" /> if it could not be determined.</returns>
    public static StorageType Detect(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return StorageType.Unknown;
        }

        try
        {
            string full = Path.GetFullPath(path);

            // Network paths have no bearing on a physical disk.
            if (full.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return StorageType.Unknown;
            }

            string root = Path.GetPathRoot(full) ?? string.Empty;
            if (root.Length < 2 || root[1] != ':')
            {
                return StorageType.Unknown;
            }

            return QuerySeekPenalty($@"\\.\{root[0]}:");
        }
        catch (Exception)
        {
            // The answer is a hint, not a fact: it only picks a thread count.
            // Anything unexpected means "unknown" rather than a failed scan.
            return StorageType.Unknown;
        }
    }

    /// <summary>Asks the driver whether seeking incurs a penalty.</summary>
    private static StorageType QuerySeekPenalty(string device)
    {
        // Zero access rights: the device description can be read without data
        // read access, which would otherwise require administrator rights.
        using SafeFileHandle handle = CreateFile(
            device,
            GenericNone,
            FileShareReadWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            return StorageType.Unknown;
        }

        StoragePropertyQuery query = new()
        {
            PropertyId = SeekPenaltyProperty,
            QueryType = StandardQuery,
        };

        SeekPenaltyDescriptor descriptor = default;
        int querySize = Marshal.SizeOf<StoragePropertyQuery>();
        int descriptorSize = Marshal.SizeOf<SeekPenaltyDescriptor>();

        IntPtr queryBuffer = Marshal.AllocHGlobal(querySize);
        IntPtr resultBuffer = Marshal.AllocHGlobal(descriptorSize);

        try
        {
            Marshal.StructureToPtr(query, queryBuffer, fDeleteOld: false);

            bool ok = DeviceIoControl(
                handle,
                IoctlStorageQueryProperty,
                queryBuffer,
                querySize,
                resultBuffer,
                descriptorSize,
                out _,
                IntPtr.Zero);

            if (!ok)
            {
                return StorageType.Unknown;
            }

            descriptor = Marshal.PtrToStructure<SeekPenaltyDescriptor>(resultBuffer);
            return descriptor.IncursSeekPenalty ? StorageType.HardDisk : StorageType.SolidState;
        }
        finally
        {
            Marshal.FreeHGlobal(queryBuffer);
            Marshal.FreeHGlobal(resultBuffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StoragePropertyQuery
    {
        public int PropertyId;
        public int QueryType;
        public byte AdditionalParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SeekPenaltyDescriptor
    {
        public uint Version;
        public uint Size;

        [MarshalAs(UnmanagedType.U1)]
        public bool IncursSeekPenalty;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        IntPtr inBuffer,
        int inBufferSize,
        IntPtr outBuffer,
        int outBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);
}
