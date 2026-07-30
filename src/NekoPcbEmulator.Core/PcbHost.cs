using NekoPcbEmulator.Core.Devices;
using NekoPcbEmulator.Core.Transport;

namespace NekoPcbEmulator.Core;

public enum PortKind
{
    /// <summary>Loopback TCP. The default: anything can attach to it.</summary>
    Tcp,

    /// <summary>A Windows named pipe, which behaves more like a serial device.</summary>
    NamedPipe,
}

/// <summary>
/// Binds a board to an emulated port. "Powering on" a PCB means opening that port; powering
/// off closes it and drops every client, leaving the device state intact until reset.
/// </summary>
public sealed class PcbHost : IDisposable
{
    private readonly LoopbackConnection _loopback = new();
    private PortServer? _server;

    public PcbHost(PcbDevice device, int tcpPort, string pipeName)
    {
        Device = device;
        TcpPort = tcpPort;
        PipeName = pipeName;
    }

    public PcbDevice Device { get; }

    public PortKind Kind { get; set; } = PortKind.Tcp;

    /// <summary>Requested TCP port. 0 lets the OS pick one, reported back through <see cref="Endpoint"/>.</summary>
    public int TcpPort { get; set; }

    public string PipeName { get; set; }

    public bool IsPowered => _server is not null;

    public string Endpoint => _server?.Endpoint ?? "offline";

    public int ClientCount => _server?.ConnectionCount ?? 0;

    public IReadOnlyCollection<IPortConnection> Clients => _server?.Connections ?? [];

    public event EventHandler? PowerChanged;

    /// <summary>
    /// Feeds bytes to the device as if they had arrived from a client, and returns the reply.
    /// Used by the board window's command panel: routing it through the real parser means the
    /// UI can only reach states the protocol actually allows, and every command shows up in
    /// the traffic log like any other.
    /// </summary>
    public byte[] Inject(ReadOnlySpan<byte> data)
    {
        Device.OnReceived(_loopback, data);
        return _loopback.LastReply;
    }

    /// <summary>Opens the port. Throws if the endpoint is already in use; the host stays off.</summary>
    public void PowerOn()
    {
        if (_server is not null) return;

        PortServer server = Kind switch
        {
            PortKind.NamedPipe => new NamedPipePortServer(Device, Device.Log, Device.Id, PipeName),
            _ => new TcpPortServer(Device, Device.Log, Device.Id, TcpPort),
        };

        try
        {
            server.Start();
        }
        catch
        {
            server.Dispose();
            throw;
        }

        _server = server;
        PowerChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PowerOff()
    {
        if (_server is null) return;

        _server.Dispose();
        _server = null;
        PowerChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => PowerOff();
}
