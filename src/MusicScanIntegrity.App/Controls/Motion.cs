using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MusicScanIntegrity.App.Controls;

/// <summary>
/// Entrance animations in the style of Windows 11 Settings: content rises from
/// below while fading in.
/// </summary>
/// <remarks>
/// <para>
/// Animations are started from code rather than storyboards in markup. A
/// template trigger keeps its storyboard frozen so it can be shared, and a
/// frozen tree accepts neither <c>DynamicResource</c> nor bindings — parsing
/// such markup throws. An animations on/off switch therefore cannot be
/// expressed in templates; the condition has to be checked in code at start.
/// </para>
/// <para>
/// The toggle knob (<see cref="KnobProperty" />) moves from here for the same
/// reason. Splitting it into two trigger branches — "on with animations" and
/// "on without" — made toggles drift: both conditions are true at rest, so
/// changing the animations setting made one branch exit, sliding the knob to
/// "off", while the other entered. Knob position must not depend on the
/// setting; animations only decide whether it slides or snaps.
/// </para>
/// </remarks>
public static class Motion
{
    /// <summary>How far content rises, in layout units.</summary>
    private const double Rise = 24;

    /// <summary>Toggle knob travel: 40 − 4 − 12 − 4 = 20.</summary>
    private const double KnobTravel = 20;

    /// <summary>Value given to newly opened windows.</summary>
    /// <remarks>
    /// A dialog's <see cref="Window.Owner" /> is not its logical parent, so the
    /// inherited attached property never reaches it. Each window gets the value
    /// on load instead, and this is the value it gets.
    /// </remarks>
    public static bool DefaultEnabled { get; set; } = true;

    /// <summary>Entrance duration.</summary>
    /// <remarks>
    /// 300 ms matches page transitions in Windows 11 Settings; shorter looks
    /// jerky, longer looks laggy.
    /// </remarks>
    private static readonly Duration Entrance = new(TimeSpan.FromMilliseconds(300));

    /// <summary>Knob travel duration, matching the system toggle.</summary>
    private static readonly Duration KnobSlide = new(TimeSpan.FromMilliseconds(150));

    /// <summary>
    /// Animations are enabled. Inherited down the tree, so setting it on the window
    /// is enough.
    /// </summary>
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled",
            typeof(bool),
            typeof(Motion),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>The element plays its entrance whenever it becomes visible.</summary>
    /// <remarks>
    /// Set on the root of each screen; switching tabs and opening settings are
    /// visibility changes, so no other event is needed.
    /// </remarks>
    public static readonly DependencyProperty EntranceProperty =
        DependencyProperty.RegisterAttached(
            "Entrance",
            typeof(bool),
            typeof(Motion),
            new PropertyMetadata(false, OnEntranceChanged));

    /// <summary>Marks a toggle knob whose position is driven from here.</summary>
    /// <remarks>
    /// Set on the knob inside the template. State comes from the enclosing
    /// toggle; the animations setting only decides whether it slides or snaps.
    /// </remarks>
    public static readonly DependencyProperty KnobProperty =
        DependencyProperty.RegisterAttached(
            "Knob",
            typeof(bool),
            typeof(Motion),
            new PropertyMetadata(false, OnKnobChanged));

    /// <summary>Toggle handler kept so it can be unsubscribed.</summary>
    private static readonly DependencyProperty KnobHandlerProperty =
        DependencyProperty.RegisterAttached(
            "KnobHandler",
            typeof(RoutedEventHandler),
            typeof(Motion),
            new PropertyMetadata(null));

    static Motion()
    {
        // One class handler covers every window: the main one and any dialog opened later.
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));
    }

    /// <summary>Gets <see cref="EnabledProperty" />.</summary>
    public static bool GetEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(EnabledProperty);
    }

    /// <summary>Sets <see cref="EnabledProperty" />.</summary>
    public static void SetEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(EnabledProperty, value);
    }

    /// <summary>Gets <see cref="EntranceProperty" />.</summary>
    public static bool GetEntrance(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(EntranceProperty);
    }

    /// <summary>Sets <see cref="EntranceProperty" />.</summary>
    public static void SetEntrance(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(EntranceProperty, value);
    }

    /// <summary>Gets <see cref="KnobProperty" />.</summary>
    public static bool GetKnob(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(KnobProperty);
    }

    /// <summary>Sets <see cref="KnobProperty" />.</summary>
    public static void SetKnob(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(KnobProperty, value);
    }

    /// <summary>Moves the knob to the position matching the toggle state.</summary>
    /// <param name="animate">Slide rather than snap.</param>
    public static void PlaceKnob(FrameworkElement knob, bool on, bool animate)
    {
        ArgumentNullException.ThrowIfNull(knob);

        if (knob.RenderTransform is not TransformGroup group)
        {
            return;
        }

        TranslateTransform? shift = FindShift(group);

        // Check the translate transform itself, not the group: the group may be
        // thawed while the transform inside it is frozen. WPF freezes everything
        // declared in a template, since the template is shared by every toggle; a
        // private copy can be modified.
        if (shift is null || shift.IsFrozen || group.IsFrozen)
        {
            group = group.CloneCurrentValue();
            knob.RenderTransform = group;
            shift = FindShift(group);
        }

        if (shift is null || shift.IsFrozen)
        {
            return;
        }

        double target = on ? KnobTravel : 0;

        if (!animate)
        {
            // Remove the running animation first, or it keeps holding its value and
            // the assignment has no effect.
            shift.BeginAnimation(TranslateTransform.XProperty, null);
            shift.X = target;
            return;
        }

        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
        {
            To = target,
            Duration = KnobSlide,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    /// <summary>
    /// Plays the entrance when animations are on; otherwise puts the element in
    /// its final position.
    /// </summary>
    /// <remarks>
    /// The "off" branch matters: without it an element interrupted mid-animation
    /// would keep its opacity and offset, leaving part of the screen shifted after
    /// animations are turned off.
    /// </remarks>
    public static void Play(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (!GetEnabled(element))
        {
            Reset(element);
            return;
        }

        TranslateTransform shift = EnsureShift(element);

        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = Entrance,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });

        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
        {
            From = Rise,
            To = 0,
            Duration = Entrance,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    /// <summary>Clears animation leftovers: fully visible and in place.</summary>
    public static void Reset(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;

        // A frozen transform cannot be touched, but it cannot carry animation state
        // either — only a template could have frozen it.
        if (element.RenderTransform is TranslateTransform shift && !shift.IsFrozen)
        {
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.Y = 0;
        }
    }

    private static void OnKnobChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement knob)
        {
            return;
        }

        knob.Loaded -= OnKnobLoaded;
        knob.Unloaded -= OnKnobUnloaded;

        if (e.NewValue is not true)
        {
            return;
        }

        knob.Loaded += OnKnobLoaded;
        knob.Unloaded += OnKnobUnloaded;

        if (knob.IsLoaded)
        {
            OnKnobLoaded(knob, new RoutedEventArgs());
        }
    }

    private static void OnKnobLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement knob || knob.TemplatedParent is not ToggleButton toggle)
        {
            return;
        }

        Detach(knob, toggle);

        // The handler closes over the knob itself; looking it up in the template by
        // name would tie behaviour to markup.
        RoutedEventHandler handler = (_, _) =>
            PlaceKnob(knob, toggle.IsChecked == true, GetEnabled(knob));

        knob.SetValue(KnobHandlerProperty, handler);
        toggle.Checked += handler;
        toggle.Unchecked += handler;

        // Initial placement without motion: the toggle has not been touched yet.
        PlaceKnob(knob, toggle.IsChecked == true, animate: false);
    }

    private static void OnKnobUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement knob && knob.TemplatedParent is ToggleButton toggle)
        {
            Detach(knob, toggle);
        }
    }

    private static void Detach(FrameworkElement knob, ToggleButton toggle)
    {
        if (knob.GetValue(KnobHandlerProperty) is RoutedEventHandler previous)
        {
            toggle.Checked -= previous;
            toggle.Unchecked -= previous;
            knob.ClearValue(KnobHandlerProperty);
        }
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // Leave explicitly set values alone; they win over the default.
        if (sender is Window window &&
            window.ReadLocalValue(EnabledProperty) == DependencyProperty.UnsetValue)
        {
            SetEnabled(window, DefaultEnabled);
        }
    }

    private static void OnEntranceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        element.IsVisibleChanged -= OnVisibleChanged;

        if (e.NewValue is true)
        {
            element.IsVisibleChanged += OnVisibleChanged;
        }
    }

    private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is FrameworkElement element)
        {
            Play(element);
        }
    }

    /// <summary>Finds the translate transform inside a transform group.</summary>
    private static TranslateTransform? FindShift(TransformGroup group)
    {
        foreach (Transform transform in group.Children)
        {
            if (transform is TranslateTransform found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Gives the element a translate transform to animate.</summary>
    /// <remarks>
    /// An existing <see cref="RenderTransform" /> belongs to someone else and is
    /// left alone; moving it would break whatever it was set for.
    /// </remarks>
    private static TranslateTransform EnsureShift(FrameworkElement element)
    {
        if (element.RenderTransform is TranslateTransform existing && !existing.IsFrozen)
        {
            return existing;
        }

        TranslateTransform shift = new();
        element.RenderTransform = shift;
        return shift;
    }
}
