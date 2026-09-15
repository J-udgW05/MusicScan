using System.Buffers.Binary;

namespace MusicScanIntegrity.Core.Integrity;

/// <summary>
/// Validates the MP4 container (M4A, ALAC, AAC) by its box structure.
/// </summary>
/// <remarks>
/// MP4 has no checksums, so something else is verified: the file is a series
/// of boxes, each declaring its length, and those lengths must land exactly at
/// the end. A partial download fails here — the last box promises more data
/// than remains. Missing mandatory boxes mean the start is damaged.
/// </remarks>
internal sealed class Mp4Validator : IContainerValidator
{
    /// <inheritdoc />
    public string Format => "MP4";

    /// <inheritdoc />
    public bool Matches(ReadOnlySpan<byte> header) =>
        header.Length >= 12 && header[4..8].SequenceEqual("ftyp"u8);

    /// <inheritdoc />
    public ContainerValidation Validate(Stream stream, ContainerBounds bounds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        stream.Position = 0;
        StreamWindow window = new(stream, 4096);

        if (!window.Skip(bounds.AudioStart))
        {
            return ContainerValidation.Damaged(
                Format,
                "Файл обрывается внутри тега в начале.",
                $"Тег занимает {bounds.AudioStart} Б, а файл короче",
                truncated: true);
        }

        int boxes = 0;
        bool sawFileType = false;
        bool sawMovie = false;
        bool sawMedia = false;

        while (window.Position < bounds.AudioEnd)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long boxPosition = window.Position;
            long remaining = bounds.AudioEnd - boxPosition;

            if (!window.Ensure(8))
            {
                return ContainerValidation.Damaged(
                    Format,
                    "Файл обрывается на заголовке блока.",
                    $"Смещение {boxPosition} Б, осталось {remaining} Б",
                    boxes,
                    boxPosition,
                    truncated: true);
            }

            ReadOnlySpan<byte> header = window.Peek(8);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            string type = System.Text.Encoding.ASCII.GetString(header[4..8]);
            int headerSize = 8;

            switch (size)
            {
                case 1 when window.Ensure(16):
                    // A length of 1 means the real 64-bit length follows.
                    size = (long)BinaryPrimitives.ReadUInt64BigEndian(window.Peek(16)[8..]);
                    headerSize = 16;
                    break;

                case 0:
                    // Zero means "to the end of the file".
                    size = remaining;
                    break;
            }

            if (size < headerSize)
            {
                return ContainerValidation.Damaged(
                    Format,
                    "Структура файла разрушена: блок объявляет невозможную длину.",
                    $"Блок «{type}» на {boxPosition} Б объявляет {size} Б",
                    boxes,
                    boxPosition);
            }

            if (size > remaining)
            {
                return ContainerValidation.Damaged(
                    Format,
                    $"Файл обрывается: блок «{type}» короче обещанного.",
                    $"Смещение {boxPosition} Б, обещано {size} Б, осталось {remaining} Б",
                    boxes,
                    boxPosition,
                    truncated: true);
            }

            sawFileType |= type == "ftyp";
            sawMovie |= type == "moov";
            sawMedia |= type is "mdat" or "mdia";

            if (!window.Skip(size))
            {
                return ContainerValidation.Damaged(
                    Format,
                    $"Файл обрывается внутри блока «{type}».",
                    $"Смещение {boxPosition} Б",
                    boxes,
                    boxPosition,
                    truncated: true);
            }

            boxes++;
        }

        if (!sawFileType)
        {
            return ContainerValidation.Damaged(
                Format,
                "В файле нет блока с описанием формата — начало разрушено.",
                "Блок «ftyp» не найден",
                boxes);
        }

        if (!sawMovie)
        {
            return ContainerValidation.Damaged(
                Format,
                "В файле нет описания дорожек — без него он не проиграется.",
                "Блок «moov» не найден",
                boxes);
        }

        if (!sawMedia)
        {
            return ContainerValidation.Damaged(
                Format,
                "В файле нет самих аудиоданных.",
                "Блок «mdat» не найден",
                boxes,
                truncated: true);
        }

        return ContainerValidation.StructureOnly(
            Format,
            boxes,
            $"Блоков {boxes}; контрольных сумм в MP4 нет — проверена только структура");
    }
}
