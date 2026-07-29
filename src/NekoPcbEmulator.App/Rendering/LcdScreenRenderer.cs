using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using NekoPcbEmulator.Core.Devices.PcbA;

namespace NekoPcbEmulator.App.Rendering;

/// <summary>
/// Renders the character LCD as a real dot matrix.
///
/// Rather than shipping a hand-written 5x8 font table, glyphs are drawn once with a monospace
/// system font into an offscreen mask, and each of the 5x8 dots per cell samples its own patch
/// of that mask. Coverage becomes dot brightness, so the result is properly pixelated and
/// still supports the full character set.
///
/// The whole thing is cached and only rebuilt when the displayed text changes, which keeps a
/// 60 Hz repaint down to a single blit.
/// </summary>
internal sealed class LcdScreenRenderer : IDisposable
{
    private const int DotPitch = 4;
    private const int DotSize = 3;
    private const int DotsX = 5;
    private const int DotsY = 8;

    private const int CellWidth = (DotsX + 1) * DotPitch;   // one dot of gutter between cells
    private const int CellHeight = (DotsY + 1) * DotPitch;
    private const int GlassWidth = LcdPanel.Columns * CellWidth;
    private const int GlassHeight = LcdPanel.Rows * CellHeight;

    // The glyph mask is rasterised at exactly one pixel per dot, so no downsampling is needed
    // and strokes land on dot boundaries the way a real character generator ROM would place them.
    private const int MaskCellWidth = DotsX + 1;
    private const int MaskCellHeight = DotsY + 1;
    private const int MaskWidth = LcdPanel.Columns * MaskCellWidth;
    private const int MaskHeight = LcdPanel.Rows * MaskCellHeight;

    private const char KeySeparator = '\n';

    private static readonly Font GlyphFont = new("Consolas", 10f, FontStyle.Regular, GraphicsUnit.Pixel);

    private Bitmap? _cache;
    private string _key = "uninitialised";

    public void Draw(Graphics g, RectangleF glass, string[] lines)
    {
        string key = string.Join(KeySeparator, lines);
        if (_cache is null || key != _key)
        {
            Rebuild(lines);
            _key = key;
        }

        var previous = g.InterpolationMode;
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.DrawImage(_cache!, glass);
        g.InterpolationMode = previous;
    }

    private void Rebuild(string[] lines)
    {
        byte[] coverage = RasterizeGlyphs(lines);

        _cache?.Dispose();
        _cache = new Bitmap(GlassWidth, GlassHeight, PixelFormat.Format32bppPArgb);

        using var g = Graphics.FromImage(_cache);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(0, 0, GlassWidth, GlassHeight);
        using (var backlight = new LinearGradientBrush(bounds, PcbPalette.LcdGlass, PcbPalette.LcdGlassEdge, 70f))
            g.FillRectangle(backlight, bounds);

        int dotsWide = LcdPanel.Columns * DotsX;

        for (int row = 0; row < LcdPanel.Rows; row++)
        {
            for (int column = 0; column < LcdPanel.Columns; column++)
            {
                for (int dy = 0; dy < DotsY; dy++)
                {
                    for (int dx = 0; dx < DotsX; dx++)
                    {
                        int index = (row * DotsY + dy) * dotsWide + column * DotsX + dx;
                        float lit = coverage[index] / 255f;

                        // Unlit dots stay faintly visible, exactly like a real backlit module.
                        var color = lit <= 0.02f
                            ? PcbPalette.LcdGhost
                            : Color.FromArgb(
                                (int)(40 + 215 * lit),
                                PcbPalette.LcdInk.R,
                                PcbPalette.LcdInk.G,
                                PcbPalette.LcdInk.B);

                        using var brush = new SolidBrush(color);
                        g.FillEllipse(
                            brush,
                            column * CellWidth + dx * DotPitch + 1,
                            row * CellHeight + dy * DotPitch + 1,
                            DotSize,
                            DotSize);
                    }
                }
            }
        }

        // Glass sheen across the top third.
        using var sheen = new LinearGradientBrush(
            new Rectangle(0, 0, GlassWidth, GlassHeight / 2),
            Color.FromArgb(34, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255),
            90f);
        g.FillRectangle(sheen, 0, 0, GlassWidth, GlassHeight / 2);
    }

    /// <summary>Draws the text into a one-pixel-per-dot mask and reads back a coverage byte per dot.</summary>
    private static byte[] RasterizeGlyphs(string[] lines)
    {
        int dotsWide = LcdPanel.Columns * DotsX;
        int dotsHigh = LcdPanel.Rows * DotsY;
        var coverage = new byte[dotsWide * dotsHigh];

        using var mask = new Bitmap(MaskWidth, MaskHeight, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(mask))
        {
            g.Clear(Color.Transparent);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using var format = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
            };

            for (int row = 0; row < Math.Min(lines.Length, LcdPanel.Rows); row++)
            {
                string line = lines[row] ?? "";
                for (int column = 0; column < Math.Min(line.Length, LcdPanel.Columns); column++)
                {
                    char c = line[column];
                    if (char.IsWhiteSpace(c) || c == '\0') continue;

                    // The glyph occupies the 5x8 dot area of the cell, never the gutter.
                    var cell = new RectangleF(column * MaskCellWidth, row * MaskCellHeight, DotsX, DotsY);
                    g.DrawString(c.ToString(), GlyphFont, Brushes.White, cell, format);
                }
            }
        }

        var data = mask.LockBits(
            new Rectangle(0, 0, MaskWidth, MaskHeight),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                byte* scan = (byte*)data.Scan0;
                for (int row = 0; row < LcdPanel.Rows; row++)
                {
                    for (int dy = 0; dy < DotsY; dy++)
                    {
                        byte* source = scan + (long)(row * MaskCellHeight + dy) * data.Stride;
                        for (int column = 0; column < LcdPanel.Columns; column++)
                        {
                            for (int dx = 0; dx < DotsX; dx++)
                            {
                                byte alpha = source[(column * MaskCellWidth + dx) * 4 + 3];

                                // Modest gain: enough that a thin stroke lights its dot, but not
                                // so much that antialiasing spills into the neighbouring one.
                                int value = alpha < 40 ? 0 : Math.Min(255, alpha * 5 / 4);
                                coverage[(row * DotsY + dy) * dotsWide + column * DotsX + dx] = (byte)value;
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            mask.UnlockBits(data);
        }

        return coverage;
    }

    public void Dispose()
    {
        _cache?.Dispose();
        _cache = null;
    }
}
