using System.Drawing;
using System.Reflection;

namespace NekoPcbEmulator.App;

/// <summary>
/// The window icon, read from the embedded resource rather than from a file beside the
/// executable — under single-file publish there is no loose .ico to read.
///
/// The bytes are cached but a fresh <see cref="Icon"/> is handed out per call, so no window
/// can dispose an instance another window is still using. There are only ever a handful of
/// windows, and the icon is a few hundred kilobytes.
/// </summary>
internal static class AppIcon
{
    private const string ResourceName = "NekoPcbEmulator.App.AppIcon.ico";

    private static readonly byte[]? Data = Load();

    /// <summary>A new icon instance, or null if the resource is missing.</summary>
    public static Icon? Create()
    {
        if (Data is null) return null;

        using var stream = new MemoryStream(Data, writable: false);
        return new Icon(stream);
    }

    private static byte[]? Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is null) return null;

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
