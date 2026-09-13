using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MusicScanIntegrity.App.Controls;

/// <summary>
/// Анимации появления — те же по характеру, что в окне параметров Windows 11:
/// содержимое всплывает снизу и одновременно проявляется.
/// </summary>
/// <remarks>
/// <para>
/// Почему анимации запускаются из кода, а не раскадровками в разметке. Триггер
/// в шаблоне держит раскадровку замороженной — иначе её нельзя было бы делить
/// между всеми одинаковыми элементами. Замороженное дерево не принимает ни
/// <c>DynamicResource</c>, ни привязок, поэтому длительность в нём нельзя
/// поменять на ходу: разбор такой разметки просто падает. Проверено — не
/// предположение. Значит, выключатель анимаций в шаблонах не сделать, и
/// анимации, которыми он управляет, должны заводиться из кода, где условие
/// проверяется в момент запуска.
/// </para>
/// <para>
/// По той же причине отсюда двигается и ползунок переключателя
/// (<see cref="KnobProperty" />). Попытка развести это двумя ветками триггеров
/// — «включён и анимации есть» против «включён и анимаций нет» — обошлась
/// дорого: оба условия завязаны на состояние, истинное в покое, и смена
/// настройки анимаций заставляла одну ветку выйти, уводя ползунок в положение
/// «выключено», а другую войти. Тумблеры разъезжались и переставали показывать
/// своё настоящее состояние. Положение ползунка не должно зависеть от того,
/// включены анимации: они решают только, двигаться плавно или встать сразу.
/// </para>
/// </remarks>
public static class Motion
{
    /// <summary>Насколько содержимое всплывает, в точках.</summary>
    private const double Rise = 24;

    /// <summary>Ход ползунка переключателя: 40 − 4 − 12 − 4 = 20.</summary>
    private const double KnobTravel = 20;

    /// <summary>
    /// Значение, которое получают вновь открытые окна.
    /// </summary>
    /// <remarks>
    /// Диалог — отдельное окно, и его <see cref="Window.Owner" /> логическим
    /// родителем не является: наследование присоединённого свойства до него не
    /// доходит. Поэтому значение ставится каждому окну при загрузке, а здесь
    /// лежит то, которое ставить.
    /// </remarks>
    public static bool DefaultEnabled { get; set; } = true;

    /// <summary>Длительность появления.</summary>
    /// <remarks>
    /// 300 мс — столько же занимает переход между разделами в параметрах
    /// Windows 11. Короче выглядит дёрганьем, длиннее — задержкой.
    /// </remarks>
    private static readonly Duration Entrance = new(TimeSpan.FromMilliseconds(300));

    /// <summary>Длительность хода ползунка — как у системного переключателя.</summary>
    private static readonly Duration KnobSlide = new(TimeSpan.FromMilliseconds(150));

    /// <summary>
    /// Анимации включены. Наследуется вниз по дереву, поэтому достаточно
    /// поставить его на окно.
    /// </summary>
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled",
            typeof(bool),
            typeof(Motion),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>
    /// Элемент проигрывает появление всякий раз, когда становится видимым.
    /// </summary>
    /// <remarks>
    /// Ставится на корень каждого экрана. Переключение вкладок и вход в
    /// настройки — это и есть смена видимости, отдельного события искать не надо.
    /// </remarks>
    public static readonly DependencyProperty EntranceProperty =
        DependencyProperty.RegisterAttached(
            "Entrance",
            typeof(bool),
            typeof(Motion),
            new PropertyMetadata(false, OnEntranceChanged));

    /// <summary>
    /// Ползунок переключателя: его положение задаётся отсюда.
    /// </summary>
    /// <remarks>
    /// Ставится на сам ползунок в шаблоне. Состояние берётся у переключателя,
    /// внутри которого он живёт, а настройка анимаций решает только, ехать
    /// плавно или встать сразу.
    /// </remarks>
    public static readonly DependencyProperty KnobProperty =
        DependencyProperty.RegisterAttached(
            "Knob",
            typeof(bool),
            typeof(Motion),
            new PropertyMetadata(false, OnKnobChanged));

    /// <summary>Обработчик переключения, сохранённый ради отписки.</summary>
    private static readonly DependencyProperty KnobHandlerProperty =
        DependencyProperty.RegisterAttached(
            "KnobHandler",
            typeof(RoutedEventHandler),
            typeof(Motion),
            new PropertyMetadata(null));

    static Motion()
    {
        // Одна подписка на весь класс окон: и главное, и любой диалог,
        // открытый когда угодно позже.
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));
    }

    /// <summary>Читает <see cref="EnabledProperty" />.</summary>
    /// <param name="element">Элемент.</param>
    /// <returns>Включены ли анимации на этом элементе.</returns>
    public static bool GetEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(EnabledProperty);
    }

    /// <summary>Пишет <see cref="EnabledProperty" />.</summary>
    /// <param name="element">Элемент.</param>
    /// <param name="value">Включены ли анимации.</param>
    public static void SetEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(EnabledProperty, value);
    }

    /// <summary>Читает <see cref="EntranceProperty" />.</summary>
    /// <param name="element">Элемент.</param>
    /// <returns>Проигрывает ли элемент появление.</returns>
    public static bool GetEntrance(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(EntranceProperty);
    }

    /// <summary>Пишет <see cref="EntranceProperty" />.</summary>
    /// <param name="element">Элемент.</param>
    /// <param name="value">Проигрывать ли появление.</param>
    public static void SetEntrance(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(EntranceProperty, value);
    }

    /// <summary>Читает <see cref="KnobProperty" />.</summary>
    /// <param name="element">Элемент.</param>
    /// <returns>Двигается ли этот элемент как ползунок переключателя.</returns>
    public static bool GetKnob(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(KnobProperty);
    }

    /// <summary>Пишет <see cref="KnobProperty" />.</summary>
    /// <param name="element">Элемент.</param>
    /// <param name="value">Двигать ли элемент как ползунок.</param>
    public static void SetKnob(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(KnobProperty, value);
    }

    /// <summary>
    /// Ставит ползунок в положение, отвечающее состоянию переключателя.
    /// </summary>
    /// <param name="knob">Ползунок.</param>
    /// <param name="on">Переключатель включён.</param>
    /// <param name="animate">Ехать плавно, а не вставать сразу.</param>
    public static void PlaceKnob(FrameworkElement knob, bool on, bool animate)
    {
        ArgumentNullException.ThrowIfNull(knob);

        if (knob.RenderTransform is not TransformGroup group)
        {
            return;
        }

        TranslateTransform? shift = FindShift(group);

        // Смотреть надо на сам сдвиг, а не на группу: группа бывает
        // разморожена, а лежащий в ней сдвиг — нет. Всё, что объявлено в
        // шаблоне, WPF замораживает: шаблон общий на все переключатели, и
        // менять его содержимое поодиночке нельзя. Своя копия — можно.
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
            // Снимаем прежнюю анимацию: иначе она держала бы своё значение
            // и присвоение не подействовало бы.
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
    /// Проигрывает появление, если анимации включены; иначе просто ставит
    /// элемент в конечное положение.
    /// </summary>
    /// <param name="element">Элемент.</param>
    /// <remarks>
    /// Ветка «выключено» нужна не для симметрии: без неё элемент, однажды
    /// анимированный, остался бы с прозрачностью и сдвигом от прерванной
    /// анимации, и после выключения анимаций часть экрана оказалась бы съехавшей.
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

    /// <summary>Снимает следы анимации: элемент виден и стоит на месте.</summary>
    /// <param name="element">Элемент.</param>
    public static void Reset(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;

        // Замороженное преобразование трогать нельзя — но и следов анимации
        // на нём быть не может: заморозить его могли только в шаблоне.
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

        // Обработчик замыкает сам ползунок: искать его в шаблоне по имени
        // значило бы привязать поведение к разметке.
        RoutedEventHandler handler = (_, _) =>
            PlaceKnob(knob, toggle.IsChecked == true, GetEnabled(knob));

        knob.SetValue(KnobHandlerProperty, handler);
        toggle.Checked += handler;
        toggle.Unchecked += handler;

        // Начальное положение — без движения: переключатель ещё не трогали.
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
        // Значение, выставленное явно, не трогаем: оно сильнее умолчания.
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

    /// <summary>Находит сдвиг внутри группы преобразований.</summary>
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

    /// <summary>Даёт элементу сдвиг, по которому его можно поднимать.</summary>
    /// <remarks>
    /// Чужое преобразование не трогаем: если у элемента уже свой
    /// <see cref="RenderTransform" />, поднимать его нельзя — сломается то,
    /// ради чего преобразование ставили.
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
