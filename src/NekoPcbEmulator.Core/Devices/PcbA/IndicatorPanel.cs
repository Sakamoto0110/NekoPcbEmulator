namespace NekoPcbEmulator.Core.Devices.PcbA;

/// <summary>
/// One indicator. <see cref="DurationMs"/> is 0 when the LED is held on (or off), which is
/// what separates "no timeout" from "a timeout about to expire".
/// </summary>
public readonly record struct LedState(Rgba Color, bool On, int RemainingMs = 0, int DurationMs = 0)
{
    /// <summary>What the LED actually emits: the switch gates the stored colour.</summary>
    public Rgba Emitted => On ? Color : Rgba.Off;

    public bool IsTimed => On && DurationMs > 0;

    /// <summary>1 when the timeout has just started, 0 at expiry. Drives the countdown ring.</summary>
    public float RemainingFraction => DurationMs <= 0 ? 1f : Math.Clamp(RemainingMs / (float)DurationMs, 0f, 1f);
}

/// <summary>
/// The 4x4 grid of RGBA indicators. Index is row-major with the origin top-left:
/// <c>index = row * 4 + column</c>.
///
/// A timeout is exactly a delayed <c>state = false</c>: it opens the switch and leaves the
/// stored colour alone. Deadlines are absolute on the monotonic
/// <see cref="Environment.TickCount64"/> clock and applied lazily whenever the state is read,
/// so no background timer is needed.
/// </summary>
public sealed class IndicatorPanel
{
    public const int Rows = 4;
    public const int Columns = 4;
    public const int Count = Rows * Columns;

    /// <summary>Same ceiling as PCB-B's 16-bit duration, so one client-side range fits both boards.</summary>
    public const int MaxDurationMs = ushort.MaxValue;

    private readonly LedState[] _leds = new LedState[Count];
    private readonly long[] _deadlines = new long[Count];

    public static bool IsValidIndex(int index) => index is >= 0 and < Count;

    public static bool IsValidDuration(int durationMs) => durationMs is >= 0 and <= MaxDurationMs;

    /// <summary>Replaces the whole LED, including any timeout still pending on it.</summary>
    public void Set(int index, Rgba color, bool on, int durationMs)
    {
        bool timed = on && durationMs > 0;
        _leds[index] = new LedState(color, on, 0, timed ? durationMs : 0);
        _deadlines[index] = timed ? Environment.TickCount64 + durationMs : 0;
    }

    public void Clear()
    {
        Array.Clear(_leds);
        Array.Clear(_deadlines);
    }

    /// <summary>True while at least one LED is counting down, i.e. the view needs to keep repainting.</summary>
    public bool HasPendingTimeouts()
    {
        foreach (long deadline in _deadlines)
            if (deadline != 0) return true;
        return false;
    }

    /// <summary>Switches off everything whose deadline has passed. Returns how many expired.</summary>
    public int ApplyTimeouts()
    {
        long now = Environment.TickCount64;
        int expired = 0;

        for (int i = 0; i < Count; i++)
        {
            if (_deadlines[i] == 0 || _deadlines[i] > now) continue;
            Set(i, _leds[i].Color, on: false, durationMs: 0);
            expired++;
        }
        return expired;
    }

    public LedState[] Snapshot()
    {
        long now = Environment.TickCount64;
        var leds = new LedState[Count];

        for (int i = 0; i < Count; i++)
            leds[i] = _leds[i] with { RemainingMs = _deadlines[i] == 0 ? 0 : (int)Math.Max(0, _deadlines[i] - now) };
        return leds;
    }
}
