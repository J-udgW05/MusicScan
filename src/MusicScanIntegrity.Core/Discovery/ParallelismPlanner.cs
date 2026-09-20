using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Discovery;

/// <summary>
/// Decides how many files are checked at once.
/// </summary>
/// <remarks>
/// Kept out of the engine as a pure function: the decision depends on the
/// settings and the drive type, and has to be testable for every combination
/// rather than whichever disk happens to be in the machine.
/// </remarks>
public static class ParallelismPlanner
{
    /// <summary>Picks the thread count.</summary>
    /// <param name="settings">Current settings.</param>
    /// <param name="storage">Drive type the collection sits on.</param>
    /// <returns>How many files to check at once.</returns>
    public static int Resolve(AppSettings settings, StorageType storage)
    {
        ArgumentNullException.ThrowIfNull(settings);

        int requested = settings.EffectiveParallelism;

        if (!settings.RespectDriveType || requested <= AppSettings.HardDiskParallelism)
        {
            return requested;
        }

        // Capped only where seeking actually costs time. "Unknown" is not a
        // reason to change behaviour; guessing would be worse.
        return storage == StorageType.HardDisk ? AppSettings.HardDiskParallelism : requested;
    }
}
