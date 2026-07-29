namespace NekoPcbEmulator.Core.Transport;

/// <summary>A single client attached to a powered PCB's port.</summary>
public interface IPortConnection
{
    /// <summary>Stable short id, e.g. <c>#3</c>. Shown in logs and on the board silkscreen.</summary>
    string Id { get; }

    /// <summary>Human readable peer description, e.g. <c>127.0.0.1:51544</c>.</summary>
    string Peer { get; }

    bool IsOpen { get; }

    long BytesReceived { get; }

    long BytesSent { get; }

    /// <summary>
    /// Writes to the client. Blocking and ordered: it is called from the connection's own
    /// read pump, so a slow reader applies backpressure to that one client only.
    /// </summary>
    void Send(ReadOnlySpan<byte> data);

    void Close();
}

/// <summary>
/// Implemented by the devices. The port server calls these on the connection's read pump
/// thread; a device is responsible for its own locking.
/// </summary>
public interface IPortHandler
{
    void OnConnected(IPortConnection connection);

    void OnDisconnected(IPortConnection connection);

    void OnReceived(IPortConnection connection, ReadOnlySpan<byte> data);
}
