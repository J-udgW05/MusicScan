using System.Buffers.Binary;

namespace MusicScanIntegrity.Core.Integrity;

/// <summary>
/// Проверка Monkey's Audio (APE) по описанию файла в его заголовке.
/// </summary>
/// <remarks>
/// Начиная с версии 3.98 файл открывается описанием, где перечислены длины всех
/// его частей: заголовка, таблицы перемоток, служебных данных, сжатого звука и
/// хвоста. Сумма этих длин должна совпасть с размером файла — на этом и
/// попадается обрыв. Контрольная сумма MD5 в описании считается по
/// распакованному звуку, поэтому без распаковщика её не сверить.
/// </remarks>
internal sealed class ApeValidator : IContainerValidator
{
    /// <summary>Размер описания файла для версий 3.98 и новее.</summary>
    private const int DescriptorSize = 52;

    /// <summary>Версия, начиная с которой у файла есть описание.</summary>
    private const int DescriptorVersion = 3980;

    /// <inheritdoc />
    public string Format => "APE";

    /// <inheritdoc />
    public bool Matches(ReadOnlySpan<byte> header) => header.StartsWith("MAC "u8);

    /// <inheritdoc />
    public ContainerValidation Validate(Stream stream, ContainerBounds bounds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        cancellationToken.ThrowIfCancellationRequested();

        long audioEnd = bounds.AudioEnd;
        stream.Position = 0;
        StreamWindow window = new(stream, 1024);

        if (!window.Skip(bounds.AudioStart) || !window.Ensure(DescriptorSize))
        {
            return ContainerValidation.Damaged(
                Format,
                "Файл слишком короткий: в нём нет даже описания.",
                $"Размер {bounds.FileLength} Б",
                truncated: true);
        }

        ReadOnlySpan<byte> descriptor = window.Peek(DescriptorSize);

        if (!descriptor.StartsWith("MAC "u8))
        {
            return ContainerValidation.Damaged(
                Format,
                "Файл не начинается подписью Monkey's Audio — заголовок разрушен.",
                "Первые байты не равны «MAC »");
        }

        int version = BinaryPrimitives.ReadUInt16LittleEndian(descriptor[4..6]);

        if (version < DescriptorVersion)
        {
            // У старых версий описания нет, и складывать нечего.
            return ContainerValidation.StructureOnly(
                Format,
                1,
                $"Версия {version / 1000.0:0.00}: у старого формата нет описания длин, проверена только подпись");
        }

        long descriptorBytes = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[8..12]);
        long headerBytes = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[12..16]);
        long seekTableBytes = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[16..20]);
        long wavHeaderBytes = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[20..24]);
        long audioBytes = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[24..28]);
        long audioBytesHigh = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[28..32]);
        long terminatingBytes = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[32..36]);

        long declared = descriptorBytes
            + headerBytes
            + seekTableBytes
            + wavHeaderBytes
            + (audioBytes | (audioBytesHigh << 32))
            + terminatingBytes;

        if (declared > audioEnd)
        {
            return ContainerValidation.Damaged(
                Format,
                "Файл обрывается: данных меньше, чем обещано его описанием.",
                $"Обещано {declared} Б, на диске {audioEnd} Б без тегов",
                1,
                truncated: true);
        }

        if (audioBytes == 0 && audioBytesHigh == 0)
        {
            return ContainerValidation.Damaged(
                Format,
                "В файле нет сжатого звука — только заголовок.",
                "Длина звуковых данных равна нулю",
                1,
                truncated: true);
        }

        return ContainerValidation.StructureOnly(
            Format,
            1,
            $"Версия {version / 1000.0:0.00}; MD5 в описании считается по распакованному звуку и здесь не сверяется");
    }
}
