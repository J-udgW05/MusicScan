using MusicScanIntegrity.Core.Models;

namespace MusicScanIntegrity.Core.Audio;

/// <summary>
/// Попытка прочитать начало файла как аудио.
/// Абстракция нужна, чтобы движок проверки можно было целиком покрыть тестами
/// без native-библиотеки BASS.
/// </summary>
public interface IAudioProbe
{
    /// <summary>Готов ли декодер к работе.</summary>
    bool IsAvailable { get; }

    /// <summary>Инициализирует декодер. Вызывается один раз при запуске программы.</summary>
    /// <returns>Описание ошибки, если инициализация не удалась; иначе <see langword="null"/>.</returns>
    string? Initialize();

    /// <summary>
    /// Пробует декодировать файл на заданную глубину.
    /// Метод не должен выбрасывать исключения на «плохих» файлах — только
    /// возвращать результат с описанием проблемы.
    /// </summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <param name="scope">Сколько читать: начало, выборочные места или всё.</param>
    /// <param name="cancellationToken">Отмена (стоп или таймаут по этому файлу).</param>
    AudioProbeResult Probe(string filePath, DecodeScope scope, CancellationToken cancellationToken);
}

/// <summary>Сколько звука читать из файла.</summary>
public enum DecodeScope
{
    /// <summary>Первые две секунды.</summary>
    Quick,

    /// <summary>Несколько окон: начало, конец и середина.</summary>
    Sampled,

    /// <summary>Весь файл до конца.</summary>
    Full,
}

/// <summary>Что получилось при попытке декодировать файл.</summary>
/// <param name="Outcome">Итог попытки.</param>
/// <param name="Message">Человеческая формулировка проблемы.</param>
/// <param name="TechnicalDetail">Техническая причина: код ошибки BASS и т.п.</param>
/// <param name="DecodedSeconds">Сколько секунд аудио удалось прочитать.</param>
/// <param name="DetectedFormat">Формат, который определил декодер («FLAC», «MP3»).</param>
/// <param name="DeclaredSeconds">Длительность, заявленная заголовком файла.</param>
/// <param name="Truncated">Звук закончился раньше, чем обещано заголовком.</param>
/// <param name="Stats">Что видно в самих отсчётах.</param>
/// <param name="Spectrum">До какой частоты в файле есть звук.</param>
/// <param name="BitrateKbps">Битрейт по данным декодера; 0 — неизвестен.</param>
public sealed record AudioProbeResult(
    AudioProbeOutcome Outcome,
    string? Message = null,
    string? TechnicalDetail = null,
    double DecodedSeconds = 0,
    string? DetectedFormat = null,
    double DeclaredSeconds = 0,
    bool Truncated = false,
    AudioStats? Stats = null,
    Analysis.SpectrumProfile? Spectrum = null,
    int BitrateKbps = 0)
{
    /// <summary>Файл успешно прочитан как аудио.</summary>
    public static AudioProbeResult Success(double seconds, string? format, double declared = 0) =>
        new(AudioProbeOutcome.Ok, DecodedSeconds: seconds, DetectedFormat: format, DeclaredSeconds: declared);

    /// <summary>Замечание, соответствующее этому итогу; <see langword="null"/> для успеха.</summary>
    public CheckIssue? ToIssue() => Outcome switch
    {
        AudioProbeOutcome.Ok => null,

        AudioProbeOutcome.OpenFailed => new CheckIssue(
            IssueCode.DecodeStartFailed,
            Message ?? "Не удалось начать декодирование файла.",
            TechnicalDetail),

        AudioProbeOutcome.ReadFailed => new CheckIssue(
            IssueCode.AudioReadFailed,
            Message ?? "Чтение аудиоданных прервалось ошибкой.",
            TechnicalDetail),

        AudioProbeOutcome.PasswordProtected => new CheckIssue(
            IssueCode.PasswordProtected,
            Message ?? "Файл защищён паролем или лицензией.",
            TechnicalDetail),

        AudioProbeOutcome.Empty => new CheckIssue(
            IssueCode.EmptyFile,
            Message ?? "Файл пустой — аудиоданных в нём нет.",
            TechnicalDetail),

        AudioProbeOutcome.EngineFailure => new CheckIssue(
            IssueCode.UnexpectedError,
            Message ?? "Механизм декодирования аудио отказал.",
            TechnicalDetail),

        _ => new CheckIssue(IssueCode.UnexpectedError, Message ?? "Неизвестная ошибка.", TechnicalDetail),
    };
}

/// <summary>Итог попытки декодирования.</summary>
public enum AudioProbeOutcome
{
    /// <summary>Файл прочитан как аудио.</summary>
    Ok,

    /// <summary>Не удалось вообще открыть поток декодирования.</summary>
    OpenFailed,

    /// <summary>Поток открылся, но чтение данных сорвалось.</summary>
    ReadFailed,

    /// <summary>Файл защищён паролем или DRM.</summary>
    PasswordProtected,

    /// <summary>В файле нет данных.</summary>
    Empty,

    /// <summary>
    /// Критический сбой самого декодера — проверку продолжать нельзя
    /// (03_IMPLEMENTATION_GUIDE.md, раздел 1, последняя строка таблицы).
    /// </summary>
    EngineFailure,
}
