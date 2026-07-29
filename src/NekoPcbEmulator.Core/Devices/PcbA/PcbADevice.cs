using System.Collections.Concurrent;
using System.Text;
using NekoPcbEmulator.Core.Transport;

namespace NekoPcbEmulator.Core.Devices.PcbA;

public readonly record struct LedState(Rgba Color, bool On)
{
    /// <summary>What the LED actually emits: the switch gates the stored colour.</summary>
    public Rgba Emitted => On ? Color : Rgba.Off;
}

public sealed record PcbASnapshot(
    LedState[] Lights,
    string[] LcdLines,
    string LcdLoaded,
    string[] LcdSlots,
    int LitPixels,
    long Commands,
    long Errors);

/// <summary>
/// PCB-A: three RGBA indicator LEDs, a 20x4 character LCD with five memory slots, and a
/// 360x120 RGBA pixel panel. Speaks the raw ASCII protocol documented in
/// <c>docs/protocol-a.md</c>: <c>;</c>-terminated statements in, <c>OK</c>/<c>ERR</c> out.
/// </summary>
public sealed class PcbADevice : PcbDevice
{
    public const int LightCount = 3;
    public const string ProtocolVersion = "1.0";

    private static readonly Encoding Wire = Encoding.Latin1;

    private readonly LedState[] _lights = new LedState[LightCount];
    private readonly LcdPanel _lcd = new();
    private readonly PixelPanel _panel = new();
    private readonly ConcurrentDictionary<string, AsciiStatementReader> _readers = new();

    private long _commands;
    private long _errors;

    public PcbADevice(LogSink log) : base(log) { }

    public override string Id => "PCB-A";

    public override string DisplayName => "PCB-A · ASCII";

    public override string ProtocolName => "raw ASCII";

    public override void Reset()
    {
        lock (Gate)
        {
            Array.Clear(_lights);
            _lcd.Clear();
            _panel.Clear(Rgba.Off);
            _commands = 0;
            _errors = 0;
        }
        Touch();
    }

    public PcbASnapshot Snapshot()
    {
        lock (Gate)
        {
            var lcd = _lcd.Snapshot();
            return new PcbASnapshot(
                [.. _lights],
                lcd.Lines,
                lcd.Loaded,
                lcd.Slots,
                _panel.LitPixelCount(),
                _commands,
                _errors);
        }
    }

    /// <summary>
    /// Copies the panel framebuffer as premultiplied BGRA. Kept out of the snapshot so the
    /// renderer can blit straight into a locked bitmap without a 170 KB allocation per frame.
    /// </summary>
    public void CopyPanelTo(Span<uint> destination)
    {
        lock (Gate) _panel.CopyToBgra(destination);
    }

    public override void OnConnected(IPortConnection connection) =>
        _readers[connection.Id] = new AsciiStatementReader();

    public override void OnDisconnected(IPortConnection connection) =>
        _readers.TryRemove(connection.Id, out _);

    public override void OnReceived(IPortConnection connection, ReadOnlySpan<byte> data)
    {
        var reader = _readers.GetOrAdd(connection.Id, static _ => new AsciiStatementReader());

        var statements = new List<string>();
        int overflows = reader.Push(data, statements);

        var response = new StringBuilder();

        for (int i = 0; i < overflows; i++)
        {
            Interlocked.Increment(ref _errors);
            Log.Write(Id, LogLevel.Warn, $"{connection.Id} statement exceeded the length limit");
            Append(response, Error("OVERFLOW", "statement too long"));
        }

        foreach (string statement in statements)
        {
            Log.Write(Id, LogLevel.Rx, $"{connection.Id} {statement};");
            Interlocked.Increment(ref _commands);

            string result;
            try
            {
                result = Execute(statement);
            }
            catch (Exception ex)
            {
                result = Error("INTERNAL", ex.Message);
            }

            if (result.StartsWith("ERR", StringComparison.Ordinal)) Interlocked.Increment(ref _errors);
            Append(response, result);
        }

        if (response.Length == 0) return;

        string text = response.ToString();
        foreach (string line in text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            Log.Write(Id, LogLevel.Tx, $"{connection.Id} {line}");

        connection.Send(Wire.GetBytes(text));
    }

    /// <summary>Every response is one <c>;</c>-terminated line, so a client can frame replies exactly like commands.</summary>
    private static void Append(StringBuilder builder, string response) => builder.Append(response).Append(";\r\n");

    private static string Ok() => "OK";

    private static string Ok(string payload) => "OK " + Sanitize(payload);

    private static string Error(string code, string detail) => $"ERR {code} {Sanitize(detail)}";

    /// <summary>
    /// Responses are framed on <c>;</c>, so a payload must never contain one, and a stray
    /// newline would split a single reply into two lines.
    /// </summary>
    private static string Sanitize(string text)
    {
        if (text.Length > 200) text = text[..200];
        return text.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ');
    }

    private string Execute(string statement)
    {
        if (TryMatchKeyword(statement, "LIGHT", out int rest)) return ExecuteLight(statement, rest);
        if (TryMatchKeyword(statement, "LCD", out rest)) return ExecuteLcd(statement, rest);
        if (TryMatchKeyword(statement, "PANEL", out rest)) return ExecutePanel(statement, rest);
        if (TryMatchKeyword(statement, "SYS", out rest)) return ExecuteSys(statement, rest);

        return Error("UNKNOWN_CMD", statement.Split(' ')[0]);
    }

    // LIGHT[<idx>] <rgba> <state>
    private string ExecuteLight(string statement, int index)
    {
        if (!AsciiSyntax.TryExtractBlock(statement, index, '[', ']', out string inner, out int after))
            return Error("SYNTAX", "expected LIGHT[<index>]");

        if (!AsciiSyntax.TryParseInt32(inner, out int led))
            return Error("BAD_ARGS", $"bad index '{inner}'");

        if (led is < 0 or >= LightCount)
            return Error("RANGE", $"index {led} outside 0..{LightCount - 1}");

        var args = AsciiSyntax.SplitAll(statement[after..]);
        if (args.Count != 2)
            return Error("BAD_ARGS", $"expected <rgba> <state>, got {args.Count} argument(s)");

        if (!AsciiSyntax.TryParseRgba(args[0], out var color))
            return Error("BAD_ARGS", $"bad rgba '{args[0]}'");

        if (!AsciiSyntax.TryParseBool(args[1], out bool on))
            return Error("BAD_ARGS", $"bad state '{args[1]}'");

        lock (Gate) _lights[led] = new LedState(color, on);
        Touch();
        return Ok();
    }

    private string ExecuteLcd(string statement, int index)
    {
        string sub = ReadWord(statement, ref index).ToUpperInvariant();

        switch (sub)
        {
            case "SAVE":
            {
                if (!AsciiSyntax.TryExtractBlock(statement, index, '<', '>', out string inner, out int after))
                    return Error("SYNTAX", "expected LCD SAVE<index, text>");
                if (RequireEnd(statement, after) is { } trailing) return trailing;

                var args = AsciiSyntax.SplitArgs(inner, 2);
                if (args.Count == 0 || !AsciiSyntax.TryParseInt32(args[0], out int slot))
                    return Error("BAD_ARGS", "expected a slot index");
                if (!LcdPanel.IsValidSlot(slot))
                    return Error("RANGE", $"slot {slot} outside 0..{LcdPanel.SlotCount - 1}");

                string text = args.Count > 1 ? AsciiSyntax.Unescape(args[1]) : "";
                lock (Gate) _lcd.Save(slot, text);
                Touch();
                return Ok();
            }

            case "LOAD":
            {
                if (!AsciiSyntax.TryExtractBlock(statement, index, '<', '>', out string inner, out int after))
                    return Error("SYNTAX", "expected LCD LOAD<index>");
                if (RequireEnd(statement, after) is { } trailing) return trailing;

                if (!AsciiSyntax.TryParseInt32(inner.Trim(), out int slot))
                    return Error("BAD_ARGS", $"bad slot '{inner}'");
                if (!LcdPanel.IsValidSlot(slot))
                    return Error("RANGE", $"slot {slot} outside 0..{LcdPanel.SlotCount - 1}");

                lock (Gate) _lcd.Load(slot);
                Touch();
                return Ok();
            }

            case "TEXT":
            {
                if (!AsciiSyntax.TryExtractBlock(statement, index, '<', '>', out string inner, out int after))
                    return Error("SYNTAX", "expected LCD TEXT<text>");
                if (RequireEnd(statement, after) is { } trailing) return trailing;

                // Not trimmed: leading spaces are how a caller centres text on the glass.
                lock (Gate) _lcd.SetText(AsciiSyntax.Unescape(inner));
                Touch();
                return Ok();
            }

            case "SHOW":
                if (RequireEnd(statement, index) is { } showTrailing) return showTrailing;
                lock (Gate) _lcd.Show();
                Touch();
                return Ok();

            case "CLR":
                if (RequireEnd(statement, index) is { } clrTrailing) return clrTrailing;
                lock (Gate) _lcd.ClearDisplay();
                Touch();
                return Ok();

            default:
                return Error("UNKNOWN_CMD", $"LCD {sub}");
        }
    }

    private string ExecutePanel(string statement, int index)
    {
        string sub = ReadWord(statement, ref index).ToUpperInvariant();

        // CLR is the only one whose argument block is optional.
        bool hasBlock = AsciiSyntax.TryExtractBlock(statement, index, '<', '>', out string inner, out int after);
        if (hasBlock && RequireEnd(statement, after) is { } trailing) return trailing;

        switch (sub)
        {
            case "POINT":
            {
                if (!hasBlock) return Error("SYNTAX", "expected PANEL POINT<x, y, rgba>");
                if (!TryTokens(inner, 3, 3, out var tokens, out string failure)) return failure;
                if (!TryInts(tokens, 2, out int[] v, out failure)) return failure;
                if (!AsciiSyntax.TryParseRgba(tokens[2], out var color)) return BadRgba(tokens[2]);

                lock (Gate) _panel.Point(v[0], v[1], color);
                Touch();
                return Ok();
            }

            case "LINE":
            {
                if (!hasBlock) return Error("SYNTAX", "expected PANEL LINE<x0, y0, x1, y1, rgba>");
                if (!TryTokens(inner, 5, 5, out var tokens, out string failure)) return failure;
                if (!TryInts(tokens, 4, out int[] v, out failure)) return failure;
                if (!AsciiSyntax.TryParseRgba(tokens[4], out var color)) return BadRgba(tokens[4]);

                lock (Gate) _panel.Line(v[0], v[1], v[2], v[3], color);
                Touch();
                return Ok();
            }

            case "RECT":
            {
                if (!hasBlock) return Error("SYNTAX", "expected PANEL RECT<x, y, w, h, rgba[, fill]>");
                if (!TryTokens(inner, 5, 6, out var tokens, out string failure)) return failure;
                if (!TryInts(tokens, 4, out int[] v, out failure)) return failure;
                if (!AsciiSyntax.TryParseRgba(tokens[4], out var color)) return BadRgba(tokens[4]);

                bool filled = false;
                if (tokens.Count == 6 && !AsciiSyntax.TryParseBool(tokens[5], out filled))
                    return Error("BAD_ARGS", $"bad fill flag '{tokens[5]}'");

                lock (Gate) _panel.Rect(v[0], v[1], v[2], v[3], color, filled);
                Touch();
                return Ok();
            }

            case "CLR":
            {
                var color = Rgba.Off;
                if (hasBlock && inner.Trim().Length > 0 && !AsciiSyntax.TryParseRgba(inner.Trim(), out color))
                    return Error("BAD_ARGS", $"bad rgba '{inner.Trim()}'");
                if (!hasBlock && RequireEnd(statement, index) is { } clrTrailing) return clrTrailing;

                lock (Gate) _panel.Clear(color);
                Touch();
                return Ok();
            }

            default:
                return Error("UNKNOWN_CMD", $"PANEL {sub}");
        }
    }

    private string ExecuteSys(string statement, int index)
    {
        string sub = ReadWord(statement, ref index).ToUpperInvariant();
        if (RequireEnd(statement, index) is { } trailing) return trailing;

        switch (sub)
        {
            case "PING":
                return Ok("PONG");

            case "ID":
                return Ok($"{Id} ASCII/{ProtocolVersion} LIGHTS={LightCount} " +
                          $"LCD={LcdPanel.Columns}x{LcdPanel.Rows}x{LcdPanel.SlotCount} " +
                          $"PANEL={PixelPanel.Width}x{PixelPanel.Height}");

            case "STAT":
            {
                var snapshot = Snapshot();
                return Ok($"CMD={snapshot.Commands} ERR={snapshot.Errors} LIT={snapshot.LitPixels}");
            }

            case "RESET":
                Reset();
                return Ok();

            default:
                return Error("UNKNOWN_CMD", $"SYS {sub}");
        }
    }

    private static string BadRgba(string token) => Error("BAD_ARGS", $"bad rgba '{token}'");

    private static bool TryTokens(string inner, int min, int max, out List<string> tokens, out string failure)
    {
        failure = "";
        tokens = AsciiSyntax.SplitAll(inner);
        if (tokens.Count >= min && tokens.Count <= max) return true;

        string expected = min == max ? $"{min}" : $"{min} or {max}";
        failure = Error("BAD_ARGS", $"expected {expected} arguments, got {tokens.Count}");
        return false;
    }

    /// <summary>Parses the leading <paramref name="count"/> tokens as coordinates; later tokens are the caller's.</summary>
    private static bool TryInts(List<string> tokens, int count, out int[] values, out string failure)
    {
        failure = "";
        values = new int[count];

        for (int i = 0; i < count; i++)
        {
            if (AsciiSyntax.TryParseInt32(tokens[i], out values[i])) continue;
            failure = Error("BAD_ARGS", $"bad number '{tokens[i]}'");
            return false;
        }
        return true;
    }

    /// <summary>Returns an error response when anything follows the command, otherwise null.</summary>
    private static string? RequireEnd(string statement, int index)
    {
        string remainder = statement[Math.Min(index, statement.Length)..].Trim();
        return remainder.Length == 0 ? null : Error("SYNTAX", $"unexpected trailing input '{remainder}'");
    }

    /// <summary>Case-insensitive keyword match; the keyword must be followed by a separator, a bracket, or the end.</summary>
    private static bool TryMatchKeyword(string statement, string keyword, out int nextIndex)
    {
        nextIndex = 0;
        if (!statement.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)) return false;

        if (statement.Length > keyword.Length)
        {
            char next = statement[keyword.Length];
            if (!char.IsWhiteSpace(next) && next is not ('[' or '<')) return false;
        }

        nextIndex = keyword.Length;
        return true;
    }

    private static string ReadWord(string text, ref int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        int start = index;
        while (index < text.Length && char.IsLetterOrDigit(text[index])) index++;
        return text[start..index];
    }
}
