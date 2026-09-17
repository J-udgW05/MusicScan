using System.Globalization;
using MusicScanIntegrity.Core.Resources;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Common;

/// <summary>
/// Single place that formats numbers, sizes, durations and counted phrases in
/// the current interface language.
/// </summary>
/// <remarks>
/// Russian groups digits with a non-breaking space and uses a decimal comma
/// ("12 480", "24,1 МБ"); English uses "12,480" and "24.1 MB". The conventions
/// follow the interface language rather than the Windows regional format, so a
/// report never mixes Russian words with English numbers or the other way round.
/// </remarks>
public static class Format
{
    /// <summary>Non-breaking space: Russian digit group separator and the gap before units.</summary>
    public const char NarrowSpace = '\u00A0';

    private static readonly NumberFormatInfo RussianNumbers = new()
    {
        NumberGroupSeparator = NarrowSpace.ToString(),
        NumberDecimalSeparator = ",",
        NumberGroupSizes = [3],
    };

    private static readonly NumberFormatInfo EnglishNumbers = new()
    {
        NumberGroupSeparator = ",",
        NumberDecimalSeparator = ".",
        NumberGroupSizes = [3],
    };

    private static bool IsEnglish => AppLanguage.Current == AppLanguage.English;

    private static NumberFormatInfo Numbers => IsEnglish ? EnglishNumbers : RussianNumbers;

    /// <summary>Seconds in words: 30 → "30 секунд" / "30 seconds".</summary>
    public static string Seconds(int value) => Count(value, "Plural_Seconds");

    /// <summary>Integer with digit groups: 12480 → "12 480" / "12,480".</summary>
    public static string Number(long value) => value.ToString("#,0", Numbers);

    /// <summary>File size in human form: 25 260 032 → "24,1 МБ" / "24.1 MB".</summary>
    public static string Size(long bytes)
    {
        if (bytes < 0)
        {
            return "—";
        }

        if (bytes < 1024)
        {
            return $"{bytes}{NarrowSpace}{Strings.Unit_B}";
        }

        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < 4)
        {
            value /= 1024;
            unit++;
        }

        // Kilobytes stay whole ("42 KB"); megabytes and above carry one decimal
        // below a hundred ("24.1 MB") and go back to whole above it ("284 MB").
        string number = unit >= 2 && value < 100
            ? value.ToString("0.0", Numbers)
            : value.ToString("#,0", Numbers);

        return $"{number}{NarrowSpace}{UnitName(unit)}";
    }

    /// <summary>Duration as "18:42", or "1:18:42" past an hour.</summary>
    public static string Duration(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            return "—";
        }

        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{(int)value.TotalMinutes}:{value.Seconds:00}";
    }

    /// <summary>Duration in words for the scan-finished dialog.</summary>
    public static string DurationWords(TimeSpan value)
    {
        if (value.TotalSeconds < 1)
        {
            return Strings.Duration_LessThanSecond;
        }

        List<string> parts = [];
        int hours = (int)value.TotalHours;
        if (hours > 0)
        {
            parts.Add(Count(hours, "Plural_Hours"));
        }

        if (value.Minutes > 0)
        {
            parts.Add(Count(value.Minutes, "Plural_Minutes"));
        }

        if (value.Seconds > 0 && hours == 0)
        {
            parts.Add(Count(value.Seconds, "Plural_Seconds"));
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// A counted phrase from the catalogue, agreeing in number with the count.
    /// </summary>
    /// <param name="count">The number; formatted with digit groups into placeholder {0}.</param>
    /// <param name="key">
    /// Resource key prefix. The catalogue holds <c>key_One</c>, <c>key_Few</c> and
    /// <c>key_Many</c>; English uses the same text for Few and Many.
    /// </param>
    /// <param name="extra">Values for placeholders {1} onwards.</param>
    public static string Count(long count, string key, params object?[] extra)
    {
        string form = key + "_" + PluralForm(count);
        string pattern = Strings.ResourceManager.GetString(form, CultureInfo.CurrentUICulture)
            ?? throw new InvalidOperationException("Missing plural resource: " + form);

        return string.Format(CultureInfo.CurrentUICulture, pattern, [Number(count), .. extra]);
    }

    /// <summary>Fills the placeholders of a catalogue pattern.</summary>
    public static string Text(string pattern, params object?[] args) =>
        string.Format(CultureInfo.CurrentUICulture, pattern, args);

    /// <summary>Count with the agreeing noun: "12 480 файлов" / "12,480 files".</summary>
    public static string Files(long count) => Count(count, "Plural_Files");

    /// <summary>Fraction as a percentage: "0,6 %" / "0.6%".</summary>
    public static string Percent(double fraction)
    {
        string number = (fraction * 100).ToString(fraction is > 0 and < 0.001 ? "0.000" : "0.#", Numbers);
        return IsEnglish ? number + "%" : number + NarrowSpace + "%";
    }

    /// <summary>
    /// Plural category of a count in the current language: One, Few or Many.
    /// </summary>
    /// <remarks>
    /// Russian: 1, 21, 101 take One; 2–4, 22–24 take Few; 0, 5–20 and 25–30 take
    /// Many, with 11–14 always Many. English only distinguishes 1 from the rest.
    /// </remarks>
    public static string PluralForm(long count)
    {
        long abs = Math.Abs(count);

        if (IsEnglish)
        {
            return abs == 1 ? "One" : "Many";
        }

        long lastTwo = abs % 100;
        if (lastTwo is >= 11 and <= 14)
        {
            return "Many";
        }

        return (lastTwo % 10) switch
        {
            1 => "One",
            2 or 3 or 4 => "Few",
            _ => "Many",
        };
    }

    private static string UnitName(int unit) => unit switch
    {
        0 => Strings.Unit_B,
        1 => Strings.Unit_KB,
        2 => Strings.Unit_MB,
        3 => Strings.Unit_GB,
        _ => Strings.Unit_TB,
    };
}
