namespace MusicScanIntegrity.Core.Audio;

/// <summary>
/// Identifies the real format from the signature at the start of a file,
/// which is what the extension-mismatch finding is based on.
/// </summary>
public static class ContentTypeSniffer
{
    /// <summary>How many bytes are enough to identify the format.</summary>
    public const int HeaderSize = 64;

    /// <summary>
    /// Format from the signature. <see langword="null"/> for an unknown
    /// signature, which is not something to report to the user.
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

        // An ID3 tag or a bare MPEG frame sync marker.
        if (Starts(header, "ID3"u8))
        {
            return "MP3";
        }

        if (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
        {
            // 0xFFF1/0xFFF9 is an AAC ADTS header; anything else is MPEG Audio.
            return (header[1] & 0x16) == 0x10 ? "AAC" : "MP3";
        }

        // Tracker formats keep their signature away from the start.
        if (header.Length >= 48 && header[44..48].SequenceEqual("SCRM"u8)) return "S3M";
        if (header.Length >= 4 && header[..4].SequenceEqual("IMPM"u8)) return "IT";
        if (header.Length >= 17 && header[..17].SequenceEqual("Extended Module: "u8)) return "XM";

        return null;
    }

    /// <summary>Reads the start of a file and identifies the format; <see langword="null"/> on a read error.</summary>
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
    /// Whether extension and contents agree. Different names for the same
    /// container (M4A/MP4/ALAC, OGG/OPUS) are not a conflict.
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
            // An MP3 starting with an ID3 tag identifies as MP3, and vice versa.
            ("MP3", "MP1" or "MP2" or "MP3") => true,
            _ => false,
        };
    }

    private static bool Starts(ReadOnlySpan<byte> header, ReadOnlySpan<byte> signature) =>
        header.Length >= signature.Length && header[..signature.Length].SequenceEqual(signature);

    private static string DetectOgg(ReadOnlySpan<byte> header)
    {
        // The codec sits in the first packet of the Ogg page, right after the 28-byte header.
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
