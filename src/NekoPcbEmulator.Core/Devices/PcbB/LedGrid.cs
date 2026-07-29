namespace NekoPcbEmulator.Core.Devices.PcbB;

public readonly record struct LedCell(bool On, Rgba Color, int RemainingMs, int DurationMs)
{
    /// <summary>1 when the timeout has just started, 0 at expiry. Drives the countdown ring.</summary>
    public float RemainingFraction => DurationMs <= 0 ? 1f : Math.Clamp(RemainingMs / (float)DurationMs, 0f, 1f);

    public bool IsTimed => On && DurationMs > 0;
}

/// <summary>
/// The 5x5 LED grid. Index is row-major with the origin top-left:
/// <c>index = row * 5 + column</c>.
///
/// Timeouts are stored as an absolute deadline on the monotonic
/// <see cref="Environment.TickCount64"/> clock and applied lazily whenever the state is read,
/// so the grid stays correct without a background timer.
/// </summary>
public sealed class LedGrid
{
    public const int Rows = 5;
    public const int Columns = 5;
    public const int Count = Rows * Columns;

    private readonly bool[] _on = new bool[Count];
    private readonly Rgba[] _colors = new Rgba[Count];
    private readonly long[] _deadlines = new long[Count];
    private readonly int[] _durations = new int[Count];

    public static bool IsValidIndex(int index) => index >= 0 && index < Count;

    public static int IndexOf(int row, int column) => row * Columns + column;

    public void Set(int index, Rgba color, int durationMs)
    {
        _on[index] = true;
        _colors[index] = color;
        _durations[index] = durationMs;
        _deadlines[index] = durationMs > 0 ? Environment.TickCount64 + durationMs : 0;
    }

    public void Clear(int index)
    {
        _on[index] = false;
        _colors[index] = Rgba.Off;
        _deadlines[index] = 0;
        _durations[index] = 0;
    }

    public void ClearAll()
    {
        for (int i = 0; i < Count; i++) Clear(i);
    }

    /// <summary>Turns off everything whose deadline has passed. Returns how many expired.</summary>
    public int ApplyTimeouts()
    {
        long now = Environment.TickCount64;
        int expired = 0;

        for (int i = 0; i < Count; i++)
        {
            if (!_on[i] || _deadlines[i] == 0 || _deadlines[i] > now) continue;
            Clear(i);
            expired++;
        }
        return expired;
    }

    /// <summary>True while at least one LED is counting down, i.e. the view needs to keep repainting.</summary>
    public bool HasPendingTimeouts()
    {
        for (int i = 0; i < Count; i++)
            if (_on[i] && _deadlines[i] != 0) return true;
        return false;
    }

    public LedCell[] Snapshot()
    {
        long now = Environment.TickCount64;
        var cells = new LedCell[Count];

        for (int i = 0; i < Count; i++)
        {
            int remaining = _deadlines[i] == 0 ? 0 : (int)Math.Max(0, _deadlines[i] - now);
            cells[i] = new LedCell(_on[i], _colors[i], remaining, _durations[i]);
        }
        return cells;
    }

    public int LitCount()
    {
        int count = 0;
        for (int i = 0; i < Count; i++)
            if (_on[i]) count++;
        return count;
    }
}
