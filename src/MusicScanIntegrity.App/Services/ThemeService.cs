using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using MusicScanIntegrity.Core.Common;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Settings;
using Wpf.Ui.Controls;

namespace MusicScanIntegrity.App.Services;

/// <summary>Переключение темы оформления и пользовательских цветов статусов.</summary>
public interface IThemeService
{
    /// <summary>Тема, которая сейчас показана на экране (System уже разрешён в Light или Dark).</summary>
    AppTheme EffectiveTheme { get; }

    /// <summary>Подложку Mica система умеет рисовать.</summary>
    bool IsMicaSupported { get; }

    /// <summary>Применяет тему, цвета статусов, подложку и анимации из настроек.</summary>
    void Apply(AppSettings settings);

    /// <summary>
    /// Применяет оформление к только что открытому окну.
    /// </summary>
    /// <param name="window">Окно.</param>
    /// <param name="withBackdrop">
    /// Ставить ли подложку. Диалогам она не положена: Mica — материал главного
    /// окна, а всплывающему поверх него он даёт грязный полупрозрачный кисель.
    /// </param>
    void ApplyToWindow(Window window, bool withBackdrop);

    /// <summary>Цвет статуса по умолчанию для текущей темы — показывается в настройках.</summary>
    string DefaultColorHex(CheckStatus status);
}

/// <summary>
/// Тема применяется подменой словаря токенов в ресурсах приложения.
/// </summary>
/// <remarks>
/// При выборе «как в системе» программа подписывается на смену системной темы
/// и переключается без перезапуска (UI_SPEC.md, раздел 2).
/// </remarks>
public sealed class ThemeService : IThemeService, IDisposable
{
    private static readonly Uri LightTokens = new("pack://application:,,,/Resources/Tokens.Light.xaml");
    private static readonly Uri DarkTokens = new("pack://application:,,,/Resources/Tokens.Dark.xaml");

    /// <summary>Цвета статусов по умолчанию — из таблицы токенов UI_SPEC.md, раздел 1.</summary>
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

        // Словарь темы подменяется РОВНО на том месте, где он объявлен в App.xaml.
        // Это принципиально: WPF ищет ресурсы в объединённых словарях с конца,
        // поэтому вставка в начало списка не работала бы — светлые токены,
        // объявленные в разметке, перебивали бы подставленные тёмные.
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
            // Словаря токенов в разметке нет — добавляем последним,
            // чтобы он имел наивысший приоритет.
            dictionaries.Add(tokens);
        }

        _current = tokens;

        // Библиотека WPF-UI рисует заголовок окна и подложку Mica — ей тоже
        // нужно сообщить, какая сейчас тема.
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
            resolved == AppTheme.Dark
                ? Wpf.Ui.Appearance.ApplicationTheme.Dark
                : Wpf.Ui.Appearance.ApplicationTheme.Light);

        SubscribeToSystemTheme(settings.Theme == AppTheme.System);

        // Окна уже открыты — им нужно сообщить о смене подложки и анимаций.
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

        // Свойство окна держим в согласии с действительностью, но полагаться
        // на него нельзя: оно применяет подложку только когда значение
        // меняется. Присвоение того же самого ничего не делает — а подложку к
        // этому моменту мог переставить кто-то другой, и окно осталось бы
        // с чужой. Поэтому ниже она ставится ещё и явно.
        if (window is FluentWindow fluent)
        {
            fluent.WindowBackdropType = _mica ? WindowBackdropType.Mica : WindowBackdropType.None;
        }

        if (_mica)
        {
            WindowBackdrop.ApplyBackdrop(window, WindowBackdropType.Mica);

            // Подложку видно только сквозь окно: сама по себе ApplyBackdrop
            // фона не трогает, и непрозрачная кисть закрывает её целиком.
            WindowBackdrop.RemoveBackground(window);
        }
        else
        {
            WindowBackdrop.RemoveBackdrop(window);
            window.SetResourceReference(Window.BackgroundProperty, "Brush.Bg");
        }
    }

    /// <summary>
    /// Делает поверхности полупрозрачными, чтобы подложку было видно.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Фон страницы уходит в ноль совсем — это и есть та плоскость, сквозь
    /// которую смотрит Mica. Заголовок и панель инструментов получают лёгкий
    /// налёт, карточки — плотный: ровно так устроено окно параметров Windows 11,
    /// где подложка живёт под содержимым, а не в нём.
    /// </para>
    /// <para>
    /// Значения непрозрачности разные для тёмной и светлой темы не для красоты:
    /// светлый налёт на светлом фоне почти не виден, и карточку пришлось бы
    /// искать глазами. Тёмная тема прощает больше, поэтому там слои тоньше.
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

        // Оправа окна уходит в прозрачность целиком: заголовок, панель
        // инструментов и линия под ними. Материал под ними один, и делить его
        // швами незачем — в Windows 11 это сплошная поверхность.
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

    /// <summary>Ищет словарь токенов темы среди объединённых словарей приложения.</summary>
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

    /// <summary>Подменяет кисти статусов на выбранные пользователем.</summary>
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

                // Подложка метки выводится из самого цвета. Иначе выбранный
                // фиолетовый «в порядке» оставался бы на зелёной подложке
                // темы — метка выглядела бы сломанной, а не перекрашенной.
                tokens["Brush." + key + "Bg"] = Frozen(Tint(color, theme));
            }
            catch (FormatException)
            {
                // Некорректный цвет в файле настроек — остаётся цвет темы.
            }
        }
    }

    /// <summary>Замороженная кисть: кисти темы читаются из разных потоков.</summary>
    private static SolidColorBrush Frozen(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Подложка метки: цвет, подмешанный к поверхности темы.</summary>
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

    /// <summary>Читает системную настройку светлой/тёмной темы Windows.</summary>
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
            // Нет доступа к реестру — считаем тему светлой.
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

        // Событие приходит не из потока интерфейса — возвращаемся в него.
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_settings.Theme == AppTheme.System)
            {
                Apply(_settings);
            }
        });
    }
}
