using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Locking;

/// <summary>
/// Выстраивает вопросы о занятых файлах в очередь и задаёт их по одному.
/// </summary>
/// <remarks>
/// Проверка идёт параллельно, поэтому несколько файлов могут одновременно
/// оказаться занятыми. Показывать несколько окон сразу — плохой опыт: непонятно,
/// какое окно к какому файлу относится (03_IMPLEMENTATION_GUIDE.md, раздел 2).
/// Здесь же обрабатывается флажок «поступать так же со всеми»: после него
/// остальные файлы решаются без вопросов.
/// </remarks>
public sealed class LockedFileQuestionQueue(ILockedFileDecisionProvider inner) : IDisposable
{
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private readonly Lock _stateLock = new();

    private LockedFileAction? _appliedToAll;
    private int _waiting;
    private bool _stopRequested;

    /// <summary>Пользователь попросил остановить проверку прямо из вопроса.</summary>
    public bool StopRequested
    {
        get
        {
            lock (_stateLock)
            {
                return _stopRequested;
            }
        }
    }

    /// <summary>Сколько вопросов сейчас ждёт своей очереди.</summary>
    public int Waiting => Volatile.Read(ref _waiting);

    /// <summary>
    /// Спрашивает пользователя (или сразу возвращает ранее выбранное «ко всем»).
    /// </summary>
    public async Task<LockedFileDecision> AskAsync(
        string filePath,
        long sizeBytes,
        LockOwnerResult owner,
        CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            if (_stopRequested)
            {
                return new LockedFileDecision(LockedFileAction.Skip, StopScan: true);
            }

            if (_appliedToAll is { } applied)
            {
                return new LockedFileDecision(applied, ApplyToAll: true);
            }
        }

        Interlocked.Increment(ref _waiting);
        try
        {
            await _oneAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Interlocked.Decrement(ref _waiting);
            throw;
        }

        try
        {
            // Пока файл стоял в очереди, пользователь мог ответить «ко всем»
            // или остановить проверку — проверяем ещё раз.
            lock (_stateLock)
            {
                if (_stopRequested)
                {
                    return new LockedFileDecision(LockedFileAction.Skip, StopScan: true);
                }

                if (_appliedToAll is { } applied)
                {
                    return new LockedFileDecision(applied, ApplyToAll: true);
                }
            }

            // Себя в счётчик «ещё в очереди» не включаем.
            int queuedAfterThis = Math.Max(0, Waiting - 1);
            LockedFileQuestion question = new(filePath, sizeBytes, owner, queuedAfterThis);
            LockedFileDecision decision = await inner.AskAsync(question, cancellationToken).ConfigureAwait(false);

            lock (_stateLock)
            {
                if (decision.StopScan)
                {
                    _stopRequested = true;
                }
                else if (decision.ApplyToAll)
                {
                    _appliedToAll = decision.Action;
                }
            }

            return decision;
        }
        finally
        {
            Interlocked.Decrement(ref _waiting);
            _oneAtATime.Release();
        }
    }

    /// <summary>Сбрасывает состояние перед новой проверкой.</summary>
    public void Reset()
    {
        lock (_stateLock)
        {
            _appliedToAll = null;
            _stopRequested = false;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _oneAtATime.Dispose();
}
