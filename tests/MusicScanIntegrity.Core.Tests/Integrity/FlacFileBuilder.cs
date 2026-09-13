using MusicScanIntegrity.Core.Integrity;

namespace MusicScanIntegrity.Core.Tests.Integrity;

/// <summary>
/// Собирает файлы FLAC побайтно: правильные и намеренно испорченные.
/// </summary>
/// <remarks>
/// Разборщику всё равно, что лежит внутри кадра: он проверяет заголовок, границу
/// и контрольную сумму. Поэтому вместо настоящего сжатого звука в кадры кладётся
/// заполнитель — это позволяет проверять разборщик без готовых файлов в репозитории
/// и без кодировщика.
/// </remarks>
internal static class FlacFileBuilder
{
    /// <summary>Размер блока в отсчётах — код 12 в заголовке кадра.</summary>
    public const int BlockSize = 4096;

    /// <summary>Частота дискретизации — код 9 в заголовке кадра.</summary>
    public const int SampleRate = 44100;

    /// <summary>Собирает исправный файл.</summary>
    /// <param name="frames">Сколько кадров положить.</param>
    /// <param name="declaredSamples">Что записать в описание потока; по умолчанию — ровно столько, сколько кадров.</param>
    /// <param name="payloadBytes">Размер заполнителя внутри кадра.</param>
    /// <param name="leadingId3">Добавить тег ID3v2 в начало.</param>
    /// <param name="trailingId3">Добавить тег ID3v1 в конец.</param>
    /// <returns>Содержимое файла.</returns>
    public static byte[] Build(
        int frames = 3,
        long? declaredSamples = null,
        int payloadBytes = 64,
        bool leadingId3 = false,
        bool trailingId3 = false)
    {
        List<byte> file = [];

        if (leadingId3)
        {
            file.AddRange(BuildId3v2(200));
        }

        file.AddRange("fLaC"u8.ToArray());
        file.AddRange(BuildStreamInfo(declaredSamples ?? ((long)frames * BlockSize), payloadBytes));

        for (int i = 0; i < frames; i++)
        {
            file.AddRange(BuildFrame(i, payloadBytes));
        }

        if (trailingId3)
        {
            byte[] tag = new byte[128];
            tag[0] = (byte)'T';
            tag[1] = (byte)'A';
            tag[2] = (byte)'G';
            file.AddRange(tag);
        }

        return [.. file];
    }

    /// <summary>Смещение первого кадра в файле, собранном с теми же настройками.</summary>
    public static int FirstFrameOffset(bool leadingId3 = false) =>
        (leadingId3 ? 210 : 0) + 4 + 4 + 34;

    /// <summary>Длина одного кадра в байтах.</summary>
    public static int FrameLength(int payloadBytes = 64) => 6 + payloadBytes + 2;

    private static byte[] BuildId3v2(int payloadSize)
    {
        byte[] tag = new byte[10 + payloadSize];
        tag[0] = (byte)'I';
        tag[1] = (byte)'D';
        tag[2] = (byte)'3';
        tag[3] = 3;
        tag[4] = 0;
        tag[5] = 0;

        // Размер пишется «синхробезопасно»: по семь значащих бит на байт.
        tag[6] = (byte)((payloadSize >> 21) & 0x7F);
        tag[7] = (byte)((payloadSize >> 14) & 0x7F);
        tag[8] = (byte)((payloadSize >> 7) & 0x7F);
        tag[9] = (byte)(payloadSize & 0x7F);

        return tag;
    }

    private static byte[] BuildStreamInfo(long totalSamples, int payloadBytes)
    {
        byte[] block = new byte[4 + 34];

        // Заголовок блока: последний в цепочке, тип 0, длина 34.
        block[0] = 0x80;
        block[1] = 0;
        block[2] = 0;
        block[3] = 34;

        int frameLength = FrameLength(payloadBytes);

        WriteBigEndian(block, 4, BlockSize, 2);
        WriteBigEndian(block, 6, BlockSize, 2);
        WriteBigEndian(block, 8, frameLength, 3);
        WriteBigEndian(block, 11, frameLength, 3);

        // Двадцать бит частоты, три канала, пять разрядности, тридцать шесть отсчётов.
        ulong packed = ((ulong)SampleRate << 44)
            | ((ulong)(2 - 1) << 41)
            | ((ulong)(16 - 1) << 36)
            | (ulong)totalSamples;

        for (int i = 0; i < 8; i++)
        {
            block[14 + i] = (byte)(packed >> (56 - (8 * i)));
        }

        return block;
    }

    private static byte[] BuildFrame(int number, int payloadBytes)
    {
        List<byte> frame =
        [
            0xFF,
            0xF8,                       // подпись и постоянный размер блока
            (12 << 4) | 9,              // блок 4096 отсчётов, частота 44,1 кГц
            (1 << 4) | (4 << 1),        // два канала, 16 бит
            (byte)number,               // номер кадра, пока помещается в один байт
        ];

        frame.Add(Crc.Flac8(CollectionsMarshalSpan(frame)));

        for (int i = 0; i < payloadBytes; i++)
        {
            // Заполнитель без байта 0xFF: он начинает подпись кадра, и случайная
            // «граница» в середине кадра сделала бы проверку бессмысленной.
            frame.Add((byte)(((i * 7) + number + 1) & 0xFE));
        }

        ushort crc = Crc.Flac16(CollectionsMarshalSpan(frame));
        frame.Add((byte)(crc >> 8));
        frame.Add((byte)(crc & 0xFF));

        return [.. frame];
    }

    private static ReadOnlySpan<byte> CollectionsMarshalSpan(List<byte> list) =>
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);

    private static void WriteBigEndian(byte[] target, int offset, int value, int bytes)
    {
        for (int i = 0; i < bytes; i++)
        {
            target[offset + i] = (byte)(value >> (8 * (bytes - 1 - i)));
        }
    }
}
