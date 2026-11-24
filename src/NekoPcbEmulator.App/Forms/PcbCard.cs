using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using NekoPcbEmulator.App.Rendering;
using NekoPcbEmulator.Core;

namespace NekoPcbEmulator.App.Forms;

/// <summary>Launcher tile for one board: transport selection, the power switch, and live status.</summary>
internal sealed class PcbCard : Panel
{
    private readonly PcbHost _host;
    private readonly IReadOnlyCollection<string> _boardPorts;

    private readonly Label _title = new();
    private readonly Label _summary = new();
    private readonly ComboBox _transport = new();
    private readonly NumericUpDown _port = new();
    private readonly TextBox _pipe = new();
    private readonly ComboBox _serialPort = new();
    private readonly Label _endpointLabel = new();
    private readonly Label _serialHint = new();
    private readonly Button _power = new();
    private readonly Label _status = new();

    /// <summary>Null until the first refresh, so the initial styling is always applied.</summary>
    private bool? _lastPowered;

    private const int NamedPipeIndex = 1;
    private const int SerialIndex = 2;

    public PcbCard(PcbHost host, string summary, IReadOnlyCollection<string> boardPorts)
    {
        _host = host;
        _boardPorts = boardPorts;

        BackColor = PcbPalette.SurfaceRaised;
        Padding = new Padding(18, 14, 18, 14);

        _title.Text = host.Device.DisplayName;
        _title.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        _title.ForeColor = PcbPalette.Text;
        _title.AutoSize = true;
        _title.Location = new Point(18, 14);

        _summary.Text = summary;
        _summary.Font = new Font("Segoe UI", 8.5f);
        _summary.ForeColor = PcbPalette.TextDim;
        _summary.Location = new Point(18, 40);
        _summary.Size = new Size(360, 34);
        _summary.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var transportLabel = MakeCaption("TRANSPORT", new Point(18, 84));
        _transport.DropDownStyle = ComboBoxStyle.DropDownList;
        _transport.FlatStyle = FlatStyle.Flat;
        _transport.BackColor = PcbPalette.Surface;
        _transport.ForeColor = PcbPalette.Text;
        _transport.Font = new Font("Segoe UI", 9f);
        _transport.Location = new Point(18, 102);
        _transport.Size = new Size(140, 24);
        _transport.Items.AddRange(["TCP (loopback)", "Named pipe", "Serial (COM)"]);
        _transport.SelectedIndex = host.Kind switch
        {
            PortKind.NamedPipe => NamedPipeIndex,
            PortKind.Serial => SerialIndex,
            _ => 0,
        };
        _transport.SelectedIndexChanged += (_, _) => ApplyTransportSelection();

        _endpointLabel.Text = "PORT";
        _endpointLabel.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        _endpointLabel.ForeColor = PcbPalette.TextDim;
        _endpointLabel.AutoSize = true;
        _endpointLabel.Location = new Point(174, 84);

        _port.Minimum = 0;
        _port.Maximum = 65535;
        _port.Value = host.TcpPort;
        _port.BorderStyle = BorderStyle.FixedSingle;
        _port.BackColor = PcbPalette.Surface;
        _port.ForeColor = PcbPalette.Text;
        _port.Font = new Font("Consolas", 9.5f);
        _port.Location = new Point(174, 102);
        _port.Size = new Size(96, 24);

        _pipe.Text = host.PipeName;
        _pipe.BorderStyle = BorderStyle.FixedSingle;
        _pipe.BackColor = PcbPalette.Surface;
        _pipe.ForeColor = PcbPalette.Text;
        _pipe.Font = new Font("Consolas", 9.5f);
        _pipe.Location = new Point(174, 102);
        _pipe.Size = new Size(180, 24);
        _pipe.Visible = false;

        _serialPort.DropDownStyle = ComboBoxStyle.DropDownList;
        _serialPort.FlatStyle = FlatStyle.Flat;
        _serialPort.BackColor = PcbPalette.Surface;
        _serialPort.ForeColor = PcbPalette.Text;
        _serialPort.Font = new Font("Consolas", 9.5f);
        _serialPort.Location = new Point(174, 102);
        _serialPort.Size = new Size(180, 24);
        _serialPort.AccessibleName = "Serial port";
        _serialPort.DropDown += (_, _) => RefreshSerialPorts();

        _serialHint.Font = new Font("Segoe UI", 8.5f);
        _serialHint.ForeColor = PcbPalette.TextDim;
        _serialHint.Location = new Point(18, 132);
        _serialHint.Size = new Size(400, 20);
        _serialHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _power.Text = "POWER ON";
        _power.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _power.FlatStyle = FlatStyle.Flat;
        _power.FlatAppearance.BorderSize = 0;
        _power.ForeColor = Color.White;
        _power.Location = new Point(18, 154);
        _power.Size = new Size(140, 36);
        _power.Cursor = Cursors.Hand;
        _power.Click += (_, _) => ToggleRequested?.Invoke(this, EventArgs.Empty);

        _status.Font = new Font("Consolas", 9f);
        _status.ForeColor = PcbPalette.TextDim;
        _status.Location = new Point(174, 160);
        _status.Size = new Size(300, 36);
        _status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        Controls.AddRange([_title, _summary, transportLabel, _transport, _endpointLabel, _port, _pipe, _serialPort, _serialHint, _power, _status]);
        RefreshSerialPorts();
        _serialPort.SelectedIndex = _serialPort.FindStringExact(host.ComPort);
        ApplyTransportSelection();
        RefreshState();
    }

    public event EventHandler? ToggleRequested;

    /// <summary>Copies the editable fields into the host. Called right before powering on.</summary>
    public void ApplySettings()
    {
        switch (_transport.SelectedIndex)
        {
            case SerialIndex:
                _host.Kind = PortKind.Serial;
                _host.ComPort = _serialPort.Text.Trim();
                break;

            case NamedPipeIndex:
                _host.Kind = PortKind.NamedPipe;
                _host.PipeName = string.IsNullOrWhiteSpace(_pipe.Text) ? _host.PipeName : _pipe.Text.Trim();
                break;

            default:
                _host.Kind = PortKind.Tcp;
                _host.TcpPort = (int)_port.Value;
                break;
        }
    }

    public void RefreshState()
    {
        bool powered = _host.IsPowered;

        if (powered != _lastPowered)
        {
            _lastPowered = powered;
            _power.Text = powered ? "POWER OFF" : "POWER ON";
            _power.BackColor = powered ? PcbPalette.Danger : PcbPalette.Accent;
            _transport.Enabled = !powered;
            _port.Enabled = !powered;
            _pipe.Enabled = !powered;
            _serialPort.Enabled = !powered;
            Invalidate();
        }

        _status.Text = powered
            ? $"{_host.Endpoint}\n{_host.ClientCount} client(s) connected"
            : "offline";
        _status.ForeColor = powered ? PcbPalette.Accent : PcbPalette.TextDim;
    }

    private void ApplyTransportSelection()
    {
        bool serial = _transport.SelectedIndex == SerialIndex;
        bool pipe = _transport.SelectedIndex == NamedPipeIndex;

        _endpointLabel.Text = serial ? "COM PORT" : pipe ? "PIPE NAME" : "PORT";

        _pipe.Visible = pipe;
        _serialPort.Visible = serial;
        _serialHint.Visible = serial;
        _port.Visible = !serial && !pipe;

        if (serial) RefreshSerialPorts();
    }

    /// <summary>Refreshes discovery without opening ports or replacing the user's selection.</summary>
    private void RefreshSerialPorts()
    {
        string selected = _serialPort.Text;
        string[] ports = Com0ComDetector.CurrentPorts()
            .Where(port => _boardPorts.Contains(port, StringComparer.OrdinalIgnoreCase))
            .Where(port => port.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(port.AsSpan(3), out int number) && number > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(port => port.Length)
            .ThenBy(port => port, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _serialPort.BeginUpdate();
        try
        {
            _serialPort.Items.Clear();
            _serialPort.Items.AddRange(ports);
            _serialPort.SelectedIndex = _serialPort.FindStringExact(selected);
        }
        finally
        {
            _serialPort.EndUpdate();
        }

        _serialHint.Text = ports.Length == 0
            ? "No configured board ports detected. Reconnect and reopen this list."
            : "115200 baud, 8N1, no flow control";
    }

    private static Label MakeCaption(string text, Point location) => new()
    {
        Text = text,
        Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
        ForeColor = PcbPalette.TextDim,
        AutoSize = true,
        Location = location,
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var border = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = BoardPainter.RoundedRect(border, 8);
        using var pen = new Pen(_host.IsPowered ? PcbPalette.AccentDim : PcbPalette.Divider, 1.4f);
        e.Graphics.DrawPath(pen, path);

        // Status dot next to the title.
        var dot = new RectangleF(Width - 30, 20, 11, 11);
        using var fill = new SolidBrush(_host.IsPowered ? PcbPalette.Accent : PcbPalette.Divider);
        e.Graphics.FillEllipse(fill, dot);

        if (!_host.IsPowered) return;
        using var halo = new SolidBrush(Color.FromArgb(70, PcbPalette.Accent));
        e.Graphics.FillEllipse(halo, RectangleF.Inflate(dot, 5, 5));
    }
}
