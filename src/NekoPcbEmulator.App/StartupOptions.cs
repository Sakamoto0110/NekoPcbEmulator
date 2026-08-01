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

    /// <summary>Serial port for PCB-A. Only used when <see cref="Kind"/> is <see cref="PortKind.Serial"/>.</summary>
    public string ComPortA { get; private init; } = "COM9";

    /// <summary>Serial port for PCB-B.</summary>
    public string ComPortB { get; private init; } = "COM10";

    /// <summary>
    /// Hides the launcher window while keeping the powered boards, their windows and their
    /// ports fully live. Intended for a supervising process that drives the emulator as a
    /// sidecar and only wants the board views on screen.
    ///
    /// Only meaningful together with <c>--power</c>: with no board powered there would be
    /// no window at all and no way to reach the application.
    /// </summary>
    public bool HideLauncher { get; private init; }

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

                case "--serial":
                case "--com0com":
                    options = options with { RequestedSerial = true };
                    break;

                case "--com-a" when i + 1 < args.Length:
                    options = options with { ComPortA = args[++i] };
                    break;

                case "--com-b" when i + 1 < args.Length:
                    options = options with { ComPortB = args[++i] };
                    break;

                case "--no-main":
                case "--hide-launcher":
                    options = options with { HideLauncher = true };
                    break;
            }
        }

        return options.ResolveSerial();
    }

    /// <summary>Set by <c>--serial</c>, before the ports are known to be openable.</summary>
    private bool RequestedSerial { get; init; }

    /// <summary>
    /// Applies <c>--serial</c> only when the requested COM ports can actually be opened.
    ///
    /// The ports are resolved last, after <c>--com-a</c>/<c>--com-b</c> have been parsed, and
    /// the check is a real open rather than "is com0com installed": an installed package whose
    /// driver will not load leaves ports that exist on paper and fail on contact. Falling back
    /// to TCP beats starting up and then failing to bind anything.
    /// </summary>
    private StartupOptions ResolveSerial()
    {
        if (!RequestedSerial) return this;

        var status = Com0ComDetector.Evaluate(ComPortA, ComPortB);
        SerialDiagnostic = status.Detail;

        return status.IsUsable ? this with { Kind = PortKind.Serial } : this;
    }

    /// <summary>Why <c>--serial</c> was or was not honoured. Surfaced in the traffic log.</summary>
    public string? SerialDiagnostic { get; private set; }
}
