using System.Globalization;

namespace MusicScanIntegrity.Core.Analysis;

/// <summary>
/// Распознаёт кракозябры в тегах — текст, прочитанный не в той кодировке.
/// </summary>
/// <remarks>
/// <para>
/// Беда старых русских коллекций: кириллица, записанная в CP1251, прочитана как
/// западноевропейская таблица — «Привет» превращается в «Ïðèâåò». Или наоборот:
/// UTF-8 прочитан побайтно, и получается «ÐŸÑ€Ð¸Ð²ÐµÑ‚». Файл при этом цел, а
/// подписи в плеере нечитаемы.
/// </para>
/// <para>
/// Это эвристика, поэтому правила подобраны так, чтобы молчать на честных
/// названиях: «Café», «Motörhead», «Björk» и «Ça va» кракозябрами не считаются —
/// одиночная буква с надстрочным знаком в обычном тексте дела не портит.
/// </para>
/// </remarks>
public static class TextIntegrity
{
    /// <summary>Знак, которым система заменяет то, что не смогла прочитать.</summary>
    private const char Replacement = '�';

    /// <summary>Доля «латиницы с надстрочными знаками», после которой текст считается сломанным.</summary>
    private const double AccentShare = 0.6;

    /// <summary>Короче этого текст не разбираем: на двух буквах ошибиться слишком легко.</summary>
    private const int MinLength = 4;

    /// <summary>Похож ли текст на прочитанный не в той кодировке.</summary>
    /// <param name="value">Строка из тега.</param>
    /// <returns><see langword="true" />, если это похоже на кракозябры.</returns>
    public static bool LooksBroken(string? value) => Describe(value) is not null;

    /// <summary>
    /// Объясняет, что именно не так с текстом.
    /// </summary>
    /// <param name="value">Строка из тега.</param>
    /// <returns>Описание проблемы или <see langword="null" />, если текст в порядке.</returns>
    public static string? Describe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < MinLength)
        {
            return null;
        }

        if (value.Contains(Replacement, StringComparison.Ordinal))
        {
            return "в тексте есть знаки, которые система не смогла прочитать";
        }

        // UTF-8, прочитанный побайтно: кириллица превращается в «Ð» и «Ñ»
        // с довеском из служебной части таблицы.
        if (HasUtf8Wreckage(value))
        {
            return "похоже на UTF-8, прочитанный побайтно";
        }

        if (HasAccentedRun(value))
        {
            return "похоже на кириллицу, прочитанную западноевропейской таблицей";
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
                // Служебная часть таблицы: в названиях не встречается, а при побайтном
                // чтении UTF-8 идёт сразу за буквами вроде Ð и Ñ.
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

            // Латиница с надстрочными знаками — та самая таблица, в которую
            // превращается кириллица при неверном чтении.
            if (symbol is >= 'À' and <= 'ÿ' && symbol != '×' && symbol != '÷')
            {
                accented++;
            }
        }

        return letters >= MinLength && (double)accented / letters >= AccentShare;
    }

    /// <summary>Собирает список полей, в которых текст выглядит сломанным.</summary>
    /// <param name="fields">Пары «название поля — значение».</param>
    /// <returns>Названия полей с кракозябрами.</returns>
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
