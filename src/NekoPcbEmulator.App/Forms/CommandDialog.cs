using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using NekoPcbEmulator.App.Interaction;
using NekoPcbEmulator.App.Rendering;

namespace NekoPcbEmulator.App.Forms;

/// <summary>
/// Collects the parameters of one command and sends it to the board.
///
/// The preview pane shows the exact statement or frame the Send button will emit, built by the
/// command's own encoder — so there is no way for the dialog to display one thing and send
/// another.
/// </summary>
internal sealed class CommandDialog : Form
{
    private readonly CommandSpec _command;
    private readonly List<Func<string>> _readers = [];
    private readonly TextBox _preview = new();

    public CommandDialog(string peripheral, CommandSpec command)
    {
        _command = command;

        Text = $"{peripheral} — {command.Name}";
        BackColor = PcbPalette.Surface;
        ForeColor = PcbPalette.Text;
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Icon = AppIcon.Create();

        BuildLayout();
        UpdatePreview();
    }

    /// <summary>The bytes to send, valid once the dialog closes with <see cref="DialogResult.OK"/>.</summary>
    public byte[] Payload { get; private set; } = [];

    private void BuildLayout()
    {
        const int labelWidth = 120;
        const int fieldWidth = 260;
        const int margin = 18;

        int y = margin;

        var summary = new Label
        {
            Text = _command.Summary,
            ForeColor = PcbPalette.TextDim,
            Location = new Point(margin, y),
            Size = new Size(labelWidth + fieldWidth + 10, 34),
        };
        Controls.Add(summary);
        y += 44;

        foreach (var field in _command.Fields)
        {
            Controls.Add(new Label
            {
                Text = field.Name,
                ForeColor = PcbPalette.Text,
                Location = new Point(margin, y + 4),
                Size = new Size(labelWidth, 20),
            });

            var editor = CreateEditor(field, new Point(margin + labelWidth, y), fieldWidth);
            Controls.Add(editor.Control);
            _readers.Add(editor.Read);

            y += 28;

            if (!string.IsNullOrEmpty(field.Hint))
            {
                Controls.Add(new Label
                {
                    Text = field.Hint,
                    ForeColor = PcbPalette.TextDim,
                    Font = new Font("Segoe UI", 8f),
                    Location = new Point(margin + labelWidth, y),
                    Size = new Size(fieldWidth, 16),
                });
                y += 18;
            }

            y += 6;
        }

        Controls.Add(new Label
        {
            Text = "ON THE WIRE",
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = PcbPalette.TextDim,
            Location = new Point(margin, y),
            AutoSize = true,
        });
        y += 20;

        _preview.Multiline = true;
        _preview.ReadOnly = true;
        _preview.BorderStyle = BorderStyle.FixedSingle;
        _preview.BackColor = PcbPalette.Backdrop;
        _preview.ForeColor = PcbPalette.Tx;
        _preview.Font = new Font("Consolas", 9f);
        _preview.Location = new Point(margin, y);
        _preview.Size = new Size(labelWidth + fieldWidth, 54);
        Controls.Add(_preview);
        y += 66;

        var send = new Button
        {
            Text = "Send to board",
            DialogResult = DialogResult.OK,
            BackColor = PcbPalette.Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Location = new Point(margin, y),
            Size = new Size(150, 32),
            Cursor = Cursors.Hand,
        };
        send.FlatAppearance.BorderSize = 0;
        send.Click += (_, _) => Payload = _command.Encode(ReadValues());

        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            BackColor = PcbPalette.SurfaceRaised,
            ForeColor = PcbPalette.Text,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(margin + 160, y),
            Size = new Size(110, 32),
            Cursor = Cursors.Hand,
        };
        cancel.FlatAppearance.BorderColor = PcbPalette.Divider;

        Controls.Add(send);
        Controls.Add(cancel);

        AcceptButton = send;
        CancelButton = cancel;
        ClientSize = new Size(labelWidth + fieldWidth + margin * 2, y + 32 + margin);
    }

    private (Control Control, Func<string> Read) CreateEditor(CommandField field, Point location, int width)
    {
        switch (field.Kind)
        {
            case FieldKind.Boolean:
            {
                var box = new CheckBox
                {
                    Checked = bool.TryParse(field.Default, out bool on) && on,
                    Location = new Point(location.X, location.Y + 2),
                    Size = new Size(width, 22),
                    ForeColor = PcbPalette.Text,
                    Text = "on",
                };
                box.CheckedChanged += (_, _) =>
                {
                    box.Text = box.Checked ? "on" : "off";
                    UpdatePreview();
                };
                return (box, () => box.Checked.ToString());
            }

            case FieldKind.Integer:
            {
                var spin = new NumericUpDown
                {
                    Minimum = field.Minimum,
                    Maximum = field.Maximum,
                    Value = int.TryParse(field.Default, out int start)
                        ? Math.Clamp(start, field.Minimum, field.Maximum)
                        : field.Minimum,
                    Location = location,
                    Size = new Size(110, 24),
                    BackColor = PcbPalette.Backdrop,
                    ForeColor = PcbPalette.Text,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font("Consolas", 9.5f),
                };
                spin.ValueChanged += (_, _) => UpdatePreview();
                return (spin, () => ((int)spin.Value).ToString(CultureInfo.InvariantCulture));
            }

            case FieldKind.Rgba:
                return CreateColourEditor(field, location, width);

            default:
            {
                var text = new TextBox
                {
                    Text = field.Default,
                    Location = location,
                    Size = new Size(width, 24),
                    BackColor = PcbPalette.Backdrop,
                    ForeColor = PcbPalette.Text,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font("Consolas", 9.5f),
                };
                text.TextChanged += (_, _) => UpdatePreview();
                return (text, () => text.Text);
            }
        }
    }

    /// <summary>Hex entry plus a swatch that opens the system picker. The picker sets RGB and leaves alpha alone.</summary>
    private (Control Control, Func<string> Read) CreateColourEditor(CommandField field, Point location, int width)
    {
        var host = new Panel { Location = location, Size = new Size(width, 24), BackColor = PcbPalette.Surface };

        var text = new TextBox
        {
            Text = field.Default,
            Location = new Point(0, 0),
            Size = new Size(width - 34, 24),
            BackColor = PcbPalette.Backdrop,
            ForeColor = PcbPalette.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9.5f),
        };

        var swatch = new Panel
        {
            Location = new Point(width - 28, 1),
            Size = new Size(28, 22),
            BorderStyle = BorderStyle.FixedSingle,
            Cursor = Cursors.Hand,
        };

        void Sync()
        {
            var (r, g, b, a) = ParseRgba(text.Text);
            swatch.BackColor = Color.FromArgb(255, r * a / 255, g * a / 255, b * a / 255);
            UpdatePreview();
        }

        text.TextChanged += (_, _) => Sync();
        swatch.Click += (_, _) =>
        {
            var (r, g, b, a) = ParseRgba(text.Text);
            using var picker = new ColorDialog { Color = Color.FromArgb(r, g, b), FullOpen = true };
            if (picker.ShowDialog(this) != DialogResult.OK) return;

            text.Text = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}{a:X2}";
        };

        host.Controls.Add(text);
        host.Controls.Add(swatch);
        Sync();

        return (host, () => text.Text);
    }

    private static (byte R, byte G, byte B, byte A) ParseRgba(string value)
    {
        string s = value.Trim().TrimStart('#');
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];

        if (s.Length != 8 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint packed))
            return (0xFF, 0xFF, 0xFF, 0xFF);

        return ((byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
    }

    private List<string> ReadValues() => [.. _readers.Select(read => read())];

    private void UpdatePreview()
    {
        if (_readers.Count != _command.Fields.Count) return;

        try
        {
            _preview.Text = _command.Preview(ReadValues());
            _preview.ForeColor = PcbPalette.Tx;
        }
        catch (Exception ex)
        {
            _preview.Text = ex.Message;
            _preview.ForeColor = PcbPalette.Danger;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DarkTitleBar.Apply(this);
    }
}
