using System.Buffers.Binary;

namespace MusicScanIntegrity.Core.Integrity;

/// <summary>
/// Проверка WavPack по цепочке блоков.
/// </summary>
/// <remarks>
/// У каждого блока WavPack есть своя контрольная сумма, но считается она по
/// <em>распакованным</em> отсчётам — сверить её, не написав собственный
/// распаковщик, невозможно. Поэтому здесь проверяется цепочка: каждый блок
/// объявляет длину, и блоки должны лечь ровно до конца файла. Это ловит обрыв
/// и мусор, но слабее настоящей сверки сумм, и вердикт об этом честно
/// сообщает.
/// </remarks>
internal sealed class WavPackValidator : IContainerValidator
{
    /// <summary>Заголовок блока: подпись, длина и служебные поля.</summary>
    private const int BlockHeaderSize = 32;

    /// <inheritdoc />
    public string Format => "WavPack";

    /// <inheritdoc />
    public bool Matches(ReadOnlySpan<byte> header) => header.StartsWith("wvpk"u8);

    /// <inheritdoc />
    public ContainerValidation Validate(Stream stream, ContainerBounds bounds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        long audioEnd = bounds.AudioEnd;
        stream.Position = 0;
        StreamWindow window = new(stream, 8192);

        if (!window.Skip(bounds.AudioStart))
        {
            return ContainerValidation.Damaged(
                Format,
                "Файл обрывается внутри тега в начале.",
                $"Тег занимает {bounds.AudioStart} Б, а файл короче",
                truncated: true);
        }

        int blocks = 0;

        while (window.Position < audioEnd)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long blockPosition = window.Position;
            long remaining = audioEnd - blockPosition;

            if (!window.Ensure(BlockHeaderSize))
            {
                return ContainerValidation.Damaged(
                    Format,
                    "Файл обрывается на заголовке блока.",
                    $"Смещение {blockPosition} Б, осталось {remaining} Б",
                    blocks,
                    blockPosition,
                    truncated: true);
            }

            ReadOnlySpan<byte> header = window.Peek(BlockHeaderSize);

            if (!header.StartsWith("wvpk"u8))
            {
                return ContainerValidation.Damaged(
                    Format,
                    blocks == 0
                        ? "Файл не начинается блоком WavPack — заголовок разрушен."
                        : $"После блока {blocks} идут данные, которые блоком не являются.",
                    $"Смещение {blockPosition} Б, ожидалась подпись «wvpk»",
                    blocks,
                    blockPosition);
            }

            // Длина записана без первых восьми байт самого заголовка.
            long size = BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]) + 8L;

            if (size < BlockHeaderSize)
            {
                return ContainerValidation.Damaged(
                    Format,
                    "Структура файла разрушена: блок объявляет невозможную длину.",
                    $"Смещение {blockPosition} Б, объявлено {size} Б",
                    blocks,
                    blockPosition);
            }

            if (size > remaining)
            {
                return ContainerValidation.Damaged(
                    Format,
                    $"Файл обрывается на блоке {blocks + 1}: он начат, но не дописан.",
                    $"Смещение {blockPosition} Б, обещано {size} Б, осталось {remaining} Б",
                    blocks,
                    blockPosition,
                    truncated: true);
            }

            window.Skip(size);
            blocks++;
        }

        if (blocks == 0)
        {
            return ContainerValidation.Damaged(
                Format,
                "В файле нет ни одного блока WavPack.",
                "Файл пуст или это не WavPack",
                truncated: true);
        }

        return ContainerValidation.StructureOnly(
            Format,
            blocks,
            $"Блоков {blocks}; суммы WavPack считаются по распакованному звуку и здесь не сверяются");
    }
}
