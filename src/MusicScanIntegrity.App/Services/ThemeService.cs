using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using MusicScanIntegrity.Core.Common;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Settings;
using Wpf.Ui.Controls;

namespace MusicScanIntegrity.App.Services;

/// <summary>Switches the theme and applies user status colours.</summary>
public interface IThemeService
{
    /// <summary>Theme currently on screen; System is already resolved to Light or Dark.</summary>
    AppTheme EffectiveTheme { get; }

    /// <summary>The OS can draw a Mica backdrop.</summary>
    bool IsMicaSupported { get; }

    /// <summary>Applies theme, status colours, backdrop and animations from settings.</summary>
    void Apply(AppSettings settings);

    /// <summary>Applies styling to a newly opened window.</summary>
    /// <param name="withBackdrop">
    /// Whether to apply the backdrop. Dialogs do not get one: Mica is the main
    /// window's material, and on a window floating above it it turns into murky
    /// translucency.
    /// </param>
    void ApplyToWindow(Window window, bool withBackdrop);

    /// <summary>Default status colour for the current theme, shown in settings.</summary>
    string DefaultColorHex(CheckStatus status);
}

/// <summary>Applies the theme by swapping the token dictionary in application resources.</summary>
/// <remarks>
/// With "follow system" selected, the service listens for Windows theme changes
/// and switches without a restart.
/// </remarks>
public sealed class ThemeService : IThemeService, IDisposable
{
    private static readonly Uri LightTokens = new("pack://application:,,,/Resources/Tokens.Light.xaml");
    private static readonly Uri DarkTokens = new("pack://application:,,,/Resources/Tokens.Dark.xaml");

    /// <summary>Default status colours from the design tokens.</summary>
    private static readonly Dictionary<CheckStatus, (string Light, string Dark)> DefaultStatusColors = new()
    {
        [CheckStatus.Ok] = ("#0F7B3F", "#5EC27F"),
        [CheckStatus.Corrupted] = ("#C42B2F", "#FF7075"),
        [CheckStatus.Warning] = ("#8A5A06", "#F0B429"),
        [CheckStatus.Skipped] = ("#6F6F76", "#9A9AA2"),
    };

    private ResourceDictionary? _current;
    private AppSettings _settings = new();
    private bool _subscribed;
    private bool _mica;
    private bool _animations = true;

    /// <inheritdoc />
    public bool IsMicaSupported { get; } = SystemEffectsReader.Read().MicaSupported;

    /// <inheritdoc />
    public AppTheme EffectiveTheme { get; private set; } = AppTheme.Light;

    /// <inheritdoc />
    public void Apply(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;

        AppTheme resolved = settings.Theme == AppTheme.System
            ? (IsSystemDark() ? AppTheme.Dark : AppTheme.Light)
            : settings.Theme;

        EffectiveTheme = resolved;

        _mica = VisualEffects.IsMicaEffective(
            settings.MicaEffect ?? false,
            new SystemEffects(true, true, IsMicaSupported));
        _animations = settings.Animations ?? true;
        Controls.Motion.DefaultEnabled = _animations;

        ResourceDictionary tokens = new() { Source = resolved == AppTheme.Dark ? DarkTokens : LightTokens };
        ApplyOverrides(tokens, settings.StatusColors, resolved);

        if (_mica)
        {
            ApplyMicaSurfaces(tokens, resolved);
        }

        Collection<ResourceDictionary> dictionaries = Application.Current.Resources.MergedDictionaries;

        // Replace the theme dictionary exactly where App.xaml declares it. WPF
        // resolves merged dictionaries from the end, so inserting at the front would
        // let the light tokens in markup override the swapped-in dark ones.
        int index = _current is not null ? dictionaries.IndexOf(_current) : -1;

        if (index < 0)
        {
            index = IndexOfTokens(dictionaries);
        }

        if (index >= 0)
        {
            dictionaries[index] = tokens;
        }
        else
        {
            // No token dictionary in markup; append it last for top priority.
            dictionaries.Add(tokens);
        }

        _current = tokens;

        // WPF-UI draws the title bar and the Mica backdrop and needs the theme too.
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
            resolved == AppTheme.Dark
                ? Wpf.Ui.Appearance.ApplicationTheme.Dark
                : Wpf.Ui.Appearance.ApplicationTheme.Light);

        SubscribeToSystemTheme(settings.Theme == AppTheme.System);

        // Windows already open need the new backdrop and animation settings.
        foreach (Window window in Application.Current?.Windows ?? [])
        {
            ApplyToWindow(window, withBackdrop: ReferenceEquals(window, Application.Current?.MainWindow));
        }
    }

    /// <inheritdoc />
    public void ApplyToWindow(Window window, bool withBackdrop)
    {
        ArgumentNullException.ThrowIfNull(window);

        Controls.Motion.DefaultEnabled = _animations;
        Controls.Motion.SetEnabled(window, _animations);

        if (!withBackdrop)
        {
            return;
        }

        // Keep the window property truthful, but do not rely on it: it only applies
        // the backdrop when the value changes. Assigning the same value does nothing,
        // while something else may have replaced the backdrop meanwhile, so it is
        // also applied explicitly below.
        if (window is FluentWindow fluent)
        {
            fluent.WindowBackdropType = _mica ? WindowBackdropType.Mica : WindowBackdropType.None;
        }

        if (_mica)
        {
            WindowBackdrop.ApplyBackdrop(window, WindowBackdropType.Mica);

            // Mica only shows through the window: ApplyBackdrop leaves the background
            // alone, and an opaque brush hides it completely.
            WindowBackdrop.RemoveBackground(window);
        }
        else
        {
            WindowBackdrop.RemoveBackdrop(window);
            window.SetResourceReference(Window.BackgroundProperty, "Brush.Bg");
        }
    }

    /// <summary>Makes surfaces translucent so the backdrop shows through.</summary>
    /// <remarks>
    /// The page background goes fully transparent — that is the plane Mica shows
    /// through. The title bar and toolbar get a light tint and cards a dense one,
    /// mirroring the Windows 11 Settings window, where the backdrop sits under the
    /// content rather than in it.
    /// <para>
    /// Opacity differs per theme for legibility: a light tint on a light
    /// background is nearly invisible, so the light theme needs denser layers.
    /// </para>
    /// </remarks>
    private static void ApplyMicaSurfaces(ResourceDictionary tokens, AppTheme theme)
    {
        tokens["Brush.Bg"] = Frozen(Colors.Transparent);

        Color surface = theme == AppTheme.Dark
            ? Color.FromRgb(0x26, 0x26, 0x2A)
            : Color.FromRgb(0xFF, 0xFF, 0xFF);

        Color mica = theme == AppTheme.Dark
            ? Color.FromRgb(0x2C, 0x2C, 0x31)
            : Color.FromRgb(0xF9, 0xF9, 0xFB);

        byte cardAlpha = theme == AppTheme.Dark ? (byte)0xD8 : (byte)0xC8;
        byte chromeAlpha = theme == AppTheme.Dark ? (byte)0x66 : (byte)0x80;

        // The window chrome goes fully transparent — title bar, toolbar and the line
        // beneath. It is one material in Windows 11, with no seams.
        tokens["Brush.Chrome"] = Frozen(Colors.Transparent);
        tokens["Brush.ChromeLine"] = Frozen(Colors.Transparent);

        tokens["Brush.TitleBar"] = Frozen(Color.FromArgb(chromeAlpha, mica.R, mica.G, mica.B));
        tokens["Brush.Mica"] = Frozen(Color.FromArgb(chromeAlpha, mica.R, mica.G, mica.B));
        tokens["Brush.Card"] = Frozen(Color.FromArgb(cardAlpha, surface.R, surface.G, surface.B));
        tokens["Brush.Card2"] = Frozen(Color.FromArgb(cardAlpha, mica.R, mica.G, mica.B));
    }

    /// <inheritdoc />
    public string DefaultColorHex(CheckStatus status)
    {
        if (!DefaultStatusColors.TryGetValue(status, out (string Light, string Dark) pair))
        {
            return "#808080";
        }

        return EffectiveTheme == AppTheme.Dark ? pair.Dark : pair.Light;
    }

    /// <inheritdoc />
    public void Dispose() => SubscribeToSystemTheme(false);

    /// <summary>Finds the theme token dictionary among the merged dictionaries.</summary>
    private static int IndexOfTokens(Collection<ResourceDictionary> dictionaries)
    {
        for (int i = 0; i < dictionaries.Count; i++)
        {
            string? source = dictionaries[i].Source?.OriginalString;

            if (source is not null && source.Contains("Tokens.", StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Replaces status brushes with the user's colours.</summary>
    private static void ApplyOverrides(ResourceDictionary tokens, StatusColorOverrides overrides, AppTheme theme)
    {
        Set("Ok", overrides.Ok);
        Set("Err", overrides.Corrupted);
        Set("Warn", overrides.Warning);
        Set("Skip", overrides.Skipped);

        void Set(string key, string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return;
            }

            try
            {
                Color color = (Color)ColorConverter.ConvertFromString(hex);
                tokens["Brush." + key] = Frozen(color);

                // Derive the badge background from the colour itself; otherwise a purple
                // "ok" would sit on the theme's green badge and look broken rather than
                // recoloured.
                tokens["Brush." + key + "Bg"] = Frozen(Tint(color, theme));
            }
            catch (FormatException)
            {
                // Invalid colour in the settings file; keep the theme colour.
            }
        }
    }

    /// <summary>Frozen brush: theme brushes are read from several threads.</summary>
    private static SolidColorBrush Frozen(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Badge background: the colour blended into the theme surface.</summary>
    private static Color Tint(Color color, AppTheme theme)
    {
        ColorMath.Rgb surface = theme == AppTheme.Dark
            ? new ColorMath.Rgb(0x1C, 0x1C, 0x1E)
            : new ColorMath.Rgb(0xFF, 0xFF, 0xFF);

        ColorMath.Rgb tinted = ColorMath.Tint(
            new ColorMath.Rgb(color.R, color.G, color.B),
            surface,
            theme == AppTheme.Dark ? 0.13 : 0.10);

        return Color.FromRgb(tinted.R, tinted.G, tinted.B);
    }

    /// <summary>Reads the Windows light/dark app theme setting.</summary>
    internal static bool IsSystemDark()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception)
        {
            // No registry access; assume light.
            return false;
        }
    }

    private void SubscribeToSystemTheme(bool subscribe)
    {
        if (subscribe == _subscribed)
        {
            return;
        }

        if (subscribe)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
        else
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }

        _subscribed = subscribe;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General)
        {
            return;
        }

        // The event arrives off the UI thread; marshal back.
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_settings.Theme == AppTheme.System)
            {
                Apply(_settings);
            }
        });
    }
}
