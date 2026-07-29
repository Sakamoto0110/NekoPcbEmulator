using NekoPcbEmulator.App.Forms;

namespace NekoPcbEmulator.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(StartupOptions.Parse(args)));
    }
}
