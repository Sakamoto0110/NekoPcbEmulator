namespace NekoPcbEmulator.Core.Devices.PcbB;

/// <summary>
/// Framing for PCB-B. Layout (all multi-byte fields big-endian, the usual network order):
/// <code>
///   0  1     2     3     4     5 ..            n   n+1
/// +----+----+-----+-----+-----+---------------+-----+-----+
/// | A5 | 5A | LEN | SEQ | CMD | DATA (LEN-2)  | CRC16     |
/// +----+----+-----+-----+-----+---------------+-----+-----+
/// </code>
/// <c>LEN</c> counts the body (SEQ + CMD + DATA) and is therefore at least 2. The CRC covers
/// the <c>LEN</c> byte plus the body — that is, everything between the sync bytes and the CRC
/// itself — so a corrupted length is caught too. Total frame size is <c>LEN + 5</c>.
/// </summary>
public static class BinaryFrame
{
    public const byte Sof0 = 0xA5;
    public const byte Sof1 = 0x5A;
    public const int Overhead = 5;
    public const int MinBodyLength = 2;
    public const int MaxDataLength = 253;

    public static byte[] Encode(byte seq, byte cmd, ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxDataLength)
            throw new ArgumentOutOfRangeException(nameof(data), $"payload limited to {MaxDataLength} bytes");

        int bodyLength = MinBodyLength + data.Length;
        var frame = new byte[bodyLength + Overhead];

        frame[0] = Sof0;
        frame[1] = Sof1;
        frame[2] = (byte)bodyLength;
        frame[3] = seq;
        frame[4] = cmd;
        data.CopyTo(frame.AsSpan(5));

        ushort crc = Crc16Ccitt.Compute(frame.AsSpan(2, bodyLength + 1));
        frame[^2] = (byte)(crc >> 8);
        frame[^1] = (byte)crc;
        return frame;
    }
}

public enum FrameDecodeStatus
{
    /// <summary>Nothing complete in the buffer yet.</summary>
    NeedMoreData,

    /// <summary>A frame was produced.</summary>
    Frame,

    /// <summary>A well-formed frame failed its CRC. Its SEQ is reported best-effort.</summary>
    ChecksumError,

    /// <summary>The length byte was impossible, so the stream is resynchronising.</summary>
    LengthError,
}

public readonly record struct DecodedFrame(byte Seq, byte Cmd, byte[] Data);

/// <summary>
/// Per-connection stream decoder. Scans for the sync pattern, discards anything before it,
/// and on a CRC failure advances a single byte so a false sync inside noise cannot swallow a
/// real frame that starts just after it.
/// </summary>
public sealed class FrameDecoder
{
    private const int MaxBuffer = 64 * 1024;

    private byte[] _buffer = new byte[1024];
    private int _length;

    /// <summary>Bytes thrown away as noise. A healthy link keeps this at zero.</summary>
    public long DiscardedBytes { get; private set; }

    public void Push(ReadOnlySpan<byte> data)
    {
        if (_length + data.Length > MaxBuffer)
        {
            // Cannot happen with a well-behaved peer: the caller drains after every push and
            // the largest frame is 258 bytes. Treat it as a desynchronised stream.
            DiscardedBytes += _length;
            _length = 0;
        }

        if (_length + data.Length > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + data.Length));

        data.CopyTo(_buffer.AsSpan(_length));
        _length += data.Length;
    }

    public FrameDecodeStatus TryRead(out DecodedFrame frame, out byte offendingSeq)
    {
        frame = default;
        offendingSeq = 0;

        // Hunt for the sync pattern.
        int start = 0;
        while (start + 1 < _length && !(_buffer[start] == BinaryFrame.Sof0 && _buffer[start + 1] == BinaryFrame.Sof1))
            start++;

        if (start + 1 >= _length)
        {
            // Keep a trailing 0xA5: its 0x5A may be in the next chunk.
            int keep = _length > 0 && _buffer[_length - 1] == BinaryFrame.Sof0 ? 1 : 0;
            Discard(_length - keep, noise: true);
            return FrameDecodeStatus.NeedMoreData;
        }

        if (start > 0) Discard(start, noise: true);

        if (_length < 3) return FrameDecodeStatus.NeedMoreData;

        int bodyLength = _buffer[2];
        if (bodyLength < BinaryFrame.MinBodyLength)
        {
            Discard(1, noise: true);
            return FrameDecodeStatus.LengthError;
        }

        int total = bodyLength + BinaryFrame.Overhead;
        if (_length < total) return FrameDecodeStatus.NeedMoreData;

        ushort computed = Crc16Ccitt.Compute(_buffer.AsSpan(2, bodyLength + 1));
        ushort received = (ushort)((_buffer[3 + bodyLength] << 8) | _buffer[4 + bodyLength]);

        if (computed != received)
        {
            offendingSeq = _buffer[3];
            Discard(1, noise: true);
            return FrameDecodeStatus.ChecksumError;
        }

        var data = new byte[bodyLength - BinaryFrame.MinBodyLength];
        _buffer.AsSpan(5, data.Length).CopyTo(data);
        frame = new DecodedFrame(_buffer[3], _buffer[4], data);

        Discard(total, noise: false);
        return FrameDecodeStatus.Frame;
    }

    private void Discard(int count, bool noise)
    {
        if (count <= 0) return;
        if (noise) DiscardedBytes += count;

        _length -= count;
        if (_length > 0) Array.Copy(_buffer, count, _buffer, 0, _length);
    }

    public void Reset()
    {
        _length = 0;
    }
}
