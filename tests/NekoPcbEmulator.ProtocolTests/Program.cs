using System.Text.Json;
using NekoPcbEmulator.Core;
using NekoPcbEmulator.Core.Devices.PcbA;
using NekoPcbEmulator.Core.Devices.PcbB;

namespace NekoPcbEmulator.ProtocolTests;

internal static class Program
{
    private static int Main(string[] args)
    {
        bool externalTcp = args.Contains("--external-tcp");
        bool external = externalTcp || args.Contains("--external");
        bool serial = !externalTcp && (external || args.Contains("--serial"));
        string mode = externalTcp ? "published-tcp" : external ? "published-serial" : serial ? "serial" : "tcp";
        string Value(string flag, string fallback)
        {
            int index = Array.IndexOf(args, flag);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        int Report(string name, List<CaseResult> results)
        {
            string report = Path.GetFullPath(Value("--report", $"artifacts/protocol-tests/{name}.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            File.WriteAllText(report, JsonSerializer.Serialize(new
            {
                Mode = name,
                Results = results,
            }, new JsonSerializerOptions { WriteIndented = true }));
            foreach (var group in results.GroupBy(result => result.Protocol))
                Console.WriteLine($"{(group.Key is "A" or "B" ? "Protocol " : "")}{group.Key}: {group.Count(result => result.Passed)}/{group.Count()} passed");
            Console.WriteLine("Report: " + report);
            return results.All(result => result.Passed) ? 0 : 1;
        }

        try
        {
            // Startup behaviour belongs to the application, so it needs the executable.
            if (args.Contains("--app"))
            {
                var startup = new StartupCases(Path.GetFullPath(Value("--app", "NekoPcbEmulator.App.exe")));
                startup.Run();
                return Report("startup", startup.Results);
            }

            var deviceA = external ? null : new PcbADevice(new LogSink());
            var deviceB = external ? null : new PcbBDevice(new LogSink());
            using var hostA = deviceA is null ? null : new PcbHost(deviceA, 0, "protocol-tests-a")
            {
                Kind = serial ? PortKind.Serial : PortKind.Tcp,
                ComPort = Value("--com-a", "COM5"),
            };
            using var hostB = deviceB is null ? null : new PcbHost(deviceB, 0, "protocol-tests-b")
            {
                Kind = serial ? PortKind.Serial : PortKind.Tcp,
                ComPort = Value("--com-b", "COM4"),
            };
            hostA?.PowerOn();
            hostB?.PowerOn();
            int portA = externalTcp ? int.Parse(Value("--port-a", "5001")) : serial ? 0 : new Uri(hostA!.Endpoint).Port;
            int portB = externalTcp ? int.Parse(Value("--port-b", "5002")) : serial ? 0 : new Uri(hostB!.Endpoint).Port;
            using var a = serial ? WireClient.Serial(Value("--client-a", "COM6")) : WireClient.Tcp(portA);
            using var b = serial ? WireClient.Serial(Value("--client-b", "COM3")) : WireClient.Tcp(portB);
            var cases = new ProtocolCases(a, b, deviceA);
            cases.RunA();
            cases.RunB();

            if (!external)
            {
                var pipes = new PipeCases();
                pipes.Run();
                cases.Results.AddRange(pipes.Results);
            }
            return Report(mode, cases.Results);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }
    }
}
