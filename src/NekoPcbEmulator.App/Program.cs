using NekoPcbEmulator.App.Forms;

namespace NekoPcbEmulator.App;

internal static class Program
{
    /// <summary>
    /// Exit code when a board requested with <c>--power</c> could not be powered on under
    /// <c>--no-main</c>. Not 1, which is also what a forced <c>taskkill</c> leaves behind.
    /// </summary>
    internal const int PowerOnFailedExitCode = 2;

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(StartupOptions.Parse(args)));
    }
}
