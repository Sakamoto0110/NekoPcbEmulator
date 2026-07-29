using System.Text;

namespace NekoPcbEmulator.TestClient;

/// <summary>
/// Host side of the PCB-A protocol. Statements go out as Latin-1 bytes terminated by
/// <c>;</c>; replies come back as <c>;</c>-terminated <c>OK</c>/<c>ERR</c> lines.
/// </summary>
internal static class AsciiClient
{
    private static readonly Encoding Wire = Encoding.Latin1;

    public static async Task RunAsync(Stream stream, bool demo, CancellationToken ct)
    {
        var reader = Task.Run(() => ReadLoopAsync(stream, ct), CancellationToken.None);

        if (demo) await RunDemoAsync(stream, ct);
        else await RunInteractiveAsync(stream, ct);

        await Task.WhenAny(reader, Task.Delay(200, CancellationToken.None));
    }

    private static async Task ReadLoopAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[4096];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buffer, ct);
                if (read <= 0) break;

                string text = Wire.GetString(buffer, 0, read);
                foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0) continue;
                    Write(trimmed.StartsWith("ERR", StringComparison.Ordinal) ? ConsoleColor.Red : ConsoleColor.Green,
                        "  <- " + trimmed);
                }
            }
        }
        catch (Exception)
        {
            // The board went away or we are shutting down.
        }
    }

    private static async Task RunInteractiveAsync(Stream stream, CancellationToken ct)
    {
        Console.WriteLine("""
            type statements without the trailing ';' — it is added for you. examples:
              SYS ID
              LIGHT[0] 0xFF0000FF true
              LCD TEXT<hello world>
              LCD SHOW
              PANEL RECT<10, 10, 100, 50, 0x00FF80FF, true>
            """);
        Console.WriteLine();

        while (!ct.IsCancellationRequested)
        {
            Console.Write("> ");
            string? line = await Task.Run(Console.ReadLine, ct);
            if (line is null) break;

            line = line.Trim();
            if (line.Length == 0) continue;
            if (line is "quit" or "exit") break;

            await SendAsync(stream, line, ct);
        }
    }

    private static async Task RunDemoAsync(Stream stream, CancellationToken ct)
    {
        await SendAsync(stream, "SYS RESET", ct);
        await SendAsync(stream, "SYS ID", ct);

        // Memory slots, then the load/show path.
        await SendAsync(stream, @"LCD SAVE<0, PCB-A READY>", ct);
        await SendAsync(stream, @"LCD SAVE<1, SLOT ONE TEXT>", ct);
        await SendAsync(stream, @"LCD SAVE<2, ASCII 8N1 115200>", ct);
        await SendAsync(stream, @"LCD SAVE<3, PANEL 360x120>", ct);
        await SendAsync(stream, @"LCD SAVE<4, TEST SUITE OK>", ct);
        await SendAsync(stream, "LCD LOAD<0>", ct);
        await SendAsync(stream, "LCD SHOW", ct);
        await Task.Delay(600, ct);

        // Inline text, including an escaped newline and an escaped semicolon.
        await SendAsync(stream, @"LCD TEXT<PCB-A ONLINE\nRX/TX SELFTEST\nescaped\; semicolon\n0123456789 ABCDEF>", ct);
        await SendAsync(stream, "LCD SHOW", ct);

        await SendAsync(stream, "LIGHT[0] 0xFF2020FF true", ct);
        await SendAsync(stream, "LIGHT[1] 0x20FF60FF true", ct);
        await SendAsync(stream, "LIGHT[2] 0x4080FFFF true", ct);
        await Task.Delay(400, ct);

        // Deliberate failures, so the error paths are visible on the wire.
        await SendAsync(stream, "LIGHT[9] 0xFFFFFFFF true", ct);
        await SendAsync(stream, "LCD LOAD<7>", ct);
        await SendAsync(stream, "NONSENSE", ct);
        await Task.Delay(400, ct);

        Console.WriteLine();
        Console.WriteLine("  panel animation (about 12 s)...");

        await SendAsync(stream, "PANEL CLR", ct, quiet: true);
        await SendAsync(stream, "PANEL RECT<0, 0, 360, 120, 0x0A1830FF, true>", ct, quiet: true);
        await SendAsync(stream, "PANEL RECT<2, 2, 356, 116, 0x2060A0FF, false>", ct, quiet: true);

        for (int frame = 0; frame < 240 && !ct.IsCancellationRequested; frame++)
        {
            var batch = new StringBuilder();
            batch.Append("PANEL RECT<4, 4, 352, 112, 0x0A1830FF, true>;");

            // Two out-of-phase sine traces plus a bouncing box.
            for (int x = 0; x < 352; x += 2)
            {
                int y1 = (int)(58 + 40 * Math.Sin((x + frame * 4) * 0.035));
                int y2 = (int)(58 + 30 * Math.Sin((x - frame * 6) * 0.05));
                batch.Append($"PANEL POINT<{x + 4}, {y1}, 0x30FFB0FF>;");
                batch.Append($"PANEL POINT<{x + 4}, {y2}, 0xFF4090C0>;");
            }

            int boxX = (int)(176 + 150 * Math.Sin(frame * 0.04)) - 14;
            batch.Append($"PANEL RECT<{boxX}, 44, 28, 28, 0xFFD040FF, false>;");
            batch.Append($"PANEL LINE<4, 8, 356, 8, 0x{(frame * 4 % 256):X2}FF80FF>;");

            await stream.WriteAsync(Wire.GetBytes(batch.ToString()), ct);
            await stream.FlushAsync(ct);
            await Task.Delay(50, ct);
        }

        await SendAsync(stream, "SYS STAT", ct);
        await Task.Delay(300, ct);
    }

    private static async Task SendAsync(Stream stream, string statement, CancellationToken ct, bool quiet = false)
    {
        if (!statement.EndsWith(';')) statement += ";";
        if (!quiet) Write(ConsoleColor.Cyan, "-> " + statement);

        await stream.WriteAsync(Wire.GetBytes(statement), ct);
        await stream.FlushAsync(ct);
        await Task.Delay(40, ct);
    }

    private static void Write(ConsoleColor color, string text)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }
}
