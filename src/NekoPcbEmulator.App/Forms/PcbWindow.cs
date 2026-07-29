using System.Drawing;
using System.Windows.Forms;
using NekoPcbEmulator.App.Rendering;
using NekoPcbEmulator.Core;

namespace NekoPcbEmulator.App.Forms;

/// <summary>
/// One window per powered board. The window is the board: closing it powers the PCB down and
/// releases its port.
/// </summary>
public sealed class PcbWindow : Form
{
    private readonly PcbHost _host;
    private readonly PcbCanvas _canvas;
    private readonly System.Windows.Forms.Timer _frameTimer = new() { Interval = 16 };

    private string _title = "";

    public PcbWindow(PcbHost host, PcbCanvas canvas, Size clientSize)
    {
        _host = host;
        _canvas = canvas;

        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = PcbPalette.Backdrop;
        ClientSize = clientSize;
        MinimumSize = new Size(clientSize.Width / 2, clientSize.Height / 2);
        StartPosition = FormStartPosition.Manual;
        KeyPreview = true;

        canvas.Dock = DockStyle.Fill;
        Controls.Add(canvas);

        _frameTimer.Tick += OnFrame;
        UpdateTitle();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DarkTitleBar.Apply(this);
        _frameTimer.Start();
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        _canvas.Sync();
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        int clients = _host.ClientCount;
        string title = $"{_host.Device.DisplayName}  —  {_host.Endpoint}  —  {clients} client(s)";
        if (title == _title) return;

        _title = title;
        Text = title;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // A quick way to get back to a clean board while iterating on a test suite.
        if (e.KeyCode == Keys.F5)
        {
            _host.Device.Reset();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _frameTimer.Stop();
            _frameTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
