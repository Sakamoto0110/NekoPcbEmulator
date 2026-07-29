using System.Net;
using System.Net.Sockets;

namespace NekoPcbEmulator.Core.Transport;

/// <summary>
/// The default emulated port: a loopback TCP listener. Any language can attach to it, which
/// is what makes it convenient as a target for an external RX/TX test suite.
/// </summary>
public sealed class TcpPortServer : PortServer
{
    private readonly int _requestedPort;
    private TcpListener? _listener;

    public TcpPortServer(IPortHandler handler, LogSink log, string source, int port)
        : base(handler, log, source)
    {
        _requestedPort = port;
    }

    /// <summary>The port actually bound. Differs from the requested one only when 0 was requested.</summary>
    public int Port { get; private set; }

    public override string Endpoint => $"tcp://127.0.0.1:{Port}";

    protected override void Bind()
    {
        var listener = new TcpListener(IPAddress.Loopback, _requestedPort);
        listener.Start();
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    protected override async Task<(Stream Stream, string Peer)?> AcceptAsync(CancellationToken ct)
    {
        if (_listener is null) return null;

        var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);

        // Commands are small and latency matters more than packing, so disable Nagle.
        client.NoDelay = true;
        return (client.GetStream(), client.Client.RemoteEndPoint?.ToString() ?? "?");
    }

    protected override void Unbind()
    {
        _listener?.Stop();
        _listener?.Dispose();
        _listener = null;
    }
}
