namespace NekoPcbEmulator.Core.Transport;

/// <summary>
/// A connection that exists only inside the process, used when the emulator's own UI sends a
/// command to a board.
///
/// It deliberately goes through <see cref="Devices.PcbDevice.OnReceived"/> like any socket
/// client, so a command issued from the board window is parsed, validated and logged by
/// exactly the same code path as one arriving over the wire. Anything else would let the UI
/// drive states the protocol cannot actually reach.
/// </summary>
public sealed class LoopbackConnection : IPortConnection
{
    private long _bytesReceived;
    private long _bytesSent;

    public string Id => "#ui";

    public string Peer => "board window";

    public bool IsOpen => true;

    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    public long BytesSent => Interlocked.Read(ref _bytesSent);

    /// <summary>The most recent reply, as raw bytes. The device has already logged it.</summary>
    public byte[] LastReply { get; private set; } = [];

    public void Send(ReadOnlySpan<byte> data)
    {
        LastReply = data.ToArray();
        Interlocked.Add(ref _bytesSent, data.Length);
    }

    internal void CountReceived(int count) => Interlocked.Add(ref _bytesReceived, count);

    public void Close()
    {
        // Nothing to release: the loopback lives as long as its host.
    }
}
