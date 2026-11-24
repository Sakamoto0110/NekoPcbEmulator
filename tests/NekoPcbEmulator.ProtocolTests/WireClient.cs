using System.IO.Ports;
using System.Net.Sockets;
using System.Text;

namespace NekoPcbEmulator.ProtocolTests;

/// <summary>Independent wire client: deliberately does not use the production frame codec.</summary>
internal sealed class WireClient(Stream stream, IDisposable owner, Func<bool> hasData) : IDisposable
{
    public static WireClient Serial(string name)
    {
        var port = new SerialPort(name, 115200, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None, DtrEnable = true, RtsEnable = true,
            ReadTimeout = 3000, WriteTimeout = 3000,
        };
        port.Open();
        return new WireClient(port.BaseStream, port, () => port.BytesToRead > 0);
    }

    public static WireClient Tcp(int port)
    {
        var client = new TcpClient("127.0.0.1", port) { NoDelay = true };
        var stream = client.GetStream();
        stream.ReadTimeout = stream.WriteTimeout = 3000;
        return new WireClient(stream, client, () => stream.DataAvailable);
    }

    public bool HasData => hasData();

    public void Send(ReadOnlySpan<byte> bytes)
    {
        stream.Write(bytes);
        stream.Flush();
    }

    public void SendAscii(string text) => Send(Encoding.Latin1.GetBytes(text));

    public string ReadAscii()
    {
        var bytes = new List<byte>();
        int next;
        while ((next = stream.ReadByte()) != ';')
        {
            if (next < 0) throw new EndOfStreamException();
            if (next is '\r' or '\n' || bytes.Count > 512)
                throw new InvalidDataException("Response payload contains a line break or exceeds its limit");
            bytes.Add((byte)next);
        }
        if (stream.ReadByte() != '\r' || stream.ReadByte() != '\n')
            throw new InvalidDataException("ASCII response must end with ;\\r\\n");
        return Encoding.Latin1.GetString([.. bytes]);
    }

    public Reply ReadBinary()
    {
        var header = new byte[3];
        stream.ReadExactly(header);
        if (header[0] != 0xA5 || header[1] != 0x5A || header[2] < 2)
            throw new InvalidDataException("Invalid sync word or length in response");
        var bodyAndCrc = new byte[header[2] + 2];
        stream.ReadExactly(bodyAndCrc);
        byte[] check = [header[2], .. bodyAndCrc.AsSpan(0, header[2])];
        ushort received = (ushort)((bodyAndCrc[^2] << 8) | bodyAndCrc[^1]);
        if (Crc(check) != received) throw new InvalidDataException("Invalid response CRC");
        return new Reply(bodyAndCrc[0], bodyAndCrc[1], bodyAndCrc[2..^2]);
    }

    public static byte[] Frame(byte sequence, byte command, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > 253) throw new ArgumentOutOfRangeException(nameof(payload));
        byte[] checkedBytes = [(byte)(payload.Length + 2), sequence, command, .. payload];
        ushort crc = Crc(checkedBytes);
        return [0xA5, 0x5A, .. checkedBytes, (byte)(crc >> 8), (byte)crc];
    }

    public static ushort Crc(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0xFFFF;
        foreach (byte value in bytes)
        {
            crc ^= (ushort)(value << 8);
            for (int bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return crc;
    }

    public void Drain()
    {
        Thread.Sleep(30);
        while (HasData) stream.ReadByte();
    }

    public void Dispose() => owner.Dispose();
}

internal readonly record struct Reply(byte Sequence, byte Command, byte[] Payload);
