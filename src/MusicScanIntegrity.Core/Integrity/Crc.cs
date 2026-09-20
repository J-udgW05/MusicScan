namespace MusicScanIntegrity.Core.Integrity;

/// <summary>
/// The checksums found inside audio formats.
/// </summary>
/// <remarks>
/// Every format computes its checksum differently — its own polynomial, seed
/// and bit order. Hence not one CRC but exactly the four the validators need:
/// FLAC frame header, FLAC frame, Ogg page and protected MPEG frame. The
/// tables are built once on first use.
/// </remarks>
internal static class Crc
{
    private static readonly byte[] Crc8Table = BuildCrc8(0x07);
    private static readonly ushort[] Crc16Table = BuildCrc16(0x8005);
    private static readonly uint[] Crc32Table = BuildCrc32(0x04C11DB7);

    /// <summary>CRC-8 of a FLAC frame header: polynomial 0x07, seed 0.</summary>
    /// <param name="data">Header bytes excluding the checksum itself.</param>
    public static byte Flac8(ReadOnlySpan<byte> data)
    {
        byte crc = 0;
        foreach (byte value in data)
        {
            crc = Crc8Table[crc ^ value];
        }

        return crc;
    }

    /// <summary>CRC-16 of a FLAC frame: polynomial 0x8005, seed 0.</summary>
    /// <param name="data">The whole frame minus its last two bytes.</param>
    public static ushort Flac16(ReadOnlySpan<byte> data) => Crc16(data, 0);

    /// <summary>
    /// One-byte step of the FLAC CRC-16.
    /// </summary>
    /// <remarks>
    /// Needed when walking frames: a frame boundary is only visible once the
    /// next frame starts, while the checksum covers the whole frame minus two
    /// bytes. Recomputing it for every candidate boundary would pass over the
    /// same bytes many times, so it is accumulated as we go.
    /// </remarks>
    public static ushort Flac16Update(ushort crc, byte value) =>
        (ushort)((crc << 8) ^ Crc16Table[((crc >> 8) & 0xFF) ^ value]);

    /// <summary>
    /// CRC-16 of a protected MPEG frame: same polynomial, seed 0xFFFF.
    /// </summary>
    /// <param name="data">Bytes covered by the checksum: two header bytes plus side info.</param>
    public static ushort Mpeg16(ReadOnlySpan<byte> data) => Crc16(data, 0xFFFF);

    /// <summary>CRC-32 of an Ogg page: polynomial 0x04C11DB7, no bit reflection.</summary>
    /// <param name="data">The whole page with the checksum field zeroed.</param>
    public static uint Ogg32(ReadOnlySpan<byte> data)
    {
        uint crc = 0;
        foreach (byte value in data)
        {
            crc = (crc << 8) ^ Crc32Table[((crc >> 24) & 0xFF) ^ value];
        }

        return crc;
    }

    /// <summary>One-byte step of the Ogg CRC-32.</summary>
    public static uint Ogg32Update(uint crc, byte value) =>
        (crc << 8) ^ Crc32Table[((crc >> 24) & 0xFF) ^ value];

    private static ushort Crc16(ReadOnlySpan<byte> data, ushort seed)
    {
        ushort crc = seed;
        foreach (byte value in data)
        {
            crc = (ushort)((crc << 8) ^ Crc16Table[((crc >> 8) & 0xFF) ^ value]);
        }

        return crc;
    }

    private static byte[] BuildCrc8(byte polynomial)
    {
        byte[] table = new byte[256];

        for (int i = 0; i < 256; i++)
        {
            int value = i;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 0x80) != 0
                    ? ((value << 1) ^ polynomial)
                    : (value << 1);
            }

            table[i] = (byte)value;
        }

        return table;
    }

    private static ushort[] BuildCrc16(ushort polynomial)
    {
        ushort[] table = new ushort[256];

        for (int i = 0; i < 256; i++)
        {
            int value = i << 8;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 0x8000) != 0
                    ? ((value << 1) ^ polynomial)
                    : (value << 1);
            }

            table[i] = (ushort)value;
        }

        return table;
    }

    private static uint[] BuildCrc32(uint polynomial)
    {
        uint[] table = new uint[256];

        for (int i = 0; i < 256; i++)
        {
            uint value = (uint)i << 24;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 0x80000000) != 0
                    ? ((value << 1) ^ polynomial)
                    : (value << 1);
            }

            table[i] = value;
        }

        return table;
    }
}
