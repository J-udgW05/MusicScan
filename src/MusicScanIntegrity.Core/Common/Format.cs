using System.Globalization;

namespace MusicScanIntegrity.Core.Common;

/// <summary>
/// Single place that formats numbers, sizes and durations: digit groups are
/// separated by a non-breaking space, sizes carry one decimal ("24,1 МБ") and
/// scan time reads as "18:42".
/// </summary>
public static class Format
{
    /// <summary>Digit group separator used across the UI and the reports.</summary>
    public const char NarrowSpace = ' ';

    private static readonly string[] SizeUnits = ["Б", "КБ", "МБ", "ГБ", "ТБ"];

    private static readonly NumberFormatInfo GroupedNumbers = new()
    {
        NumberGroupSeparator = NarrowSpace.ToString(),
        NumberDecimalSeparator = ",",
        NumberGroupSizes = [3],
    };

    /// <summary>Seconds in words: 30 → "30 секунд".</summary>
    public static string Seconds(int value) =>
        $"{value} {Plural(value, "секунда", "секунды", "секунд")}";

    /// <summary>Integer with digit groups: 12480 → "12 480".</summary>
    public static string Number(long value) => value.ToString("#,0", GroupedNumbers);

    /// <summary>File size in human form: 25 260 032 → "24,1 МБ".</summary>
    public static string Size(long bytes)
    {
        if (bytes < 0)
        {
            return "—";
        }

        if (bytes < 1024)
        {
            return $"{bytes}{NarrowSpace}Б";
        }

        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < SizeUnits.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        // Bytes and kilobytes stay whole ("42 КБ"); megabytes and above carry one
        // decimal while below a hundred ("24,1 МБ", "1,8 ГБ") and go back to whole
        // above it ("284 МБ"). This matches the design mock-up.
        string number = unit >= 2 && value < 100
            ? value.ToString("0.0", GroupedNumbers)
            : value.ToString("#,0", GroupedNumbers);

        return $"{number}{NarrowSpace}{SizeUnits[unit]}";
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
            return "меньше секунды";
        }

        List<string> parts = [];
        int hours = (int)value.TotalHours;
        if (hours > 0)
        {
            parts.Add($"{hours} {Plural(hours, "час", "часа", "часов")}");
        }

        if (value.Minutes > 0)
        {
            parts.Add($"{value.Minutes} {Plural(value.Minutes, "минута", "минуты", "минут")}");
        }

        if (value.Seconds > 0 && hours == 0)
        {
            parts.Add($"{value.Seconds} {Plural(value.Seconds, "секунда", "секунды", "секунд")}");
        }

        return string.Join(' ', parts);
    }

    /// <summary>Russian plural agreement: 1 файл / 2 файла / 5 файлов.</summary>
    public static string Plural(long count, string one, string few, string many)
    {
        long abs = Math.Abs(count) % 100;
        if (abs is >= 11 and <= 14)
        {
            return many;
        }

        return (abs % 10) switch
        {
            1 => one,
            2 or 3 or 4 => few,
            _ => many,
        };
    }

    /// <summary>Count with the agreeing noun: "12 480 файлов".</summary>
    public static string Files(long count) => $"{Number(count)} {Plural(count, "файл", "файла", "файлов")}";

    /// <summary>Fraction as a percentage: "0,6 %".</summary>
    public static string Percent(double fraction) =>
        $"{(fraction * 100).ToString(fraction is > 0 and < 0.001 ? "0.000" : "0.#", GroupedNumbers)}{NarrowSpace}%";
}
