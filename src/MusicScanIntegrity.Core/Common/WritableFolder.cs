namespace MusicScanIntegrity.Core.Common;

/// <summary>
/// Picks a folder the application may write to.
/// </summary>
/// <remarks>
/// The build is portable — settings and the history database sit next to the
/// executable so the folder can be copied and carried away — but it can also
/// be installed into Program Files, where writing is denied. In that case
/// everything moves to the user profile.
/// <para>
/// Writability is probed by writing rather than by inspecting permissions:
/// Program Files, read-only media and security policy all deny writes, and
/// only the outcome matters.
/// </para>
/// </remarks>
public static class WritableFolder
{
    private const string AppDataFolderName = "MusicScanIntegrity";

    /// <summary>Probes whether the folder accepts writes, creating it if needed.</summary>
    public static bool CanWriteTo(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);

            string probe = Path.Combine(folder, ".write-probe");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// The folder next to the executable when writable, otherwise one inside
    /// the user profile.
    /// </summary>
    /// <param name="subFolder">Nested folder name, for example "config" or "data".</param>
    /// <param name="roaming">Fall back to %APPDATA% instead of %LOCALAPPDATA%.</param>
    /// <remarks>
    /// Settings use the roaming profile because they always have: moving them
    /// would lose the settings of every existing user. The history database is
    /// local — it grows to tens of megabytes and has no business following a
    /// profile between machines.
    /// </remarks>
    public static string Resolve(string subFolder, bool roaming = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subFolder);

        string beside = Path.Combine(AppContext.BaseDirectory, subFolder);

        if (CanWriteTo(beside))
        {
            return beside;
        }

        Environment.SpecialFolder profile = roaming
            ? Environment.SpecialFolder.ApplicationData
            : Environment.SpecialFolder.LocalApplicationData;

        return Path.Combine(
            Environment.GetFolderPath(profile),
            AppDataFolderName,
            subFolder);
    }
}
