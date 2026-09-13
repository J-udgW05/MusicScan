namespace MusicScanIntegrity.Core.Models;

/// <summary>
/// Счётчики хода проверки — то, что показывает боковая колонка вкладки «Проверка»
/// и статусная строка.
/// </summary>
/// <param name="Total">Всего файлов к проверке.</param>
/// <param name="Checked">Проверено (получен любой итоговый статус).</param>
/// <param name="Ok">В порядке.</param>
/// <param name="Corrupted">Повреждено.</param>
/// <param name="Warnings">С предупреждениями.</param>
/// <param name="Skipped">Пропущено.</param>
public readonly record struct ScanCounters(
    int Total,
    int Checked,
    int Ok,
    int Corrupted,
    int Warnings,
    int Skipped)
{
    /// <summary>Доля выполненного от 0 до 1.</summary>
    public double Progress => Total <= 0 ? 0 : Math.Clamp((double)Checked / Total, 0, 1);

    /// <summary>Прибавляет к счётчикам ещё один результат.</summary>
    public ScanCounters Add(CheckStatus status) => status switch
    {
        CheckStatus.Ok => this with { Checked = Checked + 1, Ok = Ok + 1 },
        CheckStatus.Warning => this with { Checked = Checked + 1, Warnings = Warnings + 1 },
        CheckStatus.Corrupted => this with { Checked = Checked + 1, Corrupted = Corrupted + 1 },
        CheckStatus.Skipped => this with { Checked = Checked + 1, Skipped = Skipped + 1 },
        _ => this,
    };
}
