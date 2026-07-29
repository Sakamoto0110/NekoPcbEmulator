using System.Drawing;

namespace NekoPcbEmulator.App.Rendering;

/// <summary>
/// Fonts sized in design-space pixels. Because the canvas paints through a scale transform,
/// <see cref="GraphicsUnit.Pixel"/> sizes here scale with the window.
/// </summary>
public static class BoardFonts
{
    public static readonly Font Title = new("Segoe UI", 30f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font Subtitle = new("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font Label = new("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font Small = new("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font Chip = new("Consolas", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font Mono = new("Consolas", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font MonoSmall = new("Consolas", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
}
