using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MusicScanIntegrity.App.Services;

/// <summary>Uniform interface scale on top of the system DPI.</summary>
/// <remarks>
/// Applied as a layout transform, so every size in the markup keeps its design
/// value. Popups (drop-downs, context menus, tooltips) live in their own visual
/// trees and need the transform separately.
/// </remarks>
public static class UiScale
{
    /// <summary>Scale factor.</summary>
    public const double Factor = 1.10;

    /// <summary>Shared frozen transform for markup and code.</summary>
    public static ScaleTransform Transform { get; } = CreateTransform();

    private static bool _popupsRegistered;

    /// <summary>Scales tooltips and context menus; call once before any window opens.</summary>
    public static void RegisterPopups()
    {
        if (_popupsRegistered)
        {
            return;
        }

        _popupsRegistered = true;
        FrameworkElement.LayoutTransformProperty.OverrideMetadata(
            typeof(ToolTip), new FrameworkPropertyMetadata(Transform));
        FrameworkElement.LayoutTransformProperty.OverrideMetadata(
            typeof(ContextMenu), new FrameworkPropertyMetadata(Transform));
    }

    /// <summary>Scales a window's content and its size limits, keeping it on screen.</summary>
    /// <remarks>Call right after InitializeComponent, before the window is shown.</remarks>
    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.Content is FrameworkElement content)
        {
            content.LayoutTransform = Transform;
        }

        Rect area = SystemParameters.WorkArea;

        Scale(window, FrameworkElement.WidthProperty, area.Width);
        Scale(window, FrameworkElement.HeightProperty, area.Height);
        Scale(window, FrameworkElement.MinWidthProperty, area.Width);
        Scale(window, FrameworkElement.MinHeightProperty, area.Height);
        Scale(window, FrameworkElement.MaxWidthProperty, area.Width);
        Scale(window, FrameworkElement.MaxHeightProperty, area.Height);
    }

    /// <summary>Scales a size the window sets itself, never the base class defaults.</summary>
    /// <remarks>
    /// FluentWindow brings its own minimum size; scaling it made every dialog as
    /// wide as the largest one.
    /// </remarks>
    private static void Scale(Window window, DependencyProperty property, double limit)
    {
        if (window.ReadLocalValue(property) is not double value
            || double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
        {
            return;
        }

        window.SetValue(property, Math.Min(value * Factor, limit));
    }

    private static ScaleTransform CreateTransform()
    {
        ScaleTransform transform = new(Factor, Factor);
        transform.Freeze();
        return transform;
    }
}
