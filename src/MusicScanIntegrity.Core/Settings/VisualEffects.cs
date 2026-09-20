namespace MusicScanIntegrity.Core.Settings;

/// <summary>
/// What Windows itself reports about visual effects.
/// </summary>
/// <param name="TransparencyEnabled">Transparency effects are on in Personalisation.</param>
/// <param name="AnimationsEnabled">Animation effects are on in Accessibility.</param>
/// <param name="MicaSupported">The OS can draw a Mica backdrop at all; Windows 11 and up.</param>
public readonly record struct SystemEffects(
    bool TransparencyEnabled,
    bool AnimationsEnabled,
    bool MicaSupported)
{
    /// <summary>Nothing supported; the default for tests and the fallback path.</summary>
    public static readonly SystemEffects None = new(false, false, false);
}

/// <summary>
/// Decides whether the backdrop and animations are on.
/// </summary>
/// <remarks>
/// Kept as pure functions so the rule — take the system setting on first run,
/// honour the user's choice afterwards — can be tested for every combination
/// without opening a window.
/// <para>
/// Stored and effective values differ on purpose. The file records the
/// <em>intent</em>; the backdrop is only drawn where the OS supports it. Storing
/// the effective value would leave the setting off after an upgrade from
/// Windows 10, where it could never have been on.
/// </para>
/// </remarks>
public static class VisualEffects
{
    /// <summary>Whether the user wants the Mica backdrop.</summary>
    /// <param name="stored">Value from settings; <see langword="null" /> when never chosen.</param>
    /// <returns>The intent, which is what gets written to the settings file.</returns>
    public static bool ResolveMicaPreference(bool? stored, SystemEffects system) =>
        stored ?? system.TransparencyEnabled;

    /// <summary>Whether the backdrop is actually drawn.</summary>
    /// <returns><see langword="true" /> when it is both wanted and supported.</returns>
    public static bool IsMicaEffective(bool preference, SystemEffects system) =>
        preference && system.MicaSupported;

    /// <summary>Whether animations are on.</summary>
    /// <param name="stored">Value from settings; <see langword="null" /> when never chosen.</param>
    /// <remarks>
    /// There is no support question here: the application draws the animations
    /// itself, so they work anywhere.
    /// </remarks>
    public static bool ResolveAnimations(bool? stored, SystemEffects system) =>
        stored ?? system.AnimationsEnabled;
}
