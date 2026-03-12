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

    public MainForm(Func<AppConfig> getConfig, Action openSettings, Action viewLog, Action quit)
    {
        _getConfig = getConfig;
        _quit = quit;

        Text = "coverutil";
        Width = 300;
        Height = 360;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 220)); // cover
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // track
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));  // status
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // buttons

        _coverBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(30, 30, 30)
        };
        panel.Controls.Add(_coverBox, 0, 0);

        _trackLabel = new Label
        {
            Text = "No track",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f, FontStyle.Bold),
            AutoEllipsis = true
        };
        panel.Controls.Add(_trackLabel, 0, 1);

        _statusLabel = new Label
        {
            Text = "",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.Gray,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 8f)
        };
        panel.Controls.Add(_statusLabel, 0, 2);

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 4, 0, 0)
        };
        var settingsBtn = new Button { Text = "Settings", AutoSize = true };
        settingsBtn.Click += (_, _) => openSettings();
        var logBtn = new Button { Text = "Log", AutoSize = true };
        logBtn.Click += (_, _) => viewLog();
        btnPanel.Controls.Add(settingsBtn);
        btnPanel.Controls.Add(logBtn);
        panel.Controls.Add(btnPanel, 0, 3);

        Controls.Add(panel);

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
            using var ms = new System.IO.MemoryStream(bytes);
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
