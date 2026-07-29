using NekoPcbEmulator.Core;

namespace NekoPcbEmulator.App;

/// <summary>
/// Command line for the launcher. The interesting one is <c>--power</c>: it opens the ports
/// and the board windows at startup, so an external test suite can be pointed at a known
/// endpoint without anyone clicking anything.
/// </summary>
public sealed record StartupOptions
{
    public bool PowerA { get; private init; }

    public bool PowerB { get; private init; }

    public int PortA { get; private init; } = 5001;

    public int PortB { get; private init; } = 5002;

    public PortKind Kind { get; private init; } = PortKind.Tcp;

    public static StartupOptions Parse(string[] args)
    {
        var options = new StartupOptions();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--power" when i + 1 < args.Length:
                {
                    string value = args[++i].ToLowerInvariant();
                    bool all = value is "all" or "ab" or "a,b";
                    options = options with
                    {
                        PowerA = all || value.Contains('a'),
                        PowerB = all || value.Contains('b'),
                    };
                    break;
                }

                case "--port-a" when i + 1 < args.Length && int.TryParse(args[i + 1], out int portA):
                    options = options with { PortA = portA };
                    i++;
                    break;

                case "--port-b" when i + 1 < args.Length && int.TryParse(args[i + 1], out int portB):
                    options = options with { PortB = portB };
                    i++;
                    break;

                case "--pipe":
                    options = options with { Kind = PortKind.NamedPipe };
                    break;
            }
        }

        return options;
    }
}
