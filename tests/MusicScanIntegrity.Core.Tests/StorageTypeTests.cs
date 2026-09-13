using MusicScanIntegrity.Core.Discovery;
using MusicScanIntegrity.Core.Settings;
using Xunit;
using Xunit.Abstractions;

namespace MusicScanIntegrity.Core.Tests;

public sealed class StorageTypeTests(ITestOutputHelper output)
{
    [Fact]
    public void Про_системный_диск_отвечает_без_ошибок()
    {
        // Требовать определённый ответ нельзя: на живой машине драйвер его
        // даёт, а на виртуальной сборочной — нет, и это законный «не знаю».
        // Важно, что вызов отвечает, а не падает.
        StorageType type = StorageTypeDetector.Detect(Environment.SystemDirectory);

        output.WriteLine($"системный диск: {type}");
        Assert.True(Enum.IsDefined(type));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"\\сервер\общая\музыка")]
    public void Про_несуществующий_или_сетевой_путь_программа_не_гадает(string? path)
    {
        Assert.Equal(StorageType.Unknown, StorageTypeDetector.Detect(path));
    }

    [Fact]
    public void Мусор_вместо_пути_не_роняет_проверку()
    {
        // Строка со странными знаками разбирается как относительный путь — от
        // текущего диска, и ответ будет о нём. Важно другое: наружу не летят
        // исключения, потому что от этой подсказки зависит только число потоков.
        StorageType type = StorageTypeDetector.Detect("не путь вовсе |<>");

        output.WriteLine($"ответ на мусор: {type}");
        Assert.True(Enum.IsDefined(type));
    }
}

public sealed class ParallelismPlannerTests
{
    private static AppSettings Settings(int manual = 8, bool respect = true) => new()
    {
        AutoParallelism = false,
        ManualParallelism = manual,
        RespectDriveType = respect,
    };

    [Fact]
    public void На_жёстком_диске_потоков_остаётся_два()
    {
        Assert.Equal(2, ParallelismPlanner.Resolve(Settings(), StorageType.HardDisk));
    }

    [Fact]
    public void На_твердотельном_ограничения_нет()
    {
        Assert.Equal(8, ParallelismPlanner.Resolve(Settings(), StorageType.SolidState));
    }

    [Fact]
    public void Неизвестный_носитель_поведение_не_меняет()
    {
        // «Не знаю» — не повод замедлять проверку: гадать вредно.
        Assert.Equal(8, ParallelismPlanner.Resolve(Settings(), StorageType.Unknown));
    }

    [Fact]
    public void Выключенная_настройка_отменяет_ограничение()
    {
        Assert.Equal(8, ParallelismPlanner.Resolve(Settings(respect: false), StorageType.HardDisk));
    }

    [Fact]
    public void Один_поток_меньше_не_становится()
    {
        Assert.Equal(1, ParallelismPlanner.Resolve(Settings(manual: 1), StorageType.HardDisk));
    }
}
