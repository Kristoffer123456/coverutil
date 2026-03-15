using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace coverutil;

public enum MainTab { Cover, Settings, Log }

public class MainForm : Form
{
    private static readonly Color BgColor      = Color.FromArgb(14,  14,  14);
    private static readonly Color SurfaceColor = Color.FromArgb(26,  26,  26);
    private static readonly Color PrimaryText  = Color.FromArgb(204, 204, 204);
    private static readonly Color InactiveTab  = Color.FromArgb(120, 120, 120);
    private static readonly Color AccentColor  = Color.FromArgb(106, 158, 152);
    private static readonly Color WarnColor    = Color.FromArgb(200, 160,  60);
    private static readonly Color ErrorColor   = Color.FromArgb(192, 57,  43);
    private static readonly Color IdleColor    = Color.FromArgb(136, 136, 136);

    private readonly Panel      _statusBar;
    private readonly Panel      _tabStrip;
    private readonly Panel      _coverPanel;
    private readonly Panel      _settingsPanel;
    private readonly Panel      _logPanel;

    private readonly Label      _trackLabel;
    private readonly Label      _statusLabel;

    private readonly Label      _tabCover;
    private readonly Label      _tabSettings;
    private readonly Label      _tabLog;
    private readonly Panel      _tabIndicator;

    private readonly PictureBox _coverBox;

    private TextBox   _clientIdBox      = null!;
    private TextBox   _clientSecretBox  = null!;
    private TextBox   _nowPlayingBox    = null!;
    private TextBox   _outputBox        = null!;
    private TextBox   _defaultCoverBox  = null!;
    private CheckBox  _closeToTrayBox   = null!;
    private Label     _settingsStatus   = null!;

    private RichTextBox _logBox = null!;

    private readonly Func<AppConfig> _getConfig;
    private readonly Action          _onSaved;
    private readonly Action          _quit;

    private MainTab _currentTab = MainTab.Cover;

    private const int DefaultCoverSize = 280;

    public MainForm(Func<AppConfig> getConfig, Action onSaved, Action quit)
    {
        _getConfig = getConfig;
        _onSaved   = onSaved;
        _quit      = quit;

        Text            = "coverutil";
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox     = false;
        ShowInTaskbar   = true;
        BackColor       = BgColor;

        // ── Tab strip (bottom-most) ──────────────────────────────────────────
        _tabStrip = new Panel { Dock = DockStyle.Bottom, Height = 32, BackColor = BgColor };

        _tabCover = new Label
        {
            Text = "Cover", TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = AccentColor, BackColor = BgColor, Cursor = Cursors.Hand
        };
        _tabSettings = new Label
        {
            Text = "Settings", TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = InactiveTab, BackColor = BgColor, Cursor = Cursors.Hand
        };
        _tabLog = new Label
        {
            Text = "Log", TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = InactiveTab, BackColor = BgColor, Cursor = Cursors.Hand
        };
        _tabIndicator = new Panel { Height = 2, BackColor = AccentColor };

        _tabCover.Click    += (_, _) => SwitchToTab(MainTab.Cover);
        _tabSettings.Click += (_, _) => SwitchToTab(MainTab.Settings);
        _tabLog.Click      += (_, _) => SwitchToTab(MainTab.Log);

        _tabStrip.Controls.Add(_tabCover);
        _tabStrip.Controls.Add(_tabSettings);
        _tabStrip.Controls.Add(_tabLog);
        _tabStrip.Controls.Add(_tabIndicator);
        _tabStrip.Resize += (_, _) => LayoutTabStrip();

        // ── Status bar (above tab strip) ─────────────────────────────────────
        _statusBar = new Panel
        {
            Dock = DockStyle.Bottom, Height = 40,
            BackColor = BgColor, Padding = new Padding(6, 0, 6, 0)
        };
        _trackLabel = new Label
        {
            Text = "No track", ForeColor = PrimaryText, BackColor = BgColor,
            TextAlign = ContentAlignment.BottomLeft, AutoEllipsis = true,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, SystemFonts.DefaultFont.Size, FontStyle.Bold)
        };
        _statusLabel = new Label
        {
            Text = "", ForeColor = IdleColor, BackColor = BgColor,
            TextAlign = ContentAlignment.TopLeft
        };
        _statusBar.Controls.Add(_trackLabel);
        _statusBar.Controls.Add(_statusLabel);
        _statusBar.Resize += (_, _) => LayoutStatusBar();

        // ── Content panels ───────────────────────────────────────────────────
        _coverPanel    = new Panel { BackColor = SurfaceColor, Visible = true };
        _settingsPanel = new Panel { BackColor = BgColor, Visible = false, AutoScroll = true };
        _logPanel      = new Panel { BackColor = BgColor, Visible = false };

        _coverBox = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, BackColor = SurfaceColor };
        _coverPanel.Controls.Add(_coverBox);

        BuildSettingsPanel();
        BuildLogPanel();

        // ── Dock order: bottom controls first, content panels last ───────────
        Controls.Add(_tabStrip);
        Controls.Add(_statusBar);
        Controls.Add(_coverPanel);
        Controls.Add(_settingsPanel);
        Controls.Add(_logPanel);

        // ── Initial size ─────────────────────────────────────────────────────
        ClientSize = new Size(DefaultCoverSize, DefaultCoverSize + _statusBar.Height + _tabStrip.Height);

        // ── Restore saved position ───────────────────────────────────────────
        var cfg = getConfig();
        if (cfg.WindowX >= 0 &&
            Screen.AllScreens.Any(s => s.Bounds.Contains(new Point(cfg.WindowX, cfg.WindowY))))
        {
            StartPosition = FormStartPosition.Manual;
            Location      = new Point(cfg.WindowX, cfg.WindowY);
            ClientSize    = new Size(cfg.WindowWidth, cfg.WindowWidth + _statusBar.Height + _tabStrip.Height);
        }
        else
        {
            StartPosition = FormStartPosition.CenterScreen;
        }

        ResizeEnd      += (_, _) => EnforceSquareCover();
        Shown          += (_, _) => { EnforceSquareCover(); LayoutTabStrip(); LayoutStatusBar(); };
        Resize         += OnResize;
        FormClosing    += OnFormClosing;
    }

    // ── Settings panel ───────────────────────────────────────────────────────

    private void BuildSettingsPanel()
    {
        var layout = new TableLayoutPanel
        {
            AutoSize     = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount  = 3,
            BackColor    = BgColor,
            ForeColor    = PrimaryText,
            Padding      = new Padding(8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));

        int row = 0;

        Label MakeLabel(string text) => new Label
        {
            Text = text, ForeColor = PrimaryText, BackColor = BgColor,
            Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true,
            Padding = new Padding(0, 6, 0, 0)
        };

        TextBox MakeTextBox(bool readOnly = false, bool password = false) => new TextBox
        {
            Dock = DockStyle.Fill, ReadOnly = readOnly, UseSystemPasswordChar = password,
            BackColor = Color.FromArgb(30, 30, 30), ForeColor = PrimaryText,
            BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(3)
        };

        Button MakeBrowseBtn() => new Button
        {
            Text = "Browse", Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(30, 30, 30), ForeColor = PrimaryText,
            FlatStyle = FlatStyle.Flat, Margin = new Padding(3)
        };

        layout.Controls.Add(MakeLabel("Spotify Client ID:"), 0, row);
        _clientIdBox = MakeTextBox();
        layout.Controls.Add(_clientIdBox, 1, row);
        layout.SetColumnSpan(_clientIdBox, 2);
        row++;

        layout.Controls.Add(MakeLabel("Client Secret:"), 0, row);
        _clientSecretBox = MakeTextBox(password: true);
        layout.Controls.Add(_clientSecretBox, 1, row);
        layout.SetColumnSpan(_clientSecretBox, 2);
        row++;

        layout.Controls.Add(MakeLabel("now_playing.txt:"), 0, row);
        _nowPlayingBox = MakeTextBox(readOnly: true);
        layout.Controls.Add(_nowPlayingBox, 1, row);
        var browseNow = MakeBrowseBtn();
        browseNow.Click += BrowseNowPlaying;
        layout.Controls.Add(browseNow, 2, row);
        row++;

        layout.Controls.Add(MakeLabel("Output file:"), 0, row);
        _outputBox = MakeTextBox(readOnly: true);
        layout.Controls.Add(_outputBox, 1, row);
        var browseOut = MakeBrowseBtn();
        browseOut.Click += BrowseOutput;
        layout.Controls.Add(browseOut, 2, row);
        row++;

        layout.Controls.Add(MakeLabel("Default cover:"), 0, row);
        _defaultCoverBox = MakeTextBox(readOnly: true);
        layout.Controls.Add(_defaultCoverBox, 1, row);
        var browseDefault = MakeBrowseBtn();
        browseDefault.Click += BrowseDefaultCover;
        layout.Controls.Add(browseDefault, 2, row);
        row++;

        _closeToTrayBox = new CheckBox
        {
            Text = "Close window to tray (uncheck to quit on close)",
            ForeColor = PrimaryText, BackColor = BgColor,
            Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(3, 6, 3, 3)
        };
        layout.Controls.Add(_closeToTrayBox, 0, row);
        layout.SetColumnSpan(_closeToTrayBox, 3);
        row++;

        _settingsStatus = new Label
        {
            Text = "", AutoSize = true,
            ForeColor = ErrorColor, BackColor = BgColor, Margin = new Padding(3)
        };
        layout.Controls.Add(_settingsStatus, 0, row);
        layout.SetColumnSpan(_settingsStatus, 3);
        row++;

        var saveBtn = new Button
        {
            Text = "Save", Width = 80, Anchor = AnchorStyles.Right,
            BackColor = Color.FromArgb(30, 30, 30), ForeColor = PrimaryText,
            FlatStyle = FlatStyle.Flat, Margin = new Padding(3)
        };
        saveBtn.Click += SaveSettings;
        layout.Controls.Add(saveBtn, 2, row);

        var config = _getConfig();
        _clientIdBox.Text     = config.SpotifyClientId;
        _clientSecretBox.Text = config.SpotifyClientSecret;
        _nowPlayingBox.Text   = config.NowPlayingPath;
        _outputBox.Text       = config.OutputPath;
        _defaultCoverBox.Text = config.DefaultCoverPath;
        _closeToTrayBox.Checked = config.CloseToTray;

        _settingsPanel.Controls.Add(layout);
    }

    // ── Log panel ────────────────────────────────────────────────────────────

    private void BuildLogPanel()
    {
        _logBox = new RichTextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true,
            Font = new Font("Consolas", 9f),
            BackColor = Color.FromArgb(18, 18, 18), ForeColor = Color.FromArgb(220, 220, 220),
            BorderStyle = BorderStyle.None, ScrollBars = RichTextBoxScrollBars.Vertical, WordWrap = false
        };

        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom, Height = 36,
            BackColor = BgColor, Padding = new Padding(6, 4, 6, 4)
        };

        var refreshBtn = new Button
        {
            Text = "Refresh", Width = 80, Left = 0, Top = 4, Height = 26,
            BackColor = Color.FromArgb(30, 30, 30), ForeColor = PrimaryText, FlatStyle = FlatStyle.Flat
        };
        refreshBtn.Click += (_, _) => LoadLog();

        var fullLogBtn = new Button
        {
            Text = "Open full log", Width = 110, Left = 86, Top = 4, Height = 26,
            BackColor = Color.FromArgb(30, 30, 30), ForeColor = PrimaryText, FlatStyle = FlatStyle.Flat
        };
        fullLogBtn.Click += (_, _) => OpenFullLog();

        buttonPanel.Controls.AddRange(new Control[] { refreshBtn, fullLogBtn });
        _logPanel.Controls.Add(_logBox);
        _logPanel.Controls.Add(buttonPanel);
    }

    private void LoadLog()
    {
        if (!File.Exists(Logger.AppLogPath))
        {
            _logBox.Text = "No log entries yet.";
            return;
        }
        try
        {
            _logBox.Text = File.ReadAllText(Logger.AppLogPath);
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }
        catch (Exception ex) { _logBox.Text = $"Could not read log: {ex.Message}"; }
    }

    private static void OpenFullLog()
    {
        if (!File.Exists(Logger.LogPath))
        {
            MessageBox.Show("No detailed log file found.", "coverutil", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try { Process.Start(new ProcessStartInfo("notepad.exe", Logger.LogPath) { UseShellExecute = true }); }
        catch { }
    }

    // ── Layout ───────────────────────────────────────────────────────────────

    private void LayoutTabStrip()
    {
        int w = _tabStrip.ClientSize.Width;
        if (w <= 0) return;
        int third = w / 3;
        _tabCover.SetBounds(0, 0, third, 30);
        _tabSettings.SetBounds(third, 0, third, 30);
        _tabLog.SetBounds(third * 2, 0, w - third * 2, 30);
        PositionTabIndicator();
    }

    private void PositionTabIndicator()
    {
        var active = _currentTab switch
        {
            MainTab.Settings => _tabSettings,
            MainTab.Log      => _tabLog,
            _                => _tabCover
        };
        _tabIndicator.SetBounds(active.Left, 0, active.Width, 2);
    }

    private void LayoutStatusBar()
    {
        int w = _statusBar.ClientSize.Width;
        int h = _statusBar.ClientSize.Height;
        int half = h / 2;
        _trackLabel.SetBounds(6, 0, w - 12, half);
        _statusLabel.SetBounds(6, half, w - 12, h - half);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_coverBox == null) return;

        int contentHeight = ClientSize.Height - (_statusBar?.Height ?? 22) - (_tabStrip?.Height ?? 32);
        var contentBounds = new Rectangle(0, 0, ClientSize.Width, Math.Max(0, contentHeight));
        if (_coverPanel != null)    _coverPanel.Bounds    = contentBounds;
        if (_settingsPanel != null) _settingsPanel.Bounds = contentBounds;
        if (_logPanel != null)      _logPanel.Bounds      = contentBounds;

        _coverBox.SetBounds(0, 0, ClientSize.Width, ClientSize.Width);

        if (_tabStrip != null)   LayoutTabStrip();
        if (_statusBar != null)  LayoutStatusBar();
    }

    private void EnforceSquareCover()
    {
        if (WindowState != FormWindowState.Normal) return;
        int bottom = _statusBar.Height + _tabStrip.Height;
        ClientSize  = new Size(ClientSize.Width, ClientSize.Width + bottom);
        MinimumSize = new Size(
            160 + SystemInformation.BorderSize.Width * 2,
            160 + bottom + SystemInformation.CaptionHeight + SystemInformation.BorderSize.Height * 2);
    }

    // ── Tab switching ────────────────────────────────────────────────────────

    public void SwitchToTab(MainTab tab)
    {
        if (InvokeRequired) { Invoke(() => SwitchToTab(tab)); return; }
        _currentTab = tab;

        _coverPanel.Visible    = tab == MainTab.Cover;
        _settingsPanel.Visible = tab == MainTab.Settings;
        _logPanel.Visible      = tab == MainTab.Log;

        _tabCover.ForeColor    = tab == MainTab.Cover    ? AccentColor : InactiveTab;
        _tabSettings.ForeColor = tab == MainTab.Settings ? AccentColor : InactiveTab;
        _tabLog.ForeColor      = tab == MainTab.Log      ? AccentColor : InactiveTab;

        PositionTabIndicator();

        if (tab == MainTab.Settings)
        {
            var cfg = _getConfig();
            _clientIdBox.Text       = cfg.SpotifyClientId;
            _clientSecretBox.Text   = cfg.SpotifyClientSecret;
            _nowPlayingBox.Text     = cfg.NowPlayingPath;
            _outputBox.Text         = cfg.OutputPath;
            _defaultCoverBox.Text   = cfg.DefaultCoverPath;
            _closeToTrayBox.Checked = cfg.CloseToTray;
            _settingsStatus.Text    = "";
        }
        else if (tab == MainTab.Log)
        {
            LoadLog();
        }
    }

    // ── Public update methods ─────────────────────────────────────────────────

    public void UpdateTrack(string? artist, string? title)
    {
        if (InvokeRequired) { Invoke(() => UpdateTrack(artist, title)); return; }
        _trackLabel.Text = (artist != null && title != null) ? $"{artist} \u2014 {title}" : artist ?? "No track";
    }

    public void UpdateStatus(string status)
    {
        if (InvokeRequired) { Invoke(() => UpdateStatus(status)); return; }
        _statusLabel.Text = status;
        _statusLabel.ForeColor = status.StartsWith("OK")
            ? AccentColor
            : (status.StartsWith("Bad format") || status.StartsWith("File is empty"))
                ? WarnColor
                : (status.StartsWith("Error") || status.StartsWith("Artist") || status.StartsWith("Config")
                   || status.StartsWith("Read error") || status.StartsWith("Watcher") || status.StartsWith("Invalid"))
                    ? ErrorColor
                    : IdleColor;
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

    // ── Settings panel logic ──────────────────────────────────────────────────

    private void BrowseNowPlaying(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Select now_playing.txt",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"
        };
        if (!string.IsNullOrEmpty(_nowPlayingBox.Text))
            dlg.InitialDirectory = Path.GetDirectoryName(_nowPlayingBox.Text);
        if (dlg.ShowDialog() == DialogResult.OK)
            _nowPlayingBox.Text = dlg.FileName;
    }

    private void BrowseOutput(object? sender, EventArgs e)
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Select output image file",
            Filter = "JPEG (*.jpg)|*.jpg|PNG (*.png)|*.png|All files (*.*)|*.*",
            OverwritePrompt = false
        };
        if (!string.IsNullOrEmpty(_outputBox.Text))
        {
            dlg.InitialDirectory = Path.GetDirectoryName(_outputBox.Text);
            dlg.FileName         = Path.GetFileName(_outputBox.Text);
        }
        if (dlg.ShowDialog() == DialogResult.OK)
            _outputBox.Text = dlg.FileName;
    }

    private void BrowseDefaultCover(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Select default cover image",
            Filter = "Image files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|All files (*.*)|*.*"
        };
        if (!string.IsNullOrEmpty(_defaultCoverBox.Text))
            dlg.InitialDirectory = Path.GetDirectoryName(_defaultCoverBox.Text);
        if (dlg.ShowDialog() == DialogResult.OK)
            _defaultCoverBox.Text = dlg.FileName;
    }

    private void SaveSettings(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_clientIdBox.Text) || string.IsNullOrWhiteSpace(_clientSecretBox.Text))
        {
            _settingsStatus.ForeColor = ErrorColor;
            _settingsStatus.Text      = "Client ID and Secret are required.";
            return;
        }

        var cfg = _getConfig();
        cfg.SpotifyClientId     = _clientIdBox.Text.Trim();
        cfg.SpotifyClientSecret = _clientSecretBox.Text.Trim();
        cfg.NowPlayingPath      = _nowPlayingBox.Text.Trim();
        cfg.OutputPath          = _outputBox.Text.Trim();
        cfg.DefaultCoverPath    = _defaultCoverBox.Text.Trim();
        cfg.CloseToTray         = _closeToTrayBox.Checked;

        try
        {
            cfg.Save();
            _onSaved();

            var reloaded = _getConfig();
            _clientIdBox.Text       = reloaded.SpotifyClientId;
            _clientSecretBox.Text   = reloaded.SpotifyClientSecret;
            _nowPlayingBox.Text     = reloaded.NowPlayingPath;
            _outputBox.Text         = reloaded.OutputPath;
            _defaultCoverBox.Text   = reloaded.DefaultCoverPath;
            _closeToTrayBox.Checked = reloaded.CloseToTray;

            _settingsStatus.ForeColor = AccentColor;
            _settingsStatus.Text      = "Saved.";
        }
        catch (Exception ex)
        {
            _settingsStatus.ForeColor = ErrorColor;
            _settingsStatus.Text      = $"Save failed: {ex.Message}";
        }
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnResize(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized) Hide();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && WindowState == FormWindowState.Normal)
        {
            var cfg = _getConfig();
            cfg.WindowX     = Location.X;
            cfg.WindowY     = Location.Y;
            cfg.WindowWidth = ClientSize.Width;
            cfg.Save();
        }

        if (e.CloseReason != CloseReason.UserClosing) return;

        if (_getConfig().CloseToTray) { e.Cancel = true; Hide(); }
        else { _quit(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _coverBox.Image?.Dispose();
        base.Dispose(disposing);
    }
}
