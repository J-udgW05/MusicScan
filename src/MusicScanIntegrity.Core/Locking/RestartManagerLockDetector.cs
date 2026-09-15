using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MusicScanIntegrity.Core.Locking;

/// <summary>Identifies what holds a file locked.</summary>
public interface ILockOwnerDetector
{
    /// <summary>
    /// Tries to determine which processes hold the file. On failure the
    /// result carries
    /// <see cref="LockOwnerResult.IsReliable"/> = <see langword="false"/>,
    /// and that is shown to the user as such.
    /// </summary>
    LockOwnerResult Detect(string filePath);
}

/// <summary>Who holds the file, and how far the answer can be trusted.</summary>
/// <param name="IsReliable">Detection succeeded and the result can be trusted.</param>
/// <param name="TechnicalDetail">Technical cause when detection failed.</param>
public sealed record LockOwnerResult(
    IReadOnlyList<LockOwner> Owners,
    bool IsReliable,
    string? TechnicalDetail = null)
{
    /// <summary>The owner could not be determined.</summary>
    public static LockOwnerResult Unknown(string? detail = null) => new([], false, detail);

    /// <summary>Text for the user; never invents what is not known.</summary>
    public string DisplayText => !IsReliable
        ? "Определить программу-владельца не удалось."
        : Owners.Count == 0
            ? "Похоже, файл уже освободился — ни одна программа его не держит."
            : "Файл держит: " + string.Join(", ", Owners.Select(o => o.DisplayName));
}

/// <summary>A process holding the file.</summary>
/// <param name="Description">Application description, when Windows knows one.</param>
public sealed record LockOwner(int ProcessId, string ProcessName, string? Description)
{
    /// <summary>How to present the process to the user.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Description)
        ? $"{ProcessName} (PID {ProcessId})"
        : $"{Description} — {ProcessName} (PID {ProcessId})";
}

/// <summary>
/// Owner detection through Restart Manager, the most reliable documented
/// method on Windows.
/// </summary>
/// <remarks>
/// An unreliable process list must never be shown: the user could close the
/// wrong application. Any API error therefore yields "could not determine"
/// rather than a heuristic guess.
/// </remarks>
public sealed class RestartManagerLockDetector : ILockOwnerDetector
{
    private const int RmRebootReasonNone = 0;
    private const int CchRmMaxAppName = 255;
    private const int CchRmMaxSvcName = 63;
    private const int ErrorSuccess = 0;
    private const int ErrorMoreData = 234;

    /// <inheritdoc />
    public LockOwnerResult Detect(string filePath)
    {
        uint handle = 0;

        try
        {
            int result = RmStartSession(out handle, 0, Guid.NewGuid().ToString("N"));
            if (result != ErrorSuccess)
            {
                return LockOwnerResult.Unknown($"RmStartSession → {result}");
            }

            string[] resources = [filePath];
            result = RmRegisterResources(handle, (uint)resources.Length, resources, 0, null, 0, null);
            if (result != ErrorSuccess)
            {
                return LockOwnerResult.Unknown($"RmRegisterResources → {result}");
            }

            uint needed = 0;
            uint count = 0;
            uint reason = 0;

            // The first call reports the array size, the second fills it.
            result = RmGetList(handle, out needed, ref count, null, ref reason);

            if (result == ErrorSuccess && needed == 0)
            {
                return new LockOwnerResult([], true);
            }

            if (result != ErrorMoreData)
            {
                return LockOwnerResult.Unknown($"RmGetList → {result}");
            }

            RmProcessInfo[] processes = new RmProcessInfo[needed];
            count = needed;
            result = RmGetList(handle, out needed, ref count, processes, ref reason);

            if (result != ErrorSuccess)
            {
                return LockOwnerResult.Unknown($"RmGetList (2) → {result}");
            }

            List<LockOwner> owners = [];
            for (int i = 0; i < count; i++)
            {
                RmProcessInfo info = processes[i];
                string name = info.strAppName;

                try
                {
                    using Process process = Process.GetProcessById(info.Process.dwProcessId);
                    name = process.ProcessName;
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    // Process exited between calls; keep the Restart Manager name.
                }

                owners.Add(new LockOwner(
                    info.Process.dwProcessId,
                    name,
                    string.IsNullOrWhiteSpace(info.strAppName) ? null : info.strAppName));
            }

            return new LockOwnerResult(owners, true);
        }
        catch (Exception ex)
        {
            return LockOwnerResult.Unknown($"{ex.GetType().Name} · {ex.Message}");
        }
        finally
        {
            if (handle != 0)
            {
                RmEndSession(handle);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxAppName + 1)]
        public string strAppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxSvcName + 1)]
        public string strServiceShortName;

        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint pSessionHandle,
        uint nFiles,
        string[]? rgsFilenames,
        uint nApplications,
        RmUniqueProcess[]? rgApplications,
        uint nServices,
        string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RmProcessInfo[]? rgAffectedApps,
        ref uint lpdwRebootReasons);
}
