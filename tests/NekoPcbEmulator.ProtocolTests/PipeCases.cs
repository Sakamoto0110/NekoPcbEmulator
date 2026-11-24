using System.IO.Pipes;
using System.Text;
using NekoPcbEmulator.Core;
using NekoPcbEmulator.Core.Devices.PcbA;

namespace NekoPcbEmulator.ProtocolTests;

/// <summary>Client limit and recovery of the named pipe transport, over an isolated PCB-A host.</summary>
internal sealed class PipeCases : CaseRunner
{
    private const int MaxClients = 8;
    private const int TimeoutMs = 3000;

    public void Run()
    {
        Case("Pipe", "All 8 concurrent clients are served", () =>
        {
            using var board = new Board();
            board.ConnectAll();
            board.PingAll();
            Equal(board.Host.ClientCount, MaxClients);
        });
        Case("Pipe", "A 9th client is refused and the 8 already connected keep working", () =>
        {
            using var board = new Board();
            board.ConnectAll();
            board.PingAll();
            Require(board.TryConnect(300) is null, "A 9th client was accepted");
            board.PingAll();
        });
        Case("Pipe", "A waiting 9th client is served once another client leaves", () =>
        {
            using var board = new Board();
            board.ConnectAll();
            board.PingAll();

            // Repeated, because a slot that leaks on recovery only shows up later.
            for (int round = 1; round <= MaxClients + 2; round++)
            {
                var waiting = Task.Run(() => board.TryConnect(TimeoutMs));
                Thread.Sleep(50);
                board.Clients[0].Dispose();
                board.Clients.RemoveAt(0);
                var next = waiting.GetAwaiter().GetResult();
                Require(next is not null, $"Round {round}: the waiting client never got in");
                board.Clients.Add(next!);
                board.PingAll();
            }
        });
        Case("Pipe", "Being full is logged once, not repeated as accept errors", () =>
        {
            using var board = new Board();
            board.ConnectAll();
            board.PingAll();
            Thread.Sleep(700);
            var entries = board.Log.Drain(int.MaxValue);
            Equal(entries.Count(entry => entry.Level == LogLevel.Error), 0);
            Equal(entries.Count(entry => entry.Level == LogLevel.Warn), 1);
        });
        Case("Pipe", "Clients that connect and leave at once do not use up the limit", () =>
        {
            using var board = new Board();
            for (int i = 1; i <= MaxClients * 4; i++)
            {
                using var brief = board.TryConnect(TimeoutMs);
                Require(brief is not null, $"Brief client {i} could not connect");
            }
            board.ConnectAll();
            board.PingAll();
        });
        Case("Pipe", "Power cycling does not use up the limit", () =>
        {
            using var board = new Board();
            for (int i = 0; i < MaxClients * 2; i++)
            {
                board.Host.PowerOff();
                board.Host.PowerOn();
            }
            board.ConnectAll();
            board.PingAll();
        });
    }

    private sealed class Board : IDisposable
    {
        // Unique, so a running emulator or a parallel test run cannot use up the instances.
        private readonly string _pipeName = "protocol-tests-" + Guid.NewGuid().ToString("N");

        public Board()
        {
            Host = new PcbHost(new PcbADevice(Log), 0, _pipeName) { Kind = PortKind.NamedPipe };
            Host.PowerOn();
        }

        public LogSink Log { get; } = new();

        public PcbHost Host { get; }

        public List<NamedPipeClientStream> Clients { get; } = [];

        public void ConnectAll()
        {
            for (int i = 1; i <= MaxClients; i++)
            {
                var client = TryConnect(TimeoutMs);
                Require(client is not null, $"Client {i} could not connect");
                Clients.Add(client!);
            }
        }

        public void PingAll()
        {
            for (int i = 0; i < Clients.Count; i++) Ping(Clients[i], $"client {i + 1}");
        }

        public NamedPipeClientStream? TryConnect(int timeoutMs)
        {
            var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                client.Connect(timeoutMs);
                return client;
            }
            catch (TimeoutException)
            {
                client.Dispose();
                return null;
            }
        }

        private static void Ping(NamedPipeClientStream client, string who)
        {
            // Pipe streams have no read timeout, and an unserved client never sees an error.
            using var timeout = new CancellationTokenSource(TimeoutMs);
            var reply = new byte["OK PONG;\r\n".Length];
            try
            {
                client.WriteAsync("SYS PING;"u8.ToArray(), timeout.Token).AsTask().GetAwaiter().GetResult();
                client.ReadExactlyAsync(reply, timeout.Token).AsTask().GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("No reply for " + who);
            }
            Equal(Encoding.ASCII.GetString(reply), "OK PONG;\r\n");
        }

        public void Dispose()
        {
            foreach (var client in Clients) client.Dispose();
            Host.Dispose();
        }
    }
}
