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
    private string? _currentArtist;
    private string? _currentTitle;

    public TrayApp()
    {
        _config = AppConfig.Load();
        Logger.Log("coverutil started");
        BuildTrayIcon();
        StartWatcher();
    }

    private void BuildTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "tray-icon.ico");
        Icon icon;
        try { icon = new Icon(iconPath); }
        catch { icon = SystemIcons.Application; }

        // Thumbnail picture box embedded in the menu
        _thumbnailBox = new PictureBox
        {
            Size = new Size(80, 80),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(30, 30, 30),
            Cursor = Cursors.Hand
        };
        _thumbnailBox.Click += (_, _) => ShowCoverPreview();
        var thumbnailHost = new ToolStripControlHost(_thumbnailBox)
        {
            Margin = new Padding(4, 4, 4, 2)
        };

        _trackItem = new ToolStripMenuItem("No track yet") { Enabled = false };
        _statusItem = new ToolStripMenuItem("Idle") { Enabled = false };

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
    }

    private void SetStatus(string msg)
    {
        if (_trayIcon.ContextMenuStrip?.InvokeRequired == true)
        {
            _trayIcon.ContextMenuStrip.Invoke(() => SetStatus(msg));
            return;
        }
        _statusItem.Text = msg;

        // Hover tooltip: prefer "Artist - Title" if known, else status
        var hoverText = (!string.IsNullOrEmpty(_currentArtist))
            ? $"{_currentArtist} - {_currentTitle}"
            : msg;
        _trayIcon.Text = hoverText.Length > 63 ? hoverText[..63] : hoverText;
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
            // Load into a MemoryStream so the file isn't locked
            var bytes = File.ReadAllBytes(imagePath);
            using var ms = new System.IO.MemoryStream(bytes);
            var newImage = Image.FromStream(ms);
            var oldImage = _thumbnailBox.Image;
            _thumbnailBox.Image = newImage;
            oldImage?.Dispose();
        }
        catch { }
    }

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
            Logger.Log("Watcher not started: config incomplete");
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
                Logger.Log(msg);
                SetStatus(msg);
            };
            SetStatus("Watching...");
            Logger.Log($"Watching: {_config.NowPlayingPath}");
        }
        catch (Exception ex)
        {
            var msg = $"Watcher setup error: {ex.Message}";
            Logger.Log(msg);
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
        string content;
        try
        {
            content = File.ReadAllText(_config.NowPlayingPath).Trim();
        }
        catch (Exception ex)
        {
            var msg = $"Read error: {ex.Message}";
            Logger.Log(msg);
            SetStatus(msg);
            return;
        }

        if (string.IsNullOrEmpty(content))
        {
            SetStatus("File is empty");
            return;
        }

        var parsed = ParseNowPlaying(content);
        if (parsed is null)
        {
            var msg = $"Bad format: {content}";
            Logger.Log(msg);
            SetStatus(msg);
            return;
        }

        var (artist, title) = parsed.Value;
        SetCurrentTrack(artist, title);
        SetStatus($"Fetching: {artist} - {title}");
        Logger.Log($"Fetching art for: {artist} - {title}");

        try
        {
            var imageUrl = _spotify.SearchTrackAsync(artist, title).GetAwaiter().GetResult();
            _spotify.FetchAndSaveImageAsync(imageUrl, _config.OutputPath).GetAwaiter().GetResult();
            UpdateThumbnail(_config.OutputPath);
            SetStatus($"OK: {artist} - {title}");
            Logger.Log($"Art saved: {artist} - {title}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Error fetching art for {artist} - {title}: {ex.Message}");
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
            Logger.Log($"Default cover applied: {_config.DefaultCoverPath}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to apply default cover: {ex.Message}");
        }
    }

    private static (string artist, string title)? ParseNowPlaying(string content)
    {
        int idx = content.IndexOf(" - ", StringComparison.Ordinal);
        if (idx < 0) return null;
        return (content[..idx].Trim(), content[(idx + 3)..].Trim());
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

    private static void ViewLog()
    {
        if (!File.Exists(Logger.LogPath)) return;
        try { Process.Start(new ProcessStartInfo("notepad.exe", Logger.LogPath) { UseShellExecute = true }); }
        catch { }
    }

    private void OpenSettings()
    {
        var form = new SettingsForm(_config, () =>
        {
            _config = AppConfig.Load();
            StartWatcher();
        });
        form.Show();
    }

    private void Quit()
    {
        Logger.Log("coverutil exiting");
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
