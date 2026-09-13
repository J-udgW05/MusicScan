namespace MusicScanIntegrity.Core.Common;

/// <summary>
/// Куда программе можно писать свои файлы.
/// </summary>
/// <remarks>
/// <para>
/// Программа переносимая: настройки и база истории лежат рядом с ней, папку
/// можно скопировать целиком и унести. Но её же можно и установить — в
/// <c>Program Files</c>, куда обычному пользователю писать не дают. Тогда всё,
/// что программа хранит, уходит в профиль пользователя.
/// </para>
/// <para>
/// Проверка делается записью, а не разбором прав: причин запрета много —
/// <c>Program Files</c>, носитель только для чтения, политика безопасности, —
/// и перечислять их бессмысленно. Важен один ответ: получилось или нет.
/// </para>
/// </remarks>
public static class WritableFolder
{
    /// <summary>Имя вложенной папки в профиле пользователя.</summary>
    private const string AppDataFolderName = "MusicScanIntegrity";

    /// <summary>Можно ли писать в эту папку.</summary>
    /// <param name="folder">Путь к папке; она создаётся, если её нет.</param>
    /// <returns><see langword="true" />, если запись удалась.</returns>
    public static bool CanWriteTo(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);

            string probe = Path.Combine(folder, ".write-probe");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Папка рядом с программой, если туда можно писать; иначе — в профиле.
    /// </summary>
    /// <param name="subFolder">Имя вложенной папки, например «config» или «data».</param>
    /// <param name="roaming">
    /// Класть запасную копию в перемещаемую часть профиля (<c>%APPDATA%</c>),
    /// а не в локальную.
    /// </param>
    /// <returns>Путь к папке, в которую точно можно писать.</returns>
    /// <remarks>
    /// Настройки лежат в перемещаемой части, и это не вкусовщина: они там были
    /// с самого начала, и переезд потерял бы настройки у всех, кто уже
    /// пользуется программой. База истории — в локальной: она вырастает до
    /// десятков мегабайт, и таскать её за профилем между машинами незачем.
    /// </remarks>
    public static string Resolve(string subFolder, bool roaming = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subFolder);

        string beside = Path.Combine(AppContext.BaseDirectory, subFolder);

        if (CanWriteTo(beside))
        {
            return beside;
        }

        Environment.SpecialFolder profile = roaming
            ? Environment.SpecialFolder.ApplicationData
            : Environment.SpecialFolder.LocalApplicationData;

        return Path.Combine(
            Environment.GetFolderPath(profile),
            AppDataFolderName,
            subFolder);
    }
}
