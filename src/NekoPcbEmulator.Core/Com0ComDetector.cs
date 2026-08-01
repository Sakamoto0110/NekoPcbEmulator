using System.IO.Ports;

namespace NekoPcbEmulator.Core;

/// <summary>
/// Decides whether serving a board over a COM port is actually possible right now.
///
/// com0com is deliberately <em>not</em> a dependency of this project: it is a kernel-mode
/// driver installed out of band. Everything here degrades to "unavailable" and the serial
/// transport is simply offered as a disabled option.
///
/// The check is deliberately not "is the package installed". A present installation whose
/// driver fails to load — which is the normal outcome on Windows builds that no longer trust
/// cross-signed drivers — leaves configured port pairs that cannot be opened at all. Only
/// opening the port proves anything, so that is what this does.
/// </summary>
public static class Com0ComDetector
{
    private static readonly Lazy<bool> PackagePresent = new(DetectPackage, isThreadSafe: true);

    /// <summary>True when a com0com installation exists on disk. Necessary, never sufficient.</summary>
    public static bool IsPackageInstalled => PackagePresent.Value;

    /// <summary>Serial ports the OS is currently advertising.</summary>
    public static string[] CurrentPorts()
    {
        try { return SerialPort.GetPortNames(); }
        catch (Exception) { return []; }
    }

    /// <summary>
    /// Ground truth for one port: can it be opened?
    ///
    /// "Already in use" counts as available — the port exists and works, something else just
    /// holds it. Only a port that cannot be opened at all is unavailable.
    /// </summary>
    public static bool IsPortAvailable(string portName)
    {
        if (string.IsNullOrWhiteSpace(portName)) return false;

        try
        {
            using var port = new SerialPort(portName, 115200)
            {
                ReadTimeout = 200,
                WriteTimeout = 200,
            };
            port.Open();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Present, but held by another process — still a usable port.
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the given ports can all be served right now. This is what the UI gates on.
    /// </summary>
    public static Com0ComStatus Evaluate(params string[] portNames)
    {
        if (!IsPackageInstalled)
            return new Com0ComStatus(false, "com0com is not installed");

        if (portNames.Length == 0)
            return new Com0ComStatus(false, "no COM ports configured");

        var missing = portNames.Where(p => !IsPortAvailable(p)).ToArray();
        if (missing.Length == 0)
            return new Com0ComStatus(true, "ready on " + string.Join(", ", portNames));

        // The package is installed but its ports will not open. On current Windows builds the
        // usual cause is the driver being refused for its signature, which leaves the pairs
        // configured and the devices in an error state.
        return new Com0ComStatus(
            false,
            "com0com installed but " + string.Join(", ", missing) +
            " cannot be opened — the driver is most likely not loading");
    }

    private static bool DetectPackage()
    {
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "com0com"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "com0com"),
        ];

        foreach (string directory in candidates)
        {
            try
            {
                if (File.Exists(Path.Combine(directory, "setupc.exe"))) return true;
            }
            catch (Exception)
            {
                // Unreadable path: treat as absent rather than failing startup.
            }
        }

        return false;
    }
}

/// <param name="IsUsable">True only when the configured ports can actually be opened.</param>
/// <param name="Detail">Short human-readable explanation for the UI.</param>
public readonly record struct Com0ComStatus(bool IsUsable, string Detail);
