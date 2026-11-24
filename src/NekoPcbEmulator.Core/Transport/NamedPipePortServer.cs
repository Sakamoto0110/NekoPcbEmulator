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
    /// <summary>How many clients one board serves at a time.</summary>
    private const int MaxInstances = 8;

    /// <summary>HRESULT of <c>ERROR_PIPE_BUSY</c>: every instance of the pipe is taken.</summary>
    private const int PipeBusy = unchecked((int)0x800700E7);

    private static readonly TimeSpan FullRetryDelay = TimeSpan.FromMilliseconds(50);

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
        var server = await TakePendingAsync(ct).ConfigureAwait(false);

        try
        {
            await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // An instance counts against the limit until it is disposed, so one left to the
            // finalizer would take the place of a client.
            server.Dispose();
            throw;
        }

        // Have the next instance ready before handing this one off, otherwise a client that
        // connects during the handover gets a "pipe busy" error. With every instance taken
        // there is no next one, which must not cost this client its connection: the following
        // AcceptAsync picks it up, and reports anything other than being full.
        try { CreatePending(); } catch { /* retried by the next accept */ }

        string peer;
        try { peer = $"pid {server.GetImpersonationUserName()}"; }
        catch { peer = "local"; }
        return (server, peer);
    }

    /// <summary>
    /// Takes the instance the next client will connect to, waiting for a client to leave when
    /// all of them are in use. Until then Windows answers further clients with "pipe busy",
    /// which they can wait out.
    /// </summary>
    private async Task<NamedPipeServerStream> TakePendingAsync(CancellationToken ct)
    {
        bool reported = false;

        while (true)
        {
            try
            {
                lock (_pendingGate)
                {
                    ct.ThrowIfCancellationRequested();
                    var server = _pending ?? CreatePendingCore();
                    _pending = null;
                    return server;
                }
            }
            catch (IOException ex) when (ex.HResult == PipeBusy)
            {
                if (!reported)
                    Log.Write(Source, LogLevel.Warn, $"all {MaxInstances} pipe clients connected; new ones wait until one leaves");
                reported = true;
            }

            // Polling, because the instances are shared by everyone serving this pipe name and
            // Windows does not signal when one is released.
            await Task.Delay(FullRetryDelay, ct).ConfigureAwait(false);
        }
    }

    private void CreatePending()
    {
        lock (_pendingGate)
        {
            // Unbind has already run, or is waiting for the gate, once the port is stopping.
            if (Stopping.IsCancellationRequested) return;
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
