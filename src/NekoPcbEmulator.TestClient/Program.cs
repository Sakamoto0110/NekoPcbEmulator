using System.IO.Pipes;
using System.Net.Sockets;

namespace NekoPcbEmulator.TestClient;

/// <summary>
/// Reference client for both boards. It doubles as a smoke test for the emulator and as a
/// worked example of framing and parsing each protocol from the host side.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var options = ClientOptions.Parse(args);
        if (options is null)
        {
            PrintUsage();
            return 1;
        }

        Stream stream;
        try
        {
            stream = await ConnectAsync(options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"could not connect to {options.Describe()}: {ex.Message}");
            Console.Error.WriteLine("is the board powered on in the emulator?");
            return 2;
        }

        Console.WriteLine($"connected to {options.Describe()} ({options.Board})");
        Console.WriteLine(options.Demo ? "running demo sequence" : "interactive mode — type 'help', Ctrl+C to quit");
        Console.WriteLine();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await using (stream)
            {
                if (options.Board == BoardKind.A)
                    await AsciiClient.RunAsync(stream, options.Demo, cts.Token);
                else
                    await BinaryClient.RunAsync(stream, options.Demo, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C.
        }

        Console.WriteLine();
        Console.WriteLine("disconnected");
        return 0;
    }

    private static async Task<Stream> ConnectAsync(ClientOptions options)
    {
        if (options.Pipe is not null)
        {
            var pipe = new NamedPipeClientStream(".", options.Pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(3000);
            return pipe;
        }

        var client = new TcpClient();
        await client.ConnectAsync(options.Host, options.Port);
        client.NoDelay = true;
        return client.GetStream();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            usage: NekoPcbEmulator.TestClient <a|b> [options]

              a                 PCB-A, raw ASCII (default port 5001)
              b                 PCB-B, framed binary (default port 5002)

            options:
              --demo            run a scripted sequence instead of the interactive prompt
              --port <n>        TCP port to connect to
              --host <name>     TCP host (default 127.0.0.1)
              --pipe [<name>]   use a named pipe instead of TCP (default pcb-a / pcb-b)

            examples:
              NekoPcbEmulator.TestClient a --demo
              NekoPcbEmulator.TestClient b --port 5002
              NekoPcbEmulator.TestClient a --pipe
            """);
    }
}

internal enum BoardKind
{
    A,
    B,
}

internal sealed record ClientOptions(BoardKind Board, string Host, int Port, string? Pipe, bool Demo)
{
    public static ClientOptions? Parse(string[] args)
    {
        if (args.Length == 0) return null;

        BoardKind board;
        switch (args[0].ToLowerInvariant())
        {
            case "a" or "pcb-a":
                board = BoardKind.A;
                break;
            case "b" or "pcb-b":
                board = BoardKind.B;
                break;
            default:
                return null;
        }

        string host = "127.0.0.1";
        int port = board == BoardKind.A ? 5001 : 5002;
        string? pipe = null;
        bool demo = false;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--demo":
                    demo = true;
                    break;

                case "--host" when i + 1 < args.Length:
                    host = args[++i];
                    break;

                case "--port" when i + 1 < args.Length && int.TryParse(args[i + 1], out int parsed):
                    port = parsed;
                    i++;
                    break;

                case "--pipe":
                    // An optional name may follow; anything starting with '-' is the next flag.
                    pipe = i + 1 < args.Length && !args[i + 1].StartsWith('-')
                        ? args[++i]
                        : board == BoardKind.A ? "pcb-a" : "pcb-b";
                    break;

                default:
                    return null;
            }
        }

        return new ClientOptions(board, host, port, pipe, demo);
    }

    public string Describe() => Pipe is not null ? $@"\\.\pipe\{Pipe}" : $"tcp://{Host}:{Port}";
}
