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

    private readonly Label _title = new();
    private readonly Label _summary = new();
    private readonly ComboBox _transport = new();
    private readonly NumericUpDown _port = new();
    private readonly TextBox _pipe = new();
    private readonly Label _endpointLabel = new();
    private readonly CheckBox _useCom0Com = new();
    private readonly Button _power = new();
    private readonly Label _status = new();

    /// <summary>Null until the first refresh, so the initial styling is always applied.</summary>
    private bool? _lastPowered;

    /// <summary>Evaluated once at construction: can this board's COM port actually be opened?</summary>
    private readonly Com0ComStatus _com0Com;

    private const int NamedPipeIndex = 1;
    private const int SerialIndex = 2;

    /// <summary>
    /// Adds or removes the serial entry in the transport list. The checkbox changes what is
    /// offered; the list alone decides what is selected.
    /// </summary>
    private void OnCom0ComToggled()
    {
        bool offerSerial = _useCom0Com.Checked && _com0Com.IsUsable;
        bool listed = _transport.Items.Count > SerialIndex;

        if (offerSerial && !listed)
        {
            _transport.Items.Add("Serial (COM)");
        }
        else if (!offerSerial && listed)
        {
            bool wasSelected = _transport.SelectedIndex == SerialIndex;
            _transport.Items.RemoveAt(SerialIndex);
            if (wasSelected) _transport.SelectedIndex = 0;
        }

        ApplyTransportSelection();
    }

    public PcbCard(PcbHost host, string summary)
    {
        _host = host;

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
        _transport.Items.AddRange(["TCP (loopback)", "Named pipe"]);
        // Seeded from the host so a --pipe startup is reflected here; ApplySettings copies the
        // selection back on power-on, and a stale default would silently override the flag.
        _transport.SelectedIndex = host.Kind == PortKind.NamedPipe ? 1 : 0;
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

        // com0com is an optional, externally installed kernel driver, never a dependency of
        // this project. When it is absent the option stays visible but unchecked and disabled,
        // so the capability is discoverable without the app ever depending on it.
        _useCom0Com.Text = "Use com0com (serial)";
        _useCom0Com.Font = new Font("Segoe UI", 8.5f);
        _useCom0Com.ForeColor = PcbPalette.TextDim;
        _useCom0Com.Location = new Point(18, 130);
        _useCom0Com.Size = new Size(200, 20);
        _com0Com = Com0ComDetector.Evaluate(host.ComPort);
        _useCom0Com.Checked = false;
        _useCom0Com.Enabled = _com0Com.IsUsable;
        _useCom0Com.CheckedChanged += (_, _) => OnCom0ComToggled();

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
        _status.Size = new Size(300, 20);
        _status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        // Seed the list from the host, so a transport chosen on the command line (--serial,
        // --pipe) is reflected in the UI. ApplySettings reads the list, so anything not shown
        // here would be silently discarded the moment the board is powered.
        if (host.Kind == PortKind.Serial && _com0Com.IsUsable)
        {
            _useCom0Com.Checked = true;      // adds the serial entry through OnCom0ComToggled
            _transport.SelectedIndex = SerialIndex;
            _pipe.Text = host.ComPort;
        }

        Controls.AddRange([_title, _summary, transportLabel, _transport, _endpointLabel, _port, _pipe, _useCom0Com, _power, _status]);
        ApplyTransportSelection();
        RefreshState();

        // Always explain the state, so a disabled checkbox is never a mystery.
        var tip = new ToolTip();
        tip.SetToolTip(_useCom0Com, _com0Com.Detail);
    }

    public event EventHandler? ToggleRequested;

    /// <summary>Copies the editable fields into the host. Called right before powering on.</summary>
    public void ApplySettings()
    {
        // The transport list is the single source of truth. The com0com checkbox only decides
        // whether the serial entry is offered in it — two controls deciding one value is what
        // made the previous version ambiguous.
        switch (_transport.SelectedIndex)
        {
            case SerialIndex:
                _host.Kind = PortKind.Serial;
                _host.ComPort = string.IsNullOrWhiteSpace(_pipe.Text) ? _host.ComPort : _pipe.Text.Trim();
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
            _useCom0Com.Enabled = !powered && _com0Com.IsUsable;
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

        // The free-text box doubles as the pipe name and the COM port name.
        _pipe.Visible = serial || pipe;
        _port.Visible = !serial && !pipe;

        if (serial && !_pipe.Text.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            _pipe.Text = _host.ComPort;
        else if (pipe && _pipe.Text.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            _pipe.Text = _host.PipeName;
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
