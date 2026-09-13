using MusicScanIntegrity.Core.Integrity;
using Xunit;

namespace MusicScanIntegrity.Core.Tests.Integrity;

public sealed class FlacValidatorTests
{
    private static ContainerValidation Validate(byte[] file)
    {
        using MemoryStream stream = new(file, writable: false);
        return new FlacValidator().Validate(stream, ContainerBounds.Measure(stream, file.Length), CancellationToken.None);
    }

    [Fact]
    public void Исправный_файл_проходит_проверку_сумм()
    {
        byte[] file = FlacFileBuilder.Build(frames: 5);

        ContainerValidation result = Validate(file);

        Assert.Equal(ContainerVerdict.Verified, result.Verdict);
        Assert.Equal(5, result.UnitsChecked);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Теги_в_начале_и_в_конце_не_мешают()
    {
        byte[] file = FlacFileBuilder.Build(frames: 3, leadingId3: true, trailingId3: true);

        ContainerValidation result = Validate(file);

        Assert.Equal(ContainerVerdict.Verified, result.Verdict);
        Assert.Equal(3, result.UnitsChecked);
    }

    [Fact]
    public void Испорченный_байт_внутри_кадра_ловится_по_сумме()
    {
        byte[] file = FlacFileBuilder.Build(frames: 4);

        // Портим середину третьего кадра — сумма по нему перестаёт сходиться.
        int thirdFrame = FlacFileBuilder.FirstFrameOffset() + (2 * FlacFileBuilder.FrameLength());
        file[thirdFrame + 20] ^= 0x40;

        ContainerValidation result = Validate(file);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.Equal(2, result.UnitsChecked);
        Assert.Equal(thirdFrame, result.ErrorOffset);
    }

    [Fact]
    public void Обрыв_внутри_последнего_кадра_называется_обрывом()
    {
        byte[] file = FlacFileBuilder.Build(frames: 4);
        byte[] cut = file[..^20];

        ContainerValidation result = Validate(cut);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
        Assert.Equal(3, result.UnitsChecked);
    }

    [Fact]
    public void Нехватка_кадров_видна_по_заявленному_числу_отсчётов()
    {
        // В описании потока обещано шесть кадров, а лежит четыре: файл обрезали
        // ровно по границе кадра, и суммы оставшихся сходятся.
        byte[] file = FlacFileBuilder.Build(frames: 4, declaredSamples: 6L * FlacFileBuilder.BlockSize);

        ContainerValidation result = Validate(file);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
        Assert.Equal(4, result.UnitsChecked);
        Assert.Contains("обрывается", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Мусор_после_последнего_кадра_замечается()
    {
        byte[] file = [.. FlacFileBuilder.Build(frames: 3), .. new byte[64]];

        ContainerValidation result = Validate(file);

        // Последний кадр «не заканчивается»: разборщик видит, что после него
        // идут данные, которых там быть не должно.
        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.Equal(2, result.UnitsChecked);
    }

    [Fact]
    public void Подмена_подписи_файла_даёт_повреждение()
    {
        byte[] file = FlacFileBuilder.Build();
        file[0] = (byte)'x';

        ContainerValidation result = Validate(file);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.Equal(0, result.UnitsChecked);
    }

    [Fact]
    public void Файл_без_единого_кадра_считается_обрывом()
    {
        byte[] file = FlacFileBuilder.Build(frames: 0, declaredSamples: 0);

        ContainerValidation result = Validate(file);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Файл_опознаётся_по_подписи()
    {
        FlacValidator validator = new();

        Assert.True(validator.Matches("fLaC"u8));
        Assert.False(validator.Matches("OggS"u8));
    }
}
