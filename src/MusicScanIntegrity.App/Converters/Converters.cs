using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.App.Converters;

// Конвертеров «статус -> кисть» здесь намеренно нет. Конвертер разрешает
// ресурс один раз и отдаёт готовую кисть, а привязка при смене темы заново
// не считается — цвета так и остаются от прежней темы. Цвет по статусу
// раздают стили с DataTrigger и DynamicResource (Views/ResultsView.xaml).

/// <summary>Показывает элемент, когда значение не пусто.</summary>
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

/// <summary>Логическое значение в видимость (с необязательной инверсией через параметр «invert»).</summary>
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

/// <summary>Сравнение значения с параметром — для выбора варианта из нескольких кнопок.</summary>
public sealed class EqualsConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null ? parameter : Binding.DoNothing;
}

/// <summary>Число байт в человеческий размер («24,1 МБ»).</summary>
public sealed class SizeConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is long bytes ? Core.Common.Format.Size(bytes) : "—";

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Число с разрядами через неразрывный пробел.</summary>
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

/// <summary>
/// Счётчик статуса в ширину сегмента полосы распределения.
/// </summary>
/// <remarks>
/// Привязать число прямо к <c>ColumnDefinition.Width</c> нельзя: WPF считает его
/// шириной в пикселях, и полоса схлопывается. Нужна именно звёздочная доля,
/// чтобы сегменты делили ширину пропорционально счётчикам.
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

/// <summary>Цвет из строки «#RRGGBB» — для образцов цвета статусов в настройках.</summary>
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

/// <summary>Инверсия логического значения — например «включено вручную» = «не авто».</summary>
public sealed class InvertBoolConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>
/// Русская подпись значения перечисления.
/// </summary>
/// <remarks>
/// Без него выпадающие списки показывали бы имена из кода — «All», «Html», «Ask».
/// Интерфейс полностью на русском, английских служебных слов в нём быть не должно.
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
