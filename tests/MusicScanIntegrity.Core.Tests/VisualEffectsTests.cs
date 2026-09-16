using System.Text.Json;
using System.Text.Json.Serialization;
using MusicScanIntegrity.Core.Settings;
using Xunit;

namespace MusicScanIntegrity.Core.Tests;

/// <summary>
/// First run takes the system setting; afterwards the user's choice stands.
/// </summary>
/// <remarks>
/// Simple to state and easy to break by confusing "never chosen" with "off",
/// so every combination is covered.
/// </remarks>
public sealed class VisualEffectsTests
{
    /// <summary>Everything on and supported: a typical Windows 11.</summary>
    private static readonly SystemEffects Rich = new(true, true, true);

    /// <summary>Effects off in the system, but Mica is supported.</summary>
    private static readonly SystemEffects Plain = new(false, false, true);

    /// <summary>Mica unsupported: Windows 10.</summary>
    private static readonly SystemEffects NoMica = new(true, true, false);

    [Fact]
    public void First_run_takes_backdrop_from_system_effects()
    {
        Assert.True(VisualEffects.ResolveMicaPreference(null, Rich));
        Assert.False(VisualEffects.ResolveMicaPreference(null, Plain));
    }

    [Fact]
    public void First_run_takes_animations_from_system()
    {
        Assert.True(VisualEffects.ResolveAnimations(null, Rich));
        Assert.False(VisualEffects.ResolveAnimations(null, Plain));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void User_choice_is_not_overridden_by_system(bool chosen)
    {
        Assert.Equal(chosen, VisualEffects.ResolveMicaPreference(chosen, Rich));
        Assert.Equal(chosen, VisualEffects.ResolveMicaPreference(chosen, Plain));
        Assert.Equal(chosen, VisualEffects.ResolveAnimations(chosen, Rich));
        Assert.Equal(chosen, VisualEffects.ResolveAnimations(chosen, Plain));
    }

    /// <remarks>
    /// The likeliest bug here is testing for false instead of for unset, which
    /// would give the backdrop back on every launch to a user who turned it off.
    /// </remarks>
    [Fact]
    public void Explicit_off_is_not_confused_with_unset()
    {
        Assert.False(VisualEffects.ResolveMicaPreference(false, Rich));
        Assert.True(VisualEffects.ResolveMicaPreference(null, Rich));

        Assert.False(VisualEffects.ResolveAnimations(false, Rich));
        Assert.True(VisualEffects.ResolveAnimations(null, Rich));
    }

    /// <summary>The intent is stored; only what is possible is drawn.</summary>
    /// <remarks>
    /// Windows 10 cannot draw Mica, but that must not turn the setting off, or it
    /// would stay off after upgrading to Windows 11.
    /// </remarks>
    [Fact]
    public void Unsupported_backdrop_is_stored_but_not_drawn()
    {
        bool preference = VisualEffects.ResolveMicaPreference(null, NoMica);

        Assert.True(preference);
        Assert.False(VisualEffects.IsMicaEffective(preference, NoMica));
        Assert.True(VisualEffects.IsMicaEffective(preference, Rich));
    }

    [Fact]
    public void Disabled_backdrop_is_not_drawn_even_when_supported()
    {
        Assert.False(VisualEffects.IsMicaEffective(false, Rich));
    }

    /// <remarks>
    /// The application draws animations itself, so they work the same on Windows 10.
    /// </remarks>
    [Fact]
    public void Animations_do_not_depend_on_backdrop_support()
    {
        Assert.True(VisualEffects.ResolveAnimations(null, NoMica));
        Assert.True(VisualEffects.ResolveAnimations(true, NoMica));
    }
}

/// <summary>Persisting three-state settings.</summary>
public sealed class VisualEffectsStorageTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <remarks>
    /// If null were written as false, first run would never happen: the app would
    /// assume the user turned everything off and never consult the system.
    /// </remarks>
    [Fact]
    public void Unset_value_survives_round_trip()
    {
        AppSettings loaded = RoundTrip(new AppSettings());

        Assert.Null(loaded.MicaEffect);
        Assert.Null(loaded.Animations);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Chosen_values_survive_round_trip(bool mica, bool animations)
    {
        AppSettings loaded = RoundTrip(new AppSettings { MicaEffect = mica, Animations = animations });

        Assert.Equal(mica, loaded.MicaEffect);
        Assert.Equal(animations, loaded.Animations);
    }

    [Fact]
    public void Sanitising_does_not_touch_effects()
    {
        AppSettings settings = JsonSettingsService.Sanitize(
            new AppSettings { MicaEffect = false, Animations = null });

        Assert.False(settings.MicaEffect);
        Assert.Null(settings.Animations);
    }

    [Fact]
    public void Settings_clone_carries_effects()
    {
        AppSettings copy = new AppSettings { MicaEffect = true, Animations = false }.Clone();

        Assert.True(copy.MicaEffect);
        Assert.False(copy.Animations);
    }

    /// <remarks>
    /// An update installs over the old version and keeps its settings file; the
    /// user should get styling based on system settings, not none.
    /// </remarks>
    [Fact]
    public void Old_settings_file_reads_as_unset()
    {
        AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(
            """{ "SchemaVersion": 1, "Theme": "Dark", "ShowStatusBar": true }""",
            Options);

        Assert.NotNull(loaded);
        Assert.Null(loaded.MicaEffect);
        Assert.Null(loaded.Animations);
    }

    private static AppSettings RoundTrip(AppSettings settings)
    {
        string json = JsonSerializer.Serialize(settings, Options);
        return JsonSerializer.Deserialize<AppSettings>(json, Options)!;
    }
}
