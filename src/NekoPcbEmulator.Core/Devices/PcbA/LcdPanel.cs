namespace NekoPcbEmulator.Core.Devices.PcbA;

/// <summary>
/// A character LCD in the shape of a classic 2004 module: 20 columns by 4 rows, plus five
/// memory slots the host can stage text into.
///
/// Three buffers are involved, matching the command set:
/// <list type="bullet">
///   <item><c>Slots</c> — persistent storage written by <c>LCD SAVE</c>.</item>
///   <item><c>Loaded</c> — the staging buffer filled by <c>LCD LOAD</c> / <c>LCD TEXT</c>.</item>
///   <item><c>Displayed</c> — what the glass actually shows, published by <c>LCD SHOW</c>.</item>
/// </list>
/// <c>LCD CLR</c> blanks the glass only; the staging buffer survives, so
/// <c>LCD CLR; LCD SHOW;</c> brings the same text back.
/// </summary>
public sealed class LcdPanel
{
    public const int Columns = 20;
    public const int Rows = 4;
    public const int SlotCount = 5;

    private readonly string[] _slots = new string[SlotCount];

    public LcdPanel() => Clear();

    public string Loaded { get; private set; } = "";

    public string Displayed { get; private set; } = "";

    public string GetSlot(int index) => _slots[index];

    public static bool IsValidSlot(int index) => index >= 0 && index < SlotCount;

    public void Save(int index, string text) => _slots[index] = text;

    public void Load(int index) => Loaded = _slots[index];

    public void SetText(string text) => Loaded = text;

    public void Show() => Displayed = Loaded;

    /// <summary>Blanks the glass. The staging buffer and the slots are untouched.</summary>
    public void ClearDisplay() => Displayed = "";

    public void Clear()
    {
        Array.Fill(_slots, "");
        Loaded = "";
        Displayed = "";
    }

    /// <summary>
    /// Lays the displayed text out over the character grid. <c>\n</c> forces a line break,
    /// anything longer than a row wraps, and content past the last row is dropped — the same
    /// visible behaviour as a real module with no scrolling.
    /// </summary>
    public string[] RenderLines()
    {
        var lines = new string[Rows];
        Array.Fill(lines, "");

        int row = 0;
        foreach (string source in Displayed.Replace("\r\n", "\n").Split('\n'))
        {
            if (row >= Rows) break;

            if (source.Length == 0)
            {
                lines[row++] = "";
                continue;
            }

            for (int offset = 0; offset < source.Length && row < Rows; offset += Columns)
                lines[row++] = source.Substring(offset, Math.Min(Columns, source.Length - offset));
        }

        return lines;
    }

    public LcdSnapshot Snapshot() => new(RenderLines(), Loaded, [.. _slots]);
}

public readonly record struct LcdSnapshot(string[] Lines, string Loaded, string[] Slots);
