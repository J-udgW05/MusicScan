namespace MusicScanIntegrity.Core.Locking;

/// <summary>
/// Переходник между движком проверки и тем, кто на самом деле спрашивает пользователя.
/// </summary>
/// <remarks>
/// Движок создаётся раньше главного окна, а вопросы задаёт именно окно — прямая
/// зависимость дала бы цикл в контейнере. Переходник разрывает его: движок получает
/// его при создании, а окно подставляет себя в <see cref="Target"/> при запуске.
/// Пока получателя нет (пакетный режим, тесты), занятые файлы просто пропускаются.
/// </remarks>
public sealed class LockedFileDecisionRelay : ILockedFileDecisionProvider
{
    /// <summary>Кто отвечает на вопросы; <see langword="null"/> — спрашивать некого.</summary>
    public ILockedFileDecisionProvider? Target { get; set; }

    /// <inheritdoc />
    public Task<LockedFileDecision> AskAsync(LockedFileQuestion question, CancellationToken cancellationToken) =>
        Target is { } target
            ? target.AskAsync(question, cancellationToken)
            : Task.FromResult(LockedFileDecision.Skip);
}
