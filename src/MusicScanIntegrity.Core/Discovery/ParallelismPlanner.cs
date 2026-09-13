using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Discovery;

/// <summary>
/// Решает, сколько файлов проверять одновременно.
/// </summary>
/// <remarks>
/// Вынесено из движка отдельной чистой функцией: решение зависит от настроек и
/// типа носителя, и проверять его надо на всех сочетаниях, а не только на том
/// диске, который случайно оказался в машине.
/// </remarks>
public static class ParallelismPlanner
{
    /// <summary>Выбирает число потоков.</summary>
    /// <param name="settings">Текущие настройки.</param>
    /// <param name="storage">Тип носителя, на котором лежит коллекция.</param>
    /// <returns>Сколько файлов проверять одновременно.</returns>
    public static int Resolve(AppSettings settings, StorageType storage)
    {
        ArgumentNullException.ThrowIfNull(settings);

        int requested = settings.EffectiveParallelism;

        if (!settings.RespectDriveType || requested <= AppSettings.HardDiskParallelism)
        {
            return requested;
        }

        // Ограничиваем только там, где перемотка действительно стоит времени.
        // «Не знаю» — это не повод менять поведение: гадать вредно.
        return storage == StorageType.HardDisk ? AppSettings.HardDiskParallelism : requested;
    }
}
