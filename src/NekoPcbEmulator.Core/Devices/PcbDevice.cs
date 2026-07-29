using NekoPcbEmulator.Core.Transport;

namespace NekoPcbEmulator.Core.Devices;

/// <summary>
/// Base for an emulated board. Device state is mutated from socket threads and read from the
/// UI thread, so every subclass guards its state with <see cref="Gate"/> and bumps
/// <see cref="StateVersion"/> on change. The UI polls that version instead of subscribing to
/// events, which keeps repaints batched and the core free of any UI thread affinity.
/// </summary>
public abstract class PcbDevice : IPortHandler
{
    private long _stateVersion;

    protected PcbDevice(LogSink log) => Log = log;

    protected Lock Gate { get; } = new();

    public LogSink Log { get; }

    /// <summary>Short identifier used on the silkscreen and in log lines, e.g. <c>PCB-A</c>.</summary>
    public abstract string Id { get; }

    public abstract string DisplayName { get; }

    public abstract string ProtocolName { get; }

    /// <summary>Incremented whenever anything visible changes.</summary>
    public long StateVersion => Interlocked.Read(ref _stateVersion);

    /// <summary>
    /// True when state can change without any incoming traffic (PCB-B's LED timeouts), which
    /// tells the window to keep repainting instead of waiting on <see cref="StateVersion"/>.
    /// </summary>
    public virtual bool IsAnimating => false;

    protected void Touch() => Interlocked.Increment(ref _stateVersion);

    public abstract void Reset();

    public virtual void OnConnected(IPortConnection connection) { }

    public virtual void OnDisconnected(IPortConnection connection) { }

    public abstract void OnReceived(IPortConnection connection, ReadOnlySpan<byte> data);
}
