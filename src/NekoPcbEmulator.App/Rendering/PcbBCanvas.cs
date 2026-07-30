using System.Drawing;
using System.Drawing.Drawing2D;
using NekoPcbEmulator.App.Interaction;
using NekoPcbEmulator.Core;
using NekoPcbEmulator.Core.Devices.PcbB;

namespace NekoPcbEmulator.App.Rendering;

/// <summary>
/// The PCB-B illustration: a controller board whose output header fans into a ribbon that
/// bundles into a single trunk, enters the matrix at its top-right corner, and then daisy
/// chains through the 5x5 grid.
///
/// The chain is wired as a serpentine, the way a real addressable strip is routed. Logical
/// addressing stays row-major (<c>index = row * 5 + column</c>) and every LED carries its
/// index on the silkscreen, so the picture never has to be decoded to know what to send.
/// </summary>
public sealed class PcbBCanvas : PcbCanvas
{
    private static readonly RectangleF Board = new(40, 30, 800, 240);
    private static readonly RectangleF StatusPlate = new(108, 106, 656, 28);
    private static readonly RectangleF Mcu = new(110, 128, 116, 116);
    private static readonly RectangleF OutHeader = new(600, 238, 160, 32);

    private const float GridOriginX = 160f;
    private const float GridOriginY = 430f;
    private const float ColumnPitch = 140f;
    private const float RowPitch = 100f;
    private const float ModuleSize = 56f;

    // Sized so the carrier PCB stays visible as a border around the LED package.
    private const float LedRadius = 15f;

    private static readonly PointF BundlePoint = new(720, 372);

    /// <summary>Standard ribbon colours, so the six conductors read as a real cable.</summary>
    private static readonly Color[] WireColors =
    [
        Color.FromArgb(0xC0, 0x39, 0x2B), // VCC
        Color.FromArgb(0x8B, 0x5A, 0x2B), // GND
        Color.FromArgb(0xE0, 0x8A, 0x1E), // DIN
        Color.FromArgb(0xD8, 0xC8, 0x3A), // CLK
        Color.FromArgb(0x4C, 0xA6, 0x4C), // spare
        Color.FromArgb(0x4A, 0x86, 0xC8), // spare
    ];

    private static readonly string[] PinLabels = ["V+", "GND", "DIN", "CLK", "NC", "NC"];

    private readonly PcbBDevice _device;
    private readonly Hotspot[] _hotspots;

    private PcbBSnapshot _snapshot;
    private long _renderedVersion = -1;
    private int _renderedClients = -1;

    public PcbBCanvas(PcbHost host) : base(host)
    {
        _device = (PcbBDevice)host.Device;
        _snapshot = _device.Snapshot();

        _hotspots =
        [
            .. Enumerable.Range(0, LedGrid.Count).Select(index =>
            {
                var center = Center(index);
                return new Hotspot(
                    $"LED {index}  ·  R{index / LedGrid.Columns} C{index % LedGrid.Columns}",
                    new RectangleF(center.X - ModuleSize / 2, center.Y - ModuleSize / 2, ModuleSize, ModuleSize),
                    PcbBCommands.Led(index),
                    CornerRadius: 8f);
            }),

            new Hotspot("U1 · matrix controller", Mcu, PcbBCommands.Board(), CornerRadius: 3f),
        ];
    }

    public override SizeF DesignSize => new(880, 946);

    public override IReadOnlyList<Hotspot> Hotspots => _hotspots;

    public override void Sync()
    {
        long version = _device.StateVersion;
        int clients = Host.ClientCount;

        // Timeouts change the picture with no traffic at all, so animation forces a repaint.
        if (version == _renderedVersion && clients == _renderedClients && !_device.IsAnimating) return;

        _renderedVersion = version;
        _renderedClients = clients;
        _snapshot = _device.Snapshot();
        Invalidate();
    }

    private static PointF Center(int row, int column) =>
        new(GridOriginX + column * ColumnPitch, GridOriginY + row * RowPitch);

    private static PointF Center(int index) => Center(index / LedGrid.Columns, index % LedGrid.Columns);

    protected override void PaintStatic(Graphics g)
    {
        BoardPainter.DrawBoard(g, Board);
        DrawRouting(g);

        BoardPainter.DrawText(g, "PCB-B", BoardFonts.Title, PcbPalette.Silk, new RectangleF(112, 44, 400, 40));
        BoardPainter.DrawText(g, "FRAMED BINARY  ·  5x5 ADDRESSABLE MATRIX", BoardFonts.Subtitle,
            PcbPalette.SilkDim, new RectangleF(114, 84, 500, 22));
        BoardPainter.DrawSilkPlate(g, StatusPlate);

        BoardPainter.DrawChip(g, Mcu, "EMU\nCORE", BoardFonts.Chip);
        BoardPainter.DrawText(g, "U1", BoardFonts.Small, PcbPalette.SilkDim,
            new RectangleF(Mcu.Right + 14, Mcu.Top + 2, 60, 16));

        BoardPainter.DrawText(g, "J2  ·  MATRIX BUS", BoardFonts.Label, PcbPalette.SilkDim,
            new RectangleF(OutHeader.Left, OutHeader.Top - 48, 260, 18));
        BoardPainter.DrawHeader(g, OutHeader, PinLabels.Length, horizontal: true);

        DrawRibbon(g);
        DrawChainWiring(g);
        DrawModules(g);
        DrawRulers(g);
    }

    /// <summary>Copper routing on the controller board: rails, then the run out to the bus header.</summary>
    private static void DrawRouting(Graphics g)
    {
        var rail = RectangleF.Inflate(Board, -32, -32);
        BoardPainter.RouteBus(g, new PointF(rail.Left, rail.Top), new PointF(rail.Right, rail.Top), 2, 6, true, heavy: true);
        BoardPainter.RouteBus(g, new PointF(rail.Left, rail.Bottom), new PointF(rail.Right, rail.Bottom), 2, 6, true, heavy: true);
        BoardPainter.RouteBus(g, new PointF(rail.Left, rail.Top), new PointF(rail.Left, rail.Bottom), 2, 6, false, heavy: true);

        // Controller out to the matrix header: six conductors, one per pin.
        BoardPainter.RouteBus(g, new PointF(Mcu.Right, 186), new PointF(OutHeader.Left + 14, OutHeader.Top - 6), 6, 9);

        // Decoupling back to the rail.
        BoardPainter.RouteBus(g, new PointF(Mcu.Left, 150), new PointF(rail.Left, 96), 3, 8, horizontalFirst: false);

        foreach (var via in new[]
                 {
                     new PointF(OutHeader.Left + 14, OutHeader.Top - 6),
                     new PointF(rail.Left, 96),
                     new PointF(rail.Right, 200),
                 })
            BoardPainter.DrawVia(g, via);
    }

    protected override void PaintDynamic(Graphics g)
    {
        string status =
            $"PORT {Host.Endpoint}   CLIENTS {Host.ClientCount}   " +
            $"FRAMES {_snapshot.Frames}   ERR {_snapshot.Errors}   NOISE {_snapshot.DiscardedBytes}B";
        BoardPainter.DrawText(g, status, BoardFonts.Mono,
            _snapshot.Errors > 0 ? PcbPalette.Warn : PcbPalette.Readout,
            RectangleF.Inflate(StatusPlate, -8, -5));

        for (int index = 0; index < LedGrid.Count && index < _snapshot.Cells.Length; index++)
        {
            var cell = _snapshot.Cells[index];
            var center = Center(index);

            BoardPainter.DrawLed(g, center, LedRadius, cell.On ? cell.Color : Rgba.Off);

            if (cell.IsTimed)
                BoardPainter.DrawTimerRing(g, center, LedRadius + 9f, cell.RemainingFraction,
                    BoardPainter.ToColor(cell.Color));
        }
    }

    /// <summary>The six conductors leaving the header, converging into a sleeved trunk.</summary>
    private static void DrawRibbon(Graphics g)
    {
        for (int i = 0; i < WireColors.Length; i++)
        {
            float t = (i + 0.5f) / WireColors.Length;
            var start = new PointF(OutHeader.Left + OutHeader.Width * t, OutHeader.Bottom - 4);

            // Fan out slightly before converging, so the strands stay distinguishable.
            var control1 = new PointF(start.X, start.Y + 46);
            var control2 = new PointF(BundlePoint.X + (start.X - BundlePoint.X) * 0.25f, BundlePoint.Y - 34);

            using var jacket = new Pen(Color.FromArgb(0x08, 0x0A, 0x0C), 9f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            using var core = new Pen(WireColors[i], 5.5f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };

            g.DrawBezier(jacket, start, control1, control2, BundlePoint);
            g.DrawBezier(core, start, control1, control2, BundlePoint);
        }

        // Heat-shrink sleeve where the strands become one cable.
        var sleeve = new RectangleF(BundlePoint.X - 26, BundlePoint.Y - 6, 52, 30);
        using (var body = new LinearGradientBrush(sleeve, PcbPalette.PlasticLight, PcbPalette.Plastic, 0f))
        using (var path = BoardPainter.RoundedRect(sleeve, 7))
            g.FillPath(body, path);
        using (var pen = new Pen(Color.FromArgb(0x05, 0x06, 0x08), 1.6f))
        using (var path = BoardPainter.RoundedRect(sleeve, 7))
            g.DrawPath(pen, path);

        // Trunk down into the top-right corner of the matrix.
        var entry = Center(0, LedGrid.Columns - 1);
        using (var jacket = new Pen(Color.FromArgb(0x08, 0x0A, 0x0C), 13f) { EndCap = LineCap.Round })
            g.DrawLine(jacket, BundlePoint.X, sleeve.Bottom - 2, entry.X, entry.Y);
        using (var core = new Pen(Color.FromArgb(0x2A, 0x2E, 0x34), 8f) { EndCap = LineCap.Round })
            g.DrawLine(core, BundlePoint.X, sleeve.Bottom - 2, entry.X, entry.Y);

        for (int i = 0; i < PinLabels.Length; i++)
        {
            float t = (i + 0.5f) / PinLabels.Length;
            BoardPainter.DrawText(g, PinLabels[i], BoardFonts.Small, PcbPalette.SilkDim,
                new RectangleF(OutHeader.Left + OutHeader.Width * t - 20, OutHeader.Top - 22, 40, 16),
                StringAlignment.Center);
        }
    }

    /// <summary>The serpentine daisy chain through all 25 modules.</summary>
    private static void DrawChainWiring(Graphics g)
    {
        var points = new List<PointF>();

        for (int row = 0; row < LedGrid.Rows; row++)
        {
            // Even rows run right to left, matching the trunk entering at the top-right.
            for (int step = 0; step < LedGrid.Columns; step++)
            {
                int column = row % 2 == 0 ? LedGrid.Columns - 1 - step : step;
                points.Add(Center(row, column));
            }
        }

        using var jacket = new Pen(Color.FromArgb(0x08, 0x0A, 0x0C), 11f)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        using var core = new Pen(Color.FromArgb(0x24, 0x28, 0x2D), 6.5f)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        g.DrawLines(jacket, [.. points]);
        g.DrawLines(core, [.. points]);
    }

    /// <summary>The small carrier PCB under each LED, plus its index on the silkscreen.</summary>
    private static void DrawModules(Graphics g)
    {
        for (int index = 0; index < LedGrid.Count; index++)
        {
            var center = Center(index);
            var module = new RectangleF(center.X - ModuleSize / 2, center.Y - ModuleSize / 2, ModuleSize, ModuleSize);

            using (var body = new LinearGradientBrush(module, Color.FromArgb(0x1A, 0x1D, 0x21), Color.FromArgb(0x0B, 0x0D, 0x0F), 55f))
            using (var path = BoardPainter.RoundedRect(module, 8))
                g.FillPath(body, path);

            using (var pen = new Pen(Color.FromArgb(0x33, 0x39, 0x40), 1.4f))
            using (var path = BoardPainter.RoundedRect(module, 8))
                g.DrawPath(pen, path);

            // Solder pads on the left and right edges, where the chain enters and leaves.
            using (var pad = new SolidBrush(PcbPalette.GoldDark))
            {
                g.FillRectangle(pad, module.Left - 3, center.Y - 5, 8, 10);
                g.FillRectangle(pad, module.Right - 5, center.Y - 5, 8, 10);
            }

            BoardPainter.DrawText(g, index.ToString(), BoardFonts.MonoSmall, PcbPalette.SilkDim,
                new RectangleF(center.X - 30, module.Bottom + 4, 60, 14), StringAlignment.Center);
        }
    }

    /// <summary>
    /// Row and column rulers, so an index can be read off the picture directly. The column
    /// ruler sits below the grid to leave the top-right corner clear for the bus trunk.
    /// </summary>
    private static void DrawRulers(Graphics g)
    {
        float lastRowY = GridOriginY + RowPitch * (LedGrid.Rows - 1);

        for (int column = 0; column < LedGrid.Columns; column++)
            BoardPainter.DrawText(g, $"C{column}", BoardFonts.Label, PcbPalette.SilkDim,
                new RectangleF(Center(0, column).X - 30, lastRowY + 56, 60, 18), StringAlignment.Center);

        for (int row = 0; row < LedGrid.Rows; row++)
            BoardPainter.DrawText(g, $"R{row}", BoardFonts.Label, PcbPalette.SilkDim,
                new RectangleF(GridOriginX - 108, Center(row, 0).Y - 9, 50, 18), StringAlignment.Far);

        BoardPainter.DrawText(g, "index = row * 5 + column", BoardFonts.Small, PcbPalette.SilkDim,
            new RectangleF(GridOriginX - 108, lastRowY + 84, 700, 16));
    }
}
