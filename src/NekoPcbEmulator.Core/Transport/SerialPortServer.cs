using System.IO.Ports;

namespace NekoPcbEmulator.Core.Transport;

/// <summary>
/// Serves a board over a real serial port — in practice one end of a com0com virtual pair,
/// with the test suite attached to the other end.
///
/// A serial line has no accept step: the port either opens or it does not, and there is
/// exactly one implicit peer. So this yields a single connection when the port opens and then
/// parks, and it never learns that the peer went away — which is correct serial behaviour, not
/// a gap. Anything that needs connect/disconnect semantics should use TCP or a named pipe.
/// </summary>
public sealed class SerialPortServer : PortServer
{
    private readonly string _portName;
    private readonly int _baudRate;
    private SerialPort? _port;

    public SerialPortServer(IPortHandler handler, LogSink log, string source, string portName, int baudRate = 115200)
        : base(handler, log, source)
    {
        _portName = portName;
        _baudRate = baudRate;
    }

    public string PortName => _portName;

    public override string Endpoint => "serial://" + _portName + "@" + _baudRate;

    protected override void Bind()
    {
        var port = new SerialPort(_portName, _baudRate, Parity.None, 8, StopBits.One)
        {
            // The read pump owns blocking; the stream must not time out underneath it.
            ReadTimeout = SerialPort.InfiniteTimeout,
            WriteTimeout = 2000,
            Handshake = Handshake.None,
            DtrEnable = true,
            RtsEnable = true,
        };

        port.Open();
        _port = port;
    }

    protected override async Task<(Stream Stream, string Peer)?> AcceptAsync(CancellationToken ct)
    {
        if (_port is null) return null;

        // A serial line has no accept, but the session on it can still end — a write fails
        // once the peer stops reading, and the base server tears that connection down. So the
        // line is offered again whenever nobody holds it, instead of the board going deaf
        // after its first client.
        while (!ct.IsCancellationRequested)
        {
            if (ConnectionCount == 0 && _port.IsOpen)
            {
                // The wrapper is essential: the base server disposes a connection's stream on
                // teardown, and disposing SerialPort.BaseStream closes the port itself.
                return (new NonClosingStream(_port.BaseStream), _portName);
            }

            try { await Task.Delay(200, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }

        return null;
    }

    /// <summary>
    /// Passes reads and writes through but refuses to close the underlying stream. Lets a
    /// per-connection lifetime sit on top of a port-wide stream without destroying it.
    /// </summary>
    private sealed class NonClosingStream : Stream
    {
        private readonly Stream _inner;

        public NonClosingStream(Stream inner) => _inner = inner;

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            _inner.ReadAsync(buffer, ct);

        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        public override void Write(ReadOnlySpan<byte> buffer) => _inner.Write(buffer);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            // Deliberately does not touch the port's stream.
        }
    }

    protected override void Unbind()
    {
        var port = _port;
        _port = null;

        if (port is null) return;
        try { if (port.IsOpen) port.Close(); } catch (Exception) { /* already gone */ }
        port.Dispose();
    }
}
