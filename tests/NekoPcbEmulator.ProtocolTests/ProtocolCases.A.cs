using NekoPcbEmulator.Core.Devices.PcbA;

namespace NekoPcbEmulator.ProtocolTests;

internal sealed partial class ProtocolCases(WireClient a, WireClient b, PcbADevice? deviceA)
{
    public List<CaseResult> Results { get; } = [];

    public void RunA()
    {
        Case("A", "System reset, identity, ping, and counters", () =>
        {
            A("SYS RESET;");
            A("SYS PING;", "OK PONG");
            A("SYS ID;", "OK PCB-A ASCII/1.1 LIGHTS=16 LCD=20x4x5 PANEL=360x120");
            Equal(Stats()["ERR"], 0L);
        });
        Case("A", "LIGHT accepts every documented boolean and numeric format", () =>
        {
            foreach (string token in new[] { "true", "false", "1", "0", "on", "off", "high", "low", "yes", "no", "TrUe" })
                A($"LIGHT[0] 0xFF2020FF {token};");
            A("LIGHT[1] #00FF00FF true;");
            A("LIGHT[2] 4294967295 true;");
        });
        Case("A", "LIGHT addresses all 16 indicators and rejects the next index", () =>
        {
            for (int index = 0; index < 16; index++) A($"LIGHT[{index}] 0x2040FFFF true;");
            ErrorA("LIGHT[16] 0xFFFFFFFF true;", "RANGE");
        });
        Case("A", "LIGHT takes an optional duration in 0..65535 ms, valid only while on", () =>
        {
            A("LIGHT[0] 0xFF2020FF true 0;");
            A("LIGHT[0] 0xFF2020FF true 250;");
            A("LIGHT[0] 0xFF2020FF, true, 65535;");
            A("LIGHT[0] 0xFF2020FF false 0;");
            ErrorA("LIGHT[0] 0xFF2020FF true -1;", "RANGE");
            ErrorA("LIGHT[0] 0xFF2020FF true 65536;", "RANGE");
            ErrorA("LIGHT[0] 0xFF2020FF true soon;", "BAD_ARGS");
            ErrorA("LIGHT[0] 0xFF2020FF false 250;", "BAD_ARGS");
            ErrorA("LIGHT[0] 0xFF2020FF true 250 extra;", "BAD_ARGS");
        });
        Case("A", "LIGHT rejects invalid indices, colors, booleans, and argument counts", () =>
        {
            ErrorA("LIGHT[99] 0xFFFFFFFF true;", "RANGE");
            ErrorA("LIGHT[-1] 0xFFFFFFFF true;", "RANGE");
            ErrorA("LIGHT[0] 0x100000000 true;", "BAD_ARGS");
            ErrorA("LIGHT[0] -1 true;", "BAD_ARGS");
            ErrorA("LIGHT[0] 0xFFFFFFFF maybe;", "BAD_ARGS");
            ErrorA("LIGHT[0] 0xFFFFFFFF true extra;", "BAD_ARGS");
            ErrorA("LIGHT 0 0xFFFFFFFF true;", "SYNTAX");
        });
        Case("A", "All LCD commands, five slots, and the documented escaped-text example", () =>
        {
            for (int slot = 0; slot < 5; slot++)
            {
                A($"LCD SAVE<{slot}, BOOT {slot}>;");
                A($"LCD LOAD<{slot}>;");
                A("LCD SHOW;");
            }
            A(@"LCD TEXT<line 1\nline 2\ncom \; literal>;");
            A("LCD SHOW;");
            A("LCD CLR;");
            A("LCD SHOW;");
            ErrorA("LCD LOAD<7>;", "RANGE");
            ErrorA("LCD SAVE<-1, invalid>;", "RANGE");
            ErrorA("LCD LOAD<bad>;", "BAD_ARGS");
            ErrorA("LCD TEXT<unclosed;", "SYNTAX");
            ErrorA("LCD SHOW extra;", "SYNTAX");
        });
        Case("A", "PANEL POINT clips coordinates and accepts comma or space separators", () =>
        {
            A("PANEL CLR;");
            A("PANEL POINT<10, 20, 0xFF0000FF>;");
            A("PANEL POINT<11 20 #00FF00FF>;");
            A("PANEL POINT<-7, -7, 0xFFFFFFFF>;");
            A("PANEL POINT<360, 120, 0xFFFFFFFF>;");
            Equal(Stats()["LIT"], 2L);
        });
        Case("A", "PANEL LINE includes both endpoints and clips off-panel pixels", () =>
        {
            A("PANEL CLR;");
            A("PANEL LINE<-5, 0, 5, 0, 0xFFFFFFFF>;");
            Equal(Stats()["LIT"], 6L);
            A("PANEL CLR;");
            A("PANEL LINE<0, 0, 359, 119, 0xFFFFFF80>;");
            Equal(Stats()["LIT"], 360L);
        });
        Case("A", "PANEL RECT outline, fill, clipping, and nonpositive sizes", () =>
        {
            A("PANEL CLR;");
            A("PANEL RECT<0, 0, 3, 3, 0xFFFFFFFF>;");
            Equal(Stats()["LIT"], 8L);
            A("PANEL CLR;");
            A("PANEL RECT<0, 0, 3, 3, 0xFFFFFFFF, true>;");
            Equal(Stats()["LIT"], 9L);
            A("PANEL RECT<0, 0, 0, 3, 0xFFFFFFFF, true>;");
            A("PANEL RECT<0, 0, 3, -1, 0xFFFFFFFF, true>;");
            Equal(Stats()["LIT"], 9L);
            A("PANEL CLR;");
            A("PANEL RECT<-1, -1, 3, 3, 0xFFFFFFFF, true>;");
            Equal(Stats()["LIT"], 4L);
        });
        Case("A", "PANEL CLR with color fills all 43200 pixels", () =>
        {
            A("PANEL CLR<0x0A1830FF>;");
            Equal(Stats()["LIT"], 43200L);
            A("PANEL CLR;");
            Equal(Stats()["LIT"], 0L);
        });
        Case("A", "PANEL rejects malformed syntax, numbers, colors, and fill flags", () =>
        {
            ErrorA("PANEL POINT<0, 0>;", "BAD_ARGS");
            ErrorA("PANEL LINE<bad, 0, 1, 1, 0xFFFFFFFF>;", "BAD_ARGS");
            ErrorA("PANEL RECT<0, 0, 1, 1, bad>;", "BAD_ARGS");
            ErrorA("PANEL RECT<0, 0, 1, 1, 0xFFFFFFFF, maybe>;", "BAD_ARGS");
            ErrorA("PANEL CLR trailing;", "SYNTAX");
        });
        Case("A", "Leading whitespace, case-insensitive commands, and ordered batched replies", () =>
        {
            a.SendAscii("\r\n  sys ping;LCD CLR;SYS ID;");
            Equal(a.ReadAscii(), "OK PONG");
            Equal(a.ReadAscii(), "OK");
            Require(a.ReadAscii().StartsWith("OK PCB-A ASCII/1.1", StringComparison.Ordinal), "Missing identity response");
        });
        Case("A", "Fragmented statements and escapes wait for the real terminator", () =>
        {
            a.SendAscii("LCD TEXT<literal\\");
            Thread.Sleep(40);
            Require(!a.HasData, "A partial command produced a response");
            a.SendAscii("; and \\<value\\> and \\\\ path>");
            Thread.Sleep(40);
            Require(!a.HasData, "An escaped semicolon terminated the statement");
            a.SendAscii(";");
            Equal(a.ReadAscii(), "OK");
            A("SYS PING;", "OK PONG");
        });
        Case("A", "4096-byte statements succeed; overflow discards through the next terminator", () =>
        {
            A("LCD TEXT<" + new string('x', 4086) + ">;");
            a.SendAscii("LCD TEXT<" + new string('x', 4087) + ">;SYS PING;");
            Require(a.ReadAscii().StartsWith("ERR OVERFLOW ", StringComparison.Ordinal), "Expected overflow error");
            Equal(a.ReadAscii(), "OK PONG");
        });
        Case("A", "Overflow preserves reply order relative to preceding and following commands", () =>
        {
            a.SendAscii("SYS PING;" + new string('x', 4097) + ";SYS ID;");
            Equal(a.ReadAscii(), "OK PONG");
            Require(a.ReadAscii().StartsWith("ERR OVERFLOW ", StringComparison.Ordinal), "Expected second reply to be overflow");
            Require(a.ReadAscii().StartsWith("OK PCB-A ASCII/1.1", StringComparison.Ordinal), "Expected identity after overflow");
        });
        Case("A", "Unknown commands return sanitized and bounded error payloads", () =>
        {
            ErrorA("UNKNOWN;", "UNKNOWN_CMD");
            a.SendAscii(new string('x', 300) + ";");
            string error = a.ReadAscii();
            Require(error.StartsWith("ERR UNKNOWN_CMD ", StringComparison.Ordinal) && error.Length <= 216, "Error detail exceeded 200 characters");
            ErrorA(@"UNKNOWN\;\n\r;", "UNKNOWN_CMD");
        });
        Case("A", "Error counters increment and reset clears counters and pixels", () =>
        {
            A("SYS RESET;");
            ErrorA("UNKNOWN;", "UNKNOWN_CMD");
            Equal(Stats()["ERR"], 1L);
            A("PANEL POINT<0, 0, 0xFFFFFFFF>;");
            A("SYS RESET;");
            var stats = Stats();
            Equal(stats["ERR"], 0L);
            Equal(stats["LIT"], 0L);
            Equal(stats["CMD"], 1L);
        });

        if (deviceA is not null) RunAState();
        Case("A", "No unsolicited or duplicate responses remain", () =>
        {
            Thread.Sleep(40);
            Require(!a.HasData, "Extra response bytes remain");
        });
    }

    private void RunAState()
    {
        Case("A", "LED state retains stored RGBA while off", () =>
        {
            A("LIGHT[0] #FF000080 false;");
            var light = deviceA!.Snapshot().Lights[0];
            Require(!light.On && light.Color.Packed == 0xFF000080, "Stored LED color or switch is wrong");
            A("LIGHT[0] #FF000080 true;");
            Require(deviceA.Snapshot().Lights[0].On, "LED did not turn on");
        });
        Case("A", "All 16 LEDs hold independent colors and expire together with their durations", () =>
        {
            for (int index = 0; index < 16; index++) A($"LIGHT[{index}] 0x{index + 1:X2}4080FF true 250;");
            var lit = deviceA!.Snapshot().Lights;
            Equal(lit.Length, 16);
            Require(lit.All(light => light.IsTimed), "Not every LED is counting down");
            Require(lit.Select((light, index) => light.Color.R == index + 1).All(match => match), "Colors landed on the wrong LEDs");
            Thread.Sleep(400);
            Require(deviceA.Snapshot().Lights.All(light => !light.On) && !deviceA.IsAnimating, "Not every LED expired");
        });
        Case("A", "LIGHT duration turns the LED off by itself and keeps the stored color", () =>
        {
            A("LIGHT[4] #20FF60FF true 150;");
            var lit = deviceA!.Snapshot().Lights[4];
            Require(lit.On && lit.IsTimed && lit.RemainingMs is > 0 and <= 150, "Timed LED did not start counting down");
            Require(deviceA.IsAnimating, "A pending timeout must keep the view repainting");
            Thread.Sleep(300);
            var expired = deviceA.Snapshot().Lights[4];
            Require(!expired.On && !expired.IsTimed && expired.Color.Packed == 0x20FF60FF, "LED did not expire with its color kept");
        });
        Case("A", "A new LIGHT replaces or cancels the pending duration; other LEDs are unaffected", () =>
        {
            A("LIGHT[3] #FF0000FF true 150;");
            A("LIGHT[4] #00FF00FF true 150;");
            A("LIGHT[5] #0000FFFF true 150;");
            A("LIGHT[3] #FF0000FF true;");
            A("LIGHT[4] #00FF00FF true 30000;");
            Thread.Sleep(300);
            var lights = deviceA!.Snapshot().Lights;
            Require(lights[3].On && !lights[3].IsTimed, "Untimed LIGHT did not cancel the pending timeout");
            Require(lights[4].On && lights[4].RemainingMs > 20000, "Timed LIGHT did not restart the timeout");
            Require(!lights[5].On, "Untouched timeout did not expire");
        });
        Case("A", "A rejected LIGHT leaves the LED and its pending duration alone", () =>
        {
            A("LIGHT[3] #FF0000FF true 30000;");
            ErrorA("LIGHT[3] #00FF00FF true 65536;", "RANGE");
            ErrorA("LIGHT[3] #00FF00FF false 10;", "BAD_ARGS");
            var light = deviceA!.Snapshot().Lights[3];
            Require(light.On && light.IsTimed && light.Color.Packed == 0xFF0000FF, "Rejected command changed the LED");
        });
        Case("A", "LCD CLR preserves staging and slots; SHOW restores displayed text", () =>
        {
            A("LCD SAVE<0, BOOT OK>;");
            A("LCD LOAD<0>;");
            A("LCD SHOW;");
            Equal(deviceA!.Snapshot().LcdLines[0], "BOOT OK");
            A("LCD CLR;");
            var cleared = deviceA.Snapshot();
            Require(cleared.LcdLines.All(line => line.Length == 0), "LCD CLR did not clear the display");
            Equal(cleared.LcdLoaded, "BOOT OK");
            Equal(cleared.LcdSlots[0], "BOOT OK");
            A("LCD SHOW;");
            Equal(deviceA.Snapshot().LcdLines[0], "BOOT OK");
        });
        Case("A", "LCD Latin-1, leading spaces, escapes, wrapping, and four-row clipping", () =>
        {
            A(@"LCD TEXT<  café\;\<\>\\\nsecond\r\t>;");
            Equal(deviceA!.Snapshot().LcdLoaded, "  café;<>\\\nsecond\r\t");
            A("LCD SHOW;");
            Equal(deviceA.Snapshot().LcdLines[0], "  café;<>\\");
            A("LCD TEXT<12345678901234567890X\\nrow3\\nrow4\\nclipped>;");
            A("LCD SHOW;");
            Require(deviceA.Snapshot().LcdLines.SequenceEqual(new[] { "12345678901234567890", "X", "row3", "row4" }), "LCD wrap or row clipping is wrong");
            A(@"LCD SAVE<0, \  padded>;");
            Equal(deviceA.Snapshot().LcdSlots[0], "  padded");
        });
        Case("A", "Panel alpha uses source-over composition against the framebuffer", () =>
        {
            A("PANEL CLR<0x0000FFFF>;");
            A("PANEL POINT<0, 0, 0xFF000080>;");
            var pixels = new uint[360 * 120];
            deviceA!.CopyPanelTo(pixels);
            Equal(pixels[0], 0xFF80007Fu);
            Equal(pixels[1], 0xFF0000FFu);
        });
        Case("A", "SYS RESET clears LEDs, LCD screen, staging, slots, and panel", () =>
        {
            A("SYS RESET;");
            var snapshot = deviceA!.Snapshot();
            Require(snapshot.Lights.All(light => !light.On && light.Color.Packed == 0)
                && !deviceA.IsAnimating
                && snapshot.LcdLines.All(line => line.Length == 0)
                && snapshot.LcdLoaded.Length == 0 && snapshot.LcdSlots.All(slot => slot.Length == 0)
                && snapshot.LitPixels == 0, "Reset left peripheral state behind");
        });
    }

    private void A(string command, string expected = "OK")
    {
        a.SendAscii(command);
        Equal(a.ReadAscii(), expected);
    }

    private void ErrorA(string command, string code)
    {
        a.SendAscii(command);
        string response = a.ReadAscii();
        Require(response.StartsWith("ERR " + code + " ", StringComparison.Ordinal), $"Expected ERR {code}, received {response}");
    }

    private Dictionary<string, long> Stats()
    {
        a.SendAscii("SYS STAT;");
        string response = a.ReadAscii();
        Require(response.StartsWith("OK CMD=", StringComparison.Ordinal), "Invalid statistics response: " + response);
        return response.Split(' ').Skip(1).Select(item => item.Split('=')).ToDictionary(item => item[0], item => long.Parse(item[1]));
    }

    private void Case(string protocol, string name, Action action)
    {
        try
        {
            action();
            Results.Add(new(protocol, name, true, null));
            Console.WriteLine($"PASS {protocol}: {name}");
        }
        catch (Exception ex)
        {
            Results.Add(new(protocol, name, false, ex.Message));
            Console.WriteLine($"FAIL {protocol}: {name}: {ex.Message}");
            (protocol == "A" ? a : b).Drain();
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private static void Equal<T>(T actual, T expected) =>
        Require(EqualityComparer<T>.Default.Equals(actual, expected), $"Expected {expected}; received {actual}");
}

internal sealed record CaseResult(string Protocol, string Name, bool Passed, string? Error);
