using System.Text;

namespace NekoPcbEmulator.ProtocolTests;

internal sealed partial class ProtocolCases
{
    private byte _sequence;

    public void RunB()
    {
        Case("B", "Independent CRC implementation matches the standard check value and documented PING", () =>
        {
            Equal(WireClient.Crc(Encoding.ASCII.GetBytes("123456789")), (ushort)0x29B1);
            Require(WireClient.Frame(1, 0x10, []).AsSpan().SequenceEqual(Convert.FromHexString("A55A02011083FC")), "Documented PING bytes differ");
            b.Send(Convert.FromHexString("A55A02011083FC"));
            var response = b.ReadBinary();
            Equal(response.Sequence, (byte)1);
            Equal(response.Command, (byte)0x90);
            Equal(response.Payload.Length, 0);
        });
        Case("B", "GET_INFO returns version, dimensions, and board identity", () =>
        {
            var response = B(0x12, []);
            Equal(response.Command, (byte)0x92);
            Require(response.Payload.AsSpan(0, 3).SequenceEqual(new byte[] { 1, 5, 5 }), "Wrong protocol version or dimensions");
            Equal(Encoding.ASCII.GetString(response.Payload[3..]), "PCB-B");
        });
        Case("B", "CLEAR_ALL and GET_STATE expose 25 cleared seven-byte cells", () =>
        {
            Ack(0x03, []);
            Require(State().All(value => value == 0), "CLEAR_ALL left state behind");
        });
        Case("B", "Documented LED_SET example returns ACK and the expected timed RGBA cell", () =>
        {
            Ack(0x01, [12, 0x20, 0x60, 0xFF, 0xFF, 0x07, 0xD0]);
            var state = State();
            int offset = 12 * 7;
            Require(state.AsSpan(offset, 5).SequenceEqual(new byte[] { 1, 0x20, 0x60, 0xFF, 0xFF }), "Wrong LED color or switch");
            int remaining = (state[offset + 5] << 8) | state[offset + 6];
            Require(remaining is > 0 and <= 2000, "Duration is not a big-endian 2000-ms countdown");
        });
        Case("B", "LED_CLEAR clears just its indexed cell", () =>
        {
            Ack(0x01, [0, 255, 0, 0, 255, 0, 0]);
            Ack(0x02, [12]);
            var state = State();
            Require(state.AsSpan(12 * 7, 7).ToArray().All(value => value == 0) && state[0] == 1, "LED_CLEAR affected the wrong cells");
        });
        Case("B", "SET_ALL applies RGBA to all cells with zero duration held indefinitely", () =>
        {
            Ack(0x04, [1, 2, 3, 128, 0, 0]);
            var state = State();
            for (int index = 0; index < 25; index++)
                Require(state.AsSpan(index * 7, 7).SequenceEqual(new byte[] { 1, 1, 2, 3, 128, 0, 0 }), "SET_ALL cell mismatch at " + index);
        });
        Case("B", "SET_MASK uses big-endian bits 0, 12, and 24 and clears unselected cells", () =>
        {
            Ack(0x05, [0x01, 0, 0x10, 0x01, 0, 255, 0, 255, 0, 0]);
            var state = State();
            for (int index = 0; index < 25; index++)
                Equal(state[index * 7], (byte)(index is 0 or 12 or 24 ? 1 : 0));
        });
        Case("B", "SET_MASK rejects bits above 24 without changing state", () =>
        {
            byte[] before = State();
            Nak(0x05, [0x02, 0, 0, 0, 255, 0, 0, 255, 0, 0], 0x04);
            Require(State().AsSpan().SequenceEqual(before), "Invalid mask changed state");
        });
        Case("B", "SET_BATCH applies all entries and validates the whole batch atomically", () =>
        {
            Ack(0x03, []);
            Ack(0x06, [2, 1, 255, 0, 0, 255, 0, 0, 24, 0, 0, 255, 255, 0, 0]);
            byte[] before = State();
            Require(before[7] == 1 && before[24 * 7] == 1 && before.Where((_, index) => index % 7 == 0).Count(value => value == 1) == 2,
                "Batch did not set exactly two LEDs");
            Nak(0x06, [2, 4, 1, 2, 3, 255, 0, 0, 99, 1, 2, 3, 255, 0, 0], 0x03);
            Require(State().AsSpan().SequenceEqual(before), "A rejected batch partially applied its valid entry");
            Ack(0x06, [0]);
            Require(State().AsSpan().SequenceEqual(before), "An empty batch changed state");
        });
        Case("B", "Maximum 253-byte payload carries a 36-entry SET_BATCH", () =>
        {
            var payload = new List<byte> { 36 };
            for (int index = 0; index < 36; index++) payload.AddRange([(byte)(index % 25), (byte)index, 16, 32, 255, 0, 0]);
            Equal(payload.Count, 253);
            Ack(0x06, [.. payload]);
            var state = State();
            for (int index = 0; index < 25; index++) Equal(state[index * 7], (byte)1);
            Equal(state[1], (byte)25);
        });
        Case("B", "Timed LEDs expire while the maximum uint16 duration remains representable", () =>
        {
            Ack(0x03, []);
            Ack(0x01, [0, 255, 255, 255, 255, 0x01, 0x90]);
            var active = State();
            int remaining = (active[5] << 8) | active[6];
            Require(active[0] == 1 && remaining is > 0 and <= 400, "Timed LED did not start");
            Thread.Sleep(500);
            Require(State().AsSpan(0, 7).ToArray().All(value => value == 0), "Timed LED did not expire");
            Ack(0x01, [24, 255, 255, 255, 255, 255, 255]);
            var longTimer = State();
            int offset = 24 * 7;
            int longRemaining = (longTimer[offset + 5] << 8) | longTimer[offset + 6];
            Require(longRemaining is > 64000 and <= 65535, "Maximum duration was truncated or read in the wrong byte order");
        });
        Case("B", "Every documented opcode rejects the wrong payload length", () =>
        {
            foreach (var (command, payload) in new (byte, byte[])[]
            {
                (0x01, new byte[6]), (0x02, []), (0x03, [0]), (0x04, new byte[5]),
                (0x05, new byte[9]), (0x06, [2, 1, 255, 0, 0, 255, 0, 0]),
                (0x10, [0]), (0x11, [0]), (0x12, [0]),
            }) Nak(command, payload, 0x01);
        });
        Case("B", "Invalid LED indices and unknown opcodes return the documented NAK codes", () =>
        {
            Nak(0x01, [25, 255, 255, 255, 255, 0, 0], 0x03);
            Nak(0x02, [255], 0x03);
            Nak(0xFF, new byte[253], 0x02);
            b.Send(Convert.FromHexString("A55A09030163FFFFFFFF00004FE8"));
            var response = b.ReadBinary();
            Equal(response.Sequence, (byte)3);
            Require(response.Command == 0x81 && response.Payload.AsSpan().SequenceEqual(new byte[] { 0x01, 0x03 }), "Documented invalid-index example failed");
        });
        Case("B", "Sequence values 0 and 255 are echoed exactly", () =>
        {
            foreach (byte sequence in new byte[] { 0, 255 })
            {
                b.Send(WireClient.Frame(sequence, 0x10, []));
                var reply = b.ReadBinary();
                Equal(reply.Sequence, sequence);
                Equal(reply.Command, (byte)0x90);
            }
        });
        Case("B", "Fragmented sync words and frames wait until the CRC is complete", () =>
        {
            byte[] frame = WireClient.Frame(79, 0x10, []);
            b.Send(frame.AsSpan(0, 1));
            Thread.Sleep(40);
            Require(!b.HasData, "Partial sync produced a response");
            b.Send(frame.AsSpan(1, frame.Length - 2));
            Thread.Sleep(40);
            Require(!b.HasData, "Incomplete CRC produced a response");
            b.Send(frame.AsSpan(frame.Length - 1));
            var reply = b.ReadBinary();
            Equal(reply.Sequence, (byte)79);
            Equal(reply.Command, (byte)0x90);
        });
        Case("B", "Three coalesced requests produce three ordered correlated responses", () =>
        {
            b.Send([.. WireClient.Frame(80, 0x10, []), .. WireClient.Frame(81, 0x10, []), .. WireClient.Frame(82, 0x10, [])]);
            for (byte sequence = 80; sequence <= 82; sequence++)
            {
                var reply = b.ReadBinary();
                Equal(reply.Sequence, sequence);
                Equal(reply.Command, (byte)0x90);
            }
        });
        Case("B", "Noise is discarded before a valid frame", () =>
        {
            b.Send([0, 0x11, 0xA5, 0x01, 0x55, .. WireClient.Frame(83, 0x10, [])]);
            var reply = b.ReadBinary();
            Equal(reply.Sequence, (byte)83);
            Equal(reply.Command, (byte)0x90);
        });
        Case("B", "Bad CRC produces BAD_CHECKSUM and resynchronizes to the next valid frame", () =>
        {
            byte[] corrupt = WireClient.Frame(84, 0x10, []);
            corrupt[^1] ^= 1;
            b.Send([.. corrupt, .. WireClient.Frame(85, 0x10, [])]);
            var error = b.ReadBinary();
            Require(error.Command == 0x81 && error.Payload.AsSpan().SequenceEqual(new byte[] { 0, 0x05 }), "Missing BAD_CHECKSUM NAK");
            var reply = b.ReadBinary();
            Equal(reply.Sequence, (byte)85);
            Equal(reply.Command, (byte)0x90);
        });
        Case("B", "Impossible LEN values produce BAD_FRAME and recover", () =>
        {
            foreach (byte length in new byte[] { 0, 1 })
            {
                b.Send([0xA5, 0x5A, length, .. WireClient.Frame(86, 0x10, [])]);
                var error = b.ReadBinary();
                Require(error.Command == 0x81 && error.Payload.AsSpan().SequenceEqual(new byte[] { 0, 0x06 }), "Missing BAD_FRAME NAK");
                var reply = b.ReadBinary();
                Equal(reply.Sequence, (byte)86);
                Equal(reply.Command, (byte)0x90);
            }
        });
        Case("B", "No unsolicited or duplicate responses remain", () =>
        {
            Thread.Sleep(40);
            Require(!b.HasData, "Extra response bytes remain");
            Ack(0x03, []);
        });
    }

    private Reply B(byte command, byte[] payload)
    {
        _sequence = unchecked((byte)(_sequence + 1));
        b.Send(WireClient.Frame(_sequence, command, payload));
        var reply = b.ReadBinary();
        Equal(reply.Sequence, _sequence);
        return reply;
    }

    private void Ack(byte command, byte[] payload)
    {
        var reply = B(command, payload);
        Require(reply.Command == 0x80 && reply.Payload.AsSpan().SequenceEqual(new[] { command }), "Expected ACK of opcode " + command);
    }

    private void Nak(byte command, byte[] payload, byte error)
    {
        var reply = B(command, payload);
        Require(reply.Command == 0x81 && reply.Payload.AsSpan().SequenceEqual(new[] { command, error }), $"Expected NAK {command:X2}/{error:X2}");
    }

    private byte[] State()
    {
        var reply = B(0x11, []);
        Equal(reply.Command, (byte)0x91);
        Equal(reply.Payload.Length, 175);
        return reply.Payload;
    }
}
