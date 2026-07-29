using System.IO.Pipes;

namespace NekoPcbEmulator.Core.Transport;

/// <summary>
/// Alternative emulated port. A named pipe behaves much more like a real serial device than
/// a socket does (byte stream, no framing, no port numbers), so it is the better target when
/// the test suite wants to exercise a COM-port-shaped transport without installing a driver.
/// Clients open <c>\\.\pipe\{Name}</c>.
/// </summary>
public sealed class NamedPipePortServer : PortServer
{
    private const int MaxInstances = 8;

    private NamedPipeServerStream? _pending;
    private readonly Lock _pendingGate = new();

    public NamedPipePortServer(IPortHandler handler, LogSink log, string source, string pipeName)
        : base(handler, log, source)
    {
        Name = pipeName;
    }

    public string Name { get; }

    public override string Endpoint => $@"pipe://\\.\pipe\{Name}";

    protected override void Bind()
    {
        // Create the first instance eagerly so that a name collision fails inside Start().
        CreatePending();
    }

    protected override async Task<(Stream Stream, string Peer)?> AcceptAsync(CancellationToken ct)
    {
        NamedPipeServerStream server;
        lock (_pendingGate)
        {
            server = _pending ?? CreatePendingCore();
            _pending = null;
        }

        await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

        // Have the next instance ready before handing this one off, otherwise a client that
        // connects during the handover gets a "pipe busy" error.
        CreatePending();

        string peer;
        try { peer = $"pid {server.GetImpersonationUserName()}"; }
        catch { peer = "local"; }
        return (server, peer);
    }

    private void CreatePending()
    {
        lock (_pendingGate)
        {
            _pending ??= CreatePendingCore();
        }
    }

    private NamedPipeServerStream CreatePendingCore() => new(
        Name,
        PipeDirection.InOut,
        MaxInstances,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.WriteThrough);

    protected override void Unbind()
    {
        lock (_pendingGate)
        {
            _pending?.Dispose();
            _pending = null;
        }
    }
}
