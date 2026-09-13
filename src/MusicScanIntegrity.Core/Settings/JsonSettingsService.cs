using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MusicScanIntegrity.Core.Settings;

/// <summary>
/// Настройки в JSON-файле рядом с программой (портативная сборка) либо,
/// если папка программы недоступна на запись, — в %APPDATA%.
/// </summary>
/// <remarks>
/// Решение по неоднозначности: 01_SPECIFICATION.md говорит «в отдельном файле
/// конфигурации у пользователя на компьютере», а референс — «рядом с программой,
/// портативная сборка ничего не пишет в реестр». Приоритет отдан переносимости:
/// сначала пробуем папку программы, и только если туда писать нельзя
/// (Program Files, флешка только для чтения) — уходим в %APPDATA%.
/// </remarks>
public sealed class JsonSettingsService : ISettingsService
{
    private const string FileName = "settings.json";
    private const string PortableFolderName = "config";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        // Кириллица в путях должна остаться читаемой, а не превратиться в \uXXXX.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly SemaphoreSlim _fileLock = new(1, 1);

    /// <summary>Создаёт службу настроек.</summary>
    /// <param name="settingsFilePath">Явный путь к файлу (используется в тестах).</param>
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
                throw new InvalidDataException("Файл настроек пуст или содержит null.");
            }

            Current = Sanitize(loaded);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Повреждённый файл настроек не должен мешать запуску программы.
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
    /// Приводит загруженные значения в допустимые пределы: файл настроек
    /// правится вручную, и там может оказаться что угодно.
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
        return settings;
    }

    /// <summary>Приводит расширение к виду «.flac».</summary>
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

            // Пишем через временный файл: обрыв записи не должен оставить
            // пользователя с наполовину записанным (то есть нечитаемым) конфигом.
            string tempPath = SettingsFilePath + ".tmp";
            string json = JsonSerializer.Serialize(settings, SerializerOptions);
            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, SettingsFilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Настройки сохраняются сами, после каждого переключателя, и делать
            // это молча — осознанный выбор: окно с ошибкой на каждый щелчок
            // мешало бы работать. Место записи при этом выбрано так, чтобы
            // сюда попадать было почти не за что: если папка программы закрыта
            // на запись, ResolveDefaultPath заранее уводит файл в %APPDATA%.
            // Путь к нему показан в самих настройках — там и видно, куда смотреть.
            _ = ex;
        }
    }

    /// <summary>Откладывает нечитаемый файл настроек в сторону.</summary>
    /// <remarks>
    /// Это подстраховка, а не обязательный шаг: настройки уже сброшены на
    /// значения по умолчанию, и работать программа будет в любом случае.
    /// Копия нужна на случай, если пользователь захочет посмотреть, что там
    /// испортилось. Не вышло — не беда.
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
    /// Папка программы, если в неё можно писать; иначе %APPDATA%\MusicScanIntegrity.
    /// </summary>
    internal static string ResolveDefaultPath() =>
        Path.Combine(Common.WritableFolder.Resolve(PortableFolderName, roaming: true), FileName);
}
