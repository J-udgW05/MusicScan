using System.IO;
using System.Windows;
using Microsoft.Win32;
using MusicScanIntegrity.Core.Settings;
using Wpf.Ui.Controls;

namespace MusicScanIntegrity.App.Services;

/// <summary>
/// Читает, включены ли зрительные эффекты в самой Windows.
/// </summary>
/// <remarks>
/// Нужно ровно один раз — при первом запуске, чтобы программа открылась с тем
/// же оформлением, к которому человек привык в системе. Дальше решает то, что
/// он выбрал в настройках, и сюда мы больше не заглядываем.
/// </remarks>
internal static class SystemEffectsReader
{
    /// <summary>Где Windows хранит переключатель эффектов прозрачности.</summary>
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Снимает состояние системных настроек.</summary>
    /// <returns>Что система думает о прозрачности, анимациях и подложке.</returns>
    public static SystemEffects Read() => new(
        TransparencyEnabled: ReadTransparency(),
        AnimationsEnabled: ReadAnimations(),
        MicaSupported: ReadMicaSupport());

    /// <summary>
    /// «Персонализация → Цвета → Эффекты прозрачности».
    /// </summary>
    /// <remarks>
    /// Отдельного переключателя Mica в Windows нет — есть общий тумблер
    /// прозрачности, и подложка подчиняется ему. Поэтому при первом запуске
    /// смотрим именно на него.
    /// </remarks>
    private static bool ReadTransparency()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);

            // Значения может не быть вовсе — в Windows это означает «включено».
            return key?.GetValue("EnableTransparency") is not int value || value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // К реестру не пустили — считаем, что эффекты включены: так выглядит
            // свежая Windows, и это менее неожиданно, чем программа без оформления.
            return true;
        }
    }

    /// <summary>
    /// «Специальные возможности → Визуальные эффекты → Эффекты анимации».
    /// </summary>
    /// <remarks>
    /// Читается через <see cref="SystemParameters.ClientAreaAnimation" /> — это
    /// обёртка над системным запросом <c>SPI_GETCLIENTAREAANIMATION</c>, тем
    /// самым, по которому положено проверять этот тумблер. Человек, выключивший
    /// анимации из-за укачивания, не должен получить их обратно от нашей
    /// программы.
    /// </remarks>
    private static bool ReadAnimations()
    {
        try
        {
            return SystemParameters.ClientAreaAnimation;
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or InvalidOperationException)
        {
            return true;
        }
    }

    /// <summary>Умеет ли система рисовать Mica — то есть Windows 11 это или нет.</summary>
    private static bool ReadMicaSupport()
    {
        try
        {
            return WindowBackdrop.IsSupported(WindowBackdropType.Mica);
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }
}
