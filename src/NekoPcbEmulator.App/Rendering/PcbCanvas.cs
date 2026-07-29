using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace NekoPcbEmulator.App.Rendering;

/// <summary>
/// Base for the two board views. Everything is drawn in a fixed design-space coordinate
/// system that is uniformly scaled and letterboxed into the control, so the layout is written
/// once and survives resizing and DPI changes.
///
/// The board itself (fibreglass, copper, packages, silkscreen) never changes, so it is
/// rendered once into a cached layer; only the LEDs, the LCD and the pixel panel are redrawn
/// each frame.
/// </summary>
public abstract class PcbCanvas : Control
{
    private Bitmap? _staticLayer;

    protected PcbCanvas()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = PcbPalette.Backdrop;
    }

    public abstract SizeF DesignSize { get; }

    /// <summary>
    /// Called from the window's frame timer: pull fresh device state and invalidate only if
    /// something changed, so an idle board costs nothing.
    /// </summary>
    public abstract void Sync();

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width <= 0 || Height <= 0) return;

        var g = e.Graphics;
        EnsureStaticLayer();
        if (_staticLayer is not null) g.DrawImageUnscaled(_staticLayer, 0, 0);
        else g.Clear(PcbPalette.Backdrop);

        ConfigureQuality(g);
        var state = g.Save();
        ApplyDesignTransform(g);
        PaintDynamic(g);
        g.Restore(state);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        InvalidateStaticLayer();
        base.OnSizeChanged(e);
    }

    /// <summary>Board artwork: substrate, copper, packages, silkscreen. Cached until the control resizes.</summary>
    protected abstract void PaintStatic(Graphics g);

    /// <summary>Everything that reflects device state.</summary>
    protected abstract void PaintDynamic(Graphics g);

    protected void InvalidateStaticLayer()
    {
        _staticLayer?.Dispose();
        _staticLayer = null;
    }

    private void EnsureStaticLayer()
    {
        if (_staticLayer is not null && _staticLayer.Size == Size) return;

        InvalidateStaticLayer();
        if (Width <= 0 || Height <= 0) return;

        _staticLayer = new Bitmap(Width, Height);
        using var g = Graphics.FromImage(_staticLayer);
        g.Clear(PcbPalette.Backdrop);
        ConfigureQuality(g);
        ApplyDesignTransform(g);
        PaintStatic(g);
    }

    private static void ConfigureQuality(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingQuality = CompositingQuality.HighQuality;
        // ClearType misbehaves under a scale transform, so stick to grayscale antialiasing.
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    private void ApplyDesignTransform(Graphics g)
    {
        float scale = Math.Min(Width / DesignSize.Width, Height / DesignSize.Height);
        g.TranslateTransform(
            (Width - DesignSize.Width * scale) / 2f,
            (Height - DesignSize.Height * scale) / 2f);
        g.ScaleTransform(scale, scale);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) InvalidateStaticLayer();
        base.Dispose(disposing);
    }
}
