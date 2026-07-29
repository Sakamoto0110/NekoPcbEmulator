namespace NekoPcbEmulator.Core.Devices.PcbB;

/// <summary>
/// CRC-16/IBM-3740, commonly labelled "CRC-16/CCITT-FALSE": polynomial 0x1021, initial value
/// 0xFFFF, no input or output reflection, no final XOR. Picked because it is the checksum
/// nearly every embedded framing scheme reaches for, so a test suite can validate it against
/// any off-the-shelf implementation.
/// </summary>
public static class Crc16Ccitt
{
    public const ushort Initial = 0xFFFF;
    private const ushort Polynomial = 0x1021;

    private static readonly ushort[] Table = BuildTable();

    private static ushort[] BuildTable()
    {
        var table = new ushort[256];
        for (int i = 0; i < 256; i++)
        {
            ushort crc = (ushort)(i << 8);
            for (int bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ Polynomial : crc << 1);
            table[i] = crc;
        }
        return table;
    }

    public static ushort Compute(ReadOnlySpan<byte> data, ushort seed = Initial)
    {
        ushort crc = seed;
        foreach (byte b in data)
            crc = (ushort)((crc << 8) ^ Table[(crc >> 8) ^ b]);
        return crc;
    }
}
