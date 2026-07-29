using System.Collections.Concurrent;
using System.Text;
using NekoPcbEmulator.Core.Transport;

namespace NekoPcbEmulator.Core.Devices.PcbB;

public sealed record PcbBSnapshot(LedCell[] Cells, long Frames, long Errors, long DiscardedBytes);

/// <summary>
/// PCB-B: a 5x5 addressable LED grid on the far end of a ribbon cable, driven by the framed
/// binary protocol documented in <c>docs/protocol-b.md</c>. Every request is answered, with
/// the request's sequence number echoed back, so a test suite can match replies to requests.
/// </summary>
public sealed class PcbBDevice : PcbDevice
{
    public const byte ProtocolVersion = 1;

    /// <summary>Layout of one LED inside a <see cref="PcbBCommand.State"/> payload.</summary>
    public const int StateBytesPerLed = 7;

    private readonly LedGrid _grid = new();
    private readonly ConcurrentDictionary<string, FrameDecoder> _decoders = new();

    private long _frames;
    private long _errors;

    public PcbBDevice(LogSink log) : base(log) { }

    public override string Id => "PCB-B";

    public override string DisplayName => "PCB-B · Binary";

    public override string ProtocolName => "framed binary";

    /// <summary>LED timeouts change the picture with no traffic involved, so the view must keep painting.</summary>
    public override bool IsAnimating
    {
        get { lock (Gate) return _grid.HasPendingTimeouts(); }
    }

    public override void Reset()
    {
        lock (Gate)
        {
            _grid.ClearAll();
            _frames = 0;
            _errors = 0;
        }
        Touch();
    }

    public PcbBSnapshot Snapshot()
    {
        bool expired;
        LedCell[] cells;
        long frames, errors;

        lock (Gate)
        {
            expired = _grid.ApplyTimeouts() > 0;
            cells = _grid.Snapshot();
            frames = _frames;
            errors = _errors;
        }

        if (expired) Touch();

        long discarded = 0;
        foreach (var decoder in _decoders.Values) discarded += decoder.DiscardedBytes;
        return new PcbBSnapshot(cells, frames, errors, discarded);
    }

    public override void OnConnected(IPortConnection connection) =>
        _decoders[connection.Id] = new FrameDecoder();

    public override void OnDisconnected(IPortConnection connection) =>
        _decoders.TryRemove(connection.Id, out _);

    public override void OnReceived(IPortConnection connection, ReadOnlySpan<byte> data)
    {
        // The decoder is only ever touched by this connection's own read pump.
        var decoder = _decoders.GetOrAdd(connection.Id, static _ => new FrameDecoder());
        decoder.Push(data);

        while (true)
        {
            var status = decoder.TryRead(out var frame, out byte offendingSeq);

            switch (status)
            {
                case FrameDecodeStatus.NeedMoreData:
                    return;

                case FrameDecodeStatus.ChecksumError:
                    Interlocked.Increment(ref _errors);
                    Log.Write(Id, LogLevel.Warn, $"{connection.Id} CRC mismatch (seq {offendingSeq})");
                    Reply(connection, offendingSeq, PcbBCommand.Nak, [0x00, PcbBError.BadChecksum]);
                    break;

                case FrameDecodeStatus.LengthError:
                    Interlocked.Increment(ref _errors);
                    Log.Write(Id, LogLevel.Warn, $"{connection.Id} invalid length byte, resyncing");
                    Reply(connection, 0, PcbBCommand.Nak, [0x00, PcbBError.BadFrame]);
                    break;

                case FrameDecodeStatus.Frame:
                    Interlocked.Increment(ref _frames);
                    Log.Write(Id, LogLevel.Rx,
                        $"{connection.Id} seq={frame.Seq} {PcbBCommand.Name(frame.Cmd)} [{Hex(frame.Data)}]");
                    Dispatch(connection, frame);
                    break;
            }
        }
    }

    private void Dispatch(IPortConnection connection, DecodedFrame frame)
    {
        var data = frame.Data;

        switch (frame.Cmd)
        {
            case PcbBCommand.LedSet:
            {
                if (data.Length != 7) { Nak(connection, frame, PcbBError.BadLength); return; }
                int index = data[0];
                if (!LedGrid.IsValidIndex(index)) { Nak(connection, frame, PcbBError.BadIndex); return; }

                var color = new Rgba(data[1], data[2], data[3], data[4]);
                int duration = (data[5] << 8) | data[6];
                lock (Gate) _grid.Set(index, color, duration);
                Ack(connection, frame);
                break;
            }

            case PcbBCommand.LedClear:
            {
                if (data.Length != 1) { Nak(connection, frame, PcbBError.BadLength); return; }
                int index = data[0];
                if (!LedGrid.IsValidIndex(index)) { Nak(connection, frame, PcbBError.BadIndex); return; }

                lock (Gate) _grid.Clear(index);
                Ack(connection, frame);
                break;
            }

            case PcbBCommand.ClearAll:
            {
                if (data.Length != 0) { Nak(connection, frame, PcbBError.BadLength); return; }
                lock (Gate) _grid.ClearAll();
                Ack(connection, frame);
                break;
            }

            case PcbBCommand.SetAll:
            {
                if (data.Length != 6) { Nak(connection, frame, PcbBError.BadLength); return; }
                var color = new Rgba(data[0], data[1], data[2], data[3]);
                int duration = (data[4] << 8) | data[5];

                lock (Gate)
                    for (int i = 0; i < LedGrid.Count; i++) _grid.Set(i, color, duration);
                Ack(connection, frame);
                break;
            }

            case PcbBCommand.SetMask:
            {
                if (data.Length != 10) { Nak(connection, frame, PcbBError.BadLength); return; }
                uint mask = (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]);
                var color = new Rgba(data[4], data[5], data[6], data[7]);
                int duration = (data[8] << 8) | data[9];

                // Bit n selects LED n; anything above bit 24 is undefined and rejected.
                if ((mask >> LedGrid.Count) != 0) { Nak(connection, frame, PcbBError.BadParameter); return; }

                lock (Gate)
                {
                    for (int i = 0; i < LedGrid.Count; i++)
                    {
                        if ((mask & (1u << i)) != 0) _grid.Set(i, color, duration);
                        else _grid.Clear(i);
                    }
                }
                Ack(connection, frame);
                break;
            }

            case PcbBCommand.SetBatch:
            {
                if (data.Length < 1) { Nak(connection, frame, PcbBError.BadLength); return; }
                int count = data[0];
                if (data.Length != 1 + count * 7) { Nak(connection, frame, PcbBError.BadLength); return; }

                for (int i = 0; i < count; i++)
                    if (!LedGrid.IsValidIndex(data[1 + i * 7])) { Nak(connection, frame, PcbBError.BadIndex); return; }

                lock (Gate)
                {
                    for (int i = 0; i < count; i++)
                    {
                        int offset = 1 + i * 7;
                        _grid.Set(
                            data[offset],
                            new Rgba(data[offset + 1], data[offset + 2], data[offset + 3], data[offset + 4]),
                            (data[offset + 5] << 8) | data[offset + 6]);
                    }
                }
                Ack(connection, frame);
                break;
            }

            case PcbBCommand.Ping:
            {
                if (data.Length != 0) { Nak(connection, frame, PcbBError.BadLength); return; }
                Reply(connection, frame.Seq, PcbBCommand.Pong, []);
                return; // No visual change.
            }

            case PcbBCommand.GetState:
            {
                if (data.Length != 0) { Nak(connection, frame, PcbBError.BadLength); return; }
                Reply(connection, frame.Seq, PcbBCommand.State, BuildStatePayload());
                return;
            }

            case PcbBCommand.GetInfo:
            {
                if (data.Length != 0) { Nak(connection, frame, PcbBError.BadLength); return; }

                var payload = new List<byte> { ProtocolVersion, LedGrid.Rows, LedGrid.Columns };
                payload.AddRange(Encoding.ASCII.GetBytes(Id));
                Reply(connection, frame.Seq, PcbBCommand.Info, [.. payload]);
                return;
            }

            default:
                Nak(connection, frame, PcbBError.UnknownCommand);
                return;
        }

        Touch();
    }

    private byte[] BuildStatePayload()
    {
        LedCell[] cells;
        lock (Gate)
        {
            _grid.ApplyTimeouts();
            cells = _grid.Snapshot();
        }

        // Seven bytes per LED: on, r, g, b, a, remaining_ms (big-endian u16).
        var payload = new byte[LedGrid.Count * StateBytesPerLed];
        for (int i = 0; i < cells.Length; i++)
        {
            var cell = cells[i];
            int remaining = Math.Min(cell.RemainingMs, ushort.MaxValue);
            int offset = i * StateBytesPerLed;

            payload[offset] = (byte)(cell.On ? 1 : 0);
            payload[offset + 1] = cell.Color.R;
            payload[offset + 2] = cell.Color.G;
            payload[offset + 3] = cell.Color.B;
            payload[offset + 4] = cell.Color.A;
            payload[offset + 5] = (byte)(remaining >> 8);
            payload[offset + 6] = (byte)remaining;
        }
        return payload;
    }

    private void Ack(IPortConnection connection, DecodedFrame frame) =>
        Reply(connection, frame.Seq, PcbBCommand.Ack, [frame.Cmd]);

    private void Nak(IPortConnection connection, DecodedFrame frame, byte error)
    {
        Interlocked.Increment(ref _errors);
        Log.Write(Id, LogLevel.Warn,
            $"{connection.Id} {PcbBCommand.Name(frame.Cmd)} rejected: {PcbBError.Name(error)}");
        Reply(connection, frame.Seq, PcbBCommand.Nak, [frame.Cmd, error]);
    }

    private void Reply(IPortConnection connection, byte seq, byte command, byte[] payload)
    {
        var frame = BinaryFrame.Encode(seq, command, payload);
        Log.Write(Id, LogLevel.Tx, $"{connection.Id} seq={seq} {PcbBCommand.Name(command)} [{Hex(payload)}]");
        connection.Send(frame);
    }

    private static string Hex(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return "";
        const int limit = 16;

        var sb = new StringBuilder(data.Length * 3);
        for (int i = 0; i < Math.Min(data.Length, limit); i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(data[i].ToString("X2"));
        }
        if (data.Length > limit) sb.Append($" .. +{data.Length - limit}");
        return sb.ToString();
    }
}
