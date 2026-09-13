namespace MusicScanIntegrity.Core.Settings;

/// <summary>
/// Что о зрительных эффектах говорит сама Windows.
/// </summary>
/// <param name="TransparencyEnabled">
/// В «Персонализации → Цвета» включены эффекты прозрачности.
/// </param>
/// <param name="AnimationsEnabled">
/// В «Специальных возможностях → Визуальные эффекты» включены эффекты анимации.
/// </param>
/// <param name="MicaSupported">
/// Система вообще умеет рисовать подложку Mica: она появилась в Windows 11.
/// </param>
public readonly record struct SystemEffects(
    bool TransparencyEnabled,
    bool AnimationsEnabled,
    bool MicaSupported)
{
    /// <summary>Ничего не поддерживается — значение по умолчанию для тестов и запасной путь.</summary>
    public static readonly SystemEffects None = new(false, false, false);
}

/// <summary>
/// Решает, включены ли подложка и анимации.
/// </summary>
/// <remarks>
/// <para>
/// Вынесено отдельной чистой функцией не ради красоты: правило «при первом
/// запуске берём из системы, дальше — то, что выбрал человек» легко описать
/// словами и легко сломать в коде. Здесь его видно целиком и можно проверить
/// на всех сочетаниях, не поднимая окна.
/// </para>
/// <para>
/// Хранимое и действующее значения различаются намеренно. В файл пишется
/// <em>намерение</em>: хочет человек подложку или нет. Рисуется она только
/// если система умеет — на Windows 10 не умеет никто. Если хранить сразу
/// действующее, то настройка, выключенная на десятке за невозможностью,
/// осталась бы выключенной и после перехода на одиннадцатую, где всё есть.
/// </para>
/// </remarks>
public static class VisualEffects
{
    /// <summary>Хочет ли человек подложку Mica.</summary>
    /// <param name="stored">Что записано в настройках; <see langword="null" /> — выбора не было.</param>
    /// <param name="system">Что говорит система.</param>
    /// <returns>Намерение, которое и записывается в файл настроек.</returns>
    public static bool ResolveMicaPreference(bool? stored, SystemEffects system) =>
        stored ?? system.TransparencyEnabled;

    /// <summary>Рисовать ли подложку на самом деле.</summary>
    /// <param name="preference">Намерение из настроек.</param>
    /// <param name="system">Что говорит система.</param>
    /// <returns><see langword="true" />, если подложку и хотят, и могут показать.</returns>
    public static bool IsMicaEffective(bool preference, SystemEffects system) =>
        preference && system.MicaSupported;

    /// <summary>Нужны ли анимации.</summary>
    /// <param name="stored">Что записано в настройках; <see langword="null" /> — выбора не было.</param>
    /// <param name="system">Что говорит система.</param>
    /// <returns>Значение, которое и записывается в файл настроек.</returns>
    /// <remarks>
    /// Поддержки здесь спрашивать не у кого: анимации рисует сама программа,
    /// и получиться они могут где угодно.
    /// </remarks>
    public static bool ResolveAnimations(bool? stored, SystemEffects system) =>
        stored ?? system.AnimationsEnabled;
}
