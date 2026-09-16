using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MusicScanIntegrity.Core.Common;

namespace MusicScanIntegrity.App.Views.Dialogs;

/// <summary>Status colour picker.</summary>
/// <remarks>
/// The system colour dialog lives in Windows Forms and would look foreign
/// here, so this window draws its own field: horizontally the colour fades to
/// white, vertically to black, and the strip below selects the hue. Presets
/// cover "just give me a proper red"; the hex box is the only way to enter an
/// exact value, which a mouse on a gradient cannot hit. The maths lives in
/// <see cref="ColorMath" />.
/// </remarks>
public partial class ColorPickDialog
{
    /// <summary>Presets: three shades per status for both themes.</summary>
    private static readonly string[] Palette =
    [
        "#0F7B3F", "#2E9E5B", "#5EC27F",
        "#C42B2F", "#E04A4E", "#FF7075",
        "#8A5A06", "#C08A1E", "#F0B429",
        "#4A4A50", "#6F6F76", "#9A9AA2",
        "#2F6FD0", "#5B9BF3", "#7C4DBE",
    ];

    private ColorMath.Hsv _hsv = new(0, 0, 1);

    // Field and strip update the hex code, and the hex code moves them back.
    // This flag breaks the loop; otherwise rounding would drift the cursor.
    private bool _syncing;

    /// <param name="label">Status name.</param>
    /// <param name="currentHex">Current colour.</param>
    public ColorPickDialog(string label, string currentHex)
    {
        InitializeComponent();

        TitleText.Text = "Цвет статуса";
        LabelText.Text = label;
        Swatches.ItemsSource = Palette;

        SelectedHex = currentHex;
        HexBox.Text = currentHex;

        // Field size is only known after layout.
        Loaded += (_, _) => ApplyHexToPickers(SelectedHex);
    }

    /// <summary>Selected colour as "#RRGGBB".</summary>
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

    /// <summary>Positions the field and strip to match the hex code.</summary>
    private void ApplyHexToPickers(string hex)
    {
        if (!ColorMath.TryParse(hex, out ColorMath.Rgb color))
        {
            return;
        }

        ColorMath.Hsv hsv = ColorMath.ToHsv(color);

        // Grey has no hue; keep the strip where it was instead of jumping to red.
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

    /// <summary>Writes the mouse-picked value into the hex code.</summary>
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
