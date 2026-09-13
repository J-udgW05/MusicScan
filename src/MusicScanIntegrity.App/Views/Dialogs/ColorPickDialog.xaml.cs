using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MusicScanIntegrity.Core.Common;

namespace MusicScanIntegrity.App.Views.Dialogs;

/// <summary>
/// Выбор цвета статуса.
/// </summary>
/// <remarks>
/// Решение по неоднозначности: системный диалог выбора цвета живёт в Windows Forms,
/// тянуть которую в WPF-приложение ради одного окна не хочется — вид получился бы
/// чужим для остального интерфейса. Поэтому здесь своё поле оттенков: по горизонтали
/// цвет разбавляется белым, по вертикали уходит в чёрный, полоса снизу задаёт
/// оттенок. Готовые оттенки и поле кода остаются: первое — на случай «просто дай
/// нормальный красный», второе — единственный способ ввести цвет с клавиатуры,
/// потому что мышью по градиенту точное значение не поймать.
/// Сам расчёт цвета лежит в <see cref="ColorMath" />: здесь только перенос
/// координат курсора в доли.
/// </remarks>
public partial class ColorPickDialog
{
    /// <summary>Готовые оттенки: по три варианта на каждый статус в обеих темах.</summary>
    private static readonly string[] Palette =
    [
        "#0F7B3F", "#2E9E5B", "#5EC27F",
        "#C42B2F", "#E04A4E", "#FF7075",
        "#8A5A06", "#C08A1E", "#F0B429",
        "#4A4A50", "#6F6F76", "#9A9AA2",
        "#2F6FD0", "#5B9BF3", "#7C4DBE",
    ];

    private ColorMath.Hsv _hsv = new(0, 0, 1);

    // Поле и полоса меняют код цвета, код цвета двигает поле и полосу.
    // Флаг разрывает это кольцо, иначе округление гоняло бы курсор по полю.
    private bool _syncing;

    /// <summary>Создаёт окно выбора цвета.</summary>
    /// <param name="label">Название статуса.</param>
    /// <param name="currentHex">Текущий цвет.</param>
    public ColorPickDialog(string label, string currentHex)
    {
        InitializeComponent();

        TitleText.Text = "Цвет статуса";
        LabelText.Text = label;
        Swatches.ItemsSource = Palette;

        SelectedHex = currentHex;
        HexBox.Text = currentHex;

        // Размеры поля известны только после разметки — до неё ползунки
        // некуда ставить.
        Loaded += (_, _) => ApplyHexToPickers(SelectedHex);
    }

    /// <summary>Выбранный цвет в формате «#RRGGBB».</summary>
    public string SelectedHex { get; private set; }

    private static Color ToMedia(ColorMath.Rgb color) => Color.FromRgb(color.R, color.G, color.B);

    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
        {
            HexBox.Text = hex;
        }
    }

    private void OnHexChanged(object sender, TextChangedEventArgs e)
    {
        bool valid = ColorMath.TryParse(HexBox.Text, out ColorMath.Rgb color);

        ErrorText.Visibility = valid || HexBox.Text.Trim().Length == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        ApplyButton.IsEnabled = valid;

        if (!valid)
        {
            return;
        }

        SelectedHex = color.ToString();
        Preview.Background = new SolidColorBrush(ToMedia(color));

        if (!_syncing)
        {
            ApplyHexToPickers(SelectedHex);
        }
    }

    /// <summary>Ставит поле и полосу в положение, отвечающее коду цвета.</summary>
    private void ApplyHexToPickers(string hex)
    {
        if (!ColorMath.TryParse(hex, out ColorMath.Rgb color))
        {
            return;
        }

        ColorMath.Hsv hsv = ColorMath.ToHsv(color);

        // Серый цвет не имеет оттенка — у полосы остаётся прежнее положение,
        // иначе она прыгала бы в красный при каждом выборе серого.
        _hsv = hsv.Saturation > 0
            ? hsv
            : _hsv with { Saturation = hsv.Saturation, Value = hsv.Value };

        UpdatePickers();
    }

    private void UpdatePickers()
    {
        AreaHue.Background = new SolidColorBrush(ToMedia(ColorMath.ToRgb(new ColorMath.Hsv(_hsv.Hue, 1, 1))));

        if (Area.ActualWidth > 0)
        {
            Canvas.SetLeft(AreaThumb, (_hsv.Saturation * Area.ActualWidth) - (AreaThumb.Width / 2));
            Canvas.SetTop(AreaThumb, ((1 - _hsv.Value) * Area.ActualHeight) - (AreaThumb.Height / 2));
            AreaThumb.Fill = new SolidColorBrush(ToMedia(ColorMath.ToRgb(_hsv)));
        }

        if (Hue.ActualWidth > 0)
        {
            Canvas.SetLeft(HueThumb, (_hsv.Hue / 360 * Hue.ActualWidth) - (HueThumb.Width / 2));
        }
    }

    /// <summary>Переносит выбранное мышью значение в код цвета.</summary>
    private void PushToHex()
    {
        _syncing = true;
        try
        {
            HexBox.Text = ColorMath.ToRgb(_hsv).ToString();
        }
        finally
        {
            _syncing = false;
        }

        UpdatePickers();
    }

    private void OnAreaDown(object sender, MouseButtonEventArgs e)
    {
        Area.CaptureMouse();
        PickFromArea(e.GetPosition(Area));
    }

    private void OnAreaMove(object sender, MouseEventArgs e)
    {
        if (Area.IsMouseCaptured)
        {
            PickFromArea(e.GetPosition(Area));
        }
    }

    private void OnAreaUp(object sender, MouseButtonEventArgs e) => Area.ReleaseMouseCapture();

    private void PickFromArea(Point point)
    {
        _hsv = _hsv with
        {
            Saturation = ColorMath.Share(point.X, Area.ActualWidth),
            Value = 1 - ColorMath.Share(point.Y, Area.ActualHeight),
        };

        PushToHex();
    }

    private void OnHueDown(object sender, MouseButtonEventArgs e)
    {
        Hue.CaptureMouse();
        PickFromHue(e.GetPosition(Hue));
    }

    private void OnHueMove(object sender, MouseEventArgs e)
    {
        if (Hue.IsMouseCaptured)
        {
            PickFromHue(e.GetPosition(Hue));
        }
    }

    private void OnHueUp(object sender, MouseButtonEventArgs e) => Hue.ReleaseMouseCapture();

    private void PickFromHue(Point point)
    {
        _hsv = _hsv with { Hue = ColorMath.Share(point.X, Hue.ActualWidth) * 360 };
        PushToHex();
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
