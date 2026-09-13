namespace MusicScanIntegrity.Core.Integrity;

/// <summary>
/// Контрольные суммы, которые встречаются внутри аудиоформатов.
/// </summary>
/// <remarks>
/// Каждый формат считает сумму по-своему: свой многочлен, своё начальное
/// значение, свой порядок бит. Поэтому здесь не одна «CRC», а ровно те четыре,
/// что нужны разборщикам: заголовок кадра FLAC, кадр FLAC, страница Ogg и
/// защищённый кадр MPEG. Таблицы считаются один раз при первом обращении.
/// </remarks>
internal static class Crc
{
    private static readonly byte[] Crc8Table = BuildCrc8(0x07);
    private static readonly ushort[] Crc16Table = BuildCrc16(0x8005);
    private static readonly uint[] Crc32Table = BuildCrc32(0x04C11DB7);

    /// <summary>CRC-8 заголовка кадра FLAC: многочлен 0x07, начальное значение 0.</summary>
    /// <param name="data">Байты заголовка без самой суммы.</param>
    /// <returns>Ожидаемое значение суммы.</returns>
    public static byte Flac8(ReadOnlySpan<byte> data)
    {
        byte crc = 0;
        foreach (byte value in data)
        {
            crc = Crc8Table[crc ^ value];
        }

        return crc;
    }

    /// <summary>CRC-16 кадра FLAC: многочлен 0x8005, начальное значение 0.</summary>
    /// <param name="data">Весь кадр без двух последних байт.</param>
    /// <returns>Ожидаемое значение суммы.</returns>
    public static ushort Flac16(ReadOnlySpan<byte> data) => Crc16(data, 0);

    /// <summary>
    /// Шаг CRC-16 FLAC по одному байту.
    /// </summary>
    /// <param name="crc">Значение суммы на предыдущем байте.</param>
    /// <param name="value">Очередной байт.</param>
    /// <returns>Новое значение суммы.</returns>
    /// <remarks>
    /// Нужен для обхода кадров: границу кадра видно только по началу следующего,
    /// а сумма считается по всему кадру без двух последних байт. Пересчитывать её
    /// заново на каждой догадке о границе значило бы гонять по одним и тем же
    /// байтам десяток раз, поэтому сумма набирается на ходу.
    /// </remarks>
    public static ushort Flac16Update(ushort crc, byte value) =>
        (ushort)((crc << 8) ^ Crc16Table[((crc >> 8) & 0xFF) ^ value]);

    /// <summary>
    /// CRC-16 защищённого кадра MPEG: тот же многочлен, но начальное значение 0xFFFF.
    /// </summary>
    /// <param name="data">Байты, покрытые суммой: два байта заголовка и side info.</param>
    /// <returns>Ожидаемое значение суммы.</returns>
    public static ushort Mpeg16(ReadOnlySpan<byte> data) => Crc16(data, 0xFFFF);

    /// <summary>CRC-32 страницы Ogg: многочлен 0x04C11DB7, без отражения бит.</summary>
    /// <param name="data">Страница целиком, с обнулённым полем суммы.</param>
    /// <returns>Ожидаемое значение суммы.</returns>
    public static uint Ogg32(ReadOnlySpan<byte> data)
    {
        uint crc = 0;
        foreach (byte value in data)
        {
            crc = (crc << 8) ^ Crc32Table[((crc >> 24) & 0xFF) ^ value];
        }

        return crc;
    }

    /// <summary>Шаг CRC-32 Ogg по одному байту.</summary>
    /// <param name="crc">Значение суммы на предыдущем байте.</param>
    /// <param name="value">Очередной байт.</param>
    /// <returns>Новое значение суммы.</returns>
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
