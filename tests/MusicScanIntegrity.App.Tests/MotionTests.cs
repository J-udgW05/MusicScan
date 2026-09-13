using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MusicScanIntegrity.App.Controls;
using Xunit;

namespace MusicScanIntegrity.App.Tests;

/// <summary>
/// Выключатель анимаций.
/// </summary>
/// <remarks>
/// Проверяется здесь не красота движения, а то, из-за чего выключатель мог бы
/// не сработать: наследование значения по дереву, отдельные окна, к которым
/// наследование не доходит, и состояние элемента после выключения.
/// </remarks>
public sealed class MotionTests
{
    [Fact]
    public void Значение_наследуется_вниз_по_дереву()
    {
        Sta.Run(() =>
        {
            Border root = new();
            StackPanel middle = new();
            Border leaf = new();

            root.Child = middle;
            middle.Children.Add(leaf);

            Motion.SetEnabled(root, false);

            Assert.False(Motion.GetEnabled(leaf));

            Motion.SetEnabled(root, true);
            Assert.True(Motion.GetEnabled(leaf));
        });
    }

    /// <summary>
    /// Своё значение сильнее унаследованного.
    /// </summary>
    [Fact]
    public void Своё_значение_перебивает_унаследованное()
    {
        Sta.Run(() =>
        {
            Border root = new();
            Border leaf = new();
            root.Child = leaf;

            Motion.SetEnabled(root, false);
            Motion.SetEnabled(leaf, true);

            Assert.True(Motion.GetEnabled(leaf));
        });
    }

    /// <summary>
    /// При выключенных анимациях элемент сразу оказывается на месте.
    /// </summary>
    /// <remarks>
    /// Это не мелочь: без такой ветки элемент, застигнутый выключением на
    /// середине анимации, остался бы полупрозрачным и сдвинутым — и часть
    /// экрана выглядела бы съехавшей до перезапуска.
    /// </remarks>
    [Fact]
    public void С_выключенными_анимациями_элемент_сразу_на_месте()
    {
        Sta.Run(() =>
        {
            Border element = new() { Opacity = 0, RenderTransform = new TranslateTransform(0, 40) };
            Motion.SetEnabled(element, false);

            Motion.Play(element);

            Assert.Equal(1, element.Opacity);
            Assert.Equal(0, ((TranslateTransform)element.RenderTransform).Y);
            Assert.False(element.HasAnimatedProperties, "анимаций быть не должно");
        });
    }

    /// <summary>
    /// С включёнными анимациями движение действительно заводится.
    /// </summary>
    /// <remarks>
    /// Проверяется факт анимации, а не её кадр. Кадр без запущенного цикла
    /// диспетчера смотреть бесполезно: анимация уже назначена свойству, но
    /// значение ещё не пересчитано, и проверка кадра ловила бы не поведение,
    /// а отсутствие цикла сообщений.
    /// </remarks>
    [Fact]
    public void С_включёнными_анимациями_движение_заводится()
    {
        Sta.Run(() =>
        {
            Border element = new();
            Motion.SetEnabled(element, true);

            Motion.Play(element);

            Assert.True(element.HasAnimatedProperties, "прозрачность должна анимироваться");

            TranslateTransform shift = Assert.IsType<TranslateTransform>(element.RenderTransform);
            Assert.True(shift.HasAnimatedProperties, "сдвиг должен анимироваться");
        });
    }

    [Fact]
    public void Сброс_снимает_следы_прерванной_анимации()
    {
        Sta.Run(() =>
        {
            Border element = new();
            Motion.SetEnabled(element, true);
            Motion.Play(element);

            Motion.Reset(element);

            Assert.Equal(1, element.Opacity);
            Assert.Equal(0, ((TranslateTransform)element.RenderTransform).Y);
        });
    }

    /// <summary>
    /// Чужое преобразование не отбирается.
    /// </summary>
    /// <remarks>
    /// Элемент мог быть повёрнут или отмасштабирован не нами. Заменить его
    /// преобразование своим — сломать то, ради чего его ставили.
    /// </remarks>
    [Fact]
    public void Чужое_преобразование_не_подменяется()
    {
        Sta.Run(() =>
        {
            RotateTransform rotation = new(15);
            Border element = new() { RenderTransform = rotation };
            Motion.SetEnabled(element, false);

            Motion.Play(element);

            Assert.Same(rotation, element.RenderTransform);
        });
    }

    /// <summary>
    /// Ползунок стоит там, где велит состояние переключателя.
    /// </summary>
    [Theory]
    [InlineData(true, 20.0)]
    [InlineData(false, 0.0)]
    public void Ползунок_встаёт_по_состоянию(bool on, double expected)
    {
        Sta.Run(() =>
        {
            Border knob = Knob();

            Motion.PlaceKnob(knob, on, animate: false);

            Assert.Equal(expected, Shift(knob).X);
        });
    }

    /// <summary>
    /// Смена настройки анимаций не двигает ползунок.
    /// </summary>
    /// <remarks>
    /// Ровно эта ошибка и была: ход ползунка разводился двумя ветками триггеров
    /// по условиям «включён и анимации есть» и «включён и анимаций нет». Оба
    /// условия завязаны на состояние, истинное в покое, поэтому переключение
    /// настройки анимаций заставляло одну ветку выйти — уводя ползунок в
    /// положение «выключено», — а другую войти. Тумблеры разъезжались и
    /// переставали показывать своё настоящее состояние.
    /// </remarks>
    [Fact]
    public void Переключение_настройки_анимаций_не_двигает_ползунок()
    {
        Sta.Run(() =>
        {
            Border knob = Knob();
            Motion.SetEnabled(knob, true);
            Motion.PlaceKnob(knob, on: true, animate: false);

            foreach (bool animations in new[] { false, true, false, true })
            {
                Motion.SetEnabled(knob, animations);

                Assert.Equal(20.0, Shift(knob).X);
                Assert.False(
                    Shift(knob).HasAnimatedProperties,
                    "смена настройки анимаций не должна ничего анимировать");
            }
        });
    }

    /// <summary>
    /// С анимациями ползунок едет, без них встаёт сразу.
    /// </summary>
    [Fact]
    public void Ползунок_едет_только_когда_анимации_включены()
    {
        Sta.Run(() =>
        {
            Border moving = Knob();
            Motion.PlaceKnob(moving, on: true, animate: true);
            Assert.True(Shift(moving).HasAnimatedProperties, "ползунок должен ехать");

            Border instant = Knob();
            Motion.PlaceKnob(instant, on: true, animate: false);
            Assert.False(Shift(instant).HasAnimatedProperties, "ползунок должен встать сразу");
            Assert.Equal(20.0, Shift(instant).X);
        });
    }

    /// <summary>
    /// Возврат без анимации снимает предыдущую — иначе она держала бы значение.
    /// </summary>
    [Fact]
    public void Мгновенная_установка_отменяет_начатое_движение()
    {
        Sta.Run(() =>
        {
            Border knob = Knob();

            Motion.PlaceKnob(knob, on: true, animate: true);
            Motion.PlaceKnob(knob, on: false, animate: false);

            Assert.False(Shift(knob).HasAnimatedProperties);
            Assert.Equal(0.0, Shift(knob).X);
        });
    }

    /// <summary>
    /// Замороженное преобразование не мешает: оно заменяется своей копией.
    /// </summary>
    /// <remarks>
    /// Всё, что объявлено в шаблоне, WPF замораживает — шаблон общий на все
    /// переключатели. Прямой вызов анимации по замороженному объекту падает
    /// с «объект запечатан или заморожен», и программа показывает окно ошибки.
    /// Раскадровка это обходила сама, поэтому в разметке проблема не всплывала.
    /// </remarks>
    [Fact]
    public void Замороженное_преобразование_заменяется_копией()
    {
        Sta.Run(() =>
        {
            Border knob = Knob();
            knob.RenderTransform.Freeze();
            Assert.True(knob.RenderTransform.IsFrozen);

            Motion.PlaceKnob(knob, on: true, animate: true);

            Assert.False(knob.RenderTransform.IsFrozen);
            Assert.True(Shift(knob).HasAnimatedProperties);
        });
    }

    /// <summary>
    /// Замороженным бывает не вся группа, а только сдвиг внутри неё.
    /// </summary>
    /// <remarks>
    /// Первая попытка чинить это смотрела на TransformGroup.IsFrozen и мимо:
    /// группа бывает разморожена, а лежащий в ней сдвиг — нет. Ошибка «объект
    /// запечатан или заморожен» вылезала окном поверх программы. Проверка на
    /// Freeze() всей группы этот случай не воспроизводит — Freeze морозит всё
    /// дерево разом.
    /// </remarks>
    [Fact]
    public void Замороженным_может_быть_только_сдвиг_внутри_группы()
    {
        Sta.Run(() =>
        {
            TranslateTransform frozen = new(0, 0);
            frozen.Freeze();

            Border knob = new()
            {
                RenderTransform = new TransformGroup
                {
                    Children = [new ScaleTransform(1, 1), frozen],
                },
            };

            Assert.False(knob.RenderTransform.IsFrozen);
            Assert.True(((TransformGroup)knob.RenderTransform).Children[1].IsFrozen);

            Motion.PlaceKnob(knob, on: true, animate: true);

            Assert.False(Shift(knob).IsFrozen);
            Assert.True(Shift(knob).HasAnimatedProperties);
        });
    }

    /// <summary>То же без анимации: присвоение тоже требует размороженного.</summary>
    [Fact]
    public void Замороженное_преобразование_не_мешает_встать_сразу()
    {
        Sta.Run(() =>
        {
            Border knob = Knob();
            knob.RenderTransform.Freeze();

            Motion.PlaceKnob(knob, on: true, animate: false);

            Assert.Equal(20.0, Shift(knob).X);
        });
    }

    /// <summary>Ползунок такой же, как в шаблоне переключателя.</summary>
    private static Border Knob() => new()
    {
        Width = 12,
        Height = 12,
        RenderTransform = new TransformGroup
        {
            Children = [new ScaleTransform(1, 1), new TranslateTransform(0, 0)],
        },
    };

    private static TranslateTransform Shift(Border knob) =>
        (TranslateTransform)((TransformGroup)knob.RenderTransform).Children[1];

    /// <summary>
    /// Новое окно получает текущую настройку.
    /// </summary>
    /// <remarks>
    /// Диалог — отдельное окно, и его владелец логическим родителем не
    /// является: наследование до диалога не доходит. Значение ставится ему
    /// при загрузке — иначе переключатели в диалоге продолжали бы ездить
    /// при выключенных анимациях.
    /// </remarks>
    [Fact]
    public void Новое_окно_получает_текущую_настройку()
    {
        Sta.Run(() =>
        {
            bool saved = Motion.DefaultEnabled;

            try
            {
                Motion.DefaultEnabled = false;

                Window window = new() { Width = 100, Height = 100, Left = -4000, ShowInTaskbar = false };
                Border content = new();
                window.Content = content;
                window.Show();

                Assert.False(Motion.GetEnabled(window));
                Assert.False(Motion.GetEnabled(content));

                window.Close();
            }
            finally
            {
                Motion.DefaultEnabled = saved;
            }
        });
    }

    /// <summary>
    /// Значение, выставленное явно, умолчанием не перебивается.
    /// </summary>
    [Fact]
    public void Явно_заданное_значение_окна_сохраняется()
    {
        Sta.Run(() =>
        {
            bool saved = Motion.DefaultEnabled;

            try
            {
                Motion.DefaultEnabled = false;

                Window window = new() { Width = 100, Height = 100, Left = -4000, ShowInTaskbar = false };
                Motion.SetEnabled(window, true);
                window.Show();

                Assert.True(Motion.GetEnabled(window));

                window.Close();
            }
            finally
            {
                Motion.DefaultEnabled = saved;
            }
        });
    }
}
