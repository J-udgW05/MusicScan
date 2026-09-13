using System.Buffers.Binary;

namespace MusicScanIntegrity.Core.Integrity;

/// <summary>
/// Проверка контейнера RIFF (WAV и AIFF) по цепочке чанков.
/// </summary>
/// <remarks>
/// В WAV нет контрольных сумм: это несжатые отсчёты, записанные подряд. Зато
/// длины объявлены дважды — в заголовке файла и в заголовке чанка с данными.
/// Именно они и ловят самую частую беду несжатого звука: файл, скопированный
/// не до конца. Заявленная длина осталась прежней, а данных меньше.
/// </remarks>
internal sealed class RiffValidator : IContainerValidator
{
    /// <inheritdoc />
    public string Format => "WAV";

    /// <inheritdoc />
    public bool Matches(ReadOnlySpan<byte> header) =>
        header.Length >= 12
        && (header.StartsWith("RIFF"u8) || header.StartsWith("FORM"u8));

    /// <inheritdoc />
    public ContainerValidation Validate(Stream stream, ContainerBounds bounds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        stream.Position = 0;
        StreamWindow window = new(stream, 4096);

        if (!window.Skip(bounds.AudioStart) || !window.Ensure(12))
        {
            return ContainerValidation.Damaged(
                Format,
                "Файл слишком короткий: в нём нет даже заголовка.",
                $"Размер {bounds.FileLength} Б",
                truncated: true);
        }

        long riffStart = window.Position;
        ReadOnlySpan<byte> header = window.Peek(12);
        bool isRiff = header.StartsWith("RIFF"u8);
        string format = isRiff ? "WAV" : "AIFF";

        long declared = isRiff
            ? BinaryPrimitives.ReadUInt32LittleEndian(header[4..8])
            : BinaryPrimitives.ReadUInt32BigEndian(header[4..8]);

        // Ноль и «все единицы» в поле длины означают «длина не известна»: так
        // пишут заголовок те, кто записывает звук в поток и не может вернуться
        // назад, чтобы проставить размер. Это не обрыв, и объявлять такой файл
        // повреждённым нельзя — цепочка частей проверится и без объявленной длины.
        bool declaredKnown = declared is not (0 or uint.MaxValue);

        // Объявленная длина считается от девятого байта, поэтому к ней прибавляем восемь.
        long declaredEnd = riffStart + declared + 8;

        if (declaredKnown && declaredEnd > bounds.FileLength)
        {
            return ContainerValidation.Damaged(
                format,
                "Файл обрывается: он короче, чем объявлено в его же заголовке.",
                $"Объявлено {declaredEnd} Б, на диске {bounds.FileLength} Б",
                truncated: true);
        }

        window.Advance(12);

        long limit = declaredKnown ? Math.Min(bounds.FileLength, declaredEnd) : bounds.FileLength;
        int chunks = 0;
        bool sawFormat = false;
        bool sawData = false;

        while (window.Position + 8 <= limit)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long chunkPosition = window.Position;

            if (!window.Ensure(8))
            {
                break;
            }

            ReadOnlySpan<byte> chunk = window.Peek(8);
            string id = System.Text.Encoding.ASCII.GetString(chunk[..4]);
            long size = isRiff
                ? BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..8])
                : BinaryPrimitives.ReadUInt32BigEndian(chunk[4..8]);

            sawFormat |= id is "fmt " or "COMM";
            bool isData = id is "data" or "SSND";
            sawData |= isData;

            if (chunkPosition + 8 + size > bounds.FileLength)
            {
                return ContainerValidation.Damaged(
                    format,
                    isData
                        ? "Файл обрывается: звуковых данных меньше, чем обещано заголовком."
                        : $"Файл обрывается внутри части «{id}».",
                    $"Смещение {chunkPosition} Б, обещано {size} Б, осталось {bounds.FileLength - chunkPosition - 8} Б",
                    chunks,
                    chunkPosition,
                    truncated: true);
            }

            // Чанки выравниваются по чётной границе.
            long step = 8 + size + (size % 2);
            if (!window.Skip(step))
            {
                break;
            }

            chunks++;
        }

        if (!sawFormat)
        {
            return ContainerValidation.Damaged(
                format,
                "В файле нет описания формата звука — заголовок разрушен.",
                "Часть «fmt » не найдена",
                chunks);
        }

        if (!sawData)
        {
            return ContainerValidation.Damaged(
                format,
                "В файле нет самих звуковых данных.",
                "Часть «data» не найдена",
                chunks,
                truncated: true);
        }

        return ContainerValidation.StructureOnly(
            format,
            chunks,
            $"Частей {chunks}; контрольных сумм в этом формате нет — проверены объявленные длины");
    }
}
