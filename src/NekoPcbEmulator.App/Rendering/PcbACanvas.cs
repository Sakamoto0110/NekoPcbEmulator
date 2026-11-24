using System.Drawing;
using System.Drawing.Drawing2D;
using NekoPcbEmulator.App.Interaction;
using NekoPcbEmulator.Core;
using NekoPcbEmulator.Core.Devices.PcbA;

namespace NekoPcbEmulator.App.Rendering;

/// <summary>
/// The PCB-A board illustration: two rows of RGBA indicators, a 20x4 character LCD and the 360x120
/// pixel panel, laid out on a single fibreglass board with its UART header on the left edge.
/// </summary>
public sealed class PcbACanvas : PcbCanvas
{
    private static readonly RectangleF Board = new(24, 24, 992, 964);
    private static readonly RectangleF StatusPlate = new(108, 104, 656, 28);
    private static readonly RectangleF UartHeader = new(52, 189, 34, 150);
    private static readonly RectangleF Mcu = new(150, 199, 130, 130);

    private const float LedOriginX = 680f;
    private const float LedOriginY = 206f;
    private const float LedColumnPitch = 120f;
    private const float LedRowPitch = 116f;
    private const float LedRadius = 26f;

    /// <summary>Half the side of the silkscreen outline, which is also the clickable area.</summary>
    private const float LedOutline = 42f;

    private static readonly PointF[] LedCenters =
    [
        .. Enumerable.Range(0, IndicatorPanel.Count).Select(index => new PointF(
            LedOriginX + index % IndicatorPanel.Columns * LedColumnPitch,
            LedOriginY + index / IndicatorPanel.Columns * LedRowPitch)),
    ];

    private static readonly RectangleF LcdModule = new(130, 428, 660, 240);
    private static readonly RectangleF LcdGlass = new(160, 458, 600, 180);
    private static readonly RectangleF SlotPanel = new(804, 428, 166, 240);

    private static readonly RectangleF PanelModule = new(140, 690, 760, 274);
    private static readonly RectangleF PanelGlass = new(160, 716, 720, 240);

    private static readonly string[] UartPins = ["VCC", "TX", "RX", "GND"];

    private readonly PcbADevice _device;
    private readonly LcdScreenRenderer _lcdRenderer = new();
    private readonly PixelPanelRenderer _panelRenderer = new();
    private readonly Hotspot[] _hotspots;

    private PcbASnapshot _snapshot;
    private long _renderedVersion = -1;
    private int _renderedClients = -1;

    public PcbACanvas(PcbHost host) : base(host)
    {
        _device = (PcbADevice)host.Device;
        _snapshot = _device.Snapshot();
        _panelRenderer.Update(_device);

        // The pixel panel is deliberately absent: its drawing commands are a poor fit for a
        // form, and it is far more useful driven from a script.
        _hotspots =
        [
            .. LedCenters.Select((center, index) => new Hotspot(
                $"LIGHT[{index}]",
                LedBounds(center),
                PcbACommands.Light(index))),

            new Hotspot("LCD1 · character LCD 20x4", LcdModule, PcbACommands.Lcd(), CornerRadius: 9f),
            new Hotspot("U1 · controller", Mcu, PcbACommands.System(), CornerRadius: 3f),
        ];
    }

    public override SizeF DesignSize => new(1040, 1012);

    public override IReadOnlyList<Hotspot> Hotspots => _hotspots;

    /// <summary>Pulls fresh state and repaints only when something actually changed.</summary>
    public override void Sync()
    {
        long version = _device.StateVersion;
        int clients = Host.ClientCount;
        bool changed = version != _renderedVersion;

        // Timeouts change the picture with no traffic at all, so animation forces a repaint.
        if (!changed && clients == _renderedClients && !_device.IsAnimating) return;

        _renderedVersion = version;
        _renderedClients = clients;
        _snapshot = _device.Snapshot();
        if (changed) _panelRenderer.Update(_device);
        Invalidate();
    }

    private static RectangleF LedBounds(PointF center) =>
        new(center.X - LedOutline, center.Y - LedOutline, LedOutline * 2, LedOutline * 2);

    protected override void PaintStatic(Graphics g)
    {
        BoardPainter.DrawBoard(g, Board);
        DrawRouting(g);

        BoardPainter.DrawText(g, "PCB-A", BoardFonts.Title, PcbPalette.Silk, new RectangleF(112, 42, 400, 40));
        BoardPainter.DrawText(g, "RAW ASCII  ·  UART 8N1", BoardFonts.Subtitle, PcbPalette.SilkDim,
            new RectangleF(114, 82, 400, 22));
        BoardPainter.DrawSilkPlate(g, StatusPlate);

        // UART header on the left edge, the port the host attaches to.
        BoardPainter.DrawHeader(g, UartHeader, UartPins.Length, horizontal: false);
        for (int i = 0; i < UartPins.Length; i++)
        {
            float y = UartHeader.Top + UartHeader.Height * (i + 0.5f) / UartPins.Length - 8;
            BoardPainter.DrawText(g, UartPins[i], BoardFonts.Small, PcbPalette.SilkDim,
                new RectangleF(UartHeader.Right + 6, y, 46, 16));
        }
        BoardPainter.DrawText(g, "J1", BoardFonts.Label, PcbPalette.Silk,
            new RectangleF(UartHeader.Left, UartHeader.Top - 22, 40, 18));

        BoardPainter.DrawChip(g, Mcu, "EMU\nCORE", BoardFonts.Chip);
        BoardPainter.DrawText(g, "U1", BoardFonts.Small, PcbPalette.SilkDim,
            new RectangleF(Mcu.Right + 14, Mcu.Top + 2, 60, 16));

        // Indicator LEDs.
        BoardPainter.DrawText(g, "INDICATORS", BoardFonts.Label, PcbPalette.SilkDim,
            new RectangleF(LedBounds(LedCenters[0]).Left, LedBounds(LedCenters[0]).Top - 24, 300, 18));
        for (int i = 0; i < LedCenters.Length; i++)
        {
            BoardPainter.DrawSilkOutline(g, LedBounds(LedCenters[i]));

            BoardPainter.DrawText(g, $"LIGHT[{i}]", BoardFonts.Label, PcbPalette.Silk,
                new RectangleF(LedCenters[i].X - 60, LedCenters[i].Y + 48, 120, 18),
                StringAlignment.Center);
        }

        // Character LCD module.
        BoardPainter.DrawText(g, "LCD1  ·  CHARACTER LCD 20x4", BoardFonts.Label, PcbPalette.SilkDim,
            new RectangleF(LcdModule.Left, LcdModule.Top - 24, 420, 18));
        DrawModuleFrame(g, LcdModule, LcdGlass);

        // Memory slot readout.
        DrawSilkBox(g, SlotPanel, "MEM SLOTS");

        // Pixel panel module.
        BoardPainter.DrawText(g, "DM1  ·  RGBA LED MATRIX 360x120", BoardFonts.Label, PcbPalette.SilkDim,
            new RectangleF(PanelModule.Left, PanelModule.Top - 24, 480, 18));
        DrawModuleFrame(g, PanelModule, PanelGlass);
    }

    protected override void PaintDynamic(Graphics g)
    {
        string status =
            $"PORT {Host.Endpoint}   CLIENTS {Host.ClientCount}   " +
            $"CMD {_snapshot.Commands}   ERR {_snapshot.Errors}   LIT {_snapshot.LitPixels}";
        BoardPainter.DrawText(g, status, BoardFonts.Mono,
            _snapshot.Errors > 0 ? PcbPalette.Warn : PcbPalette.Readout,
            RectangleF.Inflate(StatusPlate, -8, -5));

        for (int i = 0; i < LedCenters.Length && i < _snapshot.Lights.Length; i++)
        {
            var light = _snapshot.Lights[i];
            BoardPainter.DrawLed(g, LedCenters[i], LedRadius, light.Emitted);

            if (light.IsTimed)
                BoardPainter.DrawTimerRing(g, LedCenters[i], LedRadius + 9f, light.RemainingFraction,
                    BoardPainter.ToColor(light.Color));
        }

        _lcdRenderer.Draw(g, LcdGlass, _snapshot.LcdLines);
        _panelRenderer.Draw(g, PanelGlass);

        DrawSlotContents(g);
    }

    private void DrawSlotContents(Graphics g)
    {
        float y = SlotPanel.Top + 34;

        for (int i = 0; i < _snapshot.LcdSlots.Length; i++)
        {
            string text = _snapshot.LcdSlots[i];
            bool empty = string.IsNullOrEmpty(text);

            BoardPainter.DrawText(g, $"{i}", BoardFonts.MonoSmall, PcbPalette.SilkDim,
                new RectangleF(SlotPanel.Left + 12, y, 16, 16));
            BoardPainter.DrawText(g, empty ? "—" : text, BoardFonts.MonoSmall,
                empty ? PcbPalette.SilkDim : PcbPalette.Silk,
                new RectangleF(SlotPanel.Left + 30, y, SlotPanel.Width - 42, 16));
            y += 22;
        }

        y += 14;
        BoardPainter.DrawText(g, "LOADED", BoardFonts.Small, PcbPalette.SilkDim,
            new RectangleF(SlotPanel.Left + 12, y, 120, 16));
        BoardPainter.DrawText(g,
            string.IsNullOrEmpty(_snapshot.LcdLoaded) ? "—" : _snapshot.LcdLoaded,
            BoardFonts.MonoSmall, PcbPalette.Readout,
            new RectangleF(SlotPanel.Left + 12, y + 20, SlotPanel.Width - 24, 16));
    }

    /// <summary>Plastic module frame with a recessed window, as used by both display modules.</summary>
    private static void DrawModuleFrame(Graphics g, RectangleF outer, RectangleF glass)
    {
        using (var body = new LinearGradientBrush(outer, PcbPalette.PlasticLight, PcbPalette.Plastic, 62f))
        using (var path = BoardPainter.RoundedRect(outer, 9))
            g.FillPath(body, path);

        using (var pen = new Pen(Color.FromArgb(0x04, 0x05, 0x07), 2f))
        using (var path = BoardPainter.RoundedRect(outer, 9))
            g.DrawPath(pen, path);

        // Recess: dark inside, faint highlight on the lower right.
        using (var shadow = new Pen(Color.FromArgb(170, 0, 0, 0), 5f))
            g.DrawRectangle(shadow, glass.X - 2.5f, glass.Y - 2.5f, glass.Width + 5, glass.Height + 5);
        using (var lip = new Pen(Color.FromArgb(45, 255, 255, 255), 1.2f))
            g.DrawRectangle(lip, glass.X - 5, glass.Y - 5, glass.Width + 10, glass.Height + 10);

        foreach (var screw in new[]
                 {
                     new PointF(outer.Left + 13, outer.Top + 13),
                     new PointF(outer.Right - 13, outer.Top + 13),
                     new PointF(outer.Left + 13, outer.Bottom - 13),
                     new PointF(outer.Right - 13, outer.Bottom - 13),
                 })
        {
            using var head = new SolidBrush(PcbPalette.Metal);
            using var slot = new Pen(Color.FromArgb(0x30, 0x34, 0x38), 1.6f);
            g.FillEllipse(head, screw.X - 4.5f, screw.Y - 4.5f, 9, 9);
            g.DrawLine(slot, screw.X - 3, screw.Y, screw.X + 3, screw.Y);
        }
    }

    /// <summary>
    /// Copper routing. Every run connects two real components, which is what separates a
    /// board that looks designed from one that looks decorated.
    /// </summary>
    private static void DrawRouting(Graphics g)
    {
        // Power rail following the board outline, as wide as the plotter allows.
        var rail = RectangleF.Inflate(Board, -34, -34);
        BoardPainter.RouteBus(g, new PointF(rail.Left, rail.Top), new PointF(rail.Right, rail.Top), 2, 6, true, heavy: true);
        BoardPainter.RouteBus(g, new PointF(rail.Right, rail.Top), new PointF(rail.Right, rail.Bottom), 2, 6, false, heavy: true);
        BoardPainter.RouteBus(g, new PointF(rail.Left, rail.Top), new PointF(rail.Left, rail.Bottom), 2, 6, false, heavy: true);

        // UART header into the controller.
        BoardPainter.RouteBus(g, new PointF(UartHeader.Right, 264), new PointF(Mcu.Left, 264), 4, 8);

        // Controller out to each indicator row, then along it.
        for (int row = 0; row < IndicatorPanel.Rows; row++)
        {
            int first = row * IndicatorPanel.Columns;
            BoardPainter.RouteBus(g,
                new PointF(Mcu.Right, 222 + row * 42),
                new PointF(LedCenters[first].X - 40, LedCenters[first].Y), 3, 8);

            for (int i = first; i < first + IndicatorPanel.Columns - 1; i++)
                BoardPainter.RouteBus(g,
                    new PointF(LedCenters[i].X + 40, LedCenters[i].Y),
                    new PointF(LedCenters[i + 1].X - 40, LedCenters[i + 1].Y), 3, 8);
        }

        // Controller down to the LCD module, and across to the slot readout.
        BoardPainter.RouteBus(g, new PointF(200, Mcu.Bottom), new PointF(230, LcdModule.Top), 6, 9, horizontalFirst: false);
        BoardPainter.RouteBus(g, new PointF(LcdModule.Right, 512), new PointF(SlotPanel.Left, 512), 3, 9);

        // Down the left margin to the pixel panel, clear of the LCD footprint.
        BoardPainter.RouteBus(g, new PointF(104, 345), new PointF(176, PanelModule.Top), 5, 8, horizontalFirst: false);
        BoardPainter.RouteBus(g, new PointF(Mcu.Right, 305), new PointF(PanelModule.Right - 60, PanelModule.Top), 4, 8);

        foreach (var via in new[]
                 {
                     new PointF(104, 345), new PointF(230, LcdModule.Top), new PointF(SlotPanel.Left, 512),
                     new PointF(176, PanelModule.Top), new PointF(PanelModule.Right - 60, PanelModule.Top),
                 })
            BoardPainter.DrawVia(g, via);
    }

    private static void DrawSilkBox(Graphics g, RectangleF rect, string title)
    {
        using (var fill = new SolidBrush(Color.FromArgb(56, 0, 0, 0)))
        using (var path = BoardPainter.RoundedRect(rect, 7))
            g.FillPath(fill, path);

        using (var pen = new Pen(Color.FromArgb(120, PcbPalette.Silk), 1.3f))
        using (var path = BoardPainter.RoundedRect(rect, 7))
            g.DrawPath(pen, path);

        BoardPainter.DrawText(g, title, BoardFonts.Label, PcbPalette.Silk,
            new RectangleF(rect.Left + 12, rect.Top + 9, rect.Width - 24, 18));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lcdRenderer.Dispose();
            _panelRenderer.Dispose();
        }
        base.Dispose(disposing);
    }
}
