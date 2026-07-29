using System.Drawing;
using System.Drawing.Drawing2D;
using NekoPcbEmulator.Core;

namespace NekoPcbEmulator.App.Rendering;

/// <summary>
/// Drawing primitives shared by both board views: fibreglass, copper, packages and LEDs.
/// Everything works in the caller's design-space coordinates.
/// </summary>
public static class BoardPainter
{
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

    /// <summary>Fibreglass substrate with solder mask, copper routing and mounting holes.</summary>
    public static void DrawBoard(Graphics g, RectangleF rect, int traceSeed)
    {
        using var outline = RoundedRect(rect, 14);

        using (var body = new LinearGradientBrush(rect, PcbPalette.BoardHighlight, PcbPalette.BoardShadow, 55f))
            g.FillPath(body, outline);

        DrawGroundPlane(g, rect);
        DrawTraces(g, RectangleF.Inflate(rect, -22, -22), traceSeed);

        // Bevelled edge: a dark outer stroke with a lighter inner line.
        using (var edge = new Pen(PcbPalette.BoardEdge, 3f))
            g.DrawPath(edge, outline);
        using (var inner = RoundedRect(RectangleF.Inflate(rect, -4, -4), 11))
        using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
            g.DrawPath(pen, inner);

        foreach (var corner in new[]
                 {
                     new PointF(rect.Left + 22, rect.Top + 22),
                     new PointF(rect.Right - 22, rect.Top + 22),
                     new PointF(rect.Left + 22, rect.Bottom - 22),
                     new PointF(rect.Right - 22, rect.Bottom - 22),
                 })
            DrawMountingHole(g, corner, 9);
    }

    /// <summary>The stitched via pattern typical of a poured ground plane.</summary>
    private static void DrawGroundPlane(Graphics g, RectangleF rect)
    {
        using var brush = new SolidBrush(Color.FromArgb(22, 255, 255, 255));
        for (float y = rect.Top + 18; y < rect.Bottom - 12; y += 26)
            for (float x = rect.Left + 18; x < rect.Right - 12; x += 26)
                g.FillEllipse(brush, x, y, 2.2f, 2.2f);
    }

    /// <summary>
    /// Copper routing. Deterministic from <paramref name="seed"/> so the board looks identical
    /// on every repaint, and constrained to 45-degree turns like a real autorouter.
    /// </summary>
    private static void DrawTraces(Graphics g, RectangleF rect, int seed)
    {
        var random = new Random(seed);
        (float dx, float dy)[] directions =
        [
            (1, 0), (0.707f, 0.707f), (0, 1), (-0.707f, 0.707f),
            (-1, 0), (-0.707f, -0.707f), (0, -1), (0.707f, -0.707f),
        ];

        using var thin = new Pen(PcbPalette.Trace, 2.4f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        using var thick = new Pen(PcbPalette.TraceBright, 3.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        using var viaOuter = new SolidBrush(PcbPalette.GoldDark);
        using var viaInner = new SolidBrush(PcbPalette.BoardShadow);

        int count = (int)(rect.Width * rect.Height / 9000f);
        for (int i = 0; i < count; i++)
        {
            var points = new List<PointF>();
            float x = rect.X + (float)random.NextDouble() * rect.Width;
            float y = rect.Y + (float)random.NextDouble() * rect.Height;
            points.Add(new PointF(x, y));

            int segments = random.Next(2, 6);
            int direction = random.Next(directions.Length);

            for (int s = 0; s < segments; s++)
            {
                // Turn by at most 45 degrees per corner.
                direction = (direction + random.Next(-1, 2) + directions.Length) % directions.Length;
                float length = 24 + (float)random.NextDouble() * 110;

                x = Math.Clamp(x + directions[direction].dx * length, rect.Left, rect.Right);
                y = Math.Clamp(y + directions[direction].dy * length, rect.Top, rect.Bottom);
                points.Add(new PointF(x, y));
            }

            g.DrawLines(random.Next(4) == 0 ? thick : thin, [.. points]);

            var end = points[^1];
            g.FillEllipse(viaOuter, end.X - 3.4f, end.Y - 3.4f, 6.8f, 6.8f);
            g.FillEllipse(viaInner, end.X - 1.4f, end.Y - 1.4f, 2.8f, 2.8f);
        }
    }

    public static void DrawMountingHole(Graphics g, PointF center, float radius)
    {
        using var ring = new SolidBrush(PcbPalette.GoldDark);
        using var bore = new SolidBrush(Color.FromArgb(0x05, 0x0A, 0x07));

        g.FillEllipse(ring, center.X - radius, center.Y - radius, radius * 2, radius * 2);
        g.FillEllipse(bore, center.X - radius * 0.55f, center.Y - radius * 0.55f, radius * 1.1f, radius * 1.1f);
    }

    /// <summary>A quad-flat package with pins on all four sides.</summary>
    public static void DrawChip(Graphics g, RectangleF rect, string label, Font labelFont)
    {
        const float pinLength = 9f;
        const float pinWidth = 4f;
        int perSide = Math.Max(4, (int)(rect.Width / 14));

        using (var pin = new SolidBrush(PcbPalette.Metal))
        {
            for (int i = 0; i < perSide; i++)
            {
                float tx = rect.Left + rect.Width * (i + 0.5f) / perSide - pinWidth / 2;
                g.FillRectangle(pin, tx, rect.Top - pinLength, pinWidth, pinLength);
                g.FillRectangle(pin, tx, rect.Bottom, pinWidth, pinLength);

                float ty = rect.Top + rect.Height * (i + 0.5f) / perSide - pinWidth / 2;
                g.FillRectangle(pin, rect.Left - pinLength, ty, pinLength, pinWidth);
                g.FillRectangle(pin, rect.Right, ty, pinLength, pinWidth);
            }
        }

        using (var body = new LinearGradientBrush(rect, PcbPalette.PlasticLight, PcbPalette.Plastic, 60f))
        using (var path = RoundedRect(rect, 5))
            g.FillPath(body, path);

        using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255), 1f))
        using (var path = RoundedRect(RectangleF.Inflate(rect, -2, -2), 4))
            g.DrawPath(pen, path);

        // Pin-1 dot.
        using (var dot = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
            g.FillEllipse(dot, rect.Left + 7, rect.Top + 7, 5, 5);

        DrawText(g, label, labelFont, PcbPalette.SilkDim, rect, StringAlignment.Center, StringAlignment.Center);
    }

    /// <summary>A pin header. <paramref name="horizontal"/> lays the pins along the X axis.</summary>
    public static void DrawHeader(Graphics g, RectangleF rect, int pins, bool horizontal)
    {
        using (var shell = new LinearGradientBrush(rect, PcbPalette.PlasticLight, PcbPalette.Plastic, horizontal ? 90f : 0f))
        using (var path = RoundedRect(rect, 3))
            g.FillPath(shell, path);

        using var gold = new SolidBrush(PcbPalette.Gold);
        using var goldDark = new SolidBrush(PcbPalette.GoldDark);

        for (int i = 0; i < pins; i++)
        {
            float t = (i + 0.5f) / pins;
            RectangleF pin = horizontal
                ? new RectangleF(rect.Left + rect.Width * t - 3f, rect.Top + rect.Height * 0.28f, 6f, rect.Height * 0.44f)
                : new RectangleF(rect.Left + rect.Width * 0.28f, rect.Top + rect.Height * t - 3f, rect.Width * 0.44f, 6f);

            g.FillRectangle(goldDark, pin);
            g.FillRectangle(gold, RectangleF.Inflate(pin, -1f, -1f));
        }
    }

    /// <summary>
    /// An RGBA LED. <c>color.A</c> is the emitted intensity, so a fully transparent colour
    /// renders as a dark, unlit lens.
    /// </summary>
    public static void DrawLed(Graphics g, PointF center, float radius, Rgba color)
    {
        float intensity = color.A / 255f;
        var hue = Color.FromArgb(255, color.R, color.G, color.B);

        if (intensity > 0.02f)
        {
            float glow = radius * 3.4f;
            var bounds = new RectangleF(center.X - glow, center.Y - glow, glow * 2, glow * 2);

            using var path = new GraphicsPath();
            path.AddEllipse(bounds);
            using var brush = new PathGradientBrush(path)
            {
                CenterPoint = center,
                CenterColor = Color.FromArgb((int)(170 * intensity), hue),
                SurroundColors = [Color.FromArgb(0, hue)],
                FocusScales = new PointF(0.18f, 0.18f),
            };
            g.FillPath(brush, path);
        }

        // Package: a white SMD body under the lens.
        using (var package = new SolidBrush(Color.FromArgb(0xD2, 0xD6, 0xD9)))
        using (var path = RoundedRect(new RectangleF(center.X - radius * 1.25f, center.Y - radius * 1.25f, radius * 2.5f, radius * 2.5f), radius * 0.35f))
            g.FillPath(package, path);

        using (var pen = new Pen(Color.FromArgb(0x6A, 0x6E, 0x72), 1.2f))
        using (var path = RoundedRect(new RectangleF(center.X - radius * 1.25f, center.Y - radius * 1.25f, radius * 2.5f, radius * 2.5f), radius * 0.35f))
            g.DrawPath(pen, path);

        // Lens: bright toward the top-left, so every LED reads as lit from the same angle.
        var lensBounds = new RectangleF(center.X - radius, center.Y - radius, radius * 2, radius * 2);
        using (var lens = new GraphicsPath())
        {
            lens.AddEllipse(lensBounds);
            using var brush = new PathGradientBrush(lens)
            {
                CenterPoint = new PointF(center.X - radius * 0.28f, center.Y - radius * 0.28f),
                CenterColor = Blend(hue, Color.White, 0.15f + 0.55f * intensity),
                SurroundColors = [Blend(Blend(hue, Color.Black, 0.72f), hue, intensity * 0.85f)],
            };
            g.FillPath(brush, lens);
        }

        using (var rim = new Pen(Color.FromArgb(120, 0, 0, 0), 1.4f))
            g.DrawEllipse(rim, lensBounds);

        using (var spec = new SolidBrush(Color.FromArgb((int)(90 + 110 * intensity), 255, 255, 255)))
            g.FillEllipse(spec, center.X - radius * 0.52f, center.Y - radius * 0.62f, radius * 0.42f, radius * 0.3f);
    }

    /// <summary>Countdown ring drawn around a timed LED; <paramref name="fraction"/> goes 1 to 0.</summary>
    public static void DrawTimerRing(Graphics g, PointF center, float radius, float fraction, Color color)
    {
        var bounds = new RectangleF(center.X - radius, center.Y - radius, radius * 2, radius * 2);

        using (var track = new Pen(Color.FromArgb(50, 255, 255, 255), 2.6f))
            g.DrawEllipse(track, bounds);

        if (fraction <= 0) return;
        using var pen = new Pen(Color.FromArgb(220, color), 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, bounds, -90f, 360f * Math.Clamp(fraction, 0f, 1f));
    }

    /// <summary>
    /// A darkened plate laid over the solder mask. Live readouts sit on one of these so the
    /// copper routing underneath never competes with the text.
    /// </summary>
    public static void DrawSilkPlate(Graphics g, RectangleF rect)
    {
        using (var fill = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
        using (var path = RoundedRect(rect, 5))
            g.FillPath(fill, path);

        using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
        using (var path = RoundedRect(rect, 5))
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
}
