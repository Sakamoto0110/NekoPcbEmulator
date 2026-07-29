using System.Collections.Concurrent;

namespace NekoPcbEmulator.Core.Transport;

/// <summary>
/// Base for the emulated ports. Both transports are stream oriented, so all the client
/// bookkeeping and the read pump live here and subclasses only supply an accept loop.
/// </summary>
public abstract class PortServer : IDisposable
{
    private readonly ConcurrentDictionary<string, StreamConnection> _connections = new();
    private readonly CancellationTokenSource _cts = new();
    private int _nextConnectionId;
    private Task? _acceptLoop;
    private bool _disposed;

    protected PortServer(IPortHandler handler, LogSink log, string source)
    {
        Handler = handler;
        Log = log;
        Source = source;
    }

    protected IPortHandler Handler { get; }

    protected LogSink Log { get; }

    protected string Source { get; }

    protected CancellationToken Stopping => _cts.Token;

    /// <summary>Display form of the endpoint, e.g. <c>tcp://127.0.0.1:5001</c>.</summary>
    public abstract string Endpoint { get; }

    public int ConnectionCount => _connections.Count;

    public IReadOnlyCollection<IPortConnection> Connections => _connections.Values.ToArray();

    /// <summary>Binds the port. Throws if the endpoint is already taken.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_acceptLoop is not null) return;

        Bind();
        Log.Write(Source, LogLevel.Info, $"port open at {Endpoint}");
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public void Broadcast(ReadOnlySpan<byte> data)
    {
        foreach (var connection in _connections.Values)
            connection.Send(data);
    }

    /// <summary>Binds the underlying listener synchronously so bind failures surface to the caller.</summary>
    protected abstract void Bind();

    /// <summary>Waits for the next client and returns its stream, or null once shutting down.</summary>
    protected abstract Task<(Stream Stream, string Peer)?> AcceptAsync(CancellationToken ct);

    protected abstract void Unbind();

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var accepted = await AcceptAsync(ct).ConfigureAwait(false);
                if (accepted is null) break;

                var id = "#" + Interlocked.Increment(ref _nextConnectionId);
                var connection = new StreamConnection(id, accepted.Value.Peer, accepted.Value.Stream, this);
                _connections[id] = connection;
                Log.Write(Source, LogLevel.Info, $"client {id} connected from {connection.Peer}");
                Handler.OnConnected(connection);
                _ = Task.Run(() => connection.PumpAsync(ct), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Log.Write(Source, LogLevel.Error, $"accept failed: {ex.Message}");
                await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private void OnConnectionClosed(StreamConnection connection)
    {
        if (!_connections.TryRemove(connection.Id, out _)) return;
        Log.Write(Source, LogLevel.Info, $"client {connection.Id} disconnected");
        Handler.OnDisconnected(connection);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        try { Unbind(); } catch { /* shutting down */ }

        foreach (var connection in _connections.Values)
            connection.Close();
        _connections.Clear();

        try { _acceptLoop?.Wait(TimeSpan.FromSeconds(2)); } catch { /* shutting down */ }
        _cts.Dispose();
        Log.Write(Source, LogLevel.Info, "port closed");
        GC.SuppressFinalize(this);
    }

    private sealed class StreamConnection : IPortConnection
    {
        private readonly Stream _stream;
        private readonly PortServer _server;
        private readonly Lock _writeGate = new();
        private long _bytesReceived;
        private long _bytesSent;
        private volatile bool _open = true;

        public StreamConnection(string id, string peer, Stream stream, PortServer server)
        {
            Id = id;
            Peer = peer;
            _stream = stream;
            _server = server;
        }

        public string Id { get; }

        public string Peer { get; }

        public bool IsOpen => _open;

        public long BytesReceived => Interlocked.Read(ref _bytesReceived);

        public long BytesSent => Interlocked.Read(ref _bytesSent);

        public void Send(ReadOnlySpan<byte> data)
        {
            if (!_open || data.IsEmpty) return;
            try
            {
                lock (_writeGate)
                {
                    _stream.Write(data);
                    _stream.Flush();
                }
                Interlocked.Add(ref _bytesSent, data.Length);
            }
            catch (Exception)
            {
                Close();
            }
        }

        public async Task PumpAsync(CancellationToken ct)
        {
            var buffer = new byte[8192];
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int read = await _stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                    if (read <= 0) break;

                    Interlocked.Add(ref _bytesReceived, read);
                    try
                    {
                        _server.Handler.OnReceived(this, buffer.AsSpan(0, read));
                    }
                    catch (Exception ex)
                    {
                        _server.Log.Write(_server.Source, LogLevel.Error, $"{Id} handler threw: {ex.Message}");
                    }
                }
            }
            catch (Exception)
            {
                // Client vanished or the port is closing; either way the connection is done.
            }
            finally
            {
                Close();
            }
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            try { _stream.Dispose(); } catch { /* already gone */ }
            _server.OnConnectionClosed(this);
        }
    }
}
