using System.Drawing;

namespace NekoPcbEmulator.App.Rendering;

/// <summary>Colours for the board illustration and the launcher chrome.</summary>
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

    // Fibreglass and solder mask
    public static readonly Color BoardBase = Color.FromArgb(0x0E, 0x44, 0x2B);
    public static readonly Color BoardHighlight = Color.FromArgb(0x18, 0x60, 0x3D);
    public static readonly Color BoardShadow = Color.FromArgb(0x07, 0x2C, 0x1B);
    public static readonly Color BoardEdge = Color.FromArgb(0x03, 0x18, 0x0E);
    public static readonly Color Trace = Color.FromArgb(0x18, 0x66, 0x41);
    public static readonly Color TraceBright = Color.FromArgb(0x22, 0x82, 0x54);

    // Copper and silkscreen
    public static readonly Color Gold = Color.FromArgb(0xD9, 0xB5, 0x4C);
    public static readonly Color GoldDark = Color.FromArgb(0x8E, 0x71, 0x22);
    public static readonly Color Silk = Color.FromArgb(0xE8, 0xF0, 0xEA);
    public static readonly Color SilkDim = Color.FromArgb(0x93, 0xA9, 0x9B);

    // Component packages
    public static readonly Color Plastic = Color.FromArgb(0x12, 0x14, 0x17);
    public static readonly Color PlasticLight = Color.FromArgb(0x2C, 0x31, 0x38);
    public static readonly Color Metal = Color.FromArgb(0xA8, 0xB0, 0xB8);

    // Character LCD
    public static readonly Color LcdGlass = Color.FromArgb(0x8D, 0xB0, 0x33);
    public static readonly Color LcdGlassEdge = Color.FromArgb(0x6B, 0x8B, 0x20);
    public static readonly Color LcdInk = Color.FromArgb(0x0E, 0x1A, 0x06);
    public static readonly Color LcdGhost = Color.FromArgb(0x28, 0x7F, 0xA0, 0x2A);
}
