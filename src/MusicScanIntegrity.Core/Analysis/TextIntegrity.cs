using System.Globalization;
using MusicScanIntegrity.Core.Resources;

namespace MusicScanIntegrity.Core.Analysis;

/// <summary>
/// Detects mojibake in tags: text decoded with the wrong code page.
/// </summary>
/// <remarks>
/// <para>
/// The classic failure of older collections: Cyrillic written as CP1251 and
/// read as Western European turns "Привет" into "Ïðèâåò"; UTF-8 read byte by
/// byte turns it into "ÐŸÑ€Ð¸Ð²ÐµÑ‚". The file is intact, but the labels in a
/// player are unreadable.
/// </para>
/// <para>
/// A heuristic, so the rules are tuned to stay quiet on genuine titles:
/// "Café", "Motörhead", "Björk" and "Ça va" are not mojibake — a lone accented
/// letter in ordinary text is fine.
/// </para>
/// </remarks>
public static class TextIntegrity
{
    /// <summary>The replacement character used for undecodable bytes.</summary>
    private const char Replacement = '�';

    /// <summary>Share of accented Latin letters above which the text counts as broken.</summary>
    private const double AccentShare = 0.6;

    /// <summary>Shorter text is not judged; two letters are too easy to get wrong.</summary>
    private const int MinLength = 4;

    /// <summary>Whether the text looks decoded with the wrong code page.</summary>
    /// <param name="value">Tag string.</param>
    /// <returns><see langword="true" /> when it looks like mojibake.</returns>
    public static bool LooksBroken(string? value) => Describe(value) is not null;

    /// <summary>
    /// Explains what exactly is wrong with the text.
    /// </summary>
    /// <param name="value">Tag string.</param>
    /// <returns>A description, or <see langword="null" /> when the text is fine.</returns>
    public static string? Describe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < MinLength)
        {
            return null;
        }

        if (value.Contains(Replacement, StringComparison.Ordinal))
        {
            return Strings.Mojibake_ReplacementChars;
        }

        // UTF-8 read byte by byte: Cyrillic becomes "Ð" and "Ñ" followed by
        // characters from the control range of the table.
        if (HasUtf8Wreckage(value))
        {
            return Strings.Mojibake_Utf8Bytes;
        }

        if (HasAccentedRun(value))
        {
            return Strings.Mojibake_WesternTable;
        }

        return null;
    }

    private static bool HasUtf8Wreckage(string value)
    {
        bool marker = false;
        bool tail = false;

        foreach (char symbol in value)
        {
            if (symbol is 'Ð' or 'Ñ' or 'Ã' or 'Â')
            {
                marker = true;
            }
            else if (symbol is >= '\u0080' and <= '\u00BF')
            {
                // The control range never occurs in titles, but follows
                // letters like Ð and Ñ when UTF-8 is read byte by byte.
                tail = true;
            }
        }

        return marker && tail;
    }

    private static bool HasAccentedRun(string value)
    {
        int letters = 0;
        int accented = 0;

        foreach (char symbol in value)
        {
            if (!char.IsLetter(symbol))
            {
                continue;
            }

            letters++;

            // Accented Latin is exactly what Cyrillic turns into when read
            // through the wrong table.
            if (symbol is >= 'À' and <= 'ÿ' && symbol != '×' && symbol != '÷')
            {
                accented++;
            }
        }

        return letters >= MinLength && (double)accented / letters >= AccentShare;
    }

    /// <summary>Lists the fields whose text looks broken.</summary>
    /// <param name="fields">Field name and value pairs.</param>
    /// <returns>Names of the fields containing mojibake.</returns>
    public static IReadOnlyList<string> BrokenFields(params (string Name, string? Value)[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        List<string> broken = [];

        foreach ((string name, string? value) in fields)
        {
            if (LooksBroken(value))
            {
                broken.Add(name.ToLower(CultureInfo.CurrentCulture));
            }
        }

        return broken;
    }
}
