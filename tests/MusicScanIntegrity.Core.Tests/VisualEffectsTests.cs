using System.Text.Json;
using System.Text.Json.Serialization;
using MusicScanIntegrity.Core.Settings;
using Xunit;

namespace MusicScanIntegrity.Core.Tests;

/// <summary>
/// Правило «при первом запуске берём из системы, дальше — то, что выбрал
/// человек».
/// </summary>
/// <remarks>
/// Правило простое на словах и ровно поэтому его легко сломать: достаточно
/// перепутать «ещё не выбирали» с «выключено». Здесь перебраны все сочетания.
/// </remarks>
public sealed class VisualEffectsTests
{
    /// <summary>Всё включено и всё умеет — так выглядит обычная Windows 11.</summary>
    private static readonly SystemEffects Rich = new(true, true, true);

    /// <summary>Эффекты в системе выключены, но нарисовать их есть чем.</summary>
    private static readonly SystemEffects Plain = new(false, false, true);

    /// <summary>Подложку рисовать нечем — так выглядит Windows 10.</summary>
    private static readonly SystemEffects NoMica = new(true, true, false);

    [Fact]
    public void При_первом_запуске_подложка_берётся_из_системных_эффектов()
    {
        Assert.True(VisualEffects.ResolveMicaPreference(null, Rich));
        Assert.False(VisualEffects.ResolveMicaPreference(null, Plain));
    }

    [Fact]
    public void При_первом_запуске_анимации_берутся_из_системных()
    {
        Assert.True(VisualEffects.ResolveAnimations(null, Rich));
        Assert.False(VisualEffects.ResolveAnimations(null, Plain));
    }

    /// <summary>
    /// Выбор человека сильнее системного: он на то и выбор.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Выбранное_вручную_не_перебивается_системой(bool chosen)
    {
        Assert.Equal(chosen, VisualEffects.ResolveMicaPreference(chosen, Rich));
        Assert.Equal(chosen, VisualEffects.ResolveMicaPreference(chosen, Plain));
        Assert.Equal(chosen, VisualEffects.ResolveAnimations(chosen, Rich));
        Assert.Equal(chosen, VisualEffects.ResolveAnimations(chosen, Plain));
    }

    /// <summary>
    /// «Выключено» — это выбор, а не отсутствие выбора.
    /// </summary>
    /// <remarks>
    /// Самая вероятная ошибка в таком коде: проверить значение на ложь вместо
    /// проверки на «не задано». Тогда человек, выключивший подложку, получал бы
    /// её обратно при каждом запуске.
    /// </remarks>
    [Fact]
    public void Выключено_вручную_не_путается_с_невыбранным()
    {
        Assert.False(VisualEffects.ResolveMicaPreference(false, Rich));
        Assert.True(VisualEffects.ResolveMicaPreference(null, Rich));

        Assert.False(VisualEffects.ResolveAnimations(false, Rich));
        Assert.True(VisualEffects.ResolveAnimations(null, Rich));
    }

    /// <summary>
    /// Хранится намерение, рисуется возможное.
    /// </summary>
    /// <remarks>
    /// На Windows 10 подложки нет, но настройку это выключать не должно: иначе
    /// после перехода на одиннадцатую она осталась бы выключенной без причины.
    /// </remarks>
    [Fact]
    public void Без_поддержки_системы_подложка_хранится_но_не_рисуется()
    {
        bool preference = VisualEffects.ResolveMicaPreference(null, NoMica);

        Assert.True(preference);
        Assert.False(VisualEffects.IsMicaEffective(preference, NoMica));
        Assert.True(VisualEffects.IsMicaEffective(preference, Rich));
    }

    [Fact]
    public void Выключенная_подложка_не_рисуется_и_там_где_её_умеют()
    {
        Assert.False(VisualEffects.IsMicaEffective(false, Rich));
    }

    /// <summary>
    /// Анимации от поддержки подложки не зависят.
    /// </summary>
    /// <remarks>
    /// Их рисует сама программа, а не система, — на Windows 10 они работают
    /// точно так же.
    /// </remarks>
    [Fact]
    public void Анимации_не_зависят_от_поддержки_подложки()
    {
        Assert.True(VisualEffects.ResolveAnimations(null, NoMica));
        Assert.True(VisualEffects.ResolveAnimations(true, NoMica));
    }
}

/// <summary>Хранение трёхзначных настроек в файле.</summary>
public sealed class VisualEffectsStorageTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// «Ещё не выбирали» должно доживать до следующего запуска.
    /// </summary>
    /// <remarks>
    /// Если пустое значение при записи превратится в <c>false</c>, первый
    /// запуск на деле никогда не состоится: программа решит, что человек уже
    /// всё выключил, и системные настройки не посмотрит ни разу.
    /// </remarks>
    [Fact]
    public void Невыбранное_значение_переживает_запись_и_чтение()
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
    public void Выбранные_значения_переживают_запись_и_чтение(bool mica, bool animations)
    {
        AppSettings loaded = RoundTrip(new AppSettings { MicaEffect = mica, Animations = animations });

        Assert.Equal(mica, loaded.MicaEffect);
        Assert.Equal(animations, loaded.Animations);
    }

    [Fact]
    public void Приведение_в_допустимые_пределы_не_трогает_эффекты()
    {
        AppSettings settings = JsonSettingsService.Sanitize(
            new AppSettings { MicaEffect = false, Animations = null });

        Assert.False(settings.MicaEffect);
        Assert.Null(settings.Animations);
    }

    [Fact]
    public void Копия_настроек_несёт_эффекты_с_собой()
    {
        AppSettings copy = new AppSettings { MicaEffect = true, Animations = false }.Clone();

        Assert.True(copy.MicaEffect);
        Assert.False(copy.Animations);
    }

    /// <summary>
    /// Старый файл настроек, где этих полей ещё нет, читается как «не выбирали».
    /// </summary>
    /// <remarks>
    /// Программа обновляется поверх старой, и её файл настроек остаётся прежним.
    /// Человек должен получить оформление по системным настройкам, а не пустое.
    /// </remarks>
    [Fact]
    public void Файл_настроек_прежней_версии_читается_как_невыбранное()
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
