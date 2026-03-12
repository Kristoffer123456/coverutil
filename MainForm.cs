using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace coverutil;

public class MainForm : Form
{
    private readonly PictureBox _coverBox;
    private readonly Label _trackLabel;
    private readonly Label _statusLabel;
    private readonly Func<AppConfig> _getConfig;
    private readonly Action _quit;

    private const int StripHeight = 100;
    private const int DefaultCoverSize = 280;

    public MainForm(Func<AppConfig> getConfig, Action openSettings, Action viewLog, Action quit)
    {
        _getConfig = getConfig;
        _quit = quit;

        Text = "coverutil";
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        MinimumSize = new Size(160, 160 + StripHeight + SystemInformation.CaptionHeight);
        ClientSize = new Size(DefaultCoverSize, DefaultCoverSize + StripHeight);

        // ── Cover — fills all space above the strip ───────────────────────
        _coverBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.StretchImage,
            BackColor = Color.FromArgb(20, 20, 20)
        };

        // ── Bottom strip — TableLayoutPanel avoids docking-order ambiguity ─
        var strip = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = StripHeight,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(6, 4, 6, 6)
        };
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        strip.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // track label
        strip.RowStyles.Add(new RowStyle(SizeType.Absolute, 20)); // status label
        strip.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); // buttons

        _trackLabel = new Label
        {
            Text = "No track",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f, FontStyle.Bold),
            AutoEllipsis = true
        };
        strip.Controls.Add(_trackLabel, 0, 0);

        _statusLabel = new Label
        {
            Text = "",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.Gray,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 8f)
        };
        strip.Controls.Add(_statusLabel, 0, 1);

        var btnRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0)
        };
        var settingsBtn = new Button { Text = "Settings", Height = 26, AutoSize = true };
        settingsBtn.Click += (_, _) => openSettings();
        var logBtn = new Button { Text = "View Log", Height = 26, AutoSize = true };
        logBtn.Click += (_, _) => viewLog();
        btnRow.Controls.Add(settingsBtn);
        btnRow.Controls.Add(logBtn);
        strip.Controls.Add(btnRow, 0, 2);

        // Add strip first (Bottom), cover last (Fill) — standard WinForms dock order
        Controls.Add(strip);
        Controls.Add(_coverBox);

        Resize += OnResize;
        FormClosing += OnFormClosing;
    }

    public void UpdateTrack(string? artist, string? title)
    {
        if (InvokeRequired) { Invoke(() => UpdateTrack(artist, title)); return; }
        _trackLabel.Text = (artist != null && title != null) ? $"{artist} - {title}" : "No track";
    }

    public void UpdateStatus(string status)
    {
        if (InvokeRequired) { Invoke(() => UpdateStatus(status)); return; }
        _statusLabel.Text = status;
    }

    public void UpdateCover(string? imagePath)
    {
        if (InvokeRequired) { Invoke(() => UpdateCover(imagePath)); return; }
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
        {
            _coverBox.Image?.Dispose();
            _coverBox.Image = null;
            return;
        }
        try
        {
            var bytes = File.ReadAllBytes(imagePath);
            using var ms = new MemoryStream(bytes);
            var newImage = Image.FromStream(ms);
            var old = _coverBox.Image;
            _coverBox.Image = newImage;
            old?.Dispose();
        }
        catch { }
    }

    private void OnResize(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized)
            Hide();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason != CloseReason.UserClosing) return;

        if (_getConfig().CloseToTray)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            _quit();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _coverBox.Image?.Dispose();
        base.Dispose(disposing);
    }
}
