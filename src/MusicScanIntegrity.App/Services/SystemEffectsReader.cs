using System.IO;
using System.Windows;
using Microsoft.Win32;
using MusicScanIntegrity.Core.Settings;
using Wpf.Ui.Controls;

namespace MusicScanIntegrity.App.Services;

/// <summary>Reads whether visual effects are enabled in Windows.</summary>
/// <remarks>
/// Used only on first run, so the app opens looking the way the user is used
/// to; afterwards the user's own settings decide.
/// </remarks>
internal static class SystemEffectsReader
{
    /// <summary>Registry location of the transparency effects switch.</summary>
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Takes a snapshot of the relevant system settings.</summary>
    public static SystemEffects Read() => new(
        TransparencyEnabled: ReadTransparency(),
        AnimationsEnabled: ReadAnimations(),
        MicaSupported: ReadMicaSupport());

    /// <summary>Personalisation → Colours → Transparency effects.</summary>
    /// <remarks>
    /// Windows has no separate Mica switch; the backdrop follows the general
    /// transparency toggle, so that is what first run looks at.
    /// </remarks>
    private static bool ReadTransparency()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);

            // A missing value means "on" in Windows.
            return key?.GetValue("EnableTransparency") is not int value || value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Registry access denied: assume effects are on, as in a fresh Windows
            // install — less surprising than an app with no styling.
            return true;
        }
    }

    /// <summary>Accessibility → Visual effects → Animation effects.</summary>
    /// <remarks>
    /// Read through <see cref="SystemParameters.ClientAreaAnimation" />, a wrapper
    /// over <c>SPI_GETCLIENTAREAANIMATION</c>, the documented query for this
    /// toggle. Someone who turned animations off because of motion sickness must
    /// not get them back from this app.
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

    /// <summary>Whether the OS can draw Mica, i.e. Windows 11 or later.</summary>
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
