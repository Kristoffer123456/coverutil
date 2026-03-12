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

    private const int CoverSize = 260;
    private const int StripHeight = 108; // track + status + buttons

    public MainForm(Func<AppConfig> getConfig, Action openSettings, Action viewLog, Action quit)
    {
        _getConfig = getConfig;
        _quit = quit;

        Text = "coverutil";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        // ClientSize makes the cover area exactly CoverSize×CoverSize
        ClientSize = new Size(CoverSize, CoverSize + StripHeight);

        // ── Cover (fills everything above the strip) ──────────────────────
        _coverBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(20, 20, 20)
        };

        // ── Bottom strip ──────────────────────────────────────────────────
        var strip = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = StripHeight
        };

        // Button row (docked to bottom of strip)
        var btnRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(6, 4, 6, 4)
        };
        var settingsBtn = new Button { Text = "Settings", Height = 26, AutoSize = true, Padding = new Padding(6, 0, 6, 0) };
        settingsBtn.Click += (_, _) => openSettings();
        var logBtn = new Button { Text = "View Log", Height = 26, AutoSize = true, Padding = new Padding(6, 0, 6, 0) };
        logBtn.Click += (_, _) => viewLog();
        btnRow.Controls.Add(settingsBtn);
        btnRow.Controls.Add(logBtn);

        // Status label (above buttons)
        _statusLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 20,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.Gray,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 8f),
            Padding = new Padding(4, 0, 4, 0)
        };

        // Track label (fills remaining strip space)
        _trackLabel = new Label
        {
            Text = "No track",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f, FontStyle.Bold),
            AutoEllipsis = true,
            Padding = new Padding(4, 6, 4, 0)
        };

        // Add to strip — DockStyle.Bottom items added first so they anchor at the bottom,
        // Fill added last so it takes the remaining space
        strip.Controls.Add(btnRow);
        strip.Controls.Add(_statusLabel);
        strip.Controls.Add(_trackLabel);

        // Add to form — Bottom first, Fill last
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
