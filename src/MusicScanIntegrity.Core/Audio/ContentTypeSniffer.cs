namespace MusicScanIntegrity.Core.Audio;

/// <summary>
/// Определяет реальный формат файла по сигнатуре в его начале.
/// Нужно для замечания «расширение не соответствует содержимому»
/// (03_IMPLEMENTATION_GUIDE.md, раздел 1).
/// </summary>
public static class ContentTypeSniffer
{
    /// <summary>Сколько байт достаточно прочитать, чтобы узнать формат.</summary>
    public const int HeaderSize = 64;

    /// <summary>
    /// Формат по сигнатуре: «FLAC», «MP3»… <see langword="null"/>, если
    /// сигнатура неизвестна — это не повод жаловаться пользователю.
    /// </summary>
    public static string? Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length < 4)
        {
            return null;
        }

        if (Starts(header, "fLaC"u8)) return "FLAC";
        if (Starts(header, "OggS"u8)) return DetectOgg(header);
        if (Starts(header, "MAC "u8)) return "APE";
        if (Starts(header, "wvpk"u8)) return "WV";
        if (Starts(header, "MThd"u8)) return "MIDI";
        if (Starts(header, "DSD "u8)) return "DSF";
        if (Starts(header, "FRM8"u8)) return "DFF";
        if (Starts(header, "MPCK"u8) || Starts(header, "MP+"u8)) return "MPC";
        if (Starts(header, "TTA1"u8)) return "TTA";
        if (Starts(header, "ADIF"u8)) return "AAC";

        // RIFF....WAVE
        if (Starts(header, "RIFF"u8) && header.Length >= 12 && header[8..12].SequenceEqual("WAVE"u8))
        {
            return "WAV";
        }

        // FORM....AIFF / AIFC
        if (Starts(header, "FORM"u8) && header.Length >= 12)
        {
            if (header[8..12].SequenceEqual("AIFF"u8) || header[8..12].SequenceEqual("AIFC"u8))
            {
                return "AIFF";
            }
        }

        // ISO Base Media (MP4/M4A/ALAC): ....ftyp
        if (header.Length >= 12 && header[4..8].SequenceEqual("ftyp"u8))
        {
            return DetectMp4Brand(header[8..12]);
        }

        // ASF/WMA GUID 30 26 B2 75 8E 66 CF 11
        if (header.Length >= 8 &&
            header[0] == 0x30 && header[1] == 0x26 && header[2] == 0xB2 && header[3] == 0x75 &&
            header[4] == 0x8E && header[5] == 0x66 && header[6] == 0xCF && header[7] == 0x11)
        {
            return "WMA";
        }

        // ID3-тег или прямой синхромаркер кадра MPEG.
        if (Starts(header, "ID3"u8))
        {
            return "MP3";
        }

        if (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
        {
            // 0xFFF1/0xFFF9 — ADTS-заголовок AAC, остальное — MPEG Audio.
            return (header[1] & 0x16) == 0x10 ? "AAC" : "MP3";
        }

        // Трекерные форматы: сигнатура лежит не в начале файла.
        if (header.Length >= 48 && header[44..48].SequenceEqual("SCRM"u8)) return "S3M";
        if (header.Length >= 4 && header[..4].SequenceEqual("IMPM"u8)) return "IT";
        if (header.Length >= 17 && header[..17].SequenceEqual("Extended Module: "u8)) return "XM";

        return null;
    }

    /// <summary>Читает начало файла и определяет формат; <see langword="null"/> при ошибке чтения.</summary>
    public static string? DetectFromFile(string path)
    {
        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: HeaderSize,
                FileOptions.SequentialScan);

            Span<byte> header = stackalloc byte[HeaderSize];
            int read = stream.ReadAtLeast(header, HeaderSize, throwOnEndOfStream: false);
            return read == 0 ? null : Detect(header[..read]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Считает, что расширение и содержимое согласованы.
    /// Разные названия одного контейнера (M4A/MP4/ALAC, OGG/OPUS) конфликтом не считаются.
    /// </summary>
    public static bool Matches(string extension, string detectedFormat)
    {
        string ext = extension.TrimStart('.').ToUpperInvariant();
        string detected = detectedFormat.ToUpperInvariant();

        if (ext == detected)
        {
            return true;
        }

        return (ext, detected) switch
        {
            ("MID" or "MIDI", "MIDI") => true,
            ("AIF" or "AIFC" or "AIFF", "AIFF") => true,
            ("M4A" or "MP4" or "ALAC" or "AAC", "MP4" or "ALAC" or "AAC" or "M4A") => true,
            ("OGA" or "OGG", "OGG" or "OPUS" or "FLAC-OGG") => true,
            ("OPUS", "OPUS" or "OGG") => true,
            ("DFF", "DFF") or ("DSF", "DSF") => true,
            ("WAV", "WAV") => true,
            // MP3-файл, начинающийся с ID3-тега, определяется как MP3 — и наоборот.
            ("MP3", "MP1" or "MP2" or "MP3") => true,
            _ => false,
        };
    }

    private static bool Starts(ReadOnlySpan<byte> header, ReadOnlySpan<byte> signature) =>
        header.Length >= signature.Length && header[..signature.Length].SequenceEqual(signature);

    private static string DetectOgg(ReadOnlySpan<byte> header)
    {
        // Кодек указан в первом пакете страницы Ogg, сразу после 28-байтового заголовка.
        if (header.Length >= 35)
        {
            ReadOnlySpan<byte> payload = header[28..];
            if (payload.StartsWith("OpusHead"u8)) return "OPUS";
            if (payload.StartsWith("\x7fFLAC"u8)) return "FLAC";
            if (payload.Length >= 7 && payload[1..7].SequenceEqual("vorbis"u8)) return "OGG";
        }

        return "OGG";
    }

    private static string DetectMp4Brand(ReadOnlySpan<byte> brand)
    {
        if (brand.SequenceEqual("M4A "u8)) return "M4A";
        if (brand.SequenceEqual("M4B "u8)) return "M4A";
        return "MP4";
    }
}
