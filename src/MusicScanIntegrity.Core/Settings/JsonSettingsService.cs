using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace MusicScanIntegrity.Core.Settings;

/// <summary>
/// Settings in a JSON file next to the executable, or in %APPDATA% when that
/// folder is not writable.
/// </summary>
/// <remarks>
/// Portability wins: the application folder is tried first so the build can be
/// carried on a stick, and only a read-only location (Program Files, read-only
/// media) pushes the file into the profile.
/// </remarks>
public sealed class JsonSettingsService : ISettingsService
{
    private const string FileName = "settings.json";
    private const string PortableFolderName = "config";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        // Cyrillic paths must stay readable instead of turning into \uXXXX.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly SemaphoreSlim _fileLock = new(1, 1);

    /// <param name="settingsFilePath">Explicit file path; used by tests.</param>
    public JsonSettingsService(string? settingsFilePath = null)
    {
        SettingsFilePath = settingsFilePath ?? ResolveDefaultPath();
    }

    /// <inheritdoc />
    public AppSettings Current { get; private set; } = new();

    /// <inheritdoc />
    public string SettingsFilePath { get; }

    /// <inheritdoc />
    public event EventHandler<AppSettings>? Changed;

    /// <inheritdoc />
    public async Task<bool> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                Current = new AppSettings();
                await WriteAsync(Current, cancellationToken).ConfigureAwait(false);
                return true;
            }

            string json = await File.ReadAllTextAsync(SettingsFilePath, cancellationToken).ConfigureAwait(false);
            AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);

            if (loaded is null)
            {
                throw new InvalidDataException("The settings file is empty or contains null.");
            }

            Current = Sanitize(loaded);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A damaged settings file must not stop the application starting.
            Current = new AppSettings();
            TryBackupCorruptedFile();
            await WriteAsync(Current, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _fileLock.Release();
            Changed?.Invoke(this, Current);
        }
    }

    /// <inheritdoc />
    public async Task ApplyAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Current = Sanitize(settings);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, Current);
    }

    /// <inheritdoc />
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteAsync(Current, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc />
    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        return ApplyAsync(new AppSettings(), cancellationToken);
    }

    /// <summary>
    /// Clamps loaded values into valid ranges: the file is hand-editable and
    /// may contain anything.
    /// </summary>
    internal static AppSettings Sanitize(AppSettings settings)
    {
        settings.LargeFileThresholdMb = Math.Max(0, settings.LargeFileThresholdMb);
        settings.FileTimeoutSeconds = Math.Clamp(settings.FileTimeoutSeconds, 1, 3600);
        settings.ManualParallelism = Math.Clamp(settings.ManualParallelism, 1, 64);
        settings.LockedWaitSeconds = Math.Clamp(settings.LockedWaitSeconds, 1, 3600);
        settings.LockedRetryCount = Math.Clamp(settings.LockedRetryCount, 1, 100);

        settings.DisabledExtensions = [.. settings.DisabledExtensions
            .Select(NormalizeExtension)
            .Where(e => e.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        settings.CustomExtensions = [.. settings.CustomExtensions
            .Select(NormalizeExtension)
            .Where(e => e.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        settings.StatusColors ??= new StatusColorOverrides();

        // An unknown language code counts as never chosen, so first-run
        // resolution picks one again.
        settings.Language = AppLanguage.IsSupported(settings.Language)
            ? settings.Language!.ToLowerInvariant()
            : null;

        return settings;
    }

    /// <summary>Normalises an extension to the ".flac" form.</summary>
    internal static string NormalizeExtension(string value)
    {
        string trimmed = value.Trim().Trim('*').ToLowerInvariant();
        return trimmed.StartsWith('.') ? trimmed : "." + trimmed;
    }

    private async Task WriteAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            string? folder = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            // Written through a temporary file: an interrupted write must not
            // leave a half-written, unreadable config behind.
            string tempPath = SettingsFilePath + ".tmp";
            string json = JsonSerializer.Serialize(settings, SerializerOptions);
            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, SettingsFilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Settings save themselves after every toggle, and failing
            // silently is deliberate: a dialog on each click would be
            // unusable. Reaching here is unlikely anyway — ResolveDefaultPath
            // has already moved the file to %APPDATA% if the application
            // folder is read-only, and the settings window shows the path.
            _ = ex;
        }
    }

    /// <summary>Moves an unreadable settings file aside.</summary>
    /// <remarks>
    /// Best effort only: settings have already fallen back to defaults and the
    /// application will run regardless. The copy is kept in case the user wants
    /// to see what went wrong.
    /// </remarks>
    private void TryBackupCorruptedFile()
    {
        try
        {
            string backup = SettingsFilePath + ".corrupted";
            File.Copy(SettingsFilePath, backup, overwrite: true);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// The application folder when writable, otherwise %APPDATA%\MusicScanIntegrity.
    /// </summary>
    internal static string ResolveDefaultPath() =>
        Path.Combine(Common.WritableFolder.Resolve(PortableFolderName, roaming: true), FileName);
}
