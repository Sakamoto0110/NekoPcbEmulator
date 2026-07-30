using System.Drawing;

namespace NekoPcbEmulator.App.Rendering;

/// <summary>
/// Colours for the board illustration and the launcher chrome.
///
/// The board colours are deliberately desaturated. Solder mask photographs far more olive and
/// far less emerald than the "PCB green" people reach for, and the difference between a trace
/// and bare mask is only a few percent of luminance, because the copper sits *under* the mask
/// rather than on top of it.
/// </summary>
public static class PcbPalette
{
    // Window chrome
    public static readonly Color Backdrop = Color.FromArgb(0x0B, 0x0D, 0x10);
    public static readonly Color Surface = Color.FromArgb(0x15, 0x18, 0x1D);
    public static readonly Color SurfaceRaised = Color.FromArgb(0x1D, 0x21, 0x27);
    public static readonly Color Divider = Color.FromArgb(0x2A, 0x30, 0x38);
    public static readonly Color Text = Color.FromArgb(0xE4, 0xE9, 0xEF);
    public static readonly Color TextDim = Color.FromArgb(0x8B, 0x96, 0xA5);
    public static readonly Color Accent = Color.FromArgb(0x36, 0xC7, 0x8A);
    public static readonly Color AccentDim = Color.FromArgb(0x1E, 0x6B, 0x4C);
    public static readonly Color Danger = Color.FromArgb(0xE0, 0x5A, 0x5A);
    public static readonly Color Warn = Color.FromArgb(0xE0, 0xB2, 0x4A);
    public static readonly Color Rx = Color.FromArgb(0x63, 0xB4, 0xF6);
    public static readonly Color Tx = Color.FromArgb(0x9C, 0xD9, 0x7A);

    // Solder mask over fibreglass: olive rather than emerald, and low contrast.
    public static readonly Color BoardBase = Color.FromArgb(0x18, 0x38, 0x27);
    public static readonly Color BoardHighlight = Color.FromArgb(0x21, 0x46, 0x31);
    public static readonly Color BoardShadow = Color.FromArgb(0x10, 0x27, 0x1B);
    public static readonly Color BoardEdge = Color.FromArgb(0x08, 0x15, 0x0E);

    /// <summary>The routed board edge, where bare FR4 shows through as tan.</summary>
    public static readonly Color Fr4Edge = Color.FromArgb(0xAE, 0xA0, 0x71);

    // Copper reads as a slight lift in the mask, not as a separate colour.
    public static readonly Color Trace = Color.FromArgb(0x28, 0x54, 0x37);
    public static readonly Color TraceBright = Color.FromArgb(0x32, 0x66, 0x43);

    // Exposed metal
    public static readonly Color Gold = Color.FromArgb(0xC4, 0xA4, 0x53);
    public static readonly Color GoldDark = Color.FromArgb(0x7E, 0x66, 0x28);
    public static readonly Color Solder = Color.FromArgb(0xB5, 0xB9, 0xBE);
    public static readonly Color SolderDark = Color.FromArgb(0x63, 0x69, 0x6F);

    // Silkscreen is matte and never pure white.
    public static readonly Color Silk = Color.FromArgb(0xD3, 0xD7, 0xC9);
    public static readonly Color SilkDim = Color.FromArgb(0x87, 0x91, 0x81);

    // Component packages
    public static readonly Color Plastic = Color.FromArgb(0x16, 0x17, 0x19);
    public static readonly Color PlasticLight = Color.FromArgb(0x2E, 0x31, 0x35);
    public static readonly Color Metal = Color.FromArgb(0x9B, 0xA1, 0xA7);

    /// <summary>Unlit LED phosphor: a warm off-white. A dark LED reads as a hole in the board.</summary>
    public static readonly Color LedPhosphor = Color.FromArgb(0xCF, 0xC9, 0xB0);

    public static readonly Color LedPackage = Color.FromArgb(0xDE, 0xDC, 0xD3);

    // Character LCD
    public static readonly Color LcdGlass = Color.FromArgb(0x8A, 0xA0, 0x44);
    public static readonly Color LcdGlassEdge = Color.FromArgb(0x6C, 0x80, 0x30);
    public static readonly Color LcdInk = Color.FromArgb(0x12, 0x1A, 0x0C);
    public static readonly Color LcdGhost = Color.FromArgb(0x22, 0x7A, 0x92, 0x30);

    /// <summary>Live readouts on the board: instrument grey-green, not neon.</summary>
    public static readonly Color Readout = Color.FromArgb(0xC2, 0xC8, 0xB6);

    /// <summary>Highlight ring drawn around the peripheral under the cursor.</summary>
    public static readonly Color HoverRing = Color.FromArgb(0x74, 0xD2, 0xE8);
}
