namespace MusicScanIntegrity.Core.Settings;

/// <summary>
/// Хранилище настроек пользователя: загрузка при старте, сохранение между запусками
/// (02_ARCHITECTURE.md, раздел 5).
/// </summary>
public interface ISettingsService
{
    /// <summary>Текущие настройки. Всегда не <see langword="null"/>.</summary>
    AppSettings Current { get; }

    /// <summary>Путь к файлу настроек — показывается в окне «О программе».</summary>
    string SettingsFilePath { get; }

    /// <summary>Настройки изменились: применены новые значения или выполнен сброс.</summary>
    event EventHandler<AppSettings>? Changed;

    /// <summary>
    /// Загружает настройки с диска. Если файла нет или он повреждён —
    /// берутся значения по умолчанию и создаётся новый файл.
    /// </summary>
    /// <returns><see langword="true"/>, если это первый запуск (файла настроек не было).</returns>
    Task<bool> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Заменяет текущие настройки и сохраняет их на диск.</summary>
    Task ApplyAsync(AppSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Сохраняет текущие настройки на диск.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>Сбрасывает все настройки к значениям по умолчанию и сохраняет их.</summary>
    Task ResetAsync(CancellationToken cancellationToken = default);
}
