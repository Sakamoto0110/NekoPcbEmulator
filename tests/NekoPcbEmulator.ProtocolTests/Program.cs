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

        try
        {
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

            string report = Path.GetFullPath(Value("--report", $"artifacts/protocol-tests/{mode}.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            File.WriteAllText(report, JsonSerializer.Serialize(new
            {
                Mode = mode,
                Results = cases.Results,
            }, new JsonSerializerOptions { WriteIndented = true }));
            foreach (var group in cases.Results.GroupBy(result => result.Protocol))
                Console.WriteLine($"Protocol {group.Key}: {group.Count(result => result.Passed)}/{group.Count()} passed");
            Console.WriteLine("Report: " + report);
            return cases.Results.All(result => result.Passed) ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }
    }
}
