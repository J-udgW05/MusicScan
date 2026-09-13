using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MusicScanIntegrity.Core.Discovery;

/// <summary>Каким носителем оказался диск с проверяемой папкой.</summary>
public enum StorageType
{
    /// <summary>Определить не удалось — сеть, виртуальный диск, отказ в доступе.</summary>
    Unknown,

    /// <summary>Диск с подвижной головкой: перемотка стоит времени.</summary>
    HardDisk,

    /// <summary>Твердотельный: перемотка ничего не стоит.</summary>
    SolidState,
}

/// <summary>
/// Определяет тип носителя, на котором лежит папка.
/// </summary>
/// <remarks>
/// Нужно ради одного решения: сколько файлов читать одновременно. На жёстком
/// диске восемь параллельных чтений заставляют головку метаться между дорожками,
/// и проверка идёт медленнее, чем в два потока. На твердотельном перемотки нет,
/// и ограничивать нечего.
/// </remarks>
public static class StorageTypeDetector
{
    private const uint IoctlStorageQueryProperty = 0x2D1400;
    private const int SeekPenaltyProperty = 7;
    private const int StandardQuery = 0;
    private const uint GenericNone = 0;
    private const uint FileShareReadWrite = 0x00000003;
    private const uint OpenExisting = 3;

    /// <summary>Определяет тип носителя для пути.</summary>
    /// <param name="path">Папка или файл.</param>
    /// <returns>Тип носителя; <see cref="StorageType.Unknown" />, если выяснить не удалось.</returns>
    public static StorageType Detect(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return StorageType.Unknown;
        }

        try
        {
            string full = Path.GetFullPath(path);

            // Сетевые пути к физическому диску отношения не имеют.
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
            // Ответ этой проверки — подсказка, а не факт: она только выбирает
            // число потоков. Любая неожиданность здесь означает «не знаю»,
            // а не повод ронять проверку коллекции.
            return StorageType.Unknown;
        }
    }

    /// <summary>Спрашивает у драйвера, стоит ли перемотка времени.</summary>
    private static StorageType QuerySeekPenalty(string device)
    {
        // Нулевые права доступа: описание устройства читается и без права на
        // чтение данных, а с ними Windows потребовала бы прав администратора.
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
