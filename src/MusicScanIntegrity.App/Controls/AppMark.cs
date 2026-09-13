using System;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MusicScanIntegrity.App.Controls;

/// <summary>
/// Отдаёт знак программы кадром нужного размера.
/// </summary>
/// <remarks>
/// Раньше знак лежал отдельными картинками: 128 точек для окна «О программе»
/// и 40 — для заголовка. Заголовок показывает знак в 20 точек, то есть WPF
/// ужимал картинку ещё раз, уже своими средствами, и от резкости, наведённой
/// при сборке значка, ничего не оставалось: линии рисунка тоньше точки, и
/// второй пересчёт растирает их в серое.
///
/// Здесь кадр берётся из <c>app.ico</c> — того же файла, из которого значок
/// берёт Windows. Там есть размеры 16…256, и нужный находится точно, без
/// повторного пересчёта. Экранный масштаб учитывается: при 200 % под знак
/// в 20 точек уходит кадр в 40.
/// </remarks>
public static class AppMark
{
    private static readonly Uri IconUri =
        new("pack://application:,,,/Assets/app.ico", UriKind.Absolute);

    /// <summary>Кадр под указанный размер в точках макета.</summary>
    /// <param name="owner">Элемент, по которому определяется масштаб экрана.</param>
    /// <param name="size">Размер в точках макета, в котором знак будет показан.</param>
    public static BitmapSource ForSize(Visual owner, double size)
    {
        ArgumentNullException.ThrowIfNull(owner);

        double scale = VisualTreeHelper.GetDpi(owner).DpiScaleX;
        int wanted = (int)Math.Ceiling(size * scale);

        BitmapDecoder decoder = BitmapDecoder.Create(
            IconUri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

        // Кадр берётся не меньше нужного: растянуть растр — снова его размыть.
        // Если экран настолько крупный, что подходящего кадра нет, берётся самый
        // большой из имеющихся.
        return decoder.Frames
                   .Where(frame => frame.PixelWidth >= wanted)
                   .MinBy(frame => frame.PixelWidth)
               ?? decoder.Frames.MaxBy(frame => frame.PixelWidth)!;
    }
}
