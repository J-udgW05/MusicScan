namespace MusicScanIntegrity.Core.Scanning;

/// <summary>
/// Пауза, которая не обрывает уже начатые проверки.
/// </summary>
/// <remarks>
/// 02_ARCHITECTURE.md, раздел 3: пауза не прерывает идущие в моменте проверки —
/// они докручиваются до результата, — но новые файлы из очереди не берутся,
/// пока не нажато «продолжить».
/// </remarks>
public sealed class PauseTokenSource : IDisposable
{
    // Установленное событие означает «идём дальше»; сброшенное — «стоим на паузе».
    private readonly ManualResetEventSlim _gate = new(initialState: true);

    /// <summary>Проверка сейчас на паузе.</summary>
    public bool IsPaused => !_gate.IsSet;

    /// <summary>Ставит на паузу.</summary>
    public void Pause() => _gate.Reset();

    /// <summary>Снимает с паузы.</summary>
    public void Resume() => _gate.Set();

    /// <summary>
    /// Ждёт снятия паузы. Вызывается перед тем, как взять в работу новый файл.
    /// </summary>
    public async Task WaitWhilePausedAsync(CancellationToken cancellationToken)
    {
        if (_gate.IsSet)
        {
            return;
        }

        // Ждём в пуле потоков, чтобы не занимать поток проверки блокирующим ожиданием.
        await Task.Run(() => _gate.Wait(cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();
}
