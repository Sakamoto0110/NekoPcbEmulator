using System.Drawing;
using System.Windows.Forms;
using NekoPcbEmulator.App.Rendering;
using NekoPcbEmulator.Core;
using NekoPcbEmulator.Core.Devices.PcbA;
using NekoPcbEmulator.Core.Devices.PcbB;

namespace NekoPcbEmulator.App.Forms;

/// <summary>
/// The launcher. Powering a board on opens its port and its window; closing that window powers
/// the board back off.
/// </summary>
public sealed class MainForm : Form
{
    private const int LogCapacity = 4000;

    private readonly LogSink _log = new(LogCapacity);
    private readonly PcbHost _hostA;
    private readonly PcbHost _hostB;
    private readonly PcbCard _cardA;
    private readonly PcbCard _cardB;
    private readonly Dictionary<PcbHost, PcbWindow> _windows = [];

    private readonly ListBox _logView = new();
    private readonly System.Windows.Forms.Timer _pump = new() { Interval = 100 };

    private readonly StartupOptions _startup;

    public MainForm(StartupOptions? startup = null)
    {
        _startup = startup ?? StartupOptions.Parse([]);

        _hostA = new PcbHost(new PcbADevice(_log), _startup.PortA, pipeName: "pcb-a")
        {
            Kind = _startup.Kind,
        };
        _hostB = new PcbHost(new PcbBDevice(_log), _startup.PortB, pipeName: "pcb-b")
        {
            Kind = _startup.Kind,
        };

        _cardA = new PcbCard(_hostA, "3 x RGBA indicator · character LCD 20x4 with 5 slots\n360x120 RGBA pixel panel (POINT / LINE / RECT)");
        _cardB = new PcbCard(_hostB, "5x5 addressable matrix over a ribbon bus\nper-LED colour and on-time, CRC16 framing");

        Text = "NekoPcbEmulator";
        Icon = AppIcon.Create();
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = PcbPalette.Backdrop;
        ClientSize = new Size(1000, 740);
        MinimumSize = new Size(880, 620);
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();

        _cardA.ToggleRequested += (_, _) => Toggle(_hostA, _cardA);
        _cardB.ToggleRequested += (_, _) => Toggle(_hostB, _cardB);

        _pump.Tick += OnPump;
        _pump.Start();

        _log.Write("host", LogLevel.Info, "ready — power a board to open its port");
    }

    private void BuildLayout()
    {
        var footer = BuildFooter();

        var logPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 0, 20, 0) };
        _logView.Dock = DockStyle.Fill;
        _logView.DrawMode = DrawMode.OwnerDrawFixed;
        _logView.ItemHeight = 18;
        _logView.IntegralHeight = false;
        _logView.BorderStyle = BorderStyle.FixedSingle;
        _logView.BackColor = PcbPalette.Surface;
        _logView.ForeColor = PcbPalette.Text;
        _logView.DrawItem += OnDrawLogItem;
        logPanel.Controls.Add(_logView);

        var logHeader = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(20, 8, 20, 0) };
        logHeader.Controls.Add(new Label
        {
            Text = "TRAFFIC LOG",
            Dock = DockStyle.Left,
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = PcbPalette.TextDim,
            AutoSize = true,
        });

        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 214,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(20, 0, 20, 14),
        };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        _cardA.Dock = DockStyle.Fill;
        _cardB.Dock = DockStyle.Fill;
        _cardA.Margin = new Padding(0, 0, 8, 0);
        _cardB.Margin = new Padding(8, 0, 0, 0);
        cards.Controls.Add(_cardA, 0, 0);
        cards.Controls.Add(_cardB, 1, 0);

        var header = new Panel { Dock = DockStyle.Top, Height = 78, Padding = new Padding(20, 18, 20, 0) };
        header.Controls.Add(new Label
        {
            Text = "NekoPcbEmulator",
            Font = new Font("Segoe UI", 17f, FontStyle.Bold),
            ForeColor = PcbPalette.Text,
            AutoSize = true,
            Location = new Point(20, 16),
        });
        header.Controls.Add(new Label
        {
            Text = "emulated boards with an RX/TX port for an external test suite",
            Font = new Font("Segoe UI", 9f),
            ForeColor = PcbPalette.TextDim,
            AutoSize = true,
            Location = new Point(22, 48),
        });

        // Docked children fill in reverse order of addition.
        Controls.Add(logPanel);
        Controls.Add(logHeader);
        Controls.Add(cards);
        Controls.Add(header);
        Controls.Add(footer);
    }

    private Panel BuildFooter()
    {
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(20, 10, 20, 12) };

        var clear = MakeFooterButton("Clear log", new Point(20, 14));
        clear.Click += (_, _) => _logView.Items.Clear();

        var reset = MakeFooterButton("Reset boards", new Point(140, 14));
        reset.Click += (_, _) =>
        {
            _hostA.Device.Reset();
            _hostB.Device.Reset();
            _log.Write("host", LogLevel.Info, "both boards reset");
        };

        footer.Controls.Add(clear);
        footer.Controls.Add(reset);
        footer.Controls.Add(new Label
        {
            Text = "F5 inside a board window resets that board",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = PcbPalette.TextDim,
            AutoSize = true,
            Location = new Point(276, 22),
        });
        return footer;
    }

    private static Button MakeFooterButton(string text, Point location)
    {
        var button = new Button
        {
            Text = text,
            Location = location,
            Size = new Size(110, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = PcbPalette.SurfaceRaised,
            ForeColor = PcbPalette.Text,
            Font = new Font("Segoe UI", 8.5f),
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderColor = PcbPalette.Divider;
        return button;
    }

    private void Toggle(PcbHost host, PcbCard card)
    {
        if (host.IsPowered)
        {
            // Closing the window is the single power-off path; it tears the host down for us.
            if (_windows.TryGetValue(host, out var open)) open.Close();
            else host.PowerOff();

            card.RefreshState();
            return;
        }

        card.ApplySettings();

        try
        {
            host.PowerOn();
        }
        catch (Exception ex)
        {
            _log.Write(host.Device.Id, LogLevel.Error, $"power on failed: {ex.Message}");
            MessageBox.Show(
                this,
                $"Could not open the port for {host.Device.Id}.\n\n{ex.Message}",
                "NekoPcbEmulator",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            card.RefreshState();
            return;
        }

        var window = CreateWindow(host);
        _windows[host] = window;

        window.FormClosed += (_, _) =>
        {
            _windows.Remove(host);
            host.PowerOff();
            card.RefreshState();
        };

        window.Show(this);
        card.RefreshState();
    }

    private PcbWindow CreateWindow(PcbHost host)
    {
        PcbCanvas canvas;
        Size size;

        if (host.Device is PcbADevice)
        {
            canvas = new PcbACanvas(host);
            size = new Size(920, 796);
        }
        else
        {
            canvas = new PcbBCanvas(host);
            size = new Size(712, 766);
        }

        var window = new PcbWindow(host, canvas, size);

        // Cascade to the right of the launcher so nothing lands on top of it.
        var screen = Screen.FromControl(this).WorkingArea;
        int x = Math.Min(Right + 16 + _windows.Count * 32, screen.Right - size.Width - 16);
        int y = Math.Min(Top + _windows.Count * 32, screen.Bottom - size.Height - 16);
        window.Location = new Point(Math.Max(screen.Left, x), Math.Max(screen.Top, y));

        return window;
    }

    private void OnPump(object? sender, EventArgs e)
    {
        _cardA.RefreshState();
        _cardB.RefreshState();

        var entries = _log.Drain();
        if (entries.Count == 0) return;

        bool atBottom = _logView.TopIndex >= _logView.Items.Count - (_logView.ClientSize.Height / _logView.ItemHeight) - 1;

        _logView.BeginUpdate();
        foreach (var entry in entries) _logView.Items.Add(entry);

        int excess = _logView.Items.Count - LogCapacity;
        for (int i = 0; i < excess; i++) _logView.Items.RemoveAt(0);
        _logView.EndUpdate();

        if (atBottom && _logView.Items.Count > 0)
            _logView.TopIndex = _logView.Items.Count - 1;
    }

    private void OnDrawLogItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _logView.Items.Count) return;

        var entry = (LogEntry)_logView.Items[e.Index]!;
        var bounds = e.Bounds;

        using (var background = new SolidBrush(e.Index % 2 == 0 ? PcbPalette.Surface : PcbPalette.Backdrop))
            e.Graphics.FillRectangle(background, bounds);

        Color color = entry.Level switch
        {
            LogLevel.Rx => PcbPalette.Rx,
            LogLevel.Tx => PcbPalette.Tx,
            LogLevel.Warn => PcbPalette.Warn,
            LogLevel.Error => PcbPalette.Danger,
            _ => PcbPalette.TextDim,
        };

        var font = new Font("Consolas", 9f);
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;

        TextRenderer.DrawText(e.Graphics, entry.Timestamp.ToString("HH:mm:ss.fff"), font,
            new Rectangle(bounds.X + 6, bounds.Y, 90, bounds.Height), PcbPalette.TextDim, flags);
        TextRenderer.DrawText(e.Graphics, entry.Source, font,
            new Rectangle(bounds.X + 100, bounds.Y, 56, bounds.Height), PcbPalette.Text, flags);
        TextRenderer.DrawText(e.Graphics, LevelTag(entry.Level), font,
            new Rectangle(bounds.X + 160, bounds.Y, 40, bounds.Height), color, flags);
        TextRenderer.DrawText(e.Graphics, entry.Message, font,
            new Rectangle(bounds.X + 202, bounds.Y, bounds.Width - 208, bounds.Height), color, flags);

        font.Dispose();
    }

    private static string LevelTag(LogLevel level) => level switch
    {
        LogLevel.Rx => "RX",
        LogLevel.Tx => "TX",
        LogLevel.Warn => "WARN",
        LogLevel.Error => "ERR",
        _ => "",
    };

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DarkTitleBar.Apply(this);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // --power opens the requested ports and windows without anyone clicking, which is what
        // makes the emulator usable from a scripted test run.
        if (_startup.PowerA) Toggle(_hostA, _cardA);
        if (_startup.PowerB) Toggle(_hostB, _cardB);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _pump.Stop();
        foreach (var window in _windows.Values.ToArray()) window.Close();
        _hostA.Dispose();
        _hostB.Dispose();
        base.OnFormClosed(e);
    }
}
