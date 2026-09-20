using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MusicScanIntegrity.App.Controls;

/// <summary>Icon from the application icon set.</summary>
/// <remarks>
/// Geometry comes from <c>Resources/Icons.xaml</c>, generated from the design
/// reference; icons are never hand-drawn.
/// <para>
/// Every icon is a 20×20 stroke drawing, so this control scales the geometry
/// and picks the stroke width by size: 1.6 at 16 and 20 px, 1.5 at 24, 1.4 at
/// 32. Colour is inherited from <see cref="Foreground"/>; there are no coloured
/// variants.
/// </para>
/// </remarks>
public sealed class MsIcon : FrameworkElement
{
    /// <summary>Coordinate space every icon is drawn in.</summary>
    private const double DesignSize = 20.0;

    /// <summary>Icons drawn filled rather than stroked.</summary>
    /// <remarks>
    /// The authoritative list ships with the icon set as <c>Icon.FilledKinds</c>;
    /// this copy is only a fallback when the set fails to load. It used to live
    /// here alone, so a change in the reference would have been silently ignored.
    /// </remarks>
    private static readonly HashSet<string> DefaultFilledKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "play", "pause", "stop",
    };

    private HashSet<string>? _filledKinds;

    private static readonly ConcurrentDictionary<string, Geometry?> GeometryCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Icon name from the set, e.g. "folder" or "status-ok".</summary>
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind),
        typeof(string),
        typeof(MsIcon),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Size in pixels; 16 by default, as in rows and toolbar buttons.</summary>
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size),
        typeof(double),
        typeof(MsIcon),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Icon colour; inherited from adjacent text.</summary>
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(MsIcon),
        new FrameworkPropertyMetadata(
            Brushes.Black,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>
    /// Stroke width in 20×20 units; derived from size when unset. Set explicitly
    /// only for status glyphs in table rows (14 px, weight 1.8).
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
                // Only play, pause and stop may be filled.
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

    /// <summary>Stroke width for a given size.</summary>
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

    /// <summary>Looks up and caches the parsed geometry for an icon name.</summary>
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

    /// <summary>Reads the list of filled icons from the set.</summary>
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
