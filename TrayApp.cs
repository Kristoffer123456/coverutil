using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace coverutil;

public class TrayApp : ApplicationContext
{
    private NotifyIcon _trayIcon = null!;
    private ToolStripMenuItem _statusItem = null!;
    private AppConfig _config;
    private readonly SpotifyClient _spotify = new();
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounceTimer;

    public TrayApp()
    {
        _config = AppConfig.Load();
        BuildTrayIcon();
        StartWatcher();
    }

    private void BuildTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "tray-icon.ico");
        Icon icon;
        try { icon = new Icon(iconPath); }
        catch { icon = SystemIcons.Application; }

        _statusItem = new ToolStripMenuItem("Idle") { Enabled = false };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("coverutil") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        var settingsItem = new ToolStripMenuItem("Settings...");
        settingsItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        var quitItem = new ToolStripMenuItem("Quit");
        quitItem.Click += (_, _) => Quit();
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
        _trayIcon.Text = msg.Length > 63 ? msg[..63] : msg;
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
            _watcher.Error += (_, e) => SetStatus($"Watcher error: {e.GetException().Message}");
            SetStatus("Watching...");
        }
        catch (Exception ex)
        {
            SetStatus($"Watcher setup error: {ex.Message}");
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
            SetStatus($"Read error: {ex.Message}");
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
            SetStatus($"Bad format: {content}");
            return;
        }

        var (artist, title) = parsed.Value;
        SetStatus($"Fetching: {artist} - {title}");

        try
        {
            var imageUrl = _spotify.SearchTrackAsync(artist, title).GetAwaiter().GetResult();
            _spotify.FetchAndSaveImageAsync(imageUrl, _config.OutputPath).GetAwaiter().GetResult();
            SetStatus($"OK: {artist} - {title}");
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
    }

    private static (string artist, string title)? ParseNowPlaying(string content)
    {
        int idx = content.IndexOf(" - ", StringComparison.Ordinal);
        if (idx < 0) return null;
        return (content[..idx].Trim(), content[(idx + 3)..].Trim());
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
            _trayIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
