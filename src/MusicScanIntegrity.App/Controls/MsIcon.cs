using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MusicScanIntegrity.App.Controls;

/// <summary>
/// Иконка из набора программы.
/// </summary>
/// <remarks>
/// Иконки берутся из <c>Resources/Icons.xaml</c> — файла, сгенерированного из
/// дизайн-референса скриптом <c>tools/extract-icons.py</c>. Своих иконок «по мотивам»
/// не рисуем (UI_SPEC.md, раздел 10).
/// <para>
/// Все иконки нарисованы в системе координат 20×20 и только обводкой, поэтому
/// элемент сам масштабирует геометрию и подбирает толщину линии по размеру:
/// 16 и 20 px — 1,6; 24 px — 1,5; 32 px — 1,4. Цвет наследуется от текста рядом
/// (<see cref="Foreground"/>), цветных вариантов иконок не существует.
/// </para>
/// </remarks>
public sealed class MsIcon : FrameworkElement
{
    /// <summary>Система координат, в которой нарисованы все иконки набора.</summary>
    private const double DesignSize = 20.0;

    /// <summary>Иконки, которые рисуются заливкой, а не обводкой.</summary>
    /// <remarks>
    /// Список приходит из набора: <c>tools/extract-icons.py</c> кладёт его в
    /// <c>Icon.FilledKinds</c> рядом с самими геометриями. Здесь он только
    /// запасной — на случай, если набор не загрузился. Раньше список был
    /// записан здесь и нигде больше не сверялся: поменяйся заливка в
    /// референсе, программа продолжила бы рисовать по-старому и молча.
    /// </remarks>
    private static readonly HashSet<string> DefaultFilledKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "play", "pause", "stop",
    };

    private HashSet<string>? _filledKinds;

    private static readonly ConcurrentDictionary<string, Geometry?> GeometryCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Имя иконки из набора, например «folder» или «status-ok».</summary>
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind),
        typeof(string),
        typeof(MsIcon),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Размер иконки в пикселях; по умолчанию 16 — как в строках и кнопках панели.</summary>
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size),
        typeof(double),
        typeof(MsIcon),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Цвет иконки. Наследуется от текста рядом.</summary>
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(MsIcon),
        new FrameworkPropertyMetadata(
            Brushes.Black,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>
    /// Толщина линии в координатах 20×20. Если не задана, подбирается по размеру.
    /// Явно задаётся только для статусных значков в строках таблицы (14 px, вес 1,8).
    /// </summary>
    public static readonly DependencyProperty StrokeWeightProperty = DependencyProperty.Register(
        nameof(StrokeWeight),
        typeof(double),
        typeof(MsIcon),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <inheritdoc cref="KindProperty" />
    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <inheritdoc cref="SizeProperty" />
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <inheritdoc cref="ForegroundProperty" />
    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <inheritdoc cref="StrokeWeightProperty" />
    public double StrokeWeight
    {
        get => (double)GetValue(StrokeWeightProperty);
        set => SetValue(StrokeWeightProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        Geometry? geometry = Resolve(Kind);
        if (geometry is null || Foreground is null)
        {
            return;
        }

        double scale = Size / DesignSize;
        drawingContext.PushTransform(new ScaleTransform(scale, scale));

        try
        {
            if (ResolveFilledKinds().Contains(Kind))
            {
                // Заливка допускается только у play, pause и stop.
                drawingContext.DrawGeometry(Foreground, pen: null, geometry);
            }
            else
            {
                Pen pen = new(Foreground, ResolveWeight())
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round,
                };

                pen.Freeze();
                drawingContext.DrawGeometry(brush: null, pen, geometry);
            }
        }
        finally
        {
            drawingContext.Pop();
        }
    }

    /// <summary>Толщина линии по правилу из UI_SPEC.md, раздел 10.</summary>
    private double ResolveWeight()
    {
        if (!double.IsNaN(StrokeWeight))
        {
            return StrokeWeight;
        }

        return Size switch
        {
            <= 20 => 1.6,
            <= 24 => 1.5,
            _ => 1.4,
        };
    }

    /// <summary>Находит геометрию по имени иконки и запоминает разобранную.</summary>
    private Geometry? Resolve(string kind)
    {
        if (string.IsNullOrEmpty(kind))
        {
            return null;
        }

        return GeometryCache.GetOrAdd(kind, name =>
        {
            if (TryFindResource("Icon." + name) is not string data || data.Length == 0)
            {
                return null;
            }

            Geometry geometry = Geometry.Parse(data);
            geometry.Freeze();
            return geometry;
        });
    }

    /// <summary>Читает список заливаемых иконок из набора.</summary>
    private HashSet<string> ResolveFilledKinds()
    {
        if (_filledKinds is not null)
        {
            return _filledKinds;
        }

        _filledKinds = TryFindResource("Icon.FilledKinds") is string list && list.Length > 0
            ? new HashSet<string>(
                list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase)
            : DefaultFilledKinds;

        return _filledKinds;
    }
}
