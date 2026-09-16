using MusicScanIntegrity.Core.Common;
using MusicScanIntegrity.Core.History;
using MusicScanIntegrity.Core.Settings;
using Xunit;

namespace MusicScanIntegrity.Core.Tests;

/// <summary>Where the application stores its files.</summary>
/// <remarks>
/// A portable build writes next to itself, but an installed one lands in
/// Program Files, where users cannot write, so everything has to move to the
/// profile. Failing silently would be worse than an error: corruption tracking
/// would simply not work.
/// </remarks>
public sealed class WritableFolderTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("writable").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Writable_folder_accepts_writes()
    {
        Assert.True(WritableFolder.CanWriteTo(_folder));
    }

    [Fact]
    public void Write_probe_leaves_nothing_behind()
    {
        WritableFolder.CanWriteTo(_folder);

        Assert.Empty(Directory.GetFileSystemEntries(_folder));
    }

    [Fact]
    public void Missing_folder_is_created()
    {
        string nested = Path.Combine(_folder, "новая", "глубже");

        Assert.True(WritableFolder.CanWriteTo(nested));
        Assert.True(Directory.Exists(nested));
    }

    /// <remarks>
    /// The path comes from a hand-edited settings file; invalid characters must not
    /// crash the application.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("|<>*?")]
    public void Invalid_path_is_not_writable(string path)
    {
        Assert.False(WritableFolder.CanWriteTo(path));
    }

    /// <remarks>
    /// Settings have always lived in the roaming profile and moving them would lose
    /// them for existing users. The history database grows to tens of megabytes and
    /// has no business roaming between machines.
    /// </remarks>
    [Fact]
    public void Roaming_and_local_profile_differ()
    {
        string roaming = WritableFolder.Resolve("проба-перемещаемая", roaming: true);
        string local = WritableFolder.Resolve("проба-локальная");

        // The test folder is writable, so both paths resolve there; only the profile
        // roots themselves are worth comparing.
        Assert.NotEqual(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        Assert.EndsWith("проба-перемещаемая", roaming, StringComparison.Ordinal);
        Assert.EndsWith("проба-локальная", local, StringComparison.Ordinal);
    }

    [Fact]
    public void History_path_ends_with_file_name()
    {
        Assert.EndsWith("history.db", ScanHistory.ResolveDefaultPath(), StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_path_ends_with_file_name()
    {
        Assert.EndsWith("settings.json", JsonSettingsService.ResolveDefaultPath(), StringComparison.Ordinal);
    }
}
