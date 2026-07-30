using System.Globalization;
using NekoPcbEmulator.Core.Devices.PcbB;

namespace NekoPcbEmulator.App.Interaction;

/// <summary>
/// The PCB-B command catalogue. Frames are built with the same <see cref="BinaryFrame"/>
/// encoder the test client uses, so the CRC and framing are produced by the shipping code
/// rather than by a UI-only shortcut.
/// </summary>
public static class PcbBCommands
{
    private static byte _sequence;

    private static byte NextSequence() => _sequence++;

    /// <summary>Commands for a single LED in the matrix.</summary>
    public static IReadOnlyList<CommandSpec> Led(int index) =>
    [
        new CommandSpec(
            "LED_SET — colour and on-time",
            $"0x01 on index {index} (row {index / LedGrid.Columns}, column {index % LedGrid.Columns})",
            [
                new CommandField("RGBA", FieldKind.Rgba, "#40FF60FF", Hint: "alpha is brightness"),
                new CommandField("Duration (ms)", FieldKind.Integer, "2000", 0, ushort.MaxValue,
                    "0 holds the LED on until told otherwise"),
            ],
            v => BinaryFrame.Encode(NextSequence(), PcbBCommand.LedSet, LedSetPayload(index, v)),
            v => Describe(PcbBCommand.LedSet, LedSetPayload(index, v))),

        new CommandSpec(
            "LED_CLEAR — turn this LED off",
            $"0x02 on index {index}",
            [],
            _ => BinaryFrame.Encode(NextSequence(), PcbBCommand.LedClear, [(byte)index]),
            _ => Describe(PcbBCommand.LedClear, [(byte)index])),
    ];

    private static byte[] LedSetPayload(int index, IReadOnlyList<string> v)
    {
        var (r, g, b, a) = Rgba(v[0]);
        int ms = Clamp(v[1], 0, ushort.MaxValue);
        return [(byte)index, r, g, b, a, (byte)(ms >> 8), (byte)ms];
    }

    /// <summary>Commands addressed at the whole matrix, reached through the controller.</summary>
    public static IReadOnlyList<CommandSpec> Board() =>
    [
        new CommandSpec(
            "SET_ALL — every LED at once",
            "0x04 — same colour and on-time across all 25",
            [
                new CommandField("RGBA", FieldKind.Rgba, "#3060FFFF"),
                new CommandField("Duration (ms)", FieldKind.Integer, "1500", 0, ushort.MaxValue, "0 holds them on"),
            ],
            v => BinaryFrame.Encode(NextSequence(), PcbBCommand.SetAll, SetAllPayload(v)),
            v => Describe(PcbBCommand.SetAll, SetAllPayload(v))),

        new CommandSpec(
            "SET_MASK — select by bitmask",
            "0x05 — bit n selects LED n; LEDs outside the mask are cleared",
            [
                new CommandField("Mask (hex)", FieldKind.Text, "1041041", Hint: "25 bits, e.g. 1041041 is the diagonal"),
                new CommandField("RGBA", FieldKind.Rgba, "#FF00A0FF"),
                new CommandField("Duration (ms)", FieldKind.Integer, "0", 0, ushort.MaxValue),
            ],
            v => BinaryFrame.Encode(NextSequence(), PcbBCommand.SetMask, SetMaskPayload(v)),
            v => Describe(PcbBCommand.SetMask, SetMaskPayload(v))),

        new CommandSpec(
            "CLEAR_ALL — turn everything off",
            "0x03",
            [],
            _ => BinaryFrame.Encode(NextSequence(), PcbBCommand.ClearAll, []),
            _ => Describe(PcbBCommand.ClearAll, [])),

        new CommandSpec(
            "PING",
            "0x10 — expects a PONG with the same sequence number",
            [],
            _ => BinaryFrame.Encode(NextSequence(), PcbBCommand.Ping, []),
            _ => Describe(PcbBCommand.Ping, [])),

        new CommandSpec(
            "GET_STATE",
            "0x11 — 7 bytes per LED, including remaining on-time",
            [],
            _ => BinaryFrame.Encode(NextSequence(), PcbBCommand.GetState, []),
            _ => Describe(PcbBCommand.GetState, [])),

        new CommandSpec(
            "GET_INFO",
            "0x12 — protocol version and grid geometry",
            [],
            _ => BinaryFrame.Encode(NextSequence(), PcbBCommand.GetInfo, []),
            _ => Describe(PcbBCommand.GetInfo, [])),
    ];

    private static byte[] SetAllPayload(IReadOnlyList<string> v)
    {
        var (r, g, b, a) = Rgba(v[0]);
        int ms = Clamp(v[1], 0, ushort.MaxValue);
        return [r, g, b, a, (byte)(ms >> 8), (byte)ms];
    }

    private static byte[] SetMaskPayload(IReadOnlyList<string> v)
    {
        uint mask = ParseMask(v[0]);
        var (r, g, b, a) = Rgba(v[1]);
        int ms = Clamp(v[2], 0, ushort.MaxValue);

        return
        [
            (byte)(mask >> 24), (byte)(mask >> 16), (byte)(mask >> 8), (byte)mask,
            r, g, b, a,
            (byte)(ms >> 8), (byte)ms,
        ];
    }

    private static uint ParseMask(string value)
    {
        string text = value.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        if (text.StartsWith('#')) text = text[1..];

        return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint parsed)
            ? parsed
            : 0u;
    }

    /// <summary>Renders the frame that will be sent, so the dialog preview is the real bytes.</summary>
    private static string Describe(byte command, byte[] payload)
    {
        // Preview only: peek at the next sequence number without consuming it.
        byte[] frame = BinaryFrame.Encode(_sequence, command, payload);
        return $"{PcbBCommand.Name(command)}  seq={_sequence}\n{string.Join(' ', frame.Select(b => b.ToString("X2")))}";
    }

    private static (byte R, byte G, byte B, byte A) Rgba(string value)
    {
        string text = value.Trim().TrimStart('#');
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        if (text.Length != 8 || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint packed))
            return (0xFF, 0xFF, 0xFF, 0xFF);

        return ((byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
    }

    private static int Clamp(string value, int min, int max) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? Math.Clamp(parsed, min, max)
            : min;
}
