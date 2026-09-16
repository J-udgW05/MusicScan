using System;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MusicScanIntegrity.App.Controls;

/// <summary>Provides the application mark at the frame size needed.</summary>
/// <remarks>
/// The mark used to ship as separate 128 and 40 px images, and the 20 px title
/// bar made WPF downscale again, smearing strokes thinner than a pixel into
/// grey. Frames now come straight from <c>app.ico</c> — the same file Windows
/// uses — which carries 16 to 256 px, so the right one is picked without
/// resampling. DPI is honoured: at 200 % a 20 px mark takes the 40 px frame.
/// </remarks>
public static class AppMark
{
    private static readonly Uri IconUri =
        new("pack://application:,,,/Assets/app.ico", UriKind.Absolute);

    /// <summary>Frame for the given size in layout units.</summary>
    /// <param name="owner">Element whose DPI decides the scale.</param>
    public static BitmapSource ForSize(Visual owner, double size)
    {
        ArgumentNullException.ThrowIfNull(owner);

        double scale = VisualTreeHelper.GetDpi(owner).DpiScaleX;
        int wanted = (int)Math.Ceiling(size * scale);

        BitmapDecoder decoder = BitmapDecoder.Create(
            IconUri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

        // Never pick a smaller frame: stretching a bitmap blurs it again. If the
        // screen is so dense that none fits, take the largest available.
        return decoder.Frames
                   .Where(frame => frame.PixelWidth >= wanted)
                   .MinBy(frame => frame.PixelWidth)
               ?? decoder.Frames.MaxBy(frame => frame.PixelWidth)!;
    }
}
