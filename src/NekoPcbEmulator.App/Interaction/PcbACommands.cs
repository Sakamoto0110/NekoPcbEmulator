using System.Globalization;
using System.Text;
using NekoPcbEmulator.Core.Devices.PcbA;

namespace NekoPcbEmulator.App.Interaction;

/// <summary>
/// The PCB-A command catalogue. Every entry builds a real ASCII statement, which then goes
/// through the device's own parser — so an index or colour the protocol would reject is
/// rejected here too, and shows up as an <c>ERR</c> in the traffic log.
/// </summary>
public static class PcbACommands
{
    private static readonly Encoding Wire = Encoding.Latin1;

    /// <summary>Commands for one indicator LED. The index is fixed by which LED was clicked.</summary>
    public static IReadOnlyList<CommandSpec> Light(int index) =>
    [
        new CommandSpec(
            "LIGHT — set colour and state",
            $"LIGHT[{index}] <rgba> <state>;  ·  colour is kept while the LED is off",
            [
                new CommandField("RGBA", FieldKind.Rgba, "#FF3020FF", Hint: "0xRRGGBBAA — alpha is brightness"),
                new CommandField("State", FieldKind.Boolean, "true", Hint: "the on/off switch, independent of the colour"),
            ],
            v => Wire.GetBytes(LightStatement(index, v)),
            v => LightStatement(index, v)),
    ];

    private static string LightStatement(int index, IReadOnlyList<string> v) =>
        $"LIGHT[{index}] {Hex(v[0])} {Bool(v[1])};";

    /// <summary>Commands for the character LCD.</summary>
    public static IReadOnlyList<CommandSpec> Lcd() =>
    [
        new CommandSpec(
            "LCD TEXT — stage inline text",
            "LCD TEXT<...>;  ·  fills the staging buffer, does not touch the glass",
            [new CommandField("Text", FieldKind.Text, "hello world", Hint: @"\n breaks a line, \; is a literal semicolon")],
            v => Wire.GetBytes(TextStatement(v)),
            TextStatement),

        new CommandSpec(
            "LCD SHOW — publish the staged text",
            "LCD SHOW;  ·  copies the staging buffer onto the glass",
            [],
            _ => Wire.GetBytes("LCD SHOW;"),
            _ => "LCD SHOW;"),

        new CommandSpec(
            "LCD CLR — blank the glass",
            "LCD CLR;  ·  the staging buffer survives, so SHOW brings the text back",
            [],
            _ => Wire.GetBytes("LCD CLR;"),
            _ => "LCD CLR;"),

        new CommandSpec(
            "LCD SAVE — write a memory slot",
            "LCD SAVE<index, text>;",
            [
                new CommandField("Slot", FieldKind.Integer, "0", 0, LcdPanel.SlotCount - 1),
                new CommandField("Text", FieldKind.Text, "BOOT OK"),
            ],
            v => Wire.GetBytes(SaveStatement(v)),
            SaveStatement),

        new CommandSpec(
            "LCD LOAD — stage a memory slot",
            "LCD LOAD<index>;  ·  slot into the staging buffer; follow with SHOW",
            [new CommandField("Slot", FieldKind.Integer, "0", 0, LcdPanel.SlotCount - 1)],
            v => Wire.GetBytes($"LCD LOAD<{Int(v[0])}>;"),
            v => $"LCD LOAD<{Int(v[0])}>;"),
    ];

    private static string TextStatement(IReadOnlyList<string> v) => $"LCD TEXT<{Escape(v[0])}>;";

    private static string SaveStatement(IReadOnlyList<string> v) => $"LCD SAVE<{Int(v[0])}, {Escape(v[1])}>;";

    /// <summary>Board-level commands, reached through the controller.</summary>
    public static IReadOnlyList<CommandSpec> System() =>
    [
        Simple("SYS PING", "SYS PING;  ·  expects OK PONG"),
        Simple("SYS ID", "SYS ID;  ·  identity and peripheral geometry"),
        Simple("SYS STAT", "SYS STAT;  ·  command, error and lit-pixel counters"),
        Simple("SYS RESET", "SYS RESET;  ·  clears every peripheral and the counters"),
    ];

    private static CommandSpec Simple(string statement, string summary) =>
        new(statement, summary, [], _ => Wire.GetBytes(statement + ";"), _ => statement + ";");

    /// <summary>
    /// Escapes the characters that would otherwise terminate the statement or close the
    /// argument block, matching <see cref="AsciiSyntax"/> on the device side.
    /// </summary>
    private static string Escape(string text) => text
        .Replace("\\", "\\\\")
        .Replace(";", "\\;")
        .Replace("<", "\\<")
        .Replace(">", "\\>");

    private static string Int(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : value.Trim();

    private static string Hex(string value)
    {
        string text = value.Trim();
        return text.StartsWith('#') ? "0x" + text[1..] : text;
    }

    private static string Bool(string value) =>
        bool.TryParse(value, out bool parsed) && parsed ? "true" : "false";
}
