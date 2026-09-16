using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace MusicScanIntegrity.App.Tests;

/// <summary>
/// Every key referenced from XAML exists in the string catalogue. A typo in a
/// key compiles fine and only shows up at runtime as "#Key".
/// </summary>
public sealed partial class LocalizationKeyTests
{
    [Fact]
    public void Every_xaml_key_exists_in_catalogue()
    {
        string root = RepositoryRoot();
        HashSet<string> keys = [.. XDocument
            .Load(Path.Combine(root, "src", "MusicScanIntegrity.Core", "Resources", "Strings.resx"))
            .Root!.Elements("data")
            .Select(e => (string)e.Attribute("name")!)];

        List<string> missing = [];
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "src", "MusicScanIntegrity.App"), "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in KeyReference().Matches(File.ReadAllText(file)))
            {
                string key = match.Groups["key"].Value;
                if (!keys.Contains(key))
                {
                    missing.Add(Path.GetFileName(file) + ": " + key);
                }
            }
        }

        Assert.Empty(missing);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MusicScanIntegrity.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex(@"\{svc:T\s+(?<key>[A-Za-z][A-Za-z0-9_]*)\s*\}")]
    private static partial Regex KeyReference();
}
