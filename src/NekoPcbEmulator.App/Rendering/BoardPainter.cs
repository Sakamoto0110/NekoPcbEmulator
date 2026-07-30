using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using NekoPcbEmulator.Core;

namespace NekoPcbEmulator.App.Rendering;

/// <summary>
/// Drawing primitives shared by both board views: fibreglass, copper, packages and LEDs.
/// Everything works in the caller's design-space coordinates.
/// </summary>
public static class BoardPainter
{
    /// <summary>
    /// Fine speckle over the solder mask. Matte mask is never flat, and a subtle noise layer
    /// does more for realism than any amount of extra geometry. Generated once and tiled.
    /// </summary>
    private static readonly Lazy<Bitmap> MaskTexture = new(() => CreateNoise(128, seed: 20260730));

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));

        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }

        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Substrate, solder mask, ground pour and the routed edge. Copper is drawn by the caller.</summary>
    public static void DrawBoard(Graphics g, RectangleF rect)
    {
        using var outline = RoundedRect(rect, 10);

        using (var body = new LinearGradientBrush(rect, PcbPalette.BoardHighlight, PcbPalette.BoardShadow, 62f))
            g.FillPath(body, outline);

        DrawGroundPour(g, rect);

        using (var texture = new TextureBrush(MaskTexture.Value) { WrapMode = WrapMode.Tile })
            g.FillPath(texture, outline);

        // Routed edge: bare FR4 shows tan where the router cut through the mask. Kept faint —
        // it is a 1.6 mm chamfer seen edge-on, not a painted border.
        using (var fr4 = new Pen(Color.FromArgb(120, PcbPalette.Fr4Edge), 1.5f))
        using (var inset = RoundedRect(RectangleF.Inflate(rect, -1.2f, -1.2f), 9))
            g.DrawPath(fr4, inset);

        using (var edge = new Pen(PcbPalette.BoardEdge, 2.2f))
            g.DrawPath(edge, outline);

        foreach (var corner in new[]
                 {
                     new PointF(rect.Left + 20, rect.Top + 20),
                     new PointF(rect.Right - 20, rect.Top + 20),
                     new PointF(rect.Left + 20, rect.Bottom - 20),
                     new PointF(rect.Right - 20, rect.Bottom - 20),
                 })
            DrawMountingHole(g, corner, 8.5f);
    }

    /// <summary>Stitching vias across the poured ground plane.</summary>
    private static void DrawGroundPour(Graphics g, RectangleF rect)
    {
        using var dark = new SolidBrush(Color.FromArgb(26, 0, 0, 0));
        using var light = new SolidBrush(Color.FromArgb(14, 255, 255, 255));

        for (float y = rect.Top + 24; y < rect.Bottom - 16; y += 34)
        {
            for (float x = rect.Left + 24; x < rect.Right - 16; x += 34)
            {
                g.FillEllipse(dark, x, y, 3.2f, 3.2f);
                g.FillEllipse(light, x + 0.6f, y + 0.6f, 1.8f, 1.8f);
            }
        }
    }

    /// <summary>
    /// A bundle of parallel traces routed from one component to another with 45-degree
    /// corners, the way an autorouter lays them out. Routing between real anchor points is
    /// what makes a board read as designed rather than decorated.
    /// </summary>
    public static void RouteBus(
        Graphics g,
        PointF from,
        PointF to,
        int count = 1,
        float spacing = 6f,
        bool horizontalFirst = true,
        bool heavy = false)
    {
        using var pen = new Pen(heavy ? PcbPalette.TraceBright : PcbPalette.Trace, heavy ? 3.2f : 1.9f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        for (int i = 0; i < count; i++)
        {
            float offset = (i - (count - 1) / 2f) * spacing;
            var shift = horizontalFirst ? new SizeF(0, offset) : new SizeF(offset, 0);
            g.DrawLines(pen, Route(
                new PointF(from.X + shift.Width, from.Y + shift.Height),
                new PointF(to.X + shift.Width, to.Y + shift.Height),
                horizontalFirst));
        }
    }

    /// <summary>Three-segment 45-degree route: straight run, diagonal, straight run.</summary>
    private static PointF[] Route(PointF from, PointF to, bool horizontalFirst)
    {
        float dx = to.X - from.X;
        float dy = to.Y - from.Y;
        float sx = Math.Sign(dx);
        float sy = Math.Sign(dy);
        float diagonal = Math.Min(Math.Abs(dx), Math.Abs(dy));

        PointF a = horizontalFirst
            ? new PointF(from.X + sx * (Math.Abs(dx) - diagonal), from.Y)
            : new PointF(from.X, from.Y + sy * (Math.Abs(dy) - diagonal));

        var b = new PointF(a.X + sx * diagonal, a.Y + sy * diagonal);
        return [from, a, b, to];
    }

    /// <summary>A via: annular ring with the drilled hole showing through.</summary>
    public static void DrawVia(Graphics g, PointF center, float radius = 3.2f)
    {
        using var ring = new SolidBrush(PcbPalette.GoldDark);
        using var bore = new SolidBrush(Color.FromArgb(0x06, 0x0C, 0x08));

        g.FillEllipse(ring, center.X - radius, center.Y - radius, radius * 2, radius * 2);
        g.FillEllipse(bore, center.X - radius * 0.42f, center.Y - radius * 0.42f, radius * 0.84f, radius * 0.84f);
    }

    /// <summary>A reflowed solder joint: a small bright fillet around a lead.</summary>
    public static void DrawSolderPad(Graphics g, RectangleF pad)
    {
        using var path = RoundedRect(pad, Math.Min(pad.Width, pad.Height) * 0.35f);
        using var brush = new LinearGradientBrush(
            new RectangleF(pad.X, pad.Y - 0.5f, pad.Width, pad.Height + 1f),
            PcbPalette.Solder,
            PcbPalette.SolderDark,
            70f);

        g.FillPath(brush, path);

        using var rim = new Pen(Color.FromArgb(90, 0, 0, 0), 0.9f);
        g.DrawPath(rim, path);
    }

    /// <summary>Soft contact shadow so a package reads as sitting above the board.</summary>
    public static void DrawComponentShadow(Graphics g, RectangleF rect, float radius = 6f)
    {
        for (int i = 4; i >= 1; i--)
        {
            var spread = RectangleF.Inflate(rect, i * 1.6f, i * 1.6f);
            spread.Offset(0, i * 0.7f);

            using var path = RoundedRect(spread, radius + i * 1.6f);
            using var brush = new SolidBrush(Color.FromArgb(16, 0, 0, 0));
            g.FillPath(brush, path);
        }
    }

    public static void DrawMountingHole(Graphics g, PointF center, float radius)
    {
        using var ring = new SolidBrush(PcbPalette.SolderDark);
        using var bore = new SolidBrush(Color.FromArgb(0x05, 0x0A, 0x07));

        g.FillEllipse(ring, center.X - radius, center.Y - radius, radius * 2, radius * 2);
        g.FillEllipse(bore, center.X - radius * 0.58f, center.Y - radius * 0.58f, radius * 1.16f, radius * 1.16f);

        using var sheen = new Pen(Color.FromArgb(60, 255, 255, 255), 1f);
        g.DrawArc(sheen, center.X - radius, center.Y - radius, radius * 2, radius * 2, 200, 100);
    }

    /// <summary>A quad-flat package with gull-wing leads sitting on solder pads.</summary>
    public static void DrawChip(Graphics g, RectangleF rect, string label, Font labelFont)
    {
        const float leadLength = 8f;
        const float leadWidth = 3.4f;
        int perSide = Math.Max(4, (int)(rect.Width / 15));

        DrawComponentShadow(g, rect, 4f);

        for (int i = 0; i < perSide; i++)
        {
            float tx = rect.Left + rect.Width * (i + 0.5f) / perSide - leadWidth / 2;
            DrawSolderPad(g, new RectangleF(tx, rect.Top - leadLength, leadWidth, leadLength));
            DrawSolderPad(g, new RectangleF(tx, rect.Bottom, leadWidth, leadLength));

            float ty = rect.Top + rect.Height * (i + 0.5f) / perSide - leadWidth / 2;
            DrawSolderPad(g, new RectangleF(rect.Left - leadLength, ty, leadLength, leadWidth));
            DrawSolderPad(g, new RectangleF(rect.Right, ty, leadLength, leadWidth));
        }

        using (var body = new LinearGradientBrush(rect, Color.FromArgb(0x25, 0x28, 0x2C), Color.FromArgb(0x0E, 0x0F, 0x11), 62f))
        using (var path = RoundedRect(rect, 3))
            g.FillPath(body, path);

        // Moulded epoxy has a soft top bevel, not a hard outline.
        using (var bevel = new LinearGradientBrush(
                   new RectangleF(rect.X, rect.Y, rect.Width, rect.Height * 0.4f),
                   Color.FromArgb(46, 255, 255, 255),
                   Color.FromArgb(0, 255, 255, 255),
                   90f))
        using (var path = RoundedRect(new RectangleF(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height * 0.4f), 3))
            g.FillPath(bevel, path);

        using (var pin1 = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
            g.FillEllipse(pin1, rect.Left + 6, rect.Top + 6, 4.5f, 4.5f);

        DrawText(g, label, labelFont, PcbPalette.SilkDim, rect, StringAlignment.Center, StringAlignment.Center);
    }

    /// <summary>A pin header: black shroud with square gold posts.</summary>
    public static void DrawHeader(Graphics g, RectangleF rect, int pins, bool horizontal)
    {
        DrawComponentShadow(g, rect, 3f);

        using (var shell = new LinearGradientBrush(rect, PcbPalette.PlasticLight, PcbPalette.Plastic, horizontal ? 90f : 0f))
        using (var path = RoundedRect(rect, 2.5f))
            g.FillPath(shell, path);

        using var gold = new SolidBrush(PcbPalette.Gold);
        using var goldDark = new SolidBrush(PcbPalette.GoldDark);

        for (int i = 0; i < pins; i++)
        {
            float t = (i + 0.5f) / pins;
            RectangleF pin = horizontal
                ? new RectangleF(rect.Left + rect.Width * t - 2.6f, rect.Top + rect.Height * 0.3f, 5.2f, rect.Height * 0.4f)
                : new RectangleF(rect.Left + rect.Width * 0.3f, rect.Top + rect.Height * t - 2.6f, rect.Width * 0.4f, 5.2f);

            g.FillRectangle(goldDark, pin);
            g.FillRectangle(gold, RectangleF.Inflate(pin, -0.9f, -0.9f));
        }
    }

    /// <summary>
    /// A 5050-style RGB LED. <c>color.A</c> is the emitted intensity.
    ///
    /// An unlit LED is a pale phosphor square, not a black hole: the bloom is what changes,
    /// and it stays tight — a wide halo is the single thing that makes a rendered board look
    /// like a game asset.
    /// </summary>
    public static void DrawLed(Graphics g, PointF center, float radius, Rgba color)
    {
        float intensity = color.A / 255f;
        var hue = Color.FromArgb(255, color.R, color.G, color.B);

        float package = radius * 1.4f;
        var body = new RectangleF(center.X - package, center.Y - package, package * 2, package * 2);

        DrawComponentShadow(g, body, 3f);

        // Solder pads either side of the package.
        DrawSolderPad(g, new RectangleF(body.Left - radius * 0.42f, center.Y - radius * 0.42f, radius * 0.6f, radius * 0.84f));
        DrawSolderPad(g, new RectangleF(body.Right - radius * 0.18f, center.Y - radius * 0.42f, radius * 0.6f, radius * 0.84f));

        using (var shell = new SolidBrush(PcbPalette.LedPackage))
        using (var path = RoundedRect(body, radius * 0.24f))
            g.FillPath(shell, path);

        using (var rim = new Pen(Color.FromArgb(0x8A, 0x88, 0x80), 1f))
        using (var path = RoundedRect(body, radius * 0.24f))
            g.DrawPath(rim, path);

        // Emitting window: silicone dome over the phosphor.
        var window = new RectangleF(center.X - radius, center.Y - radius, radius * 2, radius * 2);
        using (var lens = new GraphicsPath())
        {
            lens.AddEllipse(window);
            using var brush = new PathGradientBrush(lens)
            {
                CenterPoint = new PointF(center.X - radius * 0.2f, center.Y - radius * 0.24f),
                CenterColor = Blend(PcbPalette.LedPhosphor, Blend(hue, Color.White, 0.45f), intensity),
                SurroundColors = [Blend(Blend(PcbPalette.LedPhosphor, Color.Black, 0.32f), hue, intensity * 0.9f)],
            };
            g.FillPath(brush, lens);
        }

        using (var rim = new Pen(Color.FromArgb(70, 0, 0, 0), 1f))
            g.DrawEllipse(rim, window);

        // Tight bloom, drawn over the package so the light appears to spill onto it.
        if (intensity > 0.02f)
        {
            float glow = radius * 1.95f;
            var bounds = new RectangleF(center.X - glow, center.Y - glow, glow * 2, glow * 2);

            using var path = new GraphicsPath();
            path.AddEllipse(bounds);
            using var brush = new PathGradientBrush(path)
            {
                CenterPoint = center,
                CenterColor = Color.FromArgb((int)(120 * intensity), hue),
                SurroundColors = [Color.FromArgb(0, hue)],
                FocusScales = new PointF(0.42f, 0.42f),
            };
            g.FillPath(brush, path);
        }

        using (var spec = new SolidBrush(Color.FromArgb((int)(70 + 90 * intensity), 255, 255, 255)))
            g.FillEllipse(spec, center.X - radius * 0.5f, center.Y - radius * 0.58f, radius * 0.36f, radius * 0.24f);
    }

    /// <summary>Countdown ring drawn around a timed LED; <paramref name="fraction"/> goes 1 to 0.</summary>
    public static void DrawTimerRing(Graphics g, PointF center, float radius, float fraction, Color color)
    {
        var bounds = new RectangleF(center.X - radius, center.Y - radius, radius * 2, radius * 2);

        using (var track = new Pen(Color.FromArgb(38, 255, 255, 255), 2.2f))
            g.DrawEllipse(track, bounds);

        if (fraction <= 0) return;
        using var pen = new Pen(Color.FromArgb(190, color), 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, bounds, -90f, 360f * Math.Clamp(fraction, 0f, 1f));
    }

    /// <summary>Silkscreen component outline. Real silkscreen is a thin solid line, never dotted.</summary>
    public static void DrawSilkOutline(Graphics g, RectangleF rect, float radius = 3f)
    {
        using var pen = new Pen(Color.FromArgb(70, PcbPalette.Silk), 1.1f);
        using var path = RoundedRect(rect, radius);
        g.DrawPath(pen, path);
    }

    /// <summary>
    /// A darkened plate laid over the solder mask. Live readouts sit on one of these so the
    /// copper routing underneath never competes with the text.
    /// </summary>
    public static void DrawSilkPlate(Graphics g, RectangleF rect)
    {
        using (var fill = new SolidBrush(Color.FromArgb(112, 0, 0, 0)))
        using (var path = RoundedRect(rect, 4))
            g.FillPath(fill, path);

        using (var pen = new Pen(Color.FromArgb(30, 255, 255, 255), 1f))
        using (var path = RoundedRect(rect, 4))
            g.DrawPath(pen, path);
    }

    /// <summary>Highlight drawn around the peripheral under the cursor.</summary>
    public static void DrawHoverRing(Graphics g, RectangleF rect, float radius = 6f)
    {
        var bounds = RectangleF.Inflate(rect, 3, 3);

        using (var glow = new Pen(Color.FromArgb(46, PcbPalette.HoverRing), 7f))
        using (var path = RoundedRect(bounds, radius + 3))
            g.DrawPath(glow, path);

        using (var pen = new Pen(Color.FromArgb(225, PcbPalette.HoverRing), 1.8f))
        using (var path = RoundedRect(bounds, radius + 3))
            g.DrawPath(pen, path);
    }

    public static void DrawText(
        Graphics g,
        string text,
        Font font,
        Color color,
        RectangleF layout,
        StringAlignment horizontal = StringAlignment.Near,
        StringAlignment vertical = StringAlignment.Near)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = horizontal,
            LineAlignment = vertical,
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
            Trimming = StringTrimming.EllipsisCharacter,
        };
        g.DrawString(text, font, brush, layout, format);
    }

    public static Color Blend(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            a.A,
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    public static Color ToColor(Rgba rgba) => Color.FromArgb(rgba.A, rgba.R, rgba.G, rgba.B);

    private static Bitmap CreateNoise(int size, int seed)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var random = new Random(seed);

        var data = bitmap.LockBits(
            new Rectangle(0, 0, size, size),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                byte* scan = (byte*)data.Scan0;
                for (int y = 0; y < size; y++)
                {
                    byte* row = scan + (long)y * data.Stride;
                    for (int x = 0; x < size; x++)
                    {
                        // Kept very low: mask texture should read as mottling under the light,
                        // not as film grain sitting on top of the board.
                        int v = random.Next(-6, 7);
                        byte tone = v < 0 ? (byte)0 : (byte)255;
                        byte alpha = (byte)Math.Abs(v);

                        row[x * 4 + 0] = tone;
                        row[x * 4 + 1] = tone;
                        row[x * 4 + 2] = tone;
                        row[x * 4 + 3] = alpha;
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }
}
