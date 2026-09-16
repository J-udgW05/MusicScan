using System.Globalization;

namespace MusicScanIntegrity.Core.Settings;

/// <summary>
/// Interface languages and the rule for choosing one on first run.
/// </summary>
/// <remarks>
/// Lookups rely on <see cref="CultureInfo.CurrentUICulture"/> alone, which flows
/// with async calls, so parallel tests can run in different languages without
/// interfering with each other.
/// </remarks>
public static class AppLanguage
{
    /// <summary>Russian; also the neutral resource language.</summary>
    public const string Russian = "ru";

    /// <summary>English.</summary>
    public const string English = "en";

    private static readonly HashSet<string> RussianSpeakingLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "ru", "uk", "be",
    };

    private static readonly HashSet<string> RussianSpeakingRegions = new(StringComparer.OrdinalIgnoreCase)
    {
        "RU", "UA", "BY",
    };

    /// <summary>Raised after <see cref="Apply"/> switches the language.</summary>
    public static event EventHandler? Changed;

    /// <summary>Supported language codes in display order.</summary>
    public static IReadOnlyList<string> Supported { get; } = [Russian, English];

    /// <summary>Whether the code names a supported language.</summary>
    public static bool IsSupported(string? language) =>
        language is not null && Supported.Contains(language, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The language to use: the stored choice if valid, otherwise one derived
    /// from the system.
    /// </summary>
    /// <param name="stored">Value from settings; <see langword="null"/> means never chosen.</param>
    /// <param name="systemLanguage">Two-letter ISO code of the Windows display language.</param>
    /// <param name="systemRegion">Two-letter ISO region code, if known.</param>
    /// <remarks>
    /// Russian, Ukrainian and Belarusian systems — or those set to Russia,
    /// Ukraine or Belarus — start in Russian; everything else starts in English.
    /// </remarks>
    public static string Resolve(string? stored, string? systemLanguage, string? systemRegion)
    {
        if (IsSupported(stored))
        {
            return stored!.ToLowerInvariant();
        }

        if (systemLanguage is not null && RussianSpeakingLanguages.Contains(systemLanguage))
        {
            return Russian;
        }

        if (systemRegion is not null && RussianSpeakingRegions.Contains(systemRegion))
        {
            return Russian;
        }

        return English;
    }

    /// <summary>Culture used for resource lookups in the given language.</summary>
    public static CultureInfo ToCulture(string language) =>
        CultureInfo.GetCultureInfo(string.Equals(language, English, StringComparison.OrdinalIgnoreCase) ? "en-US" : "ru-RU");

    /// <summary>The language currently in effect on this thread.</summary>
    public static string Current =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == English ? English : Russian;

    /// <summary>Switches the interface language for the whole process.</summary>
    public static void Apply(string language)
    {
        CultureInfo culture = ToCulture(language);
        bool changed = !Equals(CultureInfo.CurrentUICulture, culture);

        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;

        if (changed)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
