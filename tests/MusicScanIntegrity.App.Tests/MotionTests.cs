using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MusicScanIntegrity.App.Controls;
using Xunit;

namespace MusicScanIntegrity.App.Tests;

/// <summary>Animations switch.</summary>
/// <remarks>
/// These tests cover what could make the switch fail, not how motion looks:
/// inheritance down the tree, separate windows inheritance does not reach, and
/// element state after animations are turned off.
/// </remarks>
public sealed class MotionTests
{
    [Fact]
    public void Value_is_inherited_down_the_tree()
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

    [Fact]
    public void Local_value_overrides_inherited()
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

    /// <remarks>
    /// Without this an element caught mid-animation would stay translucent and
    /// shifted until restart.
    /// </remarks>
    [Fact]
    public void With_animations_off_element_is_in_place_immediately()
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

    /// <remarks>
    /// Asserts that an animation is attached, not a frame: without a running
    /// dispatcher loop the value is not yet recomputed, so a frame check would
    /// test the missing message loop rather than the behaviour.
    /// </remarks>
    [Fact]
    public void With_animations_on_motion_starts()
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
    public void Reset_clears_interrupted_animation()
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

    /// <remarks>
    /// The element may have been rotated or scaled by someone else; replacing its
    /// transform would break that.
    /// </remarks>
    [Fact]
    public void Foreign_transform_is_not_replaced()
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

    [Theory]
    [InlineData(true, 20.0)]
    [InlineData(false, 0.0)]
    public void Knob_follows_toggle_state(bool on, double expected)
    {
        Sta.Run(() =>
        {
            Border knob = Knob();

            Motion.PlaceKnob(knob, on, animate: false);

            Assert.Equal(expected, Shift(knob).X);
        });
    }

    /// <remarks>
    /// Regression: knob travel was split into trigger branches "on with
    /// animations" and "on without". Both conditions hold at rest, so toggling the
    /// setting made one branch exit — sliding the knob to "off" — and the other
    /// enter, leaving toggles out of step with their state.
    /// </remarks>
    [Fact]
    public void Toggling_animations_setting_does_not_move_knob()
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

    [Fact]
    public void Knob_slides_only_when_animations_are_on()
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

    /// <summary>Snapping cancels a running slide, which would otherwise hold its value.</summary>
    [Fact]
    public void Snap_cancels_running_slide()
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

    /// <remarks>
    /// WPF freezes everything declared in a template. Animating a frozen object
    /// directly throws "cannot modify a frozen object"; storyboards clone it
    /// implicitly, which is why markup never hit this.
    /// </remarks>
    [Fact]
    public void Frozen_transform_is_replaced_with_copy()
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

    /// <remarks>
    /// The first fix checked TransformGroup.IsFrozen and missed: the group can be
    /// thawed while the translate transform inside it is frozen. Freezing the whole
    /// group does not reproduce this, since Freeze() freezes the entire tree.
    /// </remarks>
    [Fact]
    public void Only_translate_inside_group_may_be_frozen()
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

    /// <summary>Same without animation: plain assignment also needs a thawed transform.</summary>
    [Fact]
    public void Frozen_transform_does_not_block_snap()
    {
        Sta.Run(() =>
        {
            Border knob = Knob();
            knob.RenderTransform.Freeze();

            Motion.PlaceKnob(knob, on: true, animate: false);

            Assert.Equal(20.0, Shift(knob).X);
        });
    }

    /// <summary>A knob built like the one in the toggle template.</summary>
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

    /// <remarks>
    /// A dialog's owner is not its logical parent, so inheritance does not reach
    /// it; without the load-time value, toggles in dialogs would keep sliding with
    /// animations off.
    /// </remarks>
    [Fact]
    public void New_window_gets_current_setting()
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

    [Fact]
    public void Explicit_window_value_is_kept()
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
