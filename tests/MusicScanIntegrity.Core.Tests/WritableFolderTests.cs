using MusicScanIntegrity.Core.Common;
using MusicScanIntegrity.Core.History;
using MusicScanIntegrity.Core.Settings;
using Xunit;

namespace MusicScanIntegrity.Core.Tests;

/// <summary>
/// Куда программа кладёт свои файлы.
/// </summary>
/// <remarks>
/// Пока программа была только переносимой, ответ был один — рядом с собой.
/// С установщиком она попадает в «Program Files», где обычному пользователю
/// писать не дают, и всё, что она хранит, должно уходить в профиль. Молчаливый
/// отказ здесь хуже ошибки: слежение за порчей просто не работало бы.
/// </remarks>
public sealed class WritableFolderTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("writable").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void В_доступную_папку_писать_можно()
    {
        Assert.True(WritableFolder.CanWriteTo(_folder));
    }

    /// <summary>Проба не оставляет за собой мусора.</summary>
    [Fact]
    public void Проба_записи_ничего_не_оставляет()
    {
        WritableFolder.CanWriteTo(_folder);

        Assert.Empty(Directory.GetFileSystemEntries(_folder));
    }

    [Fact]
    public void Несуществующая_папка_создаётся()
    {
        string nested = Path.Combine(_folder, "новая", "глубже");

        Assert.True(WritableFolder.CanWriteTo(nested));
        Assert.True(Directory.Exists(nested));
    }

    /// <summary>
    /// Негодный путь — это «нельзя», а не исключение.
    /// </summary>
    /// <remarks>
    /// Путь приходит из файла настроек, который правят руками. Падать на
    /// строке с недопустимыми знаками программа не должна.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("|<>*?")]
    public void Негодный_путь_считается_недоступным(string path)
    {
        Assert.False(WritableFolder.CanWriteTo(path));
    }

    /// <summary>
    /// Настройки и база расходятся по разным частям профиля.
    /// </summary>
    /// <remarks>
    /// Настройки лежали в перемещаемой части с самого начала: переезд потерял
    /// бы их у всех, кто уже пользуется программой. База истории вырастает до
    /// десятков мегабайт, и таскать её за профилем между машинами незачем.
    /// </remarks>
    [Fact]
    public void Перемещаемая_и_локальная_части_профиля_различаются()
    {
        string roaming = WritableFolder.Resolve("проба-перемещаемая", roaming: true);
        string local = WritableFolder.Resolve("проба-локальная");

        // Рядом с тестами писать можно, поэтому оба пути ведут туда же —
        // сравнивать имеет смысл только сами корни профиля.
        Assert.NotEqual(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        Assert.EndsWith("проба-перемещаемая", roaming, StringComparison.Ordinal);
        Assert.EndsWith("проба-локальная", local, StringComparison.Ordinal);
    }

    [Fact]
    public void Путь_к_базе_истории_заканчивается_именем_файла()
    {
        Assert.EndsWith("history.db", ScanHistory.ResolveDefaultPath(), StringComparison.Ordinal);
    }

    [Fact]
    public void Путь_к_настройкам_заканчивается_именем_файла()
    {
        Assert.EndsWith("settings.json", JsonSettingsService.ResolveDefaultPath(), StringComparison.Ordinal);
    }
}
