using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace coverutil;

public class TrayApp : ApplicationContext
{
    private NotifyIcon _trayIcon = null!;
    private ToolStripMenuItem _trackItem = null!;
    private ToolStripMenuItem _statusItem = null!;
    private PictureBox _thumbnailBox = null!;
    private AppConfig _config;
    private readonly SpotifyClient _spotify = new();
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounceTimer;
    private CoverPreviewForm? _previewForm;
    private MainForm? _mainForm;
    private string? _currentArtist;
    private string? _currentTitle;
    private int _processing; // Interlocked flag — prevents concurrent ProcessChange

    public TrayApp()
    {
        _config = AppConfig.Load();
        Logger.LogApp("coverutil started");
        BuildTrayIcon();
        StartWatcher();
    }

    private void BuildTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "tray-icon.ico");
        Icon icon;
        try { icon = new Icon(iconPath); }
        catch { icon = SystemIcons.Application; }

        _thumbnailBox = new PictureBox
        {
            Size = new Size(40, 40),
            MinimumSize = new Size(40, 40),
            MaximumSize = new Size(40, 40),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(30, 30, 30),
            Cursor = Cursors.Hand
        };
        _thumbnailBox.Click += (_, _) => ShowCoverPreview();
        var thumbnailHost = new ToolStripControlHost(_thumbnailBox)
        {
            AutoSize = false,
            Size = new Size(44, 44),
            Margin = new Padding(4, 2, 4, 2)
        };

        _trackItem = new ToolStripMenuItem("No track yet") { Enabled = false };
        _statusItem = new ToolStripMenuItem("Idle") { Enabled = false };

        var openWindowItem = new ToolStripMenuItem("Open window");
        openWindowItem.Click += (_, _) => OpenMainWindow();

        var openNowPlayingItem = new ToolStripMenuItem("Open now_playing folder");
        openNowPlayingItem.Click += (_, _) => OpenFolder(_config.NowPlayingPath);

        var openCoverItem = new ToolStripMenuItem("Open cover folder");
        openCoverItem.Click += (_, _) => OpenFolder(_config.OutputPath);

        var previewItem = new ToolStripMenuItem("Show cover preview");
        previewItem.Click += (_, _) => ShowCoverPreview();

        var settingsItem = new ToolStripMenuItem("Settings...");
        settingsItem.Click += (_, _) => OpenSettings();

        var logItem = new ToolStripMenuItem("View log");
        logItem.Click += (_, _) => ViewLog();

        var quitItem = new ToolStripMenuItem("Quit");
        quitItem.Click += (_, _) => Quit();

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("coverutil") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(thumbnailHost);
        menu.Items.Add(_trackItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(openWindowItem);
        menu.Items.Add(openNowPlayingItem);
        menu.Items.Add(openCoverItem);
        menu.Items.Add(previewItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(logItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quitItem);

        _trayIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "coverutil",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => OpenMainWindow();
    }

    // ── UI update helpers ────────────────────────────────────────────────────

    private void SetStatus(string msg)
    {
        if (_trayIcon.ContextMenuStrip?.InvokeRequired == true)
        {
            _trayIcon.ContextMenuStrip.Invoke(() => SetStatus(msg));
            return;
        }
        _statusItem.Text = msg;

        var hoverText = (!string.IsNullOrEmpty(_currentArtist))
            ? $"{_currentArtist} - {_currentTitle}"
            : msg;
        _trayIcon.Text = hoverText.Length > 63 ? hoverText[..63] : hoverText;

        if (_mainForm != null && !_mainForm.IsDisposed)
            _mainForm.UpdateStatus(msg);
    }

    private void SetCurrentTrack(string artist, string title)
    {
        if (_trayIcon.ContextMenuStrip?.InvokeRequired == true)
        {
            _trayIcon.ContextMenuStrip.Invoke(() => SetCurrentTrack(artist, title));
            return;
        }
        _currentArtist = artist;
        _currentTitle = title;
        _trackItem.Text = $"{artist} - {title}";
        var hoverText = $"{artist} - {title}";
        _trayIcon.Text = hoverText.Length > 63 ? hoverText[..63] : hoverText;

        if (_mainForm != null && !_mainForm.IsDisposed)
            _mainForm.UpdateTrack(artist, title);
    }

    private void ClearCurrentTrack()
    {
        if (_trayIcon.ContextMenuStrip?.InvokeRequired == true)
        {
            _trayIcon.ContextMenuStrip.Invoke(ClearCurrentTrack);
            return;
        }
        _currentArtist = null;
        _currentTitle = null;
        _trackItem.Text = "No track";
        _trayIcon.Text = "coverutil";

        if (_mainForm != null && !_mainForm.IsDisposed)
            _mainForm.UpdateTrack(null, null);
    }

    private void SetRawDisplayTrack(string text)
    {
        if (_trayIcon.ContextMenuStrip?.InvokeRequired == true)
        {
            _trayIcon.ContextMenuStrip.Invoke(() => SetRawDisplayTrack(text));
            return;
        }
        _currentArtist = null;
        _currentTitle = null;
        _trackItem.Text = string.IsNullOrEmpty(text) ? "No track" : text;
        var hover = string.IsNullOrEmpty(text) ? "coverutil" : text;
        _trayIcon.Text = hover.Length > 63 ? hover[..63] : hover;

        if (_mainForm != null && !_mainForm.IsDisposed)
            _mainForm.UpdateTrack(text, null);
    }

    private void UpdateThumbnail(string imagePath)
    {
        if (_thumbnailBox.InvokeRequired)
        {
            _thumbnailBox.Invoke(() => UpdateThumbnail(imagePath));
            return;
        }
        try
        {
            var bytes = File.ReadAllBytes(imagePath);
            using var ms = new System.IO.MemoryStream(bytes);
            var newImage = Image.FromStream(ms);
            var oldImage = _thumbnailBox.Image;
            _thumbnailBox.Image = newImage;
            oldImage?.Dispose();
        }
        catch { }

        if (_mainForm != null && !_mainForm.IsDisposed)
            _mainForm.UpdateCover(imagePath);
    }

    // ── Watcher ──────────────────────────────────────────────────────────────

    public void StartWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;

        if (string.IsNullOrWhiteSpace(_config.SpotifyClientId) ||
            string.IsNullOrWhiteSpace(_config.SpotifyClientSecret) ||
            string.IsNullOrWhiteSpace(_config.NowPlayingPath) ||
            string.IsNullOrWhiteSpace(_config.OutputPath))
        {
            SetStatus("Config incomplete — open Settings");
            Logger.LogApp("Watcher not started: config incomplete");
            return;
        }

        _spotify.Configure(_config.SpotifyClientId, _config.SpotifyClientSecret);

        var dir = Path.GetDirectoryName(_config.NowPlayingPath);
        var file = Path.GetFileName(_config.NowPlayingPath);

        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file))
        {
            SetStatus("Invalid now_playing path");
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(dir, file)
            {
                NotifyFilter = NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Error += (_, e) =>
            {
                var msg = $"Watcher error: {e.GetException().Message}";
                Logger.LogApp(msg);
                SetStatus(msg);
            };
            SetStatus("Watching...");
            Logger.LogApp($"Watching: {_config.NowPlayingPath}");

            // Read current file immediately on (re)start
            System.Threading.Tasks.Task.Run(ProcessChange);
        }
        catch (Exception ex)
        {
            var msg = $"Watcher setup error: {ex.Message}";
            Logger.LogApp(msg);
            SetStatus(msg);
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        _debounceTimer?.Dispose();
        _debounceTimer = new System.Threading.Timer(_ => ProcessChange(), null, 500, Timeout.Infinite);
    }

    private void ProcessChange()
    {
        // Skip if a fetch is already in progress
        if (Interlocked.CompareExchange(ref _processing, 1, 0) != 0)
        {
            Logger.Log("ProcessChange skipped — already running");
            return;
        }

        try
        {
            DoProcessChange();
        }
        finally
        {
            Interlocked.Exchange(ref _processing, 0);
        }
    }

    private void DoProcessChange()
    {
        string content;
        try
        {
            content = File.ReadAllText(_config.NowPlayingPath).Trim();
        }
        catch (Exception ex)
        {
            var msg = $"Read error: {ex.Message}";
            Logger.LogApp(msg);
            SetStatus(msg);
            return;
        }

        if (string.IsNullOrEmpty(content))
        {
            SetRawDisplayTrack("");
            ApplyDefaultCover();
            Logger.LogApp("File is empty — applying default cover");
            SetStatus("File is empty");
            return;
        }

        var parsed = NowPlayingParser.Parse(content);
        if (parsed is null)
        {
            SetRawDisplayTrack(content);
            ApplyDefaultCover();
            Logger.LogApp($"Bad format: {content} — applying default cover");
            SetStatus($"Bad format: {content}");
            return;
        }

        var (artist, title) = parsed.Value;
        SetCurrentTrack(artist, title);
        SetStatus($"Fetching: {artist} - {title}");
        Logger.LogApp($"Fetching art: {artist} - {title}");

        try
        {
            var imageUrl = _spotify.SearchTrackAsync(artist, title).GetAwaiter().GetResult();
            _spotify.FetchAndSaveImageAsync(imageUrl, _config.OutputPath).GetAwaiter().GetResult();
            UpdateThumbnail(_config.OutputPath);
            SetStatus($"OK: {artist} - {title}");
            Logger.LogApp($"OK: {artist} - {title}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Exception for {artist} - {title}: {ex}");
            Logger.LogApp($"Error: {ex.Message}");
            ApplyDefaultCover();
            SetStatus($"Error: {ex.Message}");
        }
    }

    private void ApplyDefaultCover()
    {
        if (string.IsNullOrWhiteSpace(_config.DefaultCoverPath) ||
            !File.Exists(_config.DefaultCoverPath) ||
            string.IsNullOrWhiteSpace(_config.OutputPath))
            return;

        try
        {
            File.Copy(_config.DefaultCoverPath, _config.OutputPath, overwrite: true);
            UpdateThumbnail(_config.OutputPath);
            Logger.LogApp("Default cover applied");
        }
        catch (Exception ex)
        {
            Logger.LogApp($"Failed to apply default cover: {ex.Message}");
        }
    }

    // ── Window management ────────────────────────────────────────────────────

    private void OpenMainWindow()
    {
        if (_mainForm == null || _mainForm.IsDisposed)
        {
            _mainForm = new MainForm(
                () => _config,
                () => { _config = AppConfig.Load(); StartWatcher(); },
                Quit);

            // Seed current state into the new window
            _mainForm.UpdateTrack(_currentArtist, _currentTitle);
            _mainForm.UpdateStatus(_statusItem.Text ?? "");
            if (!string.IsNullOrEmpty(_config.OutputPath))
                _mainForm.UpdateCover(_config.OutputPath);
        }

        _mainForm.Show();
        if (_mainForm.WindowState == System.Windows.Forms.FormWindowState.Minimized)
            _mainForm.WindowState = System.Windows.Forms.FormWindowState.Normal;
        _mainForm.BringToFront();
    }

    private void ShowCoverPreview()
    {
        if (_previewForm != null && !_previewForm.IsDisposed)
        {
            _previewForm.BringToFront();
            return;
        }
        _previewForm = new CoverPreviewForm(_config.OutputPath, _currentArtist, _currentTitle);
        _previewForm.Show();
    }

    private static void OpenFolder(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        var dir = Directory.Exists(filePath) ? filePath : Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true }); }
        catch { }
    }

    private void ViewLog()
    {
        OpenMainWindow();
        _mainForm?.SwitchToTab(MainTab.Log);
    }

    private void OpenSettings()
    {
        OpenMainWindow();
        _mainForm?.SwitchToTab(MainTab.Settings);
    }

    public void Quit()
    {
        Logger.LogApp("coverutil exiting");
        _watcher?.Dispose();
        _debounceTimer?.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _watcher?.Dispose();
            _debounceTimer?.Dispose();
            _thumbnailBox.Image?.Dispose();
            _trayIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
