namespace MusicScanIntegrity.Core.Settings;

/// <summary>Тема оформления.</summary>
public enum AppTheme
{
    /// <summary>Следовать системной теме Windows.</summary>
    System,

    /// <summary>Всегда светлая.</summary>
    Light,

    /// <summary>Всегда тёмная.</summary>
    Dark,
}

/// <summary>Что делать, когда файл занят другой программой.</summary>
public enum LockedFileAction
{
    /// <summary>Спрашивать пользователя (по умолчанию). Вопросы задаются по одному.</summary>
    Ask,

    /// <summary>Всегда пропускать.</summary>
    Skip,

    /// <summary>Подождать освобождения и повторить.</summary>
    Wait,

    /// <summary>Сделать временную копию и проверить её.</summary>
    TempCopy,

    /// <summary>Показать программу-владельца и предложить её закрыть.</summary>
    CloseOwner,
}

/// <summary>Формат экспортируемого отчёта.</summary>
public enum ReportFormat
{
    /// <summary>Оформленная HTML-страница с той же цветовой маркировкой.</summary>
    Html,

    /// <summary>Таблица CSV для Excel и дальнейшей обработки.</summary>
    Csv,

    /// <summary>Простой текстовый список для чтения глазами.</summary>
    Text,
}

/// <summary>Насколько глубоко читать каждый файл декодером.</summary>
public enum CheckDepth
{
    /// <summary>Только начало — две секунды. Быстро, но середину не слышит.</summary>
    Quick,

    /// <summary>Начало, конец и несколько мест в середине.</summary>
    Sampled,

    /// <summary>Файл целиком. Самый точный ответ и самая долгая проверка.</summary>
    Full,
}

/// <summary>Плотность строк в таблице результатов.</summary>
public enum ListDensity
{
    /// <summary>Обычная.</summary>
    Normal,

    /// <summary>Компактная.</summary>
    Compact,
}
