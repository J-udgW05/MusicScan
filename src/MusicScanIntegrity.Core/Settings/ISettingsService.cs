namespace MusicScanIntegrity.Core.Settings;

/// <summary>
/// User settings storage: loaded at startup, persisted between runs.
/// </summary>
public interface ISettingsService
{
    /// <summary>Current settings; never <see langword="null"/>.</summary>
    AppSettings Current { get; }

    /// <summary>Path to the settings file, shown in the about window.</summary>
    string SettingsFilePath { get; }

    /// <summary>Raised when settings are applied or reset.</summary>
    event EventHandler<AppSettings>? Changed;

    /// <summary>
    /// Loads settings from disk. A missing or damaged file falls back to
    /// defaults and a new file is written.
    /// </summary>
    /// <returns><see langword="true"/> on first run, when no file existed.</returns>
    Task<bool> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the current settings and saves them.</summary>
    Task ApplyAsync(AppSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Saves the current settings to disk.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>Resets everything to defaults and saves.</summary>
    Task ResetAsync(CancellationToken cancellationToken = default);
}
