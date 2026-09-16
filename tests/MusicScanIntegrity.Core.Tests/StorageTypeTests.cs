using MusicScanIntegrity.Core.Discovery;
using MusicScanIntegrity.Core.Settings;
using Xunit;
using Xunit.Abstractions;

namespace MusicScanIntegrity.Core.Tests;

public sealed class StorageTypeTests(ITestOutputHelper output)
{
    [Fact]
    public void System_drive_answers_without_error()
    {
        // No specific answer can be required: a real machine's driver gives one, a
        // virtual build agent may not, and "unknown" is legitimate. What matters is
        // that the call returns instead of throwing.
        StorageType type = StorageTypeDetector.Detect(Environment.SystemDirectory);

        output.WriteLine($"системный диск: {type}");
        Assert.True(Enum.IsDefined(type));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"\\сервер\общая\музыка")]
    public void Missing_or_network_path_is_not_guessed(string? path)
    {
        Assert.Equal(StorageType.Unknown, StorageTypeDetector.Detect(path));
    }

    [Fact]
    public void Garbage_path_does_not_throw()
    {
        // A string of odd characters parses as a relative path on the current drive,
        // so the answer is about that drive. What matters is that no exception escapes:
        // only the thread count depends on this hint.
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
    public void Hard_disk_gets_two_threads()
    {
        Assert.Equal(2, ParallelismPlanner.Resolve(Settings(), StorageType.HardDisk));
    }

    [Fact]
    public void Solid_state_drive_is_not_capped()
    {
        Assert.Equal(8, ParallelismPlanner.Resolve(Settings(), StorageType.SolidState));
    }

    [Fact]
    public void Unknown_drive_does_not_change_behaviour()
    {
        // "Unknown" is no reason to slow the scan down; guessing would be worse.
        Assert.Equal(8, ParallelismPlanner.Resolve(Settings(), StorageType.Unknown));
    }

    [Fact]
    public void Disabled_setting_removes_cap()
    {
        Assert.Equal(8, ParallelismPlanner.Resolve(Settings(respect: false), StorageType.HardDisk));
    }

    [Fact]
    public void Single_thread_is_not_reduced()
    {
        Assert.Equal(1, ParallelismPlanner.Resolve(Settings(manual: 1), StorageType.HardDisk));
    }
}
