using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace NekoPcbEmulator.ProtocolTests;

/// <summary>
/// Drives the real application executable the way a supervising process does, to check that
/// startup through <c>--power</c> never waits for someone to dismiss a dialog.
/// </summary>
internal sealed class StartupCases(string application) : CaseRunner
{
    private const int PowerOnFailedExitCode = 2;
    private const int TimeoutMs = 20_000;

    public void Run()
    {
        Case("Startup", "--no-main with free ports serves both boards and stays running", () =>
        {
            int portA = FreePort(), portB = FreePort();
            using var app = Launch(portA, portB, "--no-main");
            app.WaitForPort(portA);
            app.WaitForPort(portB);
            Require(!app.Process.WaitForExit(500), "The application exited although both boards are on");
        });
        Case("Startup", "--no-main exits with a failure code when the first board's port is busy", () =>
        {
            using var busy = Occupy();
            using var app = Launch(Port(busy), FreePort(), "--no-main");
            string error = app.WaitForFailure();
            Require(error.Contains("PCB-A") && !error.Contains("PCB-B"), "Unexpected error output: " + error);
        });
        Case("Startup", "--no-main exits with a failure code when the last board's port is busy", () =>
        {
            using var busy = Occupy();
            using var app = Launch(FreePort(), Port(busy), "--no-main");
            string error = app.WaitForFailure();
            Require(error.Contains("PCB-B") && !error.Contains("PCB-A"), "Unexpected error output: " + error);
        });
        Case("Startup", "--no-main tries every board before exiting and reports each failure", () =>
        {
            using var busyA = Occupy();
            using var busyB = Occupy();
            using var app = Launch(Port(busyA), Port(busyB), "--no-main");
            string error = app.WaitForFailure();
            Require(error.Contains("PCB-A") && error.Contains("PCB-B"), "Unexpected error output: " + error);
        });
        Case("Startup", "With the launcher visible a busy port does not stop the other board", () =>
        {
            using var busy = Occupy();
            int portB = FreePort();
            using var app = Launch(Port(busy), portB);
            app.WaitForPort(portB);
            using (var b = WireClient.Tcp(portB))
            {
                b.Send(WireClient.Frame(1, 0x10, []));
                var reply = b.ReadBinary();
                Equal(reply.Sequence, (byte)1);
                Equal(reply.Command, (byte)0x90);
            }
            Require(!app.Process.WaitForExit(500), "The application exited although its launcher is available");
        });
    }

    private RunningApp Launch(int portA, int portB, params string[] extra) => new(application,
        ["--power", "all", "--port-a", portA.ToString(), "--port-b", portB.ToString(), .. extra]);

    private static TcpListener Occupy()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return listener;
    }

    private static int Port(TcpListener listener) => ((IPEndPoint)listener.LocalEndpoint).Port;

    /// <summary>
    /// Picks from below the Windows ephemeral range. A probe of a closed ephemeral port can be
    /// given that very port as its source and connect to itself, which looks like an open board
    /// and also takes the port away from the application.
    /// </summary>
    private static int FreePort()
    {
        while (true)
        {
            int port = Random.Shared.Next(20_000, 40_000);
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return port;
            }
            catch (SocketException) { /* taken; try another */ }
        }
    }

    private sealed class RunningApp : IDisposable
    {
        private readonly Task<string> _error;

        public RunningApp(string path, IEnumerable<string> arguments)
        {
            var start = new ProcessStartInfo(path, arguments) { RedirectStandardError = true, UseShellExecute = false };
            Process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + path);
            _error = Process.StandardError.ReadToEndAsync();
        }

        public Process Process { get; }

        public void WaitForPort(int port)
        {
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < TimeoutMs)
            {
                Require(!Process.HasExited, $"The application exited before opening port {port}");
                try
                {
                    using var probe = new TcpClient("127.0.0.1", port);
                    return;
                }
                catch (SocketException)
                {
                    Thread.Sleep(100);
                }
            }
            throw new TimeoutException($"Port {port} did not open");
        }

        public string WaitForFailure()
        {
            Require(Process.WaitForExit(TimeoutMs), "The application is still running");
            Equal(Process.ExitCode, PowerOnFailedExitCode);
            return _error.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                Process.WaitForExit();
            }
            Process.Dispose();
        }
    }
}
