namespace MusicScanIntegrity.Core.Models;

/// <summary>Русские подписи статусов — единые для интерфейса, отчётов и логов.</summary>
public static class CheckStatusExtensions
{
    /// <summary>Название статуса так, как его видит пользователь.</summary>
    public static string DisplayName(this CheckStatus status) => status switch
    {
        CheckStatus.Ok => "В порядке",
        CheckStatus.Warning => "Предупреждение",
        CheckStatus.Corrupted => "Повреждён",
        CheckStatus.Skipped => "Пропущен",
        _ => "Неизвестно",
    };

    /// <summary>
    /// Текстовый значок статуса. UI_SPEC.md, раздел 1: статус никогда не передаётся
    /// только цветом — рядом всегда значок и слово.
    /// </summary>
    public static string Glyph(this CheckStatus status) => status switch
    {
        CheckStatus.Ok => "✓",
        CheckStatus.Warning => "!",
        CheckStatus.Corrupted => "✕",
        CheckStatus.Skipped => "–",
        _ => "?",
    };

    /// <summary>Ключ иконки из набора (Icons.dc.html) для этого статуса.</summary>
    public static string IconKey(this CheckStatus status) => status switch
    {
        CheckStatus.Ok => "status-ok",
        CheckStatus.Warning => "status-warning",
        CheckStatus.Corrupted => "status-broken",
        CheckStatus.Skipped => "status-skipped",
        _ => "info",
    };

    /// <summary>
    /// Ключ «голого» знака статуса — без кружка вокруг галочки и крестика.
    /// Макет программы использует именно его там, где значок мелкий (13–14 px)
    /// и стоит внутри цветного квадрата или плашки: кружок на таком размере
    /// только замыливает знак.
    /// </summary>
    public static string MarkKey(this CheckStatus status) => status switch
    {
        CheckStatus.Ok => "mark-ok",
        CheckStatus.Warning => "mark-warning",
        CheckStatus.Corrupted => "mark-broken",
        CheckStatus.Skipped => "mark-skipped",
        _ => "info",
    };

    /// <summary>
    /// Более серьёзный из двух статусов. «Пропущен» — терминальное состояние:
    /// файл не проверялся, поэтому он не смешивается с результатами проверки.
    /// </summary>
    public static CheckStatus Combine(this CheckStatus first, CheckStatus second)
    {
        if (first == CheckStatus.Skipped || second == CheckStatus.Skipped)
        {
            return CheckStatus.Skipped;
        }

        return (CheckStatus)Math.Max((int)first, (int)second);
    }
}
