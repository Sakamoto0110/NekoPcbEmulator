namespace NekoPcbEmulator.Core.Devices.PcbA;

/// <summary>
/// The 360x120 RGBA LED matrix. The framebuffer holds straight RGBA packed as
/// <c>0xRRGGBBAA</c>; every draw composites source-over, so alpha behaves like a normal
/// drawing API. On screen the result is flattened against black, which makes alpha read as
/// emitted intensity — exactly how an LED panel behaves.
///
/// All operations clip silently: drawing partly outside the panel is not an error.
/// </summary>
public sealed class PixelPanel
{
    public const int Width = 360;
    public const int Height = 120;

    private readonly uint[] _pixels = new uint[Width * Height];

    public static bool Contains(int x, int y) => (uint)x < Width && (uint)y < Height;

    public void Clear(Rgba color)
    {
        Array.Fill(_pixels, color.Packed);
    }

    public void Point(int x, int y, Rgba color)
    {
        if (!Contains(x, y)) return;

        int index = y * Width + x;
        _pixels[index] = color.A == 255
            ? color.Packed
            : Rgba.Over(Rgba.FromPacked(_pixels[index]), color).Packed;
    }

    /// <summary>Integer Bresenham line, endpoints inclusive.</summary>
    public void Line(int x0, int y0, int x1, int y1, Rgba color)
    {
        int dx = Math.Abs(x1 - x0);
        int dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int error = dx + dy;

        // Bounded so a wildly out-of-range request cannot spin: the longest possible run is
        // the diagonal span of the requested segment.
        int guard = dx - dy + 2;

        while (guard-- > 0)
        {
            Point(x0, y0, color);
            if (x0 == x1 && y0 == y1) break;

            int e2 = error * 2;
            if (e2 >= dy) { error += dy; x0 += sx; }
            if (e2 <= dx) { error += dx; y0 += sy; }
        }
    }

    /// <summary><paramref name="x"/>,<paramref name="y"/> is the top-left corner; the size is inclusive of it.</summary>
    public void Rect(int x, int y, int w, int h, Rgba color, bool filled)
    {
        if (w <= 0 || h <= 0) return;

        int right = x + w - 1;
        int bottom = y + h - 1;

        if (filled)
        {
            for (int row = Math.Max(y, 0); row <= Math.Min(bottom, Height - 1); row++)
                for (int col = Math.Max(x, 0); col <= Math.Min(right, Width - 1); col++)
                    Point(col, row, color);
            return;
        }

        Line(x, y, right, y, color);
        Line(x, bottom, right, bottom, color);
        Line(x, y, x, bottom, color);
        Line(right, y, right, bottom, color);
    }

    public Rgba GetPixel(int x, int y) => Contains(x, y) ? Rgba.FromPacked(_pixels[y * Width + x]) : Rgba.Off;

    /// <summary>
    /// Flattens the framebuffer into premultiplied opaque BGRA, ready to be memcpy'd straight
    /// into a GDI+ <c>Format32bppPArgb</c> bitmap.
    /// </summary>
    public void CopyToBgra(Span<uint> destination)
    {
        for (int i = 0; i < _pixels.Length; i++)
            destination[i] = Rgba.FromPacked(_pixels[i]).ToOpaquePremultipliedBgra();
    }

    /// <summary>Number of pixels currently emitting light. Shown on the board as a diagnostic.</summary>
    public int LitPixelCount()
    {
        int count = 0;
        foreach (uint pixel in _pixels)
            if ((pixel & 0xFF) != 0) count++;
        return count;
    }
}
