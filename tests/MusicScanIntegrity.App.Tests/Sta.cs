using System.Runtime.ExceptionServices;

namespace MusicScanIntegrity.App.Tests;

/// <summary>
/// Выполняет проверку в потоке с однопоточной моделью.
/// </summary>
/// <remarks>
/// Элементы WPF живут только в таком потоке, а xunit запускает тесты в
/// многопоточном. Отдельного пакета ради одного атрибута сюда не тащим:
/// весь нужный механизм — три строки.
/// </remarks>
internal static class Sta
{
    /// <summary>Запускает действие в STA-потоке и ждёт его.</summary>
    /// <param name="action">Что выполнить.</param>
    public static void Run(Action action)
    {
        ExceptionDispatchInfo? failure = null;

        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                // Исключение из чужого потока иначе потерялось бы, и тест
                // прошёл бы при сломанном коде.
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        failure?.Throw();
    }
}
