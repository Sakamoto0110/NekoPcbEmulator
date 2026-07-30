using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using NekoPcbEmulator.App.Forms;
using NekoPcbEmulator.App.Interaction;
using NekoPcbEmulator.Core;

namespace NekoPcbEmulator.App.Rendering;

/// <summary>
/// Base for the two board views. Everything is drawn in a fixed design-space coordinate
/// system that is uniformly scaled and letterboxed into the control, so the layout is written
/// once and survives resizing and DPI changes.
///
/// The board itself (fibreglass, copper, packages, silkscreen) never changes, so it is
/// rendered once into a cached layer; only the LEDs, the LCD and the pixel panel are redrawn
/// each frame.
///
/// Peripherals also register hotspots: hovering one opens its command menu, and picking a
/// command opens a dialog that sends a real protocol message to the board.
/// </summary>
public abstract class PcbCanvas : Control
{
    /// <summary>Long enough that sweeping the pointer across the board does not open menus.</summary>
    private const int HoverOpenDelayMs = 320;

    private readonly System.Windows.Forms.Timer _hoverTimer = new() { Interval = HoverOpenDelayMs };

    private Bitmap? _staticLayer;
    private Hotspot? _hovered;
    private ContextMenuStrip? _menu;

    protected PcbCanvas(PcbHost host)
    {
        Host = host;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = PcbPalette.Backdrop;

        _hoverTimer.Tick += OnHoverElapsed;
    }

    public PcbHost Host { get; }

    public abstract SizeF DesignSize { get; }

    /// <summary>Regions that accept commands. Evaluated on every mouse move, so keep it cheap.</summary>
    public abstract IReadOnlyList<Hotspot> Hotspots { get; }

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
        if (_hovered is { } hotspot)
            BoardPainter.DrawHoverRing(g, hotspot.Bounds, hotspot.CornerRadius);

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

    // ---------------------------------------------------------------- interaction

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateHover(e.Location);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        // Clicking opens the menu straight away rather than waiting out the hover delay.
        if (e.Button != MouseButtons.Left || _hovered is null || _menu is not null) return;
        _hoverTimer.Stop();
        ShowCommandMenu(_hovered);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        // While the menu is open the pointer is legitimately off the canvas, on the menu.
        if (_menu is not null) return;

        _hoverTimer.Stop();
        SetHovered(null);
    }

    private void UpdateHover(Point client)
    {
        if (_menu is not null) return;

        var design = ToDesign(client);
        Hotspot? hit = null;
        foreach (var hotspot in Hotspots)
        {
            if (!hotspot.Bounds.Contains(design)) continue;
            hit = hotspot;
            break;
        }

        if (ReferenceEquals(hit, _hovered)) return;

        SetHovered(hit);
        _hoverTimer.Stop();
        if (hit is not null) _hoverTimer.Start();
    }

    private void SetHovered(Hotspot? hotspot)
    {
        if (ReferenceEquals(hotspot, _hovered)) return;

        _hovered = hotspot;
        Cursor = hotspot is null ? Cursors.Default : Cursors.Hand;
        Invalidate();
    }

    private void OnHoverElapsed(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        if (_hovered is null || _menu is not null) return;
        ShowCommandMenu(_hovered);
    }

    private void ShowCommandMenu(Hotspot hotspot)
    {
        var menu = new ContextMenuStrip
        {
            BackColor = PcbPalette.SurfaceRaised,
            ForeColor = PcbPalette.Text,
            Font = new Font("Segoe UI", 9f),
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()),
            ShowImageMargin = false,
        };

        menu.Items.Add(new ToolStripMenuItem(hotspot.Title)
        {
            Enabled = false,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
        });
        menu.Items.Add(new ToolStripSeparator());

        foreach (var command in hotspot.Commands)
        {
            var item = new ToolStripMenuItem(command.Name) { ForeColor = PcbPalette.Text };
            item.Click += (_, _) => Execute(hotspot, command);
            menu.Items.Add(item);
        }

        menu.Closed += (_, _) =>
        {
            _menu = null;

            // Disposing inline would tear the strip down before the item's Click event is
            // delivered, so let the current message finish first.
            BeginInvoke(menu.Dispose);

            // The pointer may have wandered off while the menu was up.
            UpdateHover(PointToClient(Cursor.Position));
        };

        _menu = menu;
        menu.Show(this, ToClient(new PointF(hotspot.Bounds.Left, hotspot.Bounds.Bottom + 6)));
    }

    private void Execute(Hotspot hotspot, CommandSpec command)
    {
        using var dialog = new CommandDialog(hotspot.Title, command);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        Host.Inject(dialog.Payload);
        Sync();
    }

    // ---------------------------------------------------------------- coordinates

    private float DesignScale => Math.Min(Width / DesignSize.Width, Height / DesignSize.Height);

    private PointF DesignOrigin => new(
        (Width - DesignSize.Width * DesignScale) / 2f,
        (Height - DesignSize.Height * DesignScale) / 2f);

    protected PointF ToDesign(Point client)
    {
        float scale = DesignScale;
        if (scale <= 0) return PointF.Empty;

        var origin = DesignOrigin;
        return new PointF((client.X - origin.X) / scale, (client.Y - origin.Y) / scale);
    }

    protected Point ToClient(PointF design)
    {
        float scale = DesignScale;
        var origin = DesignOrigin;
        return new Point((int)(design.X * scale + origin.X), (int)(design.Y * scale + origin.Y));
    }

    // ---------------------------------------------------------------- painting plumbing

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
        var origin = DesignOrigin;
        g.TranslateTransform(origin.X, origin.Y);
        g.ScaleTransform(DesignScale, DesignScale);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hoverTimer.Stop();
            _hoverTimer.Dispose();
            _menu?.Dispose();
            InvalidateStaticLayer();
        }
        base.Dispose(disposing);
    }

    /// <summary>Dark palette for the peripheral command menus.</summary>
    private sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => PcbPalette.SurfaceRaised;
        public override Color ImageMarginGradientBegin => PcbPalette.SurfaceRaised;
        public override Color ImageMarginGradientMiddle => PcbPalette.SurfaceRaised;
        public override Color ImageMarginGradientEnd => PcbPalette.SurfaceRaised;
        public override Color MenuBorder => PcbPalette.Divider;
        public override Color MenuItemBorder => PcbPalette.Accent;
        public override Color MenuItemSelected => PcbPalette.AccentDim;
        public override Color MenuItemSelectedGradientBegin => PcbPalette.AccentDim;
        public override Color MenuItemSelectedGradientEnd => PcbPalette.AccentDim;
        public override Color MenuItemPressedGradientBegin => PcbPalette.Surface;
        public override Color MenuItemPressedGradientEnd => PcbPalette.Surface;
        public override Color SeparatorDark => PcbPalette.Divider;
        public override Color SeparatorLight => PcbPalette.Divider;
    }
}
