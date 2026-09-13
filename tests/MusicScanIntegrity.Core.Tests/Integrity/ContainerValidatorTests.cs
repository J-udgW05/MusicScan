using MusicScanIntegrity.Core.Integrity;
using Xunit;

namespace MusicScanIntegrity.Core.Tests.Integrity;

public sealed class OggValidatorTests
{
    private static ContainerValidation Validate(byte[] file)
    {
        using MemoryStream stream = new(file, writable: false);
        return new OggValidator().Validate(stream, ContainerBounds.Measure(stream, file.Length), CancellationToken.None);
    }

    [Fact]
    public void Исправный_файл_проходит_проверку_сумм()
    {
        ContainerValidation result = Validate(SyntheticFiles.Ogg(pages: 4));

        Assert.Equal(ContainerVerdict.Verified, result.Verdict);
        Assert.Equal(4, result.UnitsChecked);
    }

    [Fact]
    public void Испорченный_байт_страницы_ловится_по_сумме()
    {
        byte[] file = SyntheticFiles.Ogg(pages: 3);
        int second = SyntheticFiles.OggPageOffset(1);
        file[second + 40] ^= 0x20;

        ContainerValidation result = Validate(file);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.Equal(1, result.UnitsChecked);
        Assert.Equal(second, result.ErrorOffset);
    }

    [Fact]
    public void Отсутствие_признака_конца_считается_обрывом()
    {
        ContainerValidation result = Validate(SyntheticFiles.Ogg(pages: 3, withEndFlag: false));

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Пропуск_номера_страницы_означает_потерю_куска()
    {
        ContainerValidation result = Validate(SyntheticFiles.Ogg(pages: 3, skipSequence: true));

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.Contains("не подряд", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Обрезанный_файл_называется_обрывом()
    {
        byte[] file = SyntheticFiles.Ogg(pages: 3);

        ContainerValidation result = Validate(file[..^40]);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
    }
}

public sealed class Mp3ValidatorTests
{
    private static ContainerValidation Validate(byte[] file)
    {
        using MemoryStream stream = new(file, writable: false);
        return new Mp3Validator().Validate(stream, ContainerBounds.Measure(stream, file.Length), CancellationToken.None);
    }

    [Fact]
    public void Цепочка_кадров_без_сумм_проверяется_как_структура()
    {
        ContainerValidation result = Validate(SyntheticFiles.Mp3(frames: 6));

        Assert.Equal(ContainerVerdict.StructureOnly, result.Verdict);
        Assert.Equal(6, result.UnitsChecked);
    }

    [Fact]
    public void Защищённые_кадры_проверяются_по_сумме()
    {
        ContainerValidation result = Validate(SyntheticFiles.Mp3(frames: 4, withCrc: true));

        Assert.Equal(ContainerVerdict.Verified, result.Verdict);
        Assert.Equal(4, result.UnitsChecked);
    }

    [Fact]
    public void Испорченная_служебная_часть_защищённого_кадра_ловится()
    {
        byte[] file = SyntheticFiles.Mp3(frames: 4, withCrc: true);
        file[SyntheticFiles.Mp3FrameOffset(2) + 10] ^= 0x11;

        ContainerValidation result = Validate(file);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.Equal(2, result.UnitsChecked);
    }

    [Fact]
    public void Тег_в_начале_не_мешает()
    {
        ContainerValidation result = Validate(SyntheticFiles.Mp3(frames: 3, leadingId3: true));

        Assert.Equal(ContainerVerdict.StructureOnly, result.Verdict);
        Assert.Equal(3, result.UnitsChecked);
    }

    [Fact]
    public void Нехватка_кадров_видна_по_заголовку_Info()
    {
        // В заголовке обещано двадцать кадров, а в файле пять.
        ContainerValidation result = Validate(SyntheticFiles.Mp3(frames: 5, declaredFrames: 20));

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Недописанный_последний_кадр_называется_обрывом()
    {
        byte[] file = SyntheticFiles.Mp3(frames: 4);

        ContainerValidation result = Validate(file[..^200]);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
        Assert.Equal(3, result.UnitsChecked);
    }

    [Fact]
    public void Мусор_в_середине_превращает_файл_в_повреждённый()
    {
        byte[] file = SyntheticFiles.Mp3(frames: 6);
        byte[] junk = new byte[4096];
        byte[] mixed = [.. file[..SyntheticFiles.Mp3FrameOffset(3)], .. junk, .. file[SyntheticFiles.Mp3FrameOffset(3)..]];

        ContainerValidation result = Validate(mixed);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.Contains("мусор", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class Mp4ValidatorTests
{
    private static ContainerValidation Validate(byte[] file)
    {
        using MemoryStream stream = new(file, writable: false);
        return new Mp4Validator().Validate(stream, ContainerBounds.Measure(stream, file.Length), CancellationToken.None);
    }

    [Fact]
    public void Целый_файл_проходит_проверку_структуры()
    {
        ContainerValidation result = Validate(SyntheticFiles.Mp4());

        Assert.Equal(ContainerVerdict.StructureOnly, result.Verdict);
        Assert.Equal(3, result.UnitsChecked);
    }

    [Fact]
    public void Блок_длиннее_остатка_файла_означает_обрыв()
    {
        ContainerValidation result = Validate(SyntheticFiles.Mp4(mediaBytes: 128, declaredMediaBytes: 4096));

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Отсутствие_описания_дорожек_замечается()
    {
        ContainerValidation result = Validate(SyntheticFiles.Mp4(withMovie: false));

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.Contains("дорожек", result.Message, StringComparison.Ordinal);
    }
}

public sealed class RiffValidatorTests
{
    private static ContainerValidation Validate(byte[] file)
    {
        using MemoryStream stream = new(file, writable: false);
        return new RiffValidator().Validate(stream, ContainerBounds.Measure(stream, file.Length), CancellationToken.None);
    }

    [Fact]
    public void Целый_файл_проходит_проверку_структуры()
    {
        ContainerValidation result = Validate(SyntheticFiles.Wav());

        Assert.Equal(ContainerVerdict.StructureOnly, result.Verdict);
    }

    [Fact]
    public void Обрезанный_файл_ловится_по_объявленной_длине()
    {
        byte[] file = SyntheticFiles.Wav(dataBytes: 512);

        ContainerValidation result = Validate(file[..^128]);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
    }

    /// <summary>
    /// Ноль и «все единицы» в поле длины означают «длина не известна».
    /// </summary>
    /// <remarks>
    /// Так пишет заголовок тот, кто записывает звук в поток и не может
    /// вернуться назад, чтобы проставить размер: живая запись, вывод в канал.
    /// Файл при этом целый, и объявлять его обрывающимся — ложная тревога.
    /// </remarks>
    [Theory]
    [InlineData(0x00u)]
    [InlineData(0xFFu)]
    public void Незаполненная_длина_в_заголовке_не_считается_обрывом(uint fill)
    {
        byte[] file = SyntheticFiles.Wav();
        for (int i = 4; i < 8; i++)
        {
            file[i] = (byte)fill;
        }

        ContainerValidation result = Validate(file);

        Assert.Equal(ContainerVerdict.StructureOnly, result.Verdict);
    }

    /// <summary>
    /// Обратная сторона: настоящий обрыв по-прежнему находится.
    /// </summary>
    [Fact]
    public void Заполненная_длина_длиннее_файла_остаётся_обрывом()
    {
        byte[] file = SyntheticFiles.Wav();
        file[4] = 0xFF;
        file[5] = 0xFF;

        ContainerValidation result = Validate(file);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Файл_без_описания_формата_повреждён()
    {
        ContainerValidation result = Validate(SyntheticFiles.Wav(withFormat: false));

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
    }
}

public sealed class WavPackValidatorTests
{
    private static ContainerValidation Validate(byte[] file)
    {
        using MemoryStream stream = new(file, writable: false);
        return new WavPackValidator().Validate(stream, ContainerBounds.Measure(stream, file.Length), CancellationToken.None);
    }

    [Fact]
    public void Цепочка_блоков_проверяется_как_структура()
    {
        ContainerValidation result = Validate(SyntheticFiles.WavPack(blocks: 4));

        Assert.Equal(ContainerVerdict.StructureOnly, result.Verdict);
        Assert.Equal(4, result.UnitsChecked);
        Assert.Contains("распакованному", result.TechnicalDetail!, StringComparison.Ordinal);
    }

    [Fact]
    public void Блок_длиннее_остатка_файла_означает_обрыв()
    {
        ContainerValidation result = Validate(SyntheticFiles.WavPack(blocks: 2, declaredExtra: 500));

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
    }
}

public sealed class ApeValidatorTests
{
    private static ContainerValidation Validate(byte[] file)
    {
        using MemoryStream stream = new(file, writable: false);
        return new ApeValidator().Validate(stream, ContainerBounds.Measure(stream, file.Length), CancellationToken.None);
    }

    [Fact]
    public void Сходящиеся_длины_дают_проверку_структуры()
    {
        ContainerValidation result = Validate(SyntheticFiles.Ape());

        Assert.Equal(ContainerVerdict.StructureOnly, result.Verdict);
    }

    [Fact]
    public void Нехватка_данных_против_описания_означает_обрыв()
    {
        ContainerValidation result = Validate(SyntheticFiles.Ape(audioBytes: 4096, actualBytes: 256));

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
    }
}

public sealed class ContainerIntegrityCheckerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "msi-integrity-" + Guid.NewGuid().ToString("N"));

    public ContainerIntegrityCheckerTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private ContainerValidation Check(string name, byte[] content)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, content);
        return new ContainerIntegrityChecker().Check(path, CancellationToken.None);
    }

    [Fact]
    public void Формат_определяется_по_содержимому_а_не_по_расширению()
    {
        // Внутри FLAC, а имя обещает MP3 — проверять надо правилами FLAC.
        ContainerValidation result = Check("подделка.mp3", FlacFileBuilder.Build(frames: 3));

        Assert.Equal("FLAC", result.Format);
        Assert.Equal(ContainerVerdict.Verified, result.Verdict);
    }

    [Fact]
    public void Незнакомый_формат_остаётся_без_вердикта()
    {
        ContainerValidation result = Check("непонятно.bin", new byte[512]);

        Assert.Equal(ContainerVerdict.NotSupported, result.Verdict);
    }

    [Fact]
    public void MP3_за_тегом_ID3_опознаётся()
    {
        ContainerValidation result = Check("трек.mp3", SyntheticFiles.Mp3(frames: 3, leadingId3: true));

        Assert.Equal("MP3", result.Format);
        Assert.Equal(ContainerVerdict.StructureOnly, result.Verdict);
    }

    [Fact]
    public void Отсутствующий_файл_не_роняет_проверку()
    {
        ContainerValidation result = new ContainerIntegrityChecker()
            .Check(Path.Combine(_folder, "нет-такого.flac"), CancellationToken.None);

        Assert.Equal(ContainerVerdict.Unreadable, result.Verdict);
    }
}
