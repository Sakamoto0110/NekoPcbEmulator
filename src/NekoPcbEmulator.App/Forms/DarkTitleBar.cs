using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace NekoPcbEmulator.App.Forms;

/// <summary>
/// Opts a window into the immersive dark title bar so the chrome matches the dark UI.
/// Silently does nothing on Windows builds that predate the attribute.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    public static void Apply(Form form)
    {
        if (!form.IsHandleCreated) return;

        try
        {
            int enabled = 1;
            DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
        }
        catch (DllNotFoundException)
        {
            // Not available; the light title bar is a cosmetic loss only.
        }
    }
}
