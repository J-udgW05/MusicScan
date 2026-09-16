using System.Globalization;
using System.Runtime.CompilerServices;

namespace MusicScanIntegrity.Core.Tests;

/// <summary>Pins the test assembly to Russian, the neutral resource language.</summary>
/// <remarks>
/// Expected strings in most tests are Russian, while build agents run with an
/// English UI culture. Tests of other languages set the culture themselves.
/// </remarks>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        CultureInfo russian = CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.DefaultThreadCurrentUICulture = russian;
        CultureInfo.CurrentUICulture = russian;
    }
}
