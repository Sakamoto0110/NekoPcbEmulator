using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using NekoPcbEmulator.Core.Devices.PcbA;

namespace NekoPcbEmulator.App.Rendering;

/// <summary>
/// Blits the 360x120 framebuffer. The device hands back premultiplied opaque BGRA, which is
/// bit-for-bit what a <see cref="PixelFormat.Format32bppPArgb"/> bitmap holds, so the update
/// is a straight row-wise memcpy and the draw is a single nearest-neighbour scale.
/// </summary>
internal sealed class PixelPanelRenderer : IDisposable
{
    private readonly Bitmap _bitmap = new(PixelPanel.Width, PixelPanel.Height, PixelFormat.Format32bppPArgb);
    private readonly uint[] _scratch = new uint[PixelPanel.Width * PixelPanel.Height];

    public void Update(PcbADevice device)
    {
        device.CopyPanelTo(_scratch);

        var data = _bitmap.LockBits(
            new Rectangle(0, 0, PixelPanel.Width, PixelPanel.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppPArgb);
        try
        {
            unsafe
            {
                fixed (uint* source = _scratch)
                {
                    byte* destination = (byte*)data.Scan0;
                    int rowBytes = PixelPanel.Width * sizeof(uint);

                    for (int y = 0; y < PixelPanel.Height; y++)
                        Buffer.MemoryCopy(
                            source + y * PixelPanel.Width,
                            destination + (long)y * data.Stride,
                            rowBytes,
                            rowBytes);
                }
            }
        }
        finally
        {
            _bitmap.UnlockBits(data);
        }
    }

    public void Draw(Graphics g, RectangleF target)
    {
        var previousInterpolation = g.InterpolationMode;
        var previousOffset = g.PixelOffsetMode;

        // Nearest neighbour keeps the pixels square: this is a LED matrix, not a photo.
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_bitmap, target);

        g.InterpolationMode = previousInterpolation;
        g.PixelOffsetMode = previousOffset;

        // A hint of glass in front of the diffuser. Kept very faint: the panel is mostly black,
        // and anything stronger reads as a grey wash rather than a reflection.
        float sheenHeight = target.Height * 0.22f;
        using var sheen = new LinearGradientBrush(
            new RectangleF(target.X, target.Y - 1, target.Width, sheenHeight + 1),
            Color.FromArgb(13, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255),
            90f);
        g.FillRectangle(sheen, target.X, target.Y, target.Width, sheenHeight);
    }

    public void Dispose() => _bitmap.Dispose();
}
