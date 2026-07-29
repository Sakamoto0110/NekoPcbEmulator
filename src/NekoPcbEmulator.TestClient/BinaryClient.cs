using System.Globalization;
using System.Text;
using NekoPcbEmulator.Core.Devices.PcbB;

namespace NekoPcbEmulator.TestClient;

/// <summary>
/// Host side of the PCB-B protocol. Frames are built with the same
/// <see cref="BinaryFrame"/> encoder the device uses, and replies are reassembled with the
/// same <see cref="FrameDecoder"/> — so this file is also the shortest description of what a
/// conforming host has to do.
/// </summary>
internal static class BinaryClient
{
    private static byte _sequence;

    public static async Task RunAsync(Stream stream, bool demo, CancellationToken ct)
    {
        var reader = Task.Run(() => ReadLoopAsync(stream, ct), CancellationToken.None);

        if (demo) await RunDemoAsync(stream, ct);
        else await RunInteractiveAsync(stream, ct);

        await Task.WhenAny(reader, Task.Delay(200, CancellationToken.None));
    }

    private static async Task ReadLoopAsync(Stream stream, CancellationToken ct)
    {
        var decoder = new FrameDecoder();
        var buffer = new byte[4096];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buffer, ct);
                if (read <= 0) break;

                decoder.Push(buffer.AsSpan(0, read));

                while (true)
                {
                    var status = decoder.TryRead(out var frame, out byte offendingSeq);
                    if (status == FrameDecodeStatus.NeedMoreData) break;

                    switch (status)
                    {
                        case FrameDecodeStatus.ChecksumError:
                            Write(ConsoleColor.Red, $"  <- CRC error near seq {offendingSeq}");
                            break;
                        case FrameDecodeStatus.LengthError:
                            Write(ConsoleColor.Red, "  <- bad length byte, resyncing");
                            break;
                        case FrameDecodeStatus.Frame:
                            PrintFrame(frame);
                            break;
                    }
                }
            }
        }
        catch (Exception)
        {
            // The board went away or we are shutting down.
        }
    }

    private static void PrintFrame(DecodedFrame frame)
    {
        string detail = frame.Cmd switch
        {
            PcbBCommand.Ack => $"of {PcbBCommand.Name(frame.Data.Length > 0 ? frame.Data[0] : (byte)0)}",
            PcbBCommand.Nak => frame.Data.Length >= 2
                ? $"of {PcbBCommand.Name(frame.Data[0])} — {PcbBError.Name(frame.Data[1])}"
                : "malformed",
            PcbBCommand.Info => DescribeInfo(frame.Data),
            PcbBCommand.State => DescribeState(frame.Data),
            _ => Hex(frame.Data),
        };

        var color = frame.Cmd == PcbBCommand.Nak ? ConsoleColor.Red : ConsoleColor.Green;
        Write(color, $"  <- seq={frame.Seq,-3} {PcbBCommand.Name(frame.Cmd),-10} {detail}");
    }

    private static string DescribeInfo(byte[] data) => data.Length < 3
        ? "malformed"
        : $"protocol v{data[0]} grid {data[1]}x{data[2]} \"{Encoding.ASCII.GetString(data, 3, data.Length - 3)}\"";

    private static string DescribeState(byte[] data)
    {
        const int stride = 7;
        if (data.Length != LedGrid.Count * stride) return $"malformed ({data.Length} bytes)";

        var lit = new List<string>();
        for (int i = 0; i < LedGrid.Count; i++)
        {
            int offset = i * stride;
            if (data[offset] == 0) continue;

            int remaining = (data[offset + 5] << 8) | data[offset + 6];
            string timing = remaining > 0 ? $"+{remaining}ms" : "held";
            lit.Add($"{i}:#{data[offset + 1]:X2}{data[offset + 2]:X2}{data[offset + 3]:X2}{data[offset + 4]:X2}/{timing}");
        }

        return lit.Count == 0 ? "all off" : $"{lit.Count} lit — {string.Join(' ', lit)}";
    }

    private static async Task RunInteractiveAsync(Stream stream, CancellationToken ct)
    {
        PrintHelp();

        while (!ct.IsCancellationRequested)
        {
            Console.Write("> ");
            string? line = await Task.Run(Console.ReadLine, ct);
            if (line is null) break;

            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            string command = parts[0].ToLowerInvariant();
            if (command is "quit" or "exit") break;
            if (command is "help" or "?") { PrintHelp(); continue; }

            try
            {
                if (!await DispatchAsync(stream, command, parts, ct))
                    Console.WriteLine("unknown command — type 'help'");
            }
            catch (Exception ex)
            {
                Write(ConsoleColor.Red, $"  !! {ex.Message}");
            }
        }
    }

    private static async Task<bool> DispatchAsync(Stream stream, string command, string[] parts, CancellationToken ct)
    {
        switch (command)
        {
            case "set":
            {
                if (parts.Length < 5) throw new ArgumentException("set <index> <r> <g> <b> [a] [ms]");
                byte index = Number(parts[1]);
                byte a = parts.Length > 5 ? Number(parts[5]) : (byte)0xFF;
                int ms = parts.Length > 6 ? int.Parse(parts[6], CultureInfo.InvariantCulture) : 0;

                await SendAsync(stream, PcbBCommand.LedSet,
                    [index, Number(parts[2]), Number(parts[3]), Number(parts[4]), a, (byte)(ms >> 8), (byte)ms], ct);
                return true;
            }

            case "clear":
                if (parts.Length < 2) throw new ArgumentException("clear <index>");
                await SendAsync(stream, PcbBCommand.LedClear, [Number(parts[1])], ct);
                return true;

            case "clearall":
                await SendAsync(stream, PcbBCommand.ClearAll, [], ct);
                return true;

            case "all":
            {
                if (parts.Length < 4) throw new ArgumentException("all <r> <g> <b> [a] [ms]");
                byte a = parts.Length > 4 ? Number(parts[4]) : (byte)0xFF;
                int ms = parts.Length > 5 ? int.Parse(parts[5], CultureInfo.InvariantCulture) : 0;

                await SendAsync(stream, PcbBCommand.SetAll,
                    [Number(parts[1]), Number(parts[2]), Number(parts[3]), a, (byte)(ms >> 8), (byte)ms], ct);
                return true;
            }

            case "mask":
            {
                if (parts.Length < 5) throw new ArgumentException("mask <hex25> <r> <g> <b> [a] [ms]");
                uint mask = uint.Parse(parts[1].TrimStart('0', 'x', 'X'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte a = parts.Length > 5 ? Number(parts[5]) : (byte)0xFF;
                int ms = parts.Length > 6 ? int.Parse(parts[6], CultureInfo.InvariantCulture) : 0;

                await SendAsync(stream, PcbBCommand.SetMask,
                [
                    (byte)(mask >> 24), (byte)(mask >> 16), (byte)(mask >> 8), (byte)mask,
                    Number(parts[2]), Number(parts[3]), Number(parts[4]), a,
                    (byte)(ms >> 8), (byte)ms,
                ], ct);
                return true;
            }

            case "ping":
                await SendAsync(stream, PcbBCommand.Ping, [], ct);
                return true;

            case "state":
                await SendAsync(stream, PcbBCommand.GetState, [], ct);
                return true;

            case "info":
                await SendAsync(stream, PcbBCommand.GetInfo, [], ct);
                return true;

            case "corrupt":
                await SendCorruptAsync(stream, ct);
                return true;

            default:
                return false;
        }
    }

    private static void PrintHelp() => Console.WriteLine("""

        commands (numbers accept 0x hex):
          set <index> <r> <g> <b> [a] [ms]   light one LED; ms = 0 holds it on
          clear <index>                      turn one LED off
          clearall                           turn everything off
          all <r> <g> <b> [a] [ms]           set all 25 at once
          mask <hex25> <r> <g> <b> [a] [ms]  bit n selects LED n
          ping | state | info                query the board
          corrupt                            send a frame with a bad CRC (expect a NAK)
          quit

        """);

    private static async Task RunDemoAsync(Stream stream, CancellationToken ct)
    {
        await SendAsync(stream, PcbBCommand.GetInfo, [], ct);
        await SendAsync(stream, PcbBCommand.Ping, [], ct);
        await SendAsync(stream, PcbBCommand.ClearAll, [], ct);
        await Task.Delay(300, ct);

        // Error paths first, so the failure responses are easy to spot in the log.
        Console.WriteLine();
        Console.WriteLine("  error handling:");
        await SendAsync(stream, PcbBCommand.LedSet, [99, 0xFF, 0xFF, 0xFF, 0xFF, 0, 0], ct); // BAD_INDEX
        await SendAsync(stream, PcbBCommand.LedSet, [0, 0xFF], ct);                          // BAD_LENGTH
        await SendAsync(stream, 0x7F, [], ct);                                               // UNKNOWN_COMMAND
        await SendCorruptAsync(stream, ct);                                                  // BAD_CHECKSUM
        await Task.Delay(600, ct);

        Console.WriteLine();
        Console.WriteLine("  chase, timed LEDs, then a colour sweep (about 20 s)...");

        // A chase along the serpentine chain, each LED holding for a second.
        for (int i = 0; i < LedGrid.Count && !ct.IsCancellationRequested; i++)
        {
            var (r, g, b) = Wheel(i * 10);
            await SendAsync(stream, PcbBCommand.LedSet,
                [(byte)i, r, g, b, 0xFF, (byte)(1000 >> 8), unchecked((byte)1000)], ct, quiet: true);
            await Task.Delay(90, ct);
        }

        await Task.Delay(1200, ct);
        await SendAsync(stream, PcbBCommand.GetState, [], ct);
        await Task.Delay(400, ct);

        // Column sweep through the mask command.
        for (int pass = 0; pass < 3 && !ct.IsCancellationRequested; pass++)
        {
            for (int column = 0; column < LedGrid.Columns; column++)
            {
                uint mask = 0;
                for (int row = 0; row < LedGrid.Rows; row++)
                    mask |= 1u << LedGrid.IndexOf(row, column);

                var (r, g, b) = Wheel(pass * 80 + column * 24);
                await SendAsync(stream, PcbBCommand.SetMask,
                [
                    (byte)(mask >> 24), (byte)(mask >> 16), (byte)(mask >> 8), (byte)mask,
                    r, g, b, 0xFF, 0, 0,
                ], ct, quiet: true);
                await Task.Delay(140, ct);
            }
        }

        // Finish on a batch so the multi-LED path gets exercised too.
        var payload = new List<byte> { LedGrid.Count };
        for (int i = 0; i < LedGrid.Count; i++)
        {
            var (r, g, b) = Wheel(i * 10);
            int hold = 2000 + i * 400;
            payload.AddRange([(byte)i, r, g, b, 0xFF, (byte)(hold >> 8), (byte)hold]);
        }
        await SendAsync(stream, PcbBCommand.SetBatch, [.. payload], ct);

        await Task.Delay(1000, ct);
        await SendAsync(stream, PcbBCommand.GetState, [], ct);
        await Task.Delay(500, ct);
    }

    /// <summary>Sends a structurally valid frame whose CRC is wrong, to exercise resynchronisation.</summary>
    private static async Task SendCorruptAsync(Stream stream, CancellationToken ct)
    {
        byte[] frame = BinaryFrame.Encode(_sequence++, PcbBCommand.Ping, []);
        frame[^1] ^= 0xFF;

        Write(ConsoleColor.Yellow, $"-> {Hex(frame)}  (CRC deliberately corrupted)");
        await stream.WriteAsync(frame, ct);
        await stream.FlushAsync(ct);
        await Task.Delay(60, ct);
    }

    private static async Task SendAsync(Stream stream, byte command, byte[] payload, CancellationToken ct, bool quiet = false)
    {
        byte seq = _sequence++;
        byte[] frame = BinaryFrame.Encode(seq, command, payload);

        if (!quiet)
            Write(ConsoleColor.Cyan, $"-> seq={seq,-3} {PcbBCommand.Name(command),-10} {Hex(frame)}");

        await stream.WriteAsync(frame, ct);
        await stream.FlushAsync(ct);
        await Task.Delay(40, ct);
    }

    /// <summary>Classic 8-bit colour wheel, used to make the demos legible at a glance.</summary>
    private static (byte R, byte G, byte B) Wheel(int position)
    {
        position &= 0xFF;
        return position switch
        {
            < 85 => ((byte)(255 - position * 3), (byte)(position * 3), 0),
            < 170 => (0, (byte)(255 - (position - 85) * 3), (byte)((position - 85) * 3)),
            _ => ((byte)((position - 170) * 3), 0, (byte)(255 - (position - 170) * 3)),
        };
    }

    private static byte Number(string token) => token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? byte.Parse(token[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
        : byte.Parse(token, CultureInfo.InvariantCulture);

    private static string Hex(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder(data.Length * 3);
        for (int i = 0; i < Math.Min(data.Length, 20); i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(data[i].ToString("X2"));
        }
        if (data.Length > 20) sb.Append($" .. +{data.Length - 20}");
        return sb.ToString();
    }

    private static void Write(ConsoleColor color, string text)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }
}
