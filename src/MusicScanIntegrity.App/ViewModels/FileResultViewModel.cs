using System.IO;
using MusicScanIntegrity.Core.Models;
using CoreFormat = MusicScanIntegrity.Core.Common.Format;

namespace MusicScanIntegrity.App.ViewModels;

/// <summary>
/// Строка таблицы результатов.
/// </summary>
/// <remarks>
/// Тип намеренно неизменяемый и без <c>INotifyPropertyChanged</c>: строк бывают
/// сотни тысяч, и каждая подписка на уведомления — это лишняя память и лишние
/// обработчики. Результат проверки после получения не меняется, уведомлять не о чем.
/// </remarks>
public sealed class FileResultViewModel(FileCheckResult result)
{
    /// <summary>Исходный результат проверки.</summary>
    public FileCheckResult Result { get; } = result;

    /// <summary>Имя файла.</summary>
    public string FileName => Result.FileName;

    /// <summary>Папка, в которой лежит файл.</summary>
    public string DirectoryPath => Result.DirectoryPath;

    /// <summary>Полный путь.</summary>
    public string FullPath => Result.FullPath;

    /// <summary>Формат («FLAC», либо «FLAC?» при несовпадении с расширением).</summary>
    public string Format => Result.Format;

    /// <summary>Размер в человеческом виде.</summary>
    public string Size => CoreFormat.Size(Result.SizeBytes);

    /// <summary>Размер в байтах — по нему сортируется колонка «Размер».</summary>
    public long SizeBytes => Result.SizeBytes;

    /// <summary>Статус.</summary>
    public CheckStatus Status => Result.Status;

    /// <summary>Подпись статуса — уточняющая, если замечание одно.</summary>
    public string StatusLabel => Result.StatusLabel;

    /// <summary>Знак статуса для квадрата 20×20 в строке таблицы.</summary>
    public string StatusIcon => Result.Status.MarkKey();

    /// <summary>Человеческое описание для панели подробностей.</summary>
    public string Description => Result.Description;

    /// <summary>Техническая причина — второй строкой в подробностях.</summary>
    public string? TechnicalDetail => Result.TechnicalDetail;

    /// <summary>Заголовок панели подробностей: «имя · статус».</summary>
    public string DetailsTitle => $"{FileName} · {StatusLabel}";

    /// <summary>Расширение в верхнем регистре — для фильтра по формату.</summary>
    public string Extension => Path.GetExtension(Result.FullPath).TrimStart('.').ToUpperInvariant();
}
