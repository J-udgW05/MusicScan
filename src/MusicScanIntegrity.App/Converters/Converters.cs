using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.App.Converters;

// There is deliberately no status-to-brush converter. A converter resolves
// the resource once and the binding is not re-evaluated on a theme change, so
// colours would stick to the old theme. Status colours come from styles with
// DataTrigger and DynamicResource instead (Views/ResultsView.xaml).

/// <summary>Visible when the value is not empty.</summary>
public sealed class NotEmptyToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool visible = value switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            int count => count > 0,
            long count => count > 0,
            bool flag => flag,
            System.Collections.ICollection collection => collection.Count > 0,
            _ => true,
        };

        if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase))
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Boolean to visibility; pass "invert" as the parameter to invert.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is true;

        if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>Compares the value with the parameter; used by radio-style button groups.</summary>
public sealed class EqualsConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null ? parameter : Binding.DoNothing;
}

/// <summary>Byte count to a human-readable size.</summary>
public sealed class SizeConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is long bytes ? Core.Common.Format.Size(bytes) : "—";

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Number with non-breaking-space digit groups.</summary>
public sealed class NumberConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        int number => Core.Common.Format.Number(number),
        long number => Core.Common.Format.Number(number),
        _ => "0",
    };

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Status count to a segment width in the distribution bar.</summary>
/// <remarks>
/// A plain number bound to <c>ColumnDefinition.Width</c> is taken as pixels and
/// the bar collapses; a star share is needed for proportional segments.
/// </remarks>
public sealed class ShareToWidthConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int share = value switch
        {
            int number => Math.Max(0, number),
            long number => (int)Math.Max(0, number),
            _ => 0,
        };

        return new GridLength(share, GridUnitType.Star);
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Brush from a "#RRGGBB" string, for the status colour swatches.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
        {
            return Brushes.Transparent;
        }

        try
        {
            SolidColorBrush brush = new((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
        catch (FormatException)
        {
            return Brushes.Transparent;
        }
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Inverts a boolean, e.g. "manual" as "not automatic".</summary>
public sealed class InvertBoolConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Display caption for an enum value.</summary>
/// <remarks>
/// Without it combo boxes would show code names such as "Html" or "Ask".
/// </remarks>
public sealed class EnumDisplayConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        AppTheme.System => "Как в системе",
        AppTheme.Light => "Светлая",
        AppTheme.Dark => "Тёмная",

        LockedFileAction.Ask => "Спрашивать",
        LockedFileAction.Skip => "Пропускать",
        LockedFileAction.Wait => "Подождать",
        LockedFileAction.TempCopy => "Временная копия",
        LockedFileAction.CloseOwner => "Закрыть владельца",

        ReportFormat.Html => "HTML-страница",
        ReportFormat.Csv => "Таблица CSV",
        ReportFormat.Text => "Текстовый список",

        CheckDepth.Quick => "Только начало",
        CheckDepth.Sampled => "Выборочно",
        CheckDepth.Full => "Файл целиком",

        ListDensity.Normal => "Обычная",
        ListDensity.Compact => "Компактная",

        _ => value?.ToString() ?? string.Empty,
    };

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
